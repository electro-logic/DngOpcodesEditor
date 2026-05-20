using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// WarpFisheye (DNG opcode 2)
// =============================================================================
//
// Fisheye distortion correction. The radial polynomial operates on the *angle*
// from the optical axis (atan(r)) instead of the radius itself — this is what
// makes the model fisheye-shaped rather than rectilinear:
//
//   t        = atan(r)                              // incoming angle
//   newAngle = kr0 + kr1·t + kr2·t² + kr3·t³        // re-mapped angle
//   newR     = tan(newAngle)                        // outgoing radius
//
// The destination pixel is sampled from the source at the new radius along the
// original direction. With kr = [0, 1, 0, 0] the warp is the identity.
//
// Parameters (OpcodeWarpFisheye)
//   planes        1 = shared coefficients; >1 = per-channel.
//   coefficients  flat double[planes * 4] of (kr0, kr1, kr2, kr3).
//   cx, cy        optical centre, normalised to [0, 1].
//
// Pipeline position
//   Typically OpcodeList3.
//
// Implementation notes
//   Catmull–Rom bicubic resampling, clamped at image edges. r ≈ 0 short-circuits
//   to (cx, cy) to avoid the 0/0 in tan(atan(r))/r at the centre.

public static partial class OpcodesImplementation
{
    public static void WarpFisheye(PixelBuffer img, OpcodeWarpFisheye p)
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
                    int b = coeffPlane * 4;
                    double kr0 = p.coefficients[b + 0];
                    double kr1 = p.coefficients[b + 1];
                    double kr2 = p.coefficients[b + 2];
                    double kr3 = p.coefficients[b + 3];
                    double dx = (x - cx) / m;
                    double dy = (y - cy) / m;
                    double r = Math.Sqrt(dx * dx + dy * dy);
                    double xSrc, ySrc;
                    if (r < 1e-12)
                    {
                        xSrc = cx;
                        ySrc = cy;
                    }
                    else
                    {
                        double t = Math.Atan(r);
                        double newt = kr0 + kr1 * t + kr2 * t * t + kr3 * t * t * t;
                        double newr = Math.Tan(newt);
                        double ratio = newr / r;
                        xSrc = cx + m * dx * ratio;
                        ySrc = cy + m * dy * ratio;
                    }
                    outPx[channel] = SampleBicubicChannel(img, xSrc, ySrc, channel);
                }
                newImg[x + y * w] = outPx[0] | ((UInt64)outPx[1] << 16) | ((UInt64)outPx[2] << 32) | ((UInt64)65535 << 48);
            }
        });
        img.SetPixels(newImg);
        Debug.WriteLine($"\tWarpFisheye executed in {sw.ElapsedMilliseconds}ms");
    }
}
