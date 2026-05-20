using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// WarpRectilinear2 (DNG opcode 14, DNG 1.6.0.0+)
// =============================================================================
//
// Extension of WarpRectilinear (opcode 1). Same Brown–Conrady distortion
// model, but the radial polynomial now goes all the way to order 14 *and*
// includes the odd powers (the original was even-only up to r⁶). Adds an
// optional valid-radius clamp + a reciprocal-radial mode for matching the
// FOV of another lens.
//
//   Δxr = ratio(r) · Δx
//   Δyr = ratio(r) · Δy
//   Δxt = t0 · (2·Δx·Δy)        + t1 · (r² + 2·Δx²)
//   Δyt = t1 · (2·Δx·Δy)        + t0 · (r² + 2·Δy²)
//
//   poly(x) = k0 + k1·x + k2·x² + … + k14·x¹⁴
//   ratio(r) = poly(clamp(r,    min_valid_radius, max_valid_radius))   useReciprocal = false
//   ratio(r) = poly(clamp(1/r,  min_valid_radius, max_valid_radius))   useReciprocal = true
//
// (Δx, Δy) are the normalised offsets from (cx, cy); r is their length;
// (x_src, y_src) = (cx + m·(Δxr + Δxt), cy + m·(Δyr + Δyt)). Backwards-map
// then bicubic resample, identical to WarpRectilinear.
//
// Parameters (OpcodeWarpRectilinear2)
//   planes                   1 = shared coefficients for every channel;
//                            >1 = per-channel (lateral CA correction).
//   radialCoefficients       double[planes × 15] — k0..k14 per plane.
//   tangentialCoefficients   double[planes × 2]  — t0, t1 per plane.
//   validRadiusRange         double[planes × 2]  — (min, max) per plane.
//   cx, cy                   optical centre, normalised to [0, 1].
//   useReciprocal            radial argument is 1/r instead of r.
//
// Pipeline position
//   Typically OpcodeList3, after demosaic.

public static partial class OpcodesImplementation
{
    public static void WarpRectilinear2(PixelBuffer img, OpcodeWarpRectilinear2 p)
    {
        var sw = Stopwatch.StartNew();
        int w = img.Width, h = img.Height;
        double cx = p.cx * (w - 1);
        double cy = p.cy * (h - 1);
        double mx = Math.Max(Math.Abs(cx), Math.Abs(w - 1 - cx));
        double my = Math.Max(Math.Abs(cy), Math.Abs(h - 1 - cy));
        double m = Math.Sqrt(mx * mx + my * my);
        int planes = (int)Math.Max(1, p.planes);
        var newImg = new UInt64[w * h];
        Parallel.For(0, h, (y) =>
        {
            Span<UInt16> outPx = stackalloc UInt16[3];
            for (int x = 0; x < w; x++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int coeffPlane = planes == 1 ? 0 : Math.Min(channel, planes - 1);
                    int radBase = coeffPlane * OpcodeWarpRectilinear2.RadialTermsPerPlane;
                    int tanBase = coeffPlane * 2;
                    int rngBase = coeffPlane * 2;
                    double t0 = p.tangentialCoefficients[tanBase + 0];
                    double t1 = p.tangentialCoefficients[tanBase + 1];
                    double minR = p.validRadiusRange[rngBase + 0];
                    double maxR = p.validRadiusRange[rngBase + 1];

                    double dx = (x - cx) / m;
                    double dy = (y - cy) / m;
                    double r2 = dx * dx + dy * dy;
                    double r = Math.Sqrt(r2);

                    // Choose r or 1/r, then clamp to [min, max].
                    double polyArg = r;
                    if (p.useReciprocal)
                        polyArg = r > 1e-12 ? 1.0 / r : maxR;
                    polyArg = Math.Clamp(polyArg, minR, maxR);

                    // Horner evaluation of k0 + k1·x + k2·x² + … + k14·x¹⁴.
                    double ratio = 0.0;
                    for (int k = OpcodeWarpRectilinear2.RadialTermsPerPlane - 1; k >= 0; k--)
                        ratio = ratio * polyArg + p.radialCoefficients[radBase + k];

                    double dxr = ratio * dx;
                    double dyr = ratio * dy;
                    double dxt = t0 * (2.0 * dx * dy) + t1 * (r2 + 2.0 * dx * dx);
                    double dyt = t1 * (2.0 * dx * dy) + t0 * (r2 + 2.0 * dy * dy);
                    double xSrc = cx + m * (dxr + dxt);
                    double ySrc = cy + m * (dyr + dyt);
                    outPx[channel] = SampleBicubicChannel(img, xSrc, ySrc, channel);
                }
                newImg[x + y * w] = outPx[0] | ((UInt64)outPx[1] << 16) | ((UInt64)outPx[2] << 32) | ((UInt64)65535 << 48);
            }
        });
        img.SetPixels(newImg);
        Debug.WriteLine($"\tWarpRectilinear2 executed in {sw.ElapsedMilliseconds}ms");
    }
}
