using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// Central hub for the DNG opcode preview pipeline. The Apply dispatcher
// routes a single Opcode to its implementation; per-opcode methods live in
// the Opcodes/ folder alongside doc comments that explain the opcode. This
// file keeps the small things every opcode shares:
//
//   - Apply(buffer, opcode)          dispatch
//   - ApplyGamma / ApplySrgbEncode   gamma stages (called before / after the
//   - ApplySrgbDecode                opcode chain by the pipeline owner)
//
// and the private helpers reused by several opcodes:
//
//   - ApplyArea           region + plane + pitch iteration loop used by
//                          MapTable / MapPolynomial / Delta* / Scale*.
//   - SampleBicubicChannel / CubicWeight   used by both Warp opcodes.
//   - NeighborAverage / FixPixel           used by both FixBadPixels opcodes.
public static partial class OpcodesImplementation
{
    // Dispatches a single opcode to its preview implementation. Both the WPF
    // view-model and the headless CLI go through here so the supported-opcodes
    // set stays in one place.
    public static void Apply(PixelBuffer img, Opcode opcode)
    {
        switch (opcode.header.id)
        {
            case OpcodeId.WarpRectilinear: WarpRectilinear(img, (OpcodeWarpRectilinear)opcode); break;
            case OpcodeId.WarpFisheye: WarpFisheye(img, (OpcodeWarpFisheye)opcode); break;
            case OpcodeId.FixVignetteRadial: FixVignetteRadial(img, (OpcodeFixVignetteRadial)opcode); break;
            case OpcodeId.FixBadPixelsConstant: FixBadPixelsConstant(img, (OpcodeFixBadPixelsConstant)opcode); break;
            case OpcodeId.FixBadPixelsList: FixBadPixelsList(img, (OpcodeFixBadPixelsList)opcode); break;
            case OpcodeId.TrimBounds: TrimBounds(img, (OpcodeTrimBounds)opcode); break;
            case OpcodeId.MapTable: MapTable(img, (OpcodeMapTable)opcode); break;
            case OpcodeId.MapPolynomial: MapPolynomial(img, (OpcodeMapPolynomial)opcode); break;
            case OpcodeId.GainMap: GainMap(img, (OpcodeGainMap)opcode); break;
            case OpcodeId.DeltaPerRow: DeltaPerRow(img, (OpcodeDeltaPerRow)opcode); break;
            case OpcodeId.DeltaPerColumn: DeltaPerColumn(img, (OpcodeDeltaPerColumn)opcode); break;
            case OpcodeId.ScalePerRow: ScalePerRow(img, (OpcodeScalePerRow)opcode); break;
            case OpcodeId.ScalePerColumn: ScalePerColumn(img, (OpcodeScalePerColumn)opcode); break;
            default: Debug.WriteLine($"\t{opcode.header.id} not implemented yet and skipped"); break;
        }
    }
    // Raises every pixel value to the given exponent (in normalized [0,1] space).
    // Used to switch between gamma-encoded and linear representations using a
    // pure power curve.
    public static void ApplyGamma(PixelBuffer img, float exponent)
    {
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
                img.ChangeRgb16Pixel(x, y, pixel => MathF.Pow(pixel / 65535.0f, exponent) * 65535.0f);
        });
    }
    // sRGB OETF (encode linear -> display-ready sRGB) — IEC 61966-2-1. A
    // piecewise function with a small linear segment near 0 and a
    // gamma-2.4 power segment elsewhere; the effective gamma averages to
    // about 2.2 but the curve is shaped correctly in the dark range, which
    // matters for shadows.
    public static void ApplySrgbEncode(PixelBuffer img)
    {
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
                img.ChangeRgb16Pixel(x, y, pixel =>
                {
                    float v = pixel / 65535.0f;
                    float e = v <= 0.0031308f
                        ? 12.92f * v
                        : 1.055f * MathF.Pow(v, 1.0f / 2.4f) - 0.055f;
                    return e * 65535.0f;
                });
        });
    }
    // sRGB EOTF (decode display-encoded sRGB -> linear). Inverse of
    // ApplySrgbEncode.
    public static void ApplySrgbDecode(PixelBuffer img)
    {
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
                img.ChangeRgb16Pixel(x, y, pixel =>
                {
                    float v = pixel / 65535.0f;
                    float l = v <= 0.04045f
                        ? v / 12.92f
                        : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
                    return l * 65535.0f;
                });
        });
    }

    // --- Shared helpers used by per-opcode files in Opcodes/ ---------------

    // Iterates the pixels of an OpcodeArea rectangle (honoring row/column
    // pitch and the plane range) and replaces each affected channel value.
    // Shared by MapTable, MapPolynomial, DeltaPerRow/Column, ScalePerRow/Column.
    static void ApplyArea(PixelBuffer img, OpcodeArea p, Func<int, int, int, UInt16, UInt16> transform)
    {
        int top = (int)Math.Min(p.top, (uint)img.Height);
        int left = (int)Math.Min(p.left, (uint)img.Width);
        int bottom = (int)Math.Min(p.bottom, (uint)img.Height);
        int right = (int)Math.Min(p.right, (uint)img.Width);
        int rowPitch = (int)Math.Max(1, p.rowPitch);
        int colPitch = (int)Math.Max(1, p.colPitch);
        int planeStart = (int)Math.Min(p.plane, 3u);
        int planeEnd = (int)Math.Min(p.plane + p.planes, 3u);
        Parallel.For(top, bottom, (y) =>
        {
            if ((y - top) % rowPitch != 0)
                return;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0)
                    continue;
                var pixel = img.GetRgb16Pixel(x, y);
                for (int plane = planeStart; plane < planeEnd; plane++)
                    pixel[plane] = transform(x, y, plane, pixel[plane]);
            }
        });
    }

    // Catmull–Rom bicubic resampling of one channel at fractional source
    // coordinates. Used by both Warp opcodes.
    static UInt16 SampleBicubicChannel(PixelBuffer img, double x, double y, int channel)
    {
        int ix = (int)Math.Floor(x);
        int iy = (int)Math.Floor(y);
        double sum = 0.0;
        for (int n = -1; n <= 2; n++)
        {
            int sy = Math.Clamp(iy + n, 0, img.Height - 1);
            double wy = CubicWeight(y - (iy + n));
            for (int mi = -1; mi <= 2; mi++)
            {
                int sx = Math.Clamp(ix + mi, 0, img.Width - 1);
                double wx = CubicWeight(x - (ix + mi));
                sum += wx * wy * img.GetRgb16Pixel(sx, sy)[channel];
            }
        }
        return (UInt16)Math.Clamp(Math.Round(sum), 0, 65535);
    }

    // Catmull-Rom cubic convolution kernel (a = -0.5).
    static double CubicWeight(double t)
    {
        const double a = -0.5;
        t = Math.Abs(t);
        if (t <= 1.0)
            return (a + 2.0) * t * t * t - (a + 3.0) * t * t + 1.0;
        if (t < 2.0)
            return a * t * t * t - 5.0 * a * t * t + 8.0 * a * t - 4.0 * a;
        return 0.0;
    }

    // Average of the four axis-aligned neighbours at (x, y) for the given
    // channel, falling back to the centre pixel at corners. Used by the
    // FixBadPixels* opcodes.
    static UInt16 NeighborAverage(PixelBuffer src, int x, int y, int channel)
    {
        int sum = 0, count = 0;
        if (x > 0) { sum += src.GetRgb16Pixel(x - 1, y)[channel]; count++; }
        if (x < src.Width - 1) { sum += src.GetRgb16Pixel(x + 1, y)[channel]; count++; }
        if (y > 0) { sum += src.GetRgb16Pixel(x, y - 1)[channel]; count++; }
        if (y < src.Height - 1) { sum += src.GetRgb16Pixel(x, y + 1)[channel]; count++; }
        // Round-half-up — plain `sum / count` truncates toward zero, biasing
        // every bad-pixel fix slightly dark.
        return count > 0 ? (UInt16)((sum + count / 2) / count) : src.GetRgb16Pixel(x, y)[channel];
    }

    // Replaces a single pixel's three channels with the 4-neighbour average
    // from src. Used by FixBadPixelsList.
    static void FixPixel(PixelBuffer img, PixelBuffer src, int x, int y)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height)
            return;
        var pixel = img.GetRgb16Pixel(x, y);
        for (int channel = 0; channel < 3; channel++)
            pixel[channel] = NeighborAverage(src, x, y, channel);
    }
}
