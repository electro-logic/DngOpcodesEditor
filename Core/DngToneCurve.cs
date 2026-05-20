using System;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// DNG ProfileToneCurve (tag 50940) — a per-channel tone-mapping curve the
// camera manufacturer recommends applying after the colour matrix. Stored in
// the file as a flat float array of (x, y) pairs in [0, 1] that monotonically
// span (0, 0) to (1, 1). DJI Mavic 3 Pro DNGs ship a 256-point shoulder
// curve, for example.
//
// We bake the curve into a 4096-entry 16-bit LUT once (linear interpolation
// between the control points is plenty given the typical point density) and
// then apply it per-channel as a fast lookup over the linear-sRGB buffer
// produced by ColorTransform, before gamma encoding.
public class DngToneCurve
{
    readonly ushort[] _lut;

    DngToneCurve(ushort[] lut) { _lut = lut; }

    public static DngToneCurve FromControlPoints(double[] xyPairs)
    {
        if (xyPairs == null || xyPairs.Length < 4 || (xyPairs.Length & 1) != 0)
            return null;
        var lut = BuildLut(xyPairs);
        return new DngToneCurve(lut);
    }

    // Builds a 4096-entry LUT mapping input/4095 -> output*65535.
    public static ushort[] BuildLut(double[] xy)
    {
        int n = xy.Length / 2;
        var lut = new ushort[4096];
        int k = 0;
        for (int i = 0; i < 4096; i++)
        {
            double x = i / 4095.0;
            // Advance the control-point cursor; the input array is assumed
            // monotonically non-decreasing in x (DNG spec requirement).
            while (k < n - 2 && xy[(k + 1) * 2] < x) k++;
            double x0 = xy[k * 2], y0 = xy[k * 2 + 1];
            double x1 = xy[(k + 1) * 2], y1 = xy[(k + 1) * 2 + 1];
            double y;
            if (x1 <= x0) y = y0;
            else if (x <= x0) y = y0;
            else if (x >= x1) y = y1;
            else y = y0 + (x - x0) / (x1 - x0) * (y1 - y0);
            lut[i] = (ushort)Math.Clamp(Math.Round(y * 65535.0), 0, 65535);
        }
        return lut;
    }

    // Applies the tone curve in place over every channel of every pixel.
    public void Apply(PixelBuffer buffer)
    {
        int W = buffer.Width, H = buffer.Height;
        var lut = _lut;
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                var px = buffer.GetRgb16Pixel(x, y);
                // 16-bit -> 12-bit LUT index (>> 4 keeps the top 12 bits).
                px[0] = lut[px[0] >> 4];
                px[1] = lut[px[1] >> 4];
                px[2] = lut[px[2] >> 4];
            }
        });
    }
}
