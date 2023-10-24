using System;
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
        public void SetPixel(int x, int y, Int32 value) => _pixels[x + y * _width] = value;
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
    }
}