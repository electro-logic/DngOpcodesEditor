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
            // Default reference image + two demo opcodes — generated entirely
            // in code so the first-launch UX doesn't depend on any external
            // file. Previously the constructor loaded `Samples\grid.tiff` and
            // two `.bin` payloads via *relative* paths, which only worked when
            // the process CWD happened to be the build-output directory.
            ViewModel.LoadReferenceBuffer(BuildReferenceGrid(640, 480, 32), "Reference Grid");
            AddDemoOpcodes();
            _ = ViewModel.ApplyOpcodes();
        }

        // 640x480 white grid on black lines every `cellSize` pixels. Cheap to
        // make and gives every opcode something visibly structured to act on.
        static PixelBuffer BuildReferenceGrid(int width, int height, int cellSize)
        {
            var pixels = new ulong[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool onLine = x % cellSize == 0 || y % cellSize == 0;
                    ushort v = onLine ? (ushort)0 : (ushort)65535;
                    pixels[x + y * width] = v | ((ulong)v << 16) | ((ulong)v << 32) | (65535UL << 48);
                }
            }
            return new PixelBuffer(pixels, width, height);
        }

        // Matches the parameters of the original `Samples/*.bin` files so the
        // visual demo at startup is unchanged: a vignette gain that doubles
        // the corners (k0 = 1.0) plus a mild Brown–Conrady rectilinear warp.
        // dngVersion / flags fall back to the OpcodeHeader defaults
        // (DNG_VERSION_1_3_0_0 / OptionalPreview) — fine for synthesised
        // opcodes.
        void AddDemoOpcodes()
        {
            var vignette = new OpcodeFixVignetteRadial { k0 = 1.0, cx = 0.5, cy = 0.5 };
            vignette.header.id = OpcodeId.FixVignetteRadial;
            vignette.ListIndex = 2;
            ViewModel.Opcodes.Add(vignette);

            var warp = new OpcodeWarpRectilinear
            {
                planes = 1,
                coefficients = new double[] { 0.75, 0.25, 0, 0, 0, 0 },
                cx = 0.5,
                cy = 0.5,
            };
            warp.header.id = OpcodeId.WarpRectilinear;
            warp.ListIndex = 3;
            ViewModel.Opcodes.Add(warp);
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
