using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DngOpcodesEditor;

// WPF-specific Image: a PixelBuffer with display-side WPF bitmap, file IO and
// the gamma-correct Update used to publish the buffer to the UI.
public partial class Image : PixelBuffer
{
    WriteableBitmap _bmpRgba64;
    [ObservableProperty]
    BitmapSource _bmp;
    public int Open(string filename)
    {
        // Resolve to an absolute path: file dialogs return absolute paths, which
        // are invalid when wrapped as a relative Uri.
        var decoder = BitmapDecoder.Create(new Uri(Path.GetFullPath(filename)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        // Convert to 16 bit/channel internal format
        var bmp = new FormatConvertedBitmap(frame, PixelFormats.Rgba64, null, 0);
        _width = bmp.PixelWidth;
        _height = bmp.PixelHeight;
        var tmp = new byte[_width * _height * INTERNAL_BPP];
        bmp.CopyPixels(tmp, _width * INTERNAL_BPP, 0);
        _pixels = UnsafeArray.CastArray<byte, UInt64>(tmp);
        _bmpRgba64 = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Rgba64, null);
        Update();
        return frame.Format.BitsPerPixel;
    }
    // Initializes the WPF bitmap surface from an already-populated pixel buffer
    // (for example one returned by DngRawReader after demosaicing).
    public void LoadFromBuffer(PixelBuffer buffer)
    {
        _pixels = buffer.Pixels;
        _width = buffer.Width;
        _height = buffer.Height;
        _bmpRgba64 = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Rgba64, null);
        Update();
    }
    public void Update()
    {
        // Convert the internal Rgba64 format to Bgr24 (WPF format)
        _bmpRgba64.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * sizeof(UInt64), 0);
        Bmp = new FormatConvertedBitmap(_bmpRgba64, PixelFormats.Bgr24, null, 0);
    }
    public Image Clone()
    {
        var clone = new Image();
        clone._pixels = (UInt64[])_pixels.Clone();
        clone._width = _width;
        clone._height = _height;
        clone._bmpRgba64 = _bmpRgba64.Clone();
        return clone;
    }
    public void SaveImage(string filename)
    {
        using var stream = new FileStream(filename, FileMode.Create);
        var encoder = new TiffBitmapEncoder() { Compression = TiffCompressOption.Lzw };
        encoder.Frames.Add(BitmapFrame.Create(_bmpRgba64, null, null, null));
        encoder.Save(stream);
    }
    // Save a float image for debugging purposes only
    public static void SaveFloatImage(float[] floatImage, int w, int h, string filename)
    {
        var min = floatImage.Min();
        var max = floatImage.Max();
        var gray16 = floatImage.Select(f => (UInt16)Math.Round((f - min) / max * 65535.0f)).ToArray();
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray16, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), gray16, w, 0);
        using var stream = new FileStream(filename, FileMode.Create);
        var encoder = new TiffBitmapEncoder() { Compression = TiffCompressOption.Lzw };
        encoder.Frames.Add(BitmapFrame.Create(bmp, null, null, null));
        encoder.Save(stream);
    }
}
