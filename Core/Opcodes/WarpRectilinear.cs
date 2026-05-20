using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// WarpRectilinear (DNG opcode 1)
// =============================================================================
//
// Corrects geometric distortion and lateral (transverse) chromatic aberration
// for rectilinear lenses. The model is the classic Brown–Conrady distortion:
//
//   Δxr = f(r) · Δx
//   Δyr = f(r) · Δy
//   Δxt = t0 · (2·Δx·Δy)        + t1 · (r² + 2·Δx²)
//   Δyt = t1 · (2·Δx·Δy)        + t0 · (r² + 2·Δy²)
//
//   f(r) = r0 + r1·r² + r2·r⁴ + r3·r⁶
//
// where (Δx, Δy) are the normalised offsets from the optical centre (cx, cy)
// and r is their Euclidean length. The source pixel for the output position
// (x, y) sits at (cx + m·(Δxr+Δxt), cy + m·(Δyr+Δyt)) — i.e. we backwards-map
// from destination to source and resample with bicubic interpolation.
//
// Parameters (OpcodeWarpRectilinear)
//   planes        1 means the same set of coefficients applies to every
//                 channel; >1 means each channel has its own 6 coefficients
//                 (used for chromatic-aberration correction).
//   coefficients  flat double[planes * 6] of (r0, r1, r2, r3, t0, t1).
//   cx, cy        optical centre, normalised to the image diagonal in [0, 1].
//
// Pipeline position
//   Typically OpcodeList3 (applied after demosaic). Operates on camera-native
//   RGB, before the colour transform.
//
// Implementation notes
//   Catmull–Rom (a = -0.5) bicubic resampling; edges clamp to the image
//   bounds so the warp never reads outside the source.

public static partial class OpcodesImplementation
{
    public static void WarpRectilinear(PixelBuffer img, OpcodeWarpRectilinear p)
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
                    // A single plane of coefficients is shared by every channel,
                    // otherwise each channel is warped with its own coefficients.
                    int coeffPlane = planes == 1 ? 0 : Math.Min(channel, planes - 1);
                    int b = coeffPlane * 6;
                    double r0 = p.coefficients[b + 0], r1 = p.coefficients[b + 1];
                    double r2c = p.coefficients[b + 2], r3 = p.coefficients[b + 3];
                    double t0 = p.coefficients[b + 4], t1 = p.coefficients[b + 5];
                    double dx = (x - cx) / m;
                    double dy = (y - cy) / m;
                    double r2 = dx * dx + dy * dy;
                    double f = r0 + r1 * r2 + r2c * r2 * r2 + r3 * r2 * r2 * r2;
                    double dxr = f * dx;
                    double dyr = f * dy;
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
        Debug.WriteLine($"\tWarpRectilinear executed in {sw.ElapsedMilliseconds}ms");
    }
}
