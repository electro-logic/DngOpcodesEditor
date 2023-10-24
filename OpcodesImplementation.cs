using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DngOpcodesEditor
{
    public static class OpcodesImplementation
    {
        public static void FixVignetteRadial(Image img, OpcodeFixVignetteRadial parameters)
        {
            double k0 = parameters.k0;
            double k1 = parameters.k1;
            double k2 = parameters.k2;
            double k3 = parameters.k3;
            double k4 = parameters.k4;
            double ncx = parameters.cx;
            double ncy = parameters.cy;
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
        }
        public static void WarpRectilinear(Image img, OpcodeWarpRectilinear parameters)
        {
            if (parameters.planes != 1)
            {
                Debug.WriteLine("Multiple planes support not implemented");
                return;
            }
            double r0 = parameters.coefficients[0];
            double r1 = parameters.coefficients[1];
            double r2 = parameters.coefficients[2];
            double r3 = parameters.coefficients[3];
            double t0 = parameters.coefficients[4];
            double t1 = parameters.coefficients[5];
            double ncx = parameters.cx;
            double ncy = parameters.cy;            
            int x0 = 0; int y0 = 0;
            int x1 = img.Width - 1; int y1 = img.Height - 1;
            double cx = x0 + ncx * (x1 - x0);
            double cy = y0 + ncy * (y1 - y0);
            double mx = Math.Max(Math.Abs(x0 - cx), Math.Abs(x1 - cx));
            double my = Math.Max(Math.Abs(y0 - cy), Math.Abs(y1 - cy));
            double m = Math.Sqrt(mx * mx + my * my);
            Int32[] newImg = new Int32[img.Width * img.Height];
            for (int newImgIndex = 0; newImgIndex < img.Width * img.Height; newImgIndex++)
            {
                unchecked { newImg[newImgIndex] = (Int32)0xFF000000; }
            }
            // TODO: resampling kernel (ex. cubic spline)
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
        }
    }
}