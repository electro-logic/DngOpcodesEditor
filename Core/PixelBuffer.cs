using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Runtime.CompilerServices;

namespace DngOpcodesEditor;

// Platform-agnostic 16-bit RGBA pixel buffer.
//
// The buffer is stored as packed UInt64 values (R | G<<16 | B<<32 | A<<48) to
// match the WPF Rgba64 pixel format, but the class itself depends on nothing
// platform-specific so it can be shared between WPF and Avalonia front-ends.
public partial class PixelBuffer : ObservableObject
{
    protected const int INTERNAL_BPP = 8; // Rgba64 = 64 bit (8 bytes per pixel)
    protected UInt64[] _pixels;
    protected int _width, _height;
    public int Width => _width;
    public int Height => _height;
    public UInt64[] Pixels => _pixels;
    public PixelBuffer() { }
    public PixelBuffer(UInt64[] pixels, int width, int height)
    {
        _pixels = pixels;
        _width = width;
        _height = height;
    }
    public ref UInt64 GetPixel(int x, int y) => ref _pixels[x + y * _width];
    public void SetPixel(int x, int y, UInt64 value) => _pixels[x + y * _width] = value;
    public void SetPixels(UInt64[] pixels) => _pixels = pixels;
    public ref UInt64 this[int x, int y] => ref GetPixel(x, y);
    // Returns a span over the R, G, B components of the pixel. Writing through
    // the span writes back to the underlying pixel buffer in place.
    public unsafe Span<UInt16> GetRgb16Pixel(int x, int y) => new Span<UInt16>(Unsafe.AsPointer(ref GetPixel(x, y)), 3);
    public void SetRgb16Pixel(int x, int y, UInt16 r, UInt16 g, UInt16 b, UInt16 a = 65535)
        => SetPixel(x, y, (UInt64)r | ((UInt64)g << 16) | ((UInt64)b << 32) | ((UInt64)a << 48));
    public void ChangeRgb16Pixel(int x, int y, Func<UInt16, float> f)
    {
        var pixel = GetRgb16Pixel(x, y);
        pixel[0] = (UInt16)Math.Clamp(MathF.Round(f(pixel[0])), 0.0f, 65535.0f);
        pixel[1] = (UInt16)Math.Clamp(MathF.Round(f(pixel[1])), 0.0f, 65535.0f);
        pixel[2] = (UInt16)Math.Clamp(MathF.Round(f(pixel[2])), 0.0f, 65535.0f);
    }
    // Clones only the pixel buffer; safe to call from any thread.
    public PixelBuffer ClonePixels() => new PixelBuffer
    {
        _pixels = (UInt64[])_pixels.Clone(),
        _width = _width,
        _height = _height
    };

    // Reorients the buffer according to a TIFF/EXIF Orientation value (1..8).
    // Returns this buffer (cloned) unchanged for orientation 1 or unknown values.
    //
    // EXIF orientation codes:
    //   1 = identity, 2 = mirror horizontal, 3 = rotate 180,
    //   4 = mirror vertical, 5 = transpose, 6 = rotate 90 CW,
    //   7 = transverse, 8 = rotate 270 CW (= 90 CCW)
    public PixelBuffer ApplyOrientation(int orientation)
    {
        if (orientation <= 1 || orientation > 8)
            return ClonePixels();
        bool swapDims = orientation >= 5;
        int newW = swapDims ? _height : _width;
        int newH = swapDims ? _width : _height;
        var newPixels = new UInt64[newW * newH];
        int W = _width, H = _height;
        System.Threading.Tasks.Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int nx, ny;
                switch (orientation)
                {
                    case 2: nx = W - 1 - x; ny = y; break;
                    case 3: nx = W - 1 - x; ny = H - 1 - y; break;
                    case 4: nx = x; ny = H - 1 - y; break;
                    case 5: nx = y; ny = x; break;
                    case 6: nx = H - 1 - y; ny = x; break;
                    case 7: nx = H - 1 - y; ny = W - 1 - x; break;
                    case 8: nx = y; ny = W - 1 - x; break;
                    default: nx = x; ny = y; break;
                }
                newPixels[nx + ny * newW] = _pixels[x + y * W];
            }
        });
        return new PixelBuffer(newPixels, newW, newH);
    }

    // Box-filter downsample so the longest side fits within maxWidth/maxHeight.
    // Returns this buffer (cloned) unchanged if it's already small enough.
    public PixelBuffer Resize(int maxWidth, int maxHeight)
    {
        double scale = Math.Min((double)maxWidth / _width, (double)maxHeight / _height);
        if (scale >= 1.0)
            return ClonePixels();
        int newW = Math.Max(1, (int)(_width * scale));
        int newH = Math.Max(1, (int)(_height * scale));
        var newPixels = new UInt64[newW * newH];
        // Per-axis scale factor (source pixels per destination pixel).
        double sxStep = (double)_width / newW;
        double syStep = (double)_height / newH;
        System.Threading.Tasks.Parallel.For(0, newH, y =>
        {
            int sy0 = (int)(y * syStep);
            int sy1 = Math.Min((int)((y + 1) * syStep + 0.5), _height);
            if (sy1 <= sy0) sy1 = sy0 + 1;
            for (int x = 0; x < newW; x++)
            {
                int sx0 = (int)(x * sxStep);
                int sx1 = Math.Min((int)((x + 1) * sxStep + 0.5), _width);
                if (sx1 <= sx0) sx1 = sx0 + 1;
                long sumR = 0, sumG = 0, sumB = 0;
                int count = 0;
                for (int yy = sy0; yy < sy1; yy++)
                {
                    for (int xx = sx0; xx < sx1; xx++)
                    {
                        var px = GetRgb16Pixel(xx, yy);
                        sumR += px[0]; sumG += px[1]; sumB += px[2];
                        count++;
                    }
                }
                // Round-half-up integer division — plain truncation here drops
                // up to 1 LSB per output pixel and accumulates a perceptible
                // dimming bias on smooth gradients.
                int half = count / 2;
                ushort r = (ushort)((sumR + half) / count);
                ushort g = (ushort)((sumG + half) / count);
                ushort b = (ushort)((sumB + half) / count);
                newPixels[x + y * newW] = r | ((UInt64)g << 16) | ((UInt64)b << 32) | (65535UL << 48);
            }
        });
        return new PixelBuffer(newPixels, newW, newH);
    }
}
