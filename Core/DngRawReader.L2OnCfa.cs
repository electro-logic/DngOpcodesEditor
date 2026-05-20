using System;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// DngRawReader — OpcodeList2 implementations that target *linearised CFA*
// data, applied between LineariseCfaInPlace and BilinearDemosaicLinearised.
// =============================================================================
//
// Each method takes the 1-plane ushort[] CFA buffer plus its dimensions and
// applies the corresponding opcode in place. They mirror the RGB
// implementations under Core/Opcodes/<Name>.cs but skip the per-plane loop
// (CFA is single-plane) and read/write the raw ushort buffer directly.
//
// The DNG spec puts these opcodes in OpcodeList2 (intended for camera-native
// linear samples after black/white-level normalisation but before demosaic).
// `ApplyOpcodeList2OnCfa` in DngRawReader.cs dispatches into the methods here.
//
// Real-world DNGs in our corpus only ship GainMap in OpcodeList2 (Pixel 6),
// so these implementations are validated against synthetic tests (see
// Tests/OpcodeList2CfaTests.cs) rather than real frames.

public static partial class DngRawReader
{
    // ----- helpers ------------------------------------------------------------

    // Resolve the region in pixel coords + pitch, clamped to the buffer.
    static (int top, int left, int bottom, int right, int rowPitch, int colPitch)
        ClampArea(OpcodeArea p, int W, int H)
    {
        int top    = (int)Math.Min(p.top,    (uint)H);
        int left   = (int)Math.Min(p.left,   (uint)W);
        int bottom = (int)Math.Min(p.bottom, (uint)H);
        int right  = (int)Math.Min(p.right,  (uint)W);
        int rowPitch = (int)Math.Max(1, p.rowPitch);
        int colPitch = (int)Math.Max(1, p.colPitch);
        return (top, left, bottom, right, rowPitch, colPitch);
    }

    // ----- MapTable (opcode 7) -----------------------------------------------

