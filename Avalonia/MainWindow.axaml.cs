using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.IO;

namespace DngOpcodesEditor.Avalonia;

public partial class MainWindow : Window
{
    MainWindowVM ViewModel => (MainWindowVM)DataContext;
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        DragDrop.SetAllowDrop(this, true);
        Opened += (_, _) =>
        {
            ViewModel.Owner = this;
            // Same auto-load as the WPF version when bundled sample assets exist.
            var samplesDir = Path.Combine(System.AppContext.BaseDirectory, "Samples");
            var grid = Path.Combine(samplesDir, "grid.tiff");
            if (File.Exists(grid))
            {
                ViewModel.OpenImage(grid);
                var vignette = Path.Combine(samplesDir, "FixVignetteRadial.bin");
                var warp = Path.Combine(samplesDir, "WarpRectilinear.bin");
                if (File.Exists(vignette)) ViewModel.ImportBin(vignette);
                if (File.Exists(warp)) ViewModel.ImportBin(warp);
                _ = ViewModel.ApplyOpcodes();
            }
        };
    }
    void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
        if (files == null) return;
        ViewModel.Opcodes.Clear();
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path == null) continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".bin": ViewModel.ImportBin(path); break;
                case ".dng": ViewModel.ImportDng(path); break;
                default: ViewModel.OpenImage(path); break;
            }
        }
        _ = ViewModel.ApplyOpcodes();
    }
}
