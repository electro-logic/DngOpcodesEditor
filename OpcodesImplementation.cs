using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

public static class OpcodesImplementation
{
    // Multiplies a specified area and plane range of an image by a gain map
    public static void GainMap(Image img, OpcodeGainMap p)
    {
        var sw = Stopwatch.StartNew();
        if (p.mapGains.Length == 0 || p.mapPointsH == 0 || p.mapPointsV == 0)
            return;
        // Split p.mapGains by p.mapPlanes and transform to a 2D array
        // Ex. float[3072] -> 3x float[32,32]
        int mapPlanes = (int)Math.Max(1, p.mapPlanes);
        var mapGainsPlanes = new float[mapPlanes][,];
        for (int planeIndex = 0; planeIndex < mapPlanes; planeIndex++)
        {
            var planeChannel = p.mapGains.Where((f, i) => i % mapPlanes == planeIndex).ToArray();
            mapGainsPlanes[planeIndex] = MathHelper.ArrayToArray2D(planeChannel, (int)p.mapPointsH);
        }
        int planeEnd = (int)Math.Min(p.plane + p.planes, 3u);
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                // The opcode only affects pixels inside its rectangle.
                if (x >= p.left && x <= p.right && y >= p.top && y <= p.bottom)
                {
                    // Convert x,y into [0,1] image range
                    double xRel = x / (img.Width - 1.0);
                    double yRel = y / (img.Height - 1.0);
                    // Convert from image [0,1]x[0,1] to map [0,mapPointsH]x[0,mapPointsV].
                    // Clamping replicates the gain values from the edge of the map.
                    double xMap = Math.Clamp((xRel - p.mapOriginH) / p.mapSpacingH, 0.0, p.mapPointsH - 1.0);
                    double yMap = Math.Clamp((yRel - p.mapOriginV) / p.mapSpacingV, 0.0, p.mapPointsV - 1.0);
                    var pixel = img.GetRgb16Pixel(x, y);
                    for (int planeIndex = (int)p.plane; planeIndex < planeEnd; planeIndex++)
                    {
                        // Use the last gain map plane if there are fewer map planes than image planes
                        int mapPlane = Math.Min(planeIndex - (int)p.plane, mapGainsPlanes.Length - 1);
                        var gain = MathHelper.BilinearInterpolation(mapGainsPlanes[mapPlane], xMap, yMap);
                        pixel[planeIndex] = (UInt16)Math.Clamp(Math.Round(pixel[planeIndex] * gain), 0, 65535);
                    }
                }
            }
        });
        Debug.WriteLine($"\tGainMap executed in {sw.ElapsedMilliseconds}ms");
    }
    // Trims the image to the rectangle specified by Top, Left, Bottom, and Right
    public static void TrimBounds(Image img, OpcodeTrimBounds p)
    {
        // In this implementation we keep the original size and we only mask trimmed pixels
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                if ((x <= p.left) || (x >= p.right) || (y <= p.top) || (y >= p.bottom))
                {
                    unchecked { img.SetPixel(x, y, 0); }
                }
            }
        });
    }
    // Applies a gain function to an image and can be used to correct vignetting
    public static void FixVignetteRadial(Image img, OpcodeFixVignetteRadial p)
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
    // Applies a warp to an image and can be used to correct geometric distortion and
    // lateral (transverse) chromatic aberration for rectilinear lenses.
    public static void WarpRectilinear(Image img, OpcodeWarpRectilinear p)
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
    // Applies a lookup table to a region and plane range of an image
    public static void MapTable(Image img, OpcodeMapTable p)
    {
        if (p.table.Length == 0)
            return;
        int last = p.table.Length - 1;
        ApplyArea(img, p, (x, y, plane, v) => p.table[Math.Clamp((int)v, 0, last)]);
    }
    // Applies a polynomial mapping to a region and plane range of an image
    public static void MapPolynomial(Image img, OpcodeMapPolynomial p)
    {
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            double n = v / 65535.0;
            double acc = 0.0, power = 1.0;
            for (int i = 0; i < p.coefficients.Length; i++)
            {
                acc += p.coefficients[i] * power;
                power *= n;
            }
            return (UInt16)Math.Clamp(Math.Round(acc * 65535.0), 0, 65535);
        });
    }
    // Adds a per-row delta to a region and plane range of an image
    public static void DeltaPerRow(Image img, OpcodeDeltaPerRow p)
    {
        int top = (int)p.top, rowPitch = (int)Math.Max(1, p.rowPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (y - top) / rowPitch;
            if (index < 0 || index >= p.deltas.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round((v / 65535.0 + p.deltas[index]) * 65535.0), 0, 65535);
        });
    }
    // Adds a per-column delta to a region and plane range of an image
    public static void DeltaPerColumn(Image img, OpcodeDeltaPerColumn p)
    {
        int left = (int)p.left, colPitch = (int)Math.Max(1, p.colPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (x - left) / colPitch;
            if (index < 0 || index >= p.deltas.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round((v / 65535.0 + p.deltas[index]) * 65535.0), 0, 65535);
        });
    }
    // Multiplies each row of a region and plane range by a scale factor
    public static void ScalePerRow(Image img, OpcodeScalePerRow p)
    {
        int top = (int)p.top, rowPitch = (int)Math.Max(1, p.rowPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (y - top) / rowPitch;
            if (index < 0 || index >= p.scales.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round(v * p.scales[index]), 0, 65535);
        });
    }
    // Multiplies each column of a region and plane range by a scale factor
    public static void ScalePerColumn(Image img, OpcodeScalePerColumn p)
    {
        int left = (int)p.left, colPitch = (int)Math.Max(1, p.colPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (x - left) / colPitch;
            if (index < 0 || index >= p.scales.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round(v * p.scales[index]), 0, 65535);
        });
    }
    // Interpolates pixels matching a constant value from their neighbors.
    // FixBadPixels opcodes are designed for raw CFA data; on a demosaiced RGB
    // preview this is approximated by averaging the 4-connected neighbors.
    public static void FixBadPixelsConstant(Image img, OpcodeFixBadPixelsConstant p)
    {
        var src = img.ClonePixels();
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                var pixel = img.GetRgb16Pixel(x, y);
                for (int channel = 0; channel < 3; channel++)
                {
                    if (pixel[channel] == p.constant)
                        pixel[channel] = NeighborAverage(src, x, y, channel);
                }
            }
        });
    }
    // Interpolates a list of bad pixels and rectangles from their neighbors.
    public static void FixBadPixelsList(Image img, OpcodeFixBadPixelsList p)
    {
        var src = img.ClonePixels();
        for (int i = 0; i + 1 < p.badPoints.Length; i += 2)
        {
            FixPixel(img, src, (int)p.badPoints[i + 1], (int)p.badPoints[i]);
        }
        for (int i = 0; i + 3 < p.badRects.Length; i += 4)
        {
            int top = (int)p.badRects[i], left = (int)p.badRects[i + 1];
            int bottom = (int)p.badRects[i + 2], right = (int)p.badRects[i + 3];
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                    FixPixel(img, src, x, y);
        }
    }
    static void FixPixel(Image img, Image src, int x, int y)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height)
            return;
        var pixel = img.GetRgb16Pixel(x, y);
        for (int channel = 0; channel < 3; channel++)
            pixel[channel] = NeighborAverage(src, x, y, channel);
    }
    static UInt16 NeighborAverage(Image src, int x, int y, int channel)
    {
        int sum = 0, count = 0;
        if (x > 0) { sum += src.GetRgb16Pixel(x - 1, y)[channel]; count++; }
        if (x < src.Width - 1) { sum += src.GetRgb16Pixel(x + 1, y)[channel]; count++; }
        if (y > 0) { sum += src.GetRgb16Pixel(x, y - 1)[channel]; count++; }
        if (y < src.Height - 1) { sum += src.GetRgb16Pixel(x, y + 1)[channel]; count++; }
        return count > 0 ? (UInt16)(sum / count) : src.GetRgb16Pixel(x, y)[channel];
    }
    // Iterates the pixels of an OpcodeArea rectangle (honoring row/column pitch
    // and the plane range) and replaces each affected channel value.
    static void ApplyArea(Image img, OpcodeArea p, Func<int, int, int, UInt16, UInt16> transform)
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
    static UInt16 SampleBicubicChannel(Image img, double x, double y, int channel)
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
}