    // Per-pixel 1-D LUT over a region. The sample value indexes directly;
    // out-of-range samples clamp to the last entry. Same semantics as the
    // RGB MapTable, just on a single-plane buffer.
    public static void ApplyMapTableToCfa(ushort[] samples, int W, int H, OpcodeMapTable p)
    {
        if (p.table.Length == 0) return;
        int last = p.table.Length - 1;
        var (top, left, bottom, right, rowPitch, colPitch) = ClampArea(p, W, H);
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                samples[rowOff + x] = p.table[Math.Clamp((int)samples[rowOff + x], 0, last)];
            }
        });
    }

    // ----- MapPolynomial (opcode 8) ------------------------------------------

    //   out = clamp((c0 + c1·v + c2·v² + … + cN·vᴺ) · 65535)
    // v is the input sample normalised to [0, 1].
    public static void ApplyMapPolynomialToCfa(ushort[] samples, int W, int H, OpcodeMapPolynomial p)
    {
        if (p.coefficients == null || p.coefficients.Length == 0) return;
        var (top, left, bottom, right, rowPitch, colPitch) = ClampArea(p, W, H);
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                double n = samples[rowOff + x] / 65535.0;
                // Horner: c0 + n·(c1 + n·(c2 + …))
                double acc = 0.0;
                for (int i = p.coefficients.Length - 1; i >= 0; i--)
                    acc = acc * n + p.coefficients[i];
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round(acc * 65535.0), 0, 65535);
            }
        });
    }

    // ----- Delta / Scale Per Row / Column (opcodes 10–13) --------------------

    // Add per-row offset (in normalised [0, 1] space) — index = (y - top)/rowPitch.
    public static void ApplyDeltaPerRowToCfa(ushort[] samples, int W, int H, OpcodeDeltaPerRow p)
    {
        if (p.deltas == null || p.deltas.Length == 0) return;
        var (top, left, bottom, right, rowPitch, colPitch) = ClampArea(p, W, H);
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            int index = (y - top) / rowPitch;
            if (index < 0 || index >= p.deltas.Length) return;
            double delta = p.deltas[index];
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                double n = samples[rowOff + x] / 65535.0;
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round((n + delta) * 65535.0), 0, 65535);
            }
        });
    }

    // Add per-column offset (in normalised [0, 1] space) — index = (x - left)/colPitch.
    public static void ApplyDeltaPerColumnToCfa(ushort[] samples, int W, int H, OpcodeDeltaPerColumn p)
    {
        if (p.deltas == null || p.deltas.Length == 0) return;
        var (top, left, bottom, right, rowPitch, colPitch) = ClampArea(p, W, H);
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                int index = (x - left) / colPitch;
                if (index < 0 || index >= p.deltas.Length) continue;
                double n = samples[rowOff + x] / 65535.0;
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round((n + p.deltas[index]) * 65535.0), 0, 65535);
            }
        });
    }

    // Per-row multiplicative gain (raw multiplier, not normalised).
    public static void ApplyScalePerRowToCfa(ushort[] samples, int W, int H, OpcodeScalePerRow p)
    {
        if (p.scales == null || p.scales.Length == 0) return;
        var (top, left, bottom, right, rowPitch, colPitch) = ClampArea(p, W, H);
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            int index = (y - top) / rowPitch;
            if (index < 0 || index >= p.scales.Length) return;
            double scale = p.scales[index];
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round(samples[rowOff + x] * scale), 0, 65535);
            }
        });
    }

    // Per-column multiplicative gain.
    public static void ApplyScalePerColumnToCfa(ushort[] samples, int W, int H, OpcodeScalePerColumn p)
    {
        if (p.scales == null || p.scales.Length == 0) return;
        var (top, left, bottom, right, rowPitch, colPitch) = ClampArea(p, W, H);
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                int index = (x - left) / colPitch;
                if (index < 0 || index >= p.scales.Length) continue;
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round(samples[rowOff + x] * p.scales[index]), 0, 65535);
            }
        });
    }

    // ----- FixBadPixels{Constant,List} (opcodes 4, 5) ------------------------
    //
    // On CFA, a "neighbour" of a given Bayer position is the same-colour pixel
    // *two* steps away — direct 4-connected neighbours are different Bayer
    // colours and would corrupt the green-versus-red/blue separation. So we
    // average four pixels at (±2, 0) and (0, ±2), with edge fallback to the
    // direct 4-connected value when no same-colour neighbour exists.

    static ushort BayerNeighbourAverage(ushort[] src, int W, int H, int x, int y)
    {
        long sum = 0; int n = 0;
        // Same-colour neighbours: skip one in each direction.
        if (x - 2 >= 0)     { sum += src[y * W + (x - 2)]; n++; }
        if (x + 2 < W)      { sum += src[y * W + (x + 2)]; n++; }
        if (y - 2 >= 0)     { sum += src[(y - 2) * W + x]; n++; }
        if (y + 2 < H)      { sum += src[(y + 2) * W + x]; n++; }
        if (n > 0) return (ushort)((sum + n / 2) / n);
        // Falls back to the centre pixel at the corners (degenerate but better
        // than emitting zero).
        return src[y * W + x];
    }

    public static void ApplyFixBadPixelsConstantToCfa(ushort[] samples, int W, int H, OpcodeFixBadPixelsConstant p)
    {
        if (p.constant > 65535) return;
        ushort target = (ushort)p.constant;
        var src = (ushort[])samples.Clone();
        Parallel.For(0, H, y =>
        {
            int rowOff = y * W;
            for (int x = 0; x < W; x++)
            {
                if (src[rowOff + x] == target)
                    samples[rowOff + x] = BayerNeighbourAverage(src, W, H, x, y);
            }
        });
    }

    public static void ApplyFixBadPixelsListToCfa(ushort[] samples, int W, int H, OpcodeFixBadPixelsList p)
    {
        var src = (ushort[])samples.Clone();
        // Single bad points — flat (row, col) pairs.
        for (int i = 0; i + 1 < p.badPoints.Length; i += 2)
        {
            int row = (int)p.badPoints[i];
            int col = (int)p.badPoints[i + 1];
            if ((uint)col < (uint)W && (uint)row < (uint)H)
                samples[row * W + col] = BayerNeighbourAverage(src, W, H, col, row);
        }
        // Bad rectangles — flat (top, left, bottom, right) quads.
        for (int i = 0; i + 3 < p.badRects.Length; i += 4)
        {
            int top = (int)p.badRects[i];
            int left = (int)p.badRects[i + 1];
            int bottom = Math.Min(H, (int)p.badRects[i + 2]);
            int right = Math.Min(W, (int)p.badRects[i + 3]);
            for (int y = Math.Max(0, top); y < bottom; y++)
                for (int x = Math.Max(0, left); x < right; x++)
                    samples[y * W + x] = BayerNeighbourAverage(src, W, H, x, y);
        }
    }
}
