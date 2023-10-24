using System.Windows;

namespace DngOpcodesEditor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //cbOpcodesIDs.ItemsSource = Enum.GetValues(typeof(OpcodeId));
            ViewModel.ImportBin(@"Samples\FixVignetteRadial.bin");
            ViewModel.ImportBin(@"Samples\WarpRectilinear.bin");
            ViewModel.OpenImage(@"Samples\grid.tiff");
            ViewModel.ApplyOpcodes();
        }
        void btnImportDNG_Click(object sender, RoutedEventArgs e) => ViewModel.ImportDng();
        void btnImportBin_Click(object sender, RoutedEventArgs e) => ViewModel.ImportBin();
        void btnOpenImage_Click(object sender, RoutedEventArgs e) => ViewModel.OpenImage();
        void btnApplyOpcodes_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyOpcodes();
        void btnDeleteOpcode_Click(object sender, RoutedEventArgs e) => ViewModel.Opcodes.Remove(ViewModel.SelectedOpcode);
        void btnExportBin_Click(object sender, RoutedEventArgs e) => ViewModel.ExportBin();
        void btnExportDNG_Click(object sender, RoutedEventArgs e) => ViewModel.ExportDNG();
        void DataGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e) => ViewModel.ApplyOpcodes();
        void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ViewModel.ApplyOpcodes();
        /*
void btnMoveUp_Click(object sender, RoutedEventArgs e) { }
void btnMoveDown_Click(object sender, RoutedEventArgs e) { }
void btnAddOpcode_Click(object sender, RoutedEventArgs e) => ViewModel.AddOpcode((OpcodeId)cbOpcodesIDs.SelectedValue);
*/
    }
}