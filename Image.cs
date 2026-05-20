using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DngOpcodesEditor;

// WPF-specific Image: a PixelBuffer with display-side WPF bitmap, file IO and
// the gamma-correct Update used to publish the buffer to the UI.
public partial class Image : PixelBuffer
{
    // Internal 16-bit-per-channel surface (also used by the TIFF save path).
    WriteableBitmap _bmpRgba64;
    // 8-bit-per-channel display surface produced from _pixels with TPDF
    // dither, so the 16->8 quantisation noise stays incoherent instead of
    // banding smooth gradients.
    WriteableBitmap _bmpDisplay;
    [ObservableProperty]
    BitmapSource _bmp;
    // Turn dither off for screenshot-comparable / deterministic output.
    public static bool DitherDisplay { get; set; } = true;
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
        AllocateBitmaps();
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
        AllocateBitmaps();
        Update();
    }
    void AllocateBitmaps()
    {
        _bmpRgba64 = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Rgba64, null);
        _bmpDisplay = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgr32, null);
    }
    public void Update()
    {
        // Keep the 16-bit-per-channel surface up to date — the TIFF saver and
        // any future "save as 16-bit" path reads from it.
        _bmpRgba64.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * sizeof(UInt64), 0);
        // Produce the 8-bit display surface with TPDF dither so banding in
        // smooth gradients turns into ±1 LSB of imperceptible noise.
        var bgr = new byte[_height * _width * 4];
        bool dither = DitherDisplay;
        Parallel.For(0, _height, y =>
        {
            // Per-row seeded RNG keeps the noise pattern deterministic for a
            // given image (so consecutive Update() calls don't visibly shimmer)
            // while still decorrelating across rows.
            var rng = new Random(unchecked((int)(y * 0x9E3779B9u + 1u)));
            int rowBase = y * _width * 4;
            for (int x = 0; x < _width; x++)
            {
                ulong p = _pixels[x + y * _width];
                int r16 = (int)(p & 0xFFFF);
                int g16 = (int)((p >> 16) & 0xFFFF);
                int b16 = (int)((p >> 32) & 0xFFFF);
                if (dither)
                {
                    // TPDF noise in [-256, 256], centred at 0 — exactly ±1 of
                    // the 8-bit LSB once we shift down by 8.
                    int n = rng.Next(0, 257) + rng.Next(0, 257) - 256;
                    r16 += n; g16 += n; b16 += n;
                }
                byte r8 = (byte)Math.Clamp(r16 >> 8, 0, 255);
                byte g8 = (byte)Math.Clamp(g16 >> 8, 0, 255);
                byte b8 = (byte)Math.Clamp(b16 >> 8, 0, 255);
                int i = rowBase + x * 4;
                bgr[i + 0] = b8;
                bgr[i + 1] = g8;
                bgr[i + 2] = r8;
                bgr[i + 3] = 0;
            }
        });
        _bmpDisplay.WritePixels(new Int32Rect(0, 0, _width, _height), bgr, _width * 4, 0);
        Bmp = _bmpDisplay;
    }
    public Image Clone()
    {
        var clone = new Image();
        clone._pixels = (UInt64[])_pixels.Clone();
        clone._width = _width;
        clone._height = _height;
        clone._bmpRgba64 = _bmpRgba64.Clone();
        clone._bmpDisplay = _bmpDisplay.Clone();
        // Propagate the displayed bitmap to the observable property so anything
        // bound to ImgSrc.Bmp / ImgDst.Bmp paints immediately on a clone — without
        // this, RebuildWorkingImage's small-image path (`ImgSrc = original.Clone()`)
        // would leave the WPF preview blank until something later triggers Update().
        clone.Bmp = clone._bmpDisplay;
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
