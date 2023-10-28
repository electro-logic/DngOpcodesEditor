using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace DngOpcodesEditor
{
    public static class OpcodesImplementation
    {
        // Convert a row-major 1d array to a 2d array
        static float[,] ArrayToArray2D(float[] array, int width)
        {
            int height = array.Length / width;
            var newArray = new float[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    newArray[x, y] = array[x + y * width];
                }
            }
            return newArray;
        }
        static double BilinearInterpolation(float[,] array, double x, double y)
        {
            int x1 = (int)x;
            int y1 = (int)y;
            int x2 = Math.Min(array.GetLength(0) - 1, x1 + 1);
            int y2 = Math.Min(array.GetLength(1) - 1, y1 + 1);

            if (x2 == x1)
                x1--;
            if (y2 == y1)
                y1--;

            float q11 = array[x1, y1];
            float q21 = array[x2, y1];
            float q12 = array[x1, y2];
            float q22 = array[x2, y2];
            double wd = (x2 - x1) * (y2 - y1);
            double w11 = (x2 - x) * (y2 - y) / wd;
            double w12 = (x2 - x) * (y - y1) / wd;
            double w21 = (x - x1) * (y2 - y) / wd;
            double w22 = (x - x1) * (y - y1) / wd;
            double result = w11 * q11 + w12 * q12 + w21 * q21 + w22 * q22;
            return result;
        }
        public static void GainMap(Image img, OpcodeGainMap p)
        {
            var sw = Stopwatch.StartNew();

            // Split p.mapGains by p.planes and transform to a 2D array
            // Ex. float[3072] - > 3x float[32,32]
            var mapGainsPlanes = new float[p.planes][,];
            for (int planeIndex = 0; planeIndex < p.planes; planeIndex++)
            {
                mapGainsPlanes[planeIndex] = ArrayToArray2D(p.mapGains.Where((f, i) => i % p.planes == planeIndex).ToArray(), (int)p.mapPointsH);
            }
            Parallel.For(0, img.Height, (y) =>
            //for (int y = 0; y < img.Height; y++)
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
                        var pixel = img.GetPixelRGB8(x, y);
                        for (int planeIndex = 0; planeIndex < 3; planeIndex++)
                        {
                            // use the last gain map if planes > mapPlanes
                            var gain = BilinearInterpolation(mapGainsPlanes[Math.Min(planeIndex, mapGainsPlanes.Length - 1)], xMap, yMap);
                            var black = 0;
                            pixel[planeIndex] = (byte)Math.Clamp(Math.Round((pixel[planeIndex] - black) * gain + black), 0, 255);
                        }
                        img.SetPixelRGB8(x, y, pixel[0], pixel[1], pixel[2]);
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
        public static void TrimBounds(Image img, OpcodeTrimBounds p)
        {
            // In this implementation we keep the original size and we only mask trimmed pixels
            Parallel.For(0, img.Height, (y) =>
            {
                for (int x = 0; x < img.Width; x++)
                {
                    if ((x <= p.left) || (x >= p.right) || (y <= p.top) || (y >= p.bottom))
                    {
                        unchecked { img[x, y] = (Int32)0xFF000000; }
                    }
                }
            });
        }
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
                    var pixel = img[x, y];
                    // Unpack / Pack BGRA32
                    byte pixel_b = (byte)(pixel & 0xFF);
                    byte pixel_g = (byte)((pixel >> 8) & 0xFF);
                    byte pixel_r = (byte)((pixel >> 16) & 0xFF);
                    byte pixel_a = (byte)((pixel >> 24) & 0xFF);
                    pixel_b = (byte)Math.Clamp(Math.Round(pixel_b * g), 0, 255);
                    pixel_g = (byte)Math.Clamp(Math.Round(pixel_g * g), 0, 255);
                    pixel_r = (byte)Math.Clamp(Math.Round(pixel_r * g), 0, 255);
                    img[x, y] = pixel_b | (pixel_g << 8) | (pixel_r << 16) | (pixel_a << 24);
                }
            });
            Debug.WriteLine($"\tFixVignetteRadial executed in {sw.ElapsedMilliseconds}ms");
        }
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
            Int32[] newImg = new Int32[img.Width * img.Height];
            for (int newImgIndex = 0; newImgIndex < img.Width * img.Height; newImgIndex++)
            {
                unchecked { newImg[newImgIndex] = (Int32)0xFF000000; }
            }
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
            img._pixels = newImg;
            Debug.WriteLine($"\tWarpRectilinear executed in {sw.ElapsedMilliseconds}ms");
        }
    }
}