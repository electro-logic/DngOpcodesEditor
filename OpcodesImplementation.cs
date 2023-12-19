using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DngOpcodesEditor;

public static class OpcodesImplementation
{
    // Multiplies a specified area and plane range of an image by a gain map
    public static void GainMap(Image img, OpcodeGainMap p)
    {
        var sw = Stopwatch.StartNew();
        // Split p.mapGains by p.planes and transform to a 2D array
        // Ex. float[3072] - > 3x float[32,32]
        var mapGainsPlanes = new float[p.planes][,];
        for (int planeIndex = 0; planeIndex < p.planes; planeIndex++)
        {
            var planeChannel = p.mapGains.Where((f, i) => i % p.planes == planeIndex).ToArray();
            //Image.SaveFloatImage(planeChannel, (int)p.mapPointsH, (int)p.mapPointsV, $"gainMap{planeIndex}.tiff"); 
            mapGainsPlanes[planeIndex] = MathHelper.ArrayToArray2D(planeChannel, (int)p.mapPointsH);
        }
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                // Inside the gain map values are interpolated using the bilinear interpolation
                if ((x >= p.left) && (x <= p.right) && (y >= p.top) || (y <= p.bottom))
                {
                    // Convert x,y into [0,1] image range
                    double xRel = x / (img.Width - 1.0);
                    double yRel = y / (img.Height - 1.0);
                    // convert from image [0,1]x[0,1] to array [0,mapPointsH]x[0,mapPointsV]
                    double xMap = Math.Min((xRel - p.mapOriginH) / p.mapSpacingH, p.mapPointsH - 1.0);
                    double yMap = Math.Min((yRel - p.mapOriginV) / p.mapSpacingV, p.mapPointsV - 1.0);
                    var pixel = img.GetRgb16Pixel(x, y);
                    // apply the gain from plane p.plane
                    for (uint planeIndex = p.plane; planeIndex < 3; planeIndex++)
                    {
                        // use the last gain map if p.planes > p.mapPlanes
                        var gain = MathHelper.BilinearInterpolation(mapGainsPlanes[Math.Min(planeIndex, mapGainsPlanes.Length - 1)], xMap, yMap);
                        pixel[planeIndex] = (UInt16)Math.Clamp(Math.Round(pixel[planeIndex] * gain), 0, 65535);
                    }
                    img.SetRgb16Pixel(x, y, pixel[0], pixel[1], pixel[2]);
                }
                else
                {
                    // Outside the gain map, values are replicated from the edge of the map
                    // TODO
                }
            }
        });
        img.Update();
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
        double k0 = p.k0;
        double k1 = p.k1;
        double k2 = p.k2;
        double k3 = p.k3;
        double k4 = p.k4;
        double ncx = p.cx;
        double ncy = p.cy;
        int x0 = 0; int y0 = 0;
        int x1 = img.Width - 1; int y1 = img.Height - 1;
        double cx = x0 + ncx * (x1 - x0);
        double cy = y0 + ncy * (y1 - y0);
        double mx = Math.Max(Math.Abs(x0 - cx), Math.Abs(x1 - cx));
        double my = Math.Max(Math.Abs(y0 - cy), Math.Abs(y1 - cy));
        double m = Math.Sqrt(mx * mx + my * my);
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                double r = Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2)) / m;
                double g = 1.0 + k0 * Math.Pow(r, 2) + k1 * Math.Pow(r, 4) + k2 * Math.Pow(r, 6) + k3 * Math.Pow(r, 8) + k4 * Math.Pow(r, 10);
                img.ChangeRgb16Pixel(x, y, pixel => (float)(pixel * g));
            }
        });
        Debug.WriteLine($"\tFixVignetteRadial executed in {sw.ElapsedMilliseconds}ms");
    }
    // Applies a warp to an image and can be used to correct geometric distortion and
    // lateral (transverse) chromatic aberration for rectilinear lenses.
    // The warp function supports both radial and tangential distortion correction.
    public static void WarpRectilinear(Image img, OpcodeWarpRectilinear p)
    {
        // TODO: resampling kernel (ex. cubic spline)
        var sw = Stopwatch.StartNew();
        if (p.planes != 1)
        {
            Debug.WriteLine("Multiple planes support not implemented yet");
            return;
        }
        double r0 = p.coefficients[0];
        double r1 = p.coefficients[1];
        double r2 = p.coefficients[2];
        double r3 = p.coefficients[3];
        double t0 = p.coefficients[4];
        double t1 = p.coefficients[5];
        double ncx = p.cx;
        double ncy = p.cy;
        int x0 = 0; int y0 = 0;
        int x1 = img.Width - 1; int y1 = img.Height - 1;
        double cx = x0 + ncx * (x1 - x0);
        double cy = y0 + ncy * (y1 - y0);
        double mx = Math.Max(Math.Abs(x0 - cx), Math.Abs(x1 - cx));
        double my = Math.Max(Math.Abs(y0 - cy), Math.Abs(y1 - cy));
        double m = Math.Sqrt(mx * mx + my * my);
        // Create a new black image to copy pixels in new positions
        var newImg = new UInt64[img.Width * img.Height];
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                double deltaX = (x - cx) / m;
                double deltaY = (y - cy) / m;
                double r = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                double f = r0 + r1 * Math.Pow(r, 2) + r2 * Math.Pow(r, 4) + r3 * Math.Pow(r, 6);
                double deltaXr = f * deltaX;
                double deltaYr = f * deltaY;
                double deltaXt = t0 * (2.0 * deltaX * deltaY) + t1 * (r * r + 2.0 * deltaX * deltaX);
                double deltaYt = t1 * (2.0 * deltaX * deltaY) + t0 * (r * r + 2.0 * deltaY * deltaY);
                int xSrc = (int)Math.Round(cx + m * (deltaXr + deltaXt));
                int ySrc = (int)Math.Round(cy + m * (deltaYr + deltaYt));
                if ((xSrc >= 0) && (ySrc >= 0) && (xSrc < img.Width) && (ySrc < img.Height))
                {
                    newImg[x + y * img.Width] = img[xSrc, ySrc];
                }
            }
        });
        img.SetPixels(newImg);
        Debug.WriteLine($"\tWarpRectilinear executed in {sw.ElapsedMilliseconds}ms");
    }
}