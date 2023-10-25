using System.Windows;

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
        /*
void btnMoveUp_Click(object sender, RoutedEventArgs e) { }
void btnMoveDown_Click(object sender, RoutedEventArgs e) { }
void btnAddOpcode_Click(object sender, RoutedEventArgs e) => ViewModel.AddOpcode((OpcodeId)cbOpcodesIDs.SelectedValue);
*/
    }
}