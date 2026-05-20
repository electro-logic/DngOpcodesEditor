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
}
