using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace DngOpcodesEditor.Avalonia;

// Avalonia equivalent of the WPF Image class: pairs the Core PixelBuffer
// with an Avalonia WriteableBitmap for display. The 16-bit linear math is
// kept identical between back-ends; only the on-screen surface differs.
public partial class AvaloniaImage : PixelBuffer
{
    WriteableBitmap _displayBitmap;
    [ObservableProperty]
    Bitmap _displayed;

    public int Open(string filename)
    {
        // DNG: route through the built-in raw reader so a developed TIFF isn't required.
        if (Path.GetExtension(filename).Equals(".dng", StringComparison.OrdinalIgnoreCase))
        {
            var buffer = DngRawReader.Read(File.ReadAllBytes(filename));
            LoadFromBuffer(buffer);
            return 48; // 16-bit per channel
        }
        // Other formats go through Avalonia's bitmap decoder, which returns
        // 8-bit-per-channel pixel data. We promote it to the internal Rgba64
        // representation so the opcode math is unchanged.
        var bmp = new Bitmap(filename);
        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;
        int stride = w * 4;
        var bgra = new byte[h * stride];
        unsafe
        {
            fixed (byte* p = bgra)
            {
                bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, bgra.Length, stride);
            }
        }
        var pixels = new UInt64[w * h];
        for (int i = 0; i < w * h; i++)
        {
            // Bgra8888 -> Rgba64. 8->16-bit promotion uses *257 to keep 0 and 255
            // mapped exactly to 0 and 65535.
            byte b = bgra[i * 4 + 0];
            byte g = bgra[i * 4 + 1];
            byte r = bgra[i * 4 + 2];
            byte a = bgra[i * 4 + 3];
            ushort r16 = (ushort)(r * 257);
            ushort g16 = (ushort)(g * 257);
            ushort b16 = (ushort)(b * 257);
            ushort a16 = (ushort)(a * 257);
            pixels[i] = r16 | ((UInt64)g16 << 16) | ((UInt64)b16 << 32) | ((UInt64)a16 << 48);
        }
        _pixels = pixels;
        _width = w;
        _height = h;
        _displayBitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Displayed = _displayBitmap;
        Update();
        return 32;
    }

    public void LoadFromBuffer(PixelBuffer buffer)
    {
        _pixels = buffer.Pixels;
        _width = buffer.Width;
        _height = buffer.Height;
        _displayBitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Displayed = _displayBitmap;
        Update();
    }

    public void Update()
    {
        // The internal pixel buffer is updated in place; copy the high byte of
        // each 16-bit channel into the on-screen BGRA8 framebuffer.
        using (var fb = _displayBitmap.Lock())
        {
            unsafe
            {
                byte* dst = (byte*)fb.Address;
                int stride = fb.RowBytes;
                for (int y = 0; y < _height; y++)
                {
                    byte* row = dst + y * stride;
                    for (int x = 0; x < _width; x++)
                    {
                        UInt64 p = _pixels[y * _width + x];
                        byte r = (byte)((p >> 8) & 0xFF);
                        byte g = (byte)((p >> 24) & 0xFF);
                        byte b = (byte)((p >> 40) & 0xFF);
                        row[x * 4 + 0] = b;
                        row[x * 4 + 1] = g;
                        row[x * 4 + 2] = r;
                        row[x * 4 + 3] = 255;
                    }
                }
            }
        }
        // Force the bound Image control to redraw by re-raising PropertyChanged
        // even though the bitmap instance is unchanged.
        OnPropertyChanged(nameof(Displayed));
    }

    public AvaloniaImage Clone()
    {
        var clone = new AvaloniaImage();
        clone._pixels = (UInt64[])_pixels.Clone();
        clone._width = _width;
        clone._height = _height;
        clone._displayBitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        clone.Displayed = clone._displayBitmap;
        clone.Update();
        return clone;
    }

    // PNG save: full TIFF/16-bit save requires SkiaSharp or ImageSharp.
    public void SaveImage(string filename)
    {
        using var stream = File.Create(filename);
        _displayBitmap.Save(stream);
    }
}
