using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DngOpcodesEditor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ViewModel.OpenImage(@"Samples\grid.tiff");
            ViewModel.ImportBin(@"Samples\FixVignetteRadial.bin");
            ViewModel.ImportBin(@"Samples\WarpRectilinear.bin");
            _ = ViewModel.ApplyOpcodes();
        }
        void Image_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (ViewModel.ImgSrc == null || ViewModel.ImgDst == null)
                return;

            try
            {
                var image = sender as System.Windows.Controls.Image;
                var position = e.GetPosition(image);
                // Uniform stretching only
                var scaleRatio = image.Source.Width / image.ActualWidth;
                int x = (int)Math.Floor(position.X * scaleRatio);
                int y = (int)Math.Floor(position.Y * scaleRatio);
                tbPosition.Text = $"X: {x} Y: {y}";
                var src = ViewModel.ImgSrc.GetRgb16Pixel(x, y);
                var dst = ViewModel.ImgDst.GetRgb16Pixel(x, y);
                tbInfo.Text = $"{src[0]:D3} {src[1]:D3} {src[2]:D3} - {dst[0]:D3} {dst[1]:D3} {dst[2]:D3}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        void Image_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => tbInfo.Text = string.Empty;
        void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    ViewModel.Opcodes.Clear();
                    foreach (string file in files)
                    {
                        switch (Path.GetExtension(file).ToLower())
                        {
                            case ".bin":
                                ViewModel.ImportBin(file);
                                break;
                            case ".dng":
                                ViewModel.ImportDng(file);
                                break;
                            default:
                                ViewModel.OpenImage(file);
                                break;
                        }
                    }
                    _ = ViewModel.ApplyOpcodes();
                }
            }
        }
    }
}
