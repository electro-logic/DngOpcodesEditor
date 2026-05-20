using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace DngOpcodesEditor;

public partial class MainWindowVM : ObservableObject
{
    [ObservableProperty]
    ObservableCollection<Opcode> _opcodes = new ObservableCollection<Opcode>();
    [ObservableProperty]
    Opcode _selectedOpcode;
    [ObservableProperty]
    Image _imgSrc, _imgDst;
    [ObservableProperty]
    bool _encodeGamma, _decodeGamma;
    [ObservableProperty]
    OpcodeId _selectedOpcodeId = OpcodeId.FixVignetteRadial;
    [ObservableProperty]
    System.Collections.Generic.List<DngMetadata.Entry> _metadata = new();
    public OpcodeId[] OpcodeIds { get; } = Enum.GetValues<OpcodeId>();
    readonly string SAMPLES_DIR = Path.Combine(AppContext.BaseDirectory, "Samples");
    // Re-entrancy guard: rapid edits (ex. dragging a slider) coalesce into a
    // single trailing run instead of stacking concurrent passes.
    bool _applyRunning, _applyPending;
    public MainWindowVM()
    {
        EncodeGamma = true;
        SetWindowTitle();
        _opcodes.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach (Opcode item in e.NewItems)
                {
                    item.PropertyChanged += (ps, pe) => _ = ApplyOpcodes();
                }
            }
            if (!Opcodes.Contains(SelectedOpcode))
            {
                SelectedOpcode = Opcodes.LastOrDefault();
            }
        };
    }
    public void SetWindowTitle(string filename = "")
    {
        var title = $"DNG Opcodes Editor v{Assembly.GetExecutingAssembly().GetName().Version}";
        if (!string.IsNullOrWhiteSpace(filename))
        {
            title += $" - {Path.GetFileName(filename)}";
        }
        App.Current.MainWindow.Title = title;
    }
    [RelayCommand]
    void OpenImage()
    {
        var dialog = new OpenFileDialog() { Filter = "All files (*.*)|*.*", InitialDirectory = SAMPLES_DIR };
        if (dialog.ShowDialog() == true)
        {
            OpenImage(dialog.FileName);
            _ = ApplyOpcodes();
        }
    }
    public void OpenImage(string filename)
    {
        try
        {
            var img = new Image();
            // DNG files are routed through the built-in CFA reader so users no
            // longer need to develop the file with dcraw_emu first. Other
            // formats (TIFF, PNG, ...) go through WPF's BitmapDecoder.
            if (Path.GetExtension(filename).Equals(".dng", StringComparison.OrdinalIgnoreCase))
            {
                var buffer = DngRawReader.Read(File.ReadAllBytes(filename));
                img.LoadFromBuffer(buffer);
                // Raw CFA data is linear; no input-gamma decode required.
                DecodeGamma = false;
            }
            else
            {
                int bpp = img.Open(filename);
                DecodeGamma = bpp <= 32;
            }
            ImgSrc = img;
            ImgDst = ImgSrc.Clone();
            Metadata = ReadFileMetadata(filename);
            SetWindowTitle(filename);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open image:\n{filename}\n\n{ex.Message}", "Open Image",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    static System.Collections.Generic.List<DngMetadata.Entry> ReadFileMetadata(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext != ".tiff" && ext != ".tif" && ext != ".dng")
            return new System.Collections.Generic.List<DngMetadata.Entry>();
        try { return DngMetadata.Read(File.ReadAllBytes(filename)); }
        catch { return new System.Collections.Generic.List<DngMetadata.Entry>(); }
    }
    [RelayCommand]
    void SaveImage()
    {
        if (ImgDst == null)
            return;
        var dialog = new SaveFileDialog()
        {
            Filter = "Tiff image (*.tiff)|*.tiff",
            FileName = "processed_" + DateTime.Now.ToString("HHmmss") + ".tiff"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                ImgDst.SaveImage(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save image:\n{ex.Message}", "Save Image",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    public void AddOpcode(OpcodeId id)
    {
        Opcode op = id switch
        {
            OpcodeId.WarpRectilinear => new OpcodeWarpRectilinear { planes = 1, coefficients = new double[] { 1, 0, 0, 0, 0, 0 } },
            OpcodeId.WarpFisheye => new OpcodeWarpFisheye { planes = 1, coefficients = new double[4] },
            OpcodeId.FixVignetteRadial => new OpcodeFixVignetteRadial(),
            OpcodeId.FixBadPixelsConstant => new OpcodeFixBadPixelsConstant(),
            OpcodeId.FixBadPixelsList => new OpcodeFixBadPixelsList(),
            OpcodeId.TrimBounds => new OpcodeTrimBounds(),
            OpcodeId.MapTable => new OpcodeMapTable(),
            OpcodeId.MapPolynomial => new OpcodeMapPolynomial(),
            OpcodeId.GainMap => new OpcodeGainMap(),
            OpcodeId.DeltaPerRow => new OpcodeDeltaPerRow(),
            OpcodeId.DeltaPerColumn => new OpcodeDeltaPerColumn(),
            OpcodeId.ScalePerRow => new OpcodeScalePerRow(),
            OpcodeId.ScalePerColumn => new OpcodeScalePerColumn(),
            _ => new Opcode()
        };
        op.header.id = id;
        // A freshly added region opcode defaults to covering the whole image
        // (the apply step clamps bottom/right to the image bounds).
        if (op is OpcodeArea area and not OpcodeGainMap)
        {
            area.bottom = uint.MaxValue;
            area.right = uint.MaxValue;
        }
        Opcodes.Add(op);
        SelectedOpcode = op;
    }
    [RelayCommand]
    async Task AddSelectedOpcode()
    {
        AddOpcode(SelectedOpcodeId);
        await ApplyOpcodes();
    }
    [RelayCommand]
    async Task ImportDng()
    {
        var dialog = new OpenFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*", InitialDirectory = SAMPLES_DIR };
        if (dialog.ShowDialog() == true)
        {
            ImportDng(dialog.FileName);
            await ApplyOpcodes();
        }
    }
    public void ImportDng(string filename)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var tiff = File.ReadAllBytes(filename);
            bool found = false;
            // Import OpcodeList1, OpcodeList2 and OpcodeList3 from any IFD.
            for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                var bytes = TiffFile.ReadOpcodeList(tiff, listIndex);
                if (bytes != null && bytes.Length > 0)
                {
                    found = true;
                    foreach (Opcode opcode in ImportBin(bytes))
                    {
                        opcode.ListIndex = listIndex;
                        Opcodes.Add(opcode);
                    }
                }
            }
            if (found)
                SelectedOpcode = Opcodes.LastOrDefault();
            else
                MessageBox.Show("No opcodes were found in the selected DNG file.", "Import DNG",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to import opcodes from DNG:\n{ex.Message}", "Import DNG",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    [RelayCommand]
    async Task ImportBin()
    {
        var dialog = new OpenFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*", InitialDirectory = SAMPLES_DIR };
        if (dialog.ShowDialog() == true)
        {
            ImportBin(dialog.FileName);
            await ApplyOpcodes();
        }
    }
    public void ImportBin(string filename)
    {
        try
        {
            foreach (var opcode in ImportBin(File.ReadAllBytes(filename)))
            {
                Opcodes.Add(opcode);
            }
            SelectedOpcode = Opcodes.LastOrDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to import opcodes:\n{ex.Message}", "Import Binary",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    [RelayCommand]
    void ExportBin()
    {
        var dialog = new SaveFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllBytes(dialog.FileName, new OpcodesWriter().WriteOpcodeList(Opcodes));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to export opcodes:\n{ex.Message}", "Export Binary",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    [RelayCommand]
    void ExportDng()
    {
        // OpcodeList1: applied as read directly from the file
        // OpcodeList2: applied after mapping to linear reference values
        // OpcodeList3: applied after demosaicing
        var dialog = new SaveFileDialog()
        {
            Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*",
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var tiff = File.ReadAllBytes(dialog.FileName);
            // Each OpcodeList tag is written from the opcodes that were tagged
            // with the matching ListIndex.
            for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                var listOpcodes = Opcodes.Where(o => o.ListIndex == listIndex).ToArray();
                if (listOpcodes.Length == 0)
                    continue;
                var payload = new OpcodesWriter().WriteOpcodeList(listOpcodes);
                tiff = TiffFile.WriteOpcodeList(tiff, listIndex, payload);
            }
            File.WriteAllBytes(dialog.FileName, tiff);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to export opcodes to DNG:\n{ex.Message}", "Export DNG",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    [RelayCommand]
    async Task DeleteOpcode()
    {
        if (SelectedOpcode != null)
        {
            Opcodes.Remove(SelectedOpcode);
            await ApplyOpcodes();
        }
    }
    [RelayCommand]
    async Task Clear()
    {
        Opcodes.Clear();
        await ApplyOpcodes();
    }
    Opcode[] ImportBin(byte[] binaryData) => new OpcodesReader().ReadOpcodeList(binaryData);
    partial void OnDecodeGammaChanged(bool value) => _ = ApplyOpcodes();
    partial void OnEncodeGammaChanged(bool value) => _ = ApplyOpcodes();
    [RelayCommand]
    public async Task ApplyOpcodes()
    {
        if (ImgSrc == null)
            return;
        // Coalesce bursts of triggers into one trailing run.
        if (_applyRunning)
        {
            _applyPending = true;
            return;
        }
        _applyRunning = true;
        try
        {
            do
            {
                _applyPending = false;
                Mouse.OverrideCursor = Cursors.Wait;
                var dst = ImgSrc.Clone();
                var ops = Opcodes.Where(o => o.Enabled).ToArray();
                bool decode = DecodeGamma, encode = EncodeGamma;
                string error = null;
                var sw = Stopwatch.StartNew();
                // The pixel work touches only the managed pixel buffer, so it
                // runs off the UI thread; the WPF bitmap is refreshed afterwards.
                await Task.Run(() =>
                {
                    try
                    {
                        // Opcodes operate on linear values, before gamma encoding
                        if (decode)
                            OpcodesImplementation.ApplyGamma(dst, 2.2f);
                        foreach (var op in ops)
                            OpcodesImplementation.Apply(dst, op);
                        if (encode)
                            OpcodesImplementation.ApplyGamma(dst, 1.0f / 2.2f);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        Debug.WriteLine(ex);
                    }
                });
                dst.Update();
                ImgDst = dst;
                Debug.WriteLine($"ApplyOpcodes executed in {sw.ElapsedMilliseconds}ms");
                if (error != null)
                {
                    MessageBox.Show($"Error while applying opcodes:\n{error}", "Apply Opcodes",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            while (_applyPending);
        }
        finally
        {
            _applyRunning = false;
            Mouse.OverrideCursor = null;
        }
    }
}
