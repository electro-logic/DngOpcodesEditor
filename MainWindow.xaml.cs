using System.Diagnostics;
using System;
using System.Windows;
using System.IO;

namespace DngOpcodesEditor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //cbOpcodesIDs.ItemsSource = Enum.GetValues(typeof(OpcodeId));
            ViewModel.OpenImage(@"Samples\grid.tiff");
            ViewModel.ImportBin(@"Samples\FixVignetteRadial.bin");
            ViewModel.ImportBin(@"Samples\WarpRectilinear.bin");            
            //ViewModel.ImportBin(@"Samples\GainMap.bin");
            //ViewModel.ImportBin(@"Samples\TrimsBound.bin");
            ViewModel.ApplyOpcodes();
        }
        void btnImportDNG_Click(object sender, RoutedEventArgs e) { ViewModel.ImportDng(); ViewModel.ApplyOpcodes(); }
        void btnImportBin_Click(object sender, RoutedEventArgs e) { ViewModel.ImportBin(); ViewModel.ApplyOpcodes(); }
        void btnOpenImage_Click(object sender, RoutedEventArgs e) { ViewModel.OpenImage(); ViewModel.ApplyOpcodes(); }
        void btnApplyOpcodes_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyOpcodes();
        void btnDeleteOpcode_Click(object sender, RoutedEventArgs e) { ViewModel.Opcodes.Remove(ViewModel.SelectedOpcode); ViewModel.ApplyOpcodes(); }
        void btnExportBin_Click(object sender, RoutedEventArgs e) => ViewModel.ExportBin();
        void btnExportDNG_Click(object sender, RoutedEventArgs e) => ViewModel.ExportDNG();
        void btnSaveImage_Click(object sender, RoutedEventArgs e) => ViewModel.SaveImage();
        void btnClear_Click(object sender, RoutedEventArgs e) { ViewModel.Opcodes.Clear(); ViewModel.ApplyOpcodes(); }
        void Image_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (ViewModel.ImgSrc == null)
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
                tbInfo.Text = $"{src[0].ToString("D3")} {src[1].ToString("D3")} {src[2].ToString("D3")} - {dst[0].ToString("D3")} {dst[1].ToString("D3")} {dst[2].ToString("D3")}";
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
                    ViewModel.ApplyOpcodes();
                }
            }
        }
        void CheckBox_Checked(object sender, RoutedEventArgs e) => ViewModel.ApplyOpcodes();
        void CheckBox_Unchecked(object sender, RoutedEventArgs e) => ViewModel.ApplyOpcodes();
        /*
        void btnMoveUp_Click(object sender, RoutedEventArgs e) { }
        void btnMoveDown_Click(object sender, RoutedEventArgs e) { }
        void btnAddOpcode_Click(object sender, RoutedEventArgs e) => ViewModel.AddOpcode((OpcodeId)cbOpcodesIDs.SelectedValue);
        */
    }
}