using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// FixVignetteRadial (DNG opcode 3)
// =============================================================================
//
// Compensates for radial light fall-off (the dark corners produced by most
// lenses at wide apertures). The gain at distance r from the optical centre
// is a degree-10 even polynomial in r:
//
//   g(r) = 1 + k0·r² + k1·r⁴ + k2·r⁶ + k3·r⁸ + k4·r¹⁰
//
// Each pixel is multiplied by g(r) for its position; values clamp at 65535.
//
// Parameters (OpcodeFixVignetteRadial)
//   k0..k4   polynomial coefficients (5 doubles).
//   cx, cy   optical centre, normalised to [0, 1].
//
// Pipeline position
//   Typically OpcodeList2 or OpcodeList3.

public static partial class OpcodesImplementation
{
    public static void FixVignetteRadial(PixelBuffer img, OpcodeFixVignetteRadial p)
    {
        var sw = Stopwatch.StartNew();
        double k0 = p.k0, k1 = p.k1, k2 = p.k2, k3 = p.k3, k4 = p.k4;
        int x1 = img.Width - 1, y1 = img.Height - 1;
        double cx = p.cx * x1;
        double cy = p.cy * y1;
        double mx = Math.Max(Math.Abs(cx), Math.Abs(x1 - cx));
        double my = Math.Max(Math.Abs(cy), Math.Abs(y1 - cy));
        double m = Math.Sqrt(mx * mx + my * my);
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                double dx = x - cx, dy = y - cy;
                double r2 = (dx * dx + dy * dy) / (m * m);
                double r4 = r2 * r2;
                double g = 1.0 + k0 * r2 + k1 * r4 + k2 * r4 * r2 + k3 * r4 * r4 + k4 * r4 * r4 * r2;
                img.ChangeRgb16Pixel(x, y, pixel => (float)(pixel * g));
            }
        });
        Debug.WriteLine($"\tFixVignetteRadial executed in {sw.ElapsedMilliseconds}ms");
    }
}
