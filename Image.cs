using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DngOpcodesEditor
{
    public class Image
    {
        public Int32[] _pixels;
        int _width, _height;
        public WriteableBitmap Bmp { get; set; }
        public int Width => _width;
        public int Height => _height;
        private Image() { }
        public Image(string filename)
        {
            var decoder = BitmapDecoder.Create(new Uri(filename, UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var bmp = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            _width = bmp.PixelWidth;
            _height = bmp.PixelHeight;
            _pixels = new Int32[_width * _height];
            bmp.CopyPixels(_pixels, _width * 4, 0);
            Bmp = new WriteableBitmap(bmp);
        }
        public void Update() => Bmp.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
        public Int32 GetPixel(int x, int y) => _pixels[x + y * _width];
        public byte[] GetPixelRGB8(int x, int y)
        {
            var pixel = GetPixel(x, y);
            byte b = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte r = (byte)((pixel >> 16) & 0xFF);
            byte a = (byte)((pixel >> 24) & 0xFF);
            return new byte[] { r, g, b};
        }
        public void SetPixel(int x, int y, Int32 value) => _pixels[x + y * _width] = value;
        public void SetPixelRGB8(int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            Int32 value = b | (g << 8) | (r << 16) | (a << 24);
            SetPixel(x, y, value);
        }
        public Image Clone()
        {
            var clone = new Image();
            clone._pixels = (int[])_pixels.Clone();
            clone._width = _width;
            clone._height = _height;
            clone.Bmp = Bmp.Clone();
            return clone;
        }
        public Int32 this[int x, int y]
        {
            get { return GetPixel(x, y); }
            set { SetPixel(x, y, value); }
        }
        public void SaveImage(string filename)
        {
            using (var stream = new FileStream(filename, FileMode.Create))
            {
                var encoder = new TiffBitmapEncoder() { Compression = TiffCompressOption.Lzw };
                BitmapFrame bmpFrame = BitmapFrame.Create(Bmp, null, null, null);
                encoder.Frames.Add(bmpFrame);
                encoder.Save(stream);
            }
        }
    }
}