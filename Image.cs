using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DngOpcodesEditor;

public partial class Image : ObservableObject
{
    const int INTERNAL_BPP = 8; // Rgba64 = 64 bit (8 Bytes per pixel)
    byte[] _pixels;
    int _width, _height;
    public int Width => _width;
    public int Height => _height;
    WriteableBitmap _bmpRgba64;
    [ObservableProperty]
    BitmapSource _bmp;
    public int Open(string filename)
    {
        var decoder = BitmapDecoder.Create(new Uri(filename, UriKind.Relative), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        // Convert to 16 bit/channel internal format
        var bmp = new FormatConvertedBitmap(frame, PixelFormats.Rgba64, null, 0);
        _width = bmp.PixelWidth;
        _height = bmp.PixelHeight;
        // Copy to a managed byte[]
        _pixels = new byte[_width * _height * INTERNAL_BPP];
        bmp.CopyPixels(_pixels, _width * INTERNAL_BPP, 0);
        _bmpRgba64 = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Rgba64, null);
        Update();
        return frame.Format.BitsPerPixel;
    }
    public void Update()
    {
        //  Convert the internal Rgba64 format to Bgr24 (WPF format)
        _bmpRgba64.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * sizeof(UInt64), 0);
        Bmp = new FormatConvertedBitmap(_bmpRgba64, PixelFormats.Bgr24, null, 0);
    }
    public UInt64 GetPixel(int x, int y) => BitConverter.ToUInt64(_pixels, (x + y * _width) * INTERNAL_BPP);
    public void SetPixel(int x, int y, UInt64 value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, _pixels, (x + y * _width) * INTERNAL_BPP, INTERNAL_BPP);
    public void SetPixels(UInt64[] pixels) => Buffer.BlockCopy(pixels, 0, _pixels, 0, pixels.Length * sizeof(UInt64));
    public Image Clone()
    {
        var clone = new Image();
        clone._pixels = (byte[])_pixels.Clone();
        clone._width = _width;
        clone._height = _height;
        clone._bmpRgba64 = _bmpRgba64.Clone();
        return clone;
    }
    public UInt64 this[int x, int y] => GetPixel(x, y);
    public void SaveImage(string filename)
    {
        using (var stream = new FileStream(filename, FileMode.Create))
        {
            var encoder = new TiffBitmapEncoder() { Compression = TiffCompressOption.Lzw };
            BitmapFrame bmpFrame = BitmapFrame.Create(_bmpRgba64, null, null, null);
            encoder.Frames.Add(bmpFrame);
            encoder.Save(stream);
        }
    }
    // Save a float image for debugging purposes only
    public static void SaveFloatImage(float[] floatImage, int w, int h, string filename)
    {
        // Scale values from [Min,Max] to [0,255]
        var min = floatImage.Min();
        var max = floatImage.Max();
        var gray16 = floatImage.Select(f => (UInt16)Math.Round((f - min) / max * 65535.0f)).ToArray();
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray16, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), gray16, w, 0);
        using (var stream = new FileStream(filename, FileMode.Create))
        {
            var encoder = new TiffBitmapEncoder() { Compression = TiffCompressOption.Lzw };
            BitmapFrame bmpFrame = BitmapFrame.Create(bmp, null, null, null);
            encoder.Frames.Add(bmpFrame);
            encoder.Save(stream);
        }
    }
    public UInt16[] GetRgb16Pixel(int x, int y)
    {
        var pixel = GetPixel(x, y);
        var r = (UInt16)(pixel & 0xFFFF);
        var g = (UInt16)((pixel >> 16) & 0xFFFF);
        var b = (UInt16)((pixel >> 32) & 0xFFFF);
        var a = (UInt16)((pixel >> 48) & 0xFFFF);
        return new UInt16[] { r, g, b };
    }
    public void SetRgb16Pixel(int x, int y, UInt16 r, UInt16 g, UInt16 b, UInt16 a = 65535) => SetPixel(x, y, (UInt64)r | ((UInt64)g << 16) | ((UInt64)b << 32) | ((UInt64)a << 48));
    public void ChangeRgb16Pixel(int x, int y, Func<UInt16, float> f)
    {
        var pixel = GetRgb16Pixel(x, y);
        var r = (UInt16)Math.Clamp(MathF.Round(f(pixel[0])), 0.0f, 65535.0f);
        var g = (UInt16)Math.Clamp(MathF.Round(f(pixel[1])), 0.0f, 65535.0f);
        var b = (UInt16)Math.Clamp(MathF.Round(f(pixel[2])), 0.0f, 65535.0f);
        SetRgb16Pixel(x, y, r, g, b);
    }
}