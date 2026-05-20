using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DngOpcodesEditor.Avalonia;

// View-model for the Avalonia front-end. Mirrors the WPF MainWindowVM but
// uses Avalonia's async IStorageProvider for file dialogs and Avalonia's
// Cursor type for the wait indicator. The opcode pipeline (ApplyOpcodes,
// gamma encode/decode, per-opcode dispatch) is identical to the WPF version
// because it operates on the shared Core PixelBuffer.
public partial class MainWindowVM : ObservableObject
{
    [ObservableProperty]
    ObservableCollection<Opcode> _opcodes = new ObservableCollection<Opcode>();
    [ObservableProperty]
    Opcode _selectedOpcode;
    [ObservableProperty]
    AvaloniaImage _imgSrc, _imgDst;
    [ObservableProperty]
    bool _encodeGamma, _decodeGamma;
    [ObservableProperty]
    OpcodeId _selectedOpcodeId = OpcodeId.FixVignetteRadial;
    public OpcodeId[] OpcodeIds { get; } = Enum.GetValues<OpcodeId>();
    public Window Owner { get; set; }
    readonly string SAMPLES_DIR = Path.Combine(AppContext.BaseDirectory, "Samples");
    bool _applyRunning, _applyPending;
    public MainWindowVM()
    {
        EncodeGamma = true;
        _opcodes.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach (Opcode item in e.NewItems)
                    item.PropertyChanged += (ps, pe) => _ = ApplyOpcodes();
            }
            if (!Opcodes.Contains(SelectedOpcode))
                SelectedOpcode = Opcodes.LastOrDefault();
        };
    }
    [RelayCommand]
    async Task OpenImage()
    {
        var path = await PickOpenFileAsync("Open image", AllFilesFilter());
        if (path != null)
        {
            OpenImage(path);
            await ApplyOpcodes();
        }
    }
    public void OpenImage(string filename)
    {
        try
        {
            var img = new AvaloniaImage();
            int bpp = img.Open(filename);
            ImgSrc = img;
            DecodeGamma = bpp <= 32;
            ImgDst = ImgSrc.Clone();
            Owner.Title = $"DNG Opcodes Editor (Avalonia) - {Path.GetFileName(filename)}";
        }
        catch (Exception ex)
        {
            ShowError("Open Image", $"Unable to open image:\n{filename}\n\n{ex.Message}");
        }
    }
    [RelayCommand]
    async Task SaveImage()
    {
        if (ImgDst == null)
            return;
        var path = await PickSaveFileAsync("Save preview image",
            new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } },
            "preview_" + DateTime.Now.ToString("HHmmss") + ".png");
        if (path == null)
            return;
        try { ImgDst.SaveImage(path); }
        catch (Exception ex) { ShowError("Save Image", $"Unable to save:\n{ex.Message}"); }
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
        var path = await PickOpenFileAsync("Import DNG",
            new[] { new FilePickerFileType("DNG files") { Patterns = new[] { "*.dng" } } });
        if (path != null)
        {
            ImportDng(path);
            await ApplyOpcodes();
        }
    }
    public void ImportDng(string filename)
    {
        try
        {
            var tiff = File.ReadAllBytes(filename);
            bool found = false;
            for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                var bytes = TiffFile.ReadOpcodeList(tiff, listIndex);
                if (bytes != null && bytes.Length > 0)
                {
                    found = true;
                    foreach (Opcode opcode in new OpcodesReader().ReadOpcodeList(bytes))
                    {
                        opcode.ListIndex = listIndex;
                        Opcodes.Add(opcode);
                    }
                }
            }
            if (found)
                SelectedOpcode = Opcodes.LastOrDefault();
            else
                ShowError("Import DNG", "No opcodes were found in the selected DNG file.");
        }
        catch (Exception ex)
        {
            ShowError("Import DNG", $"Unable to import opcodes from DNG:\n{ex.Message}");
        }
    }
    [RelayCommand]
    async Task ImportBin()
    {
        var path = await PickOpenFileAsync("Import binary",
            new[] { new FilePickerFileType("Binary opcode list") { Patterns = new[] { "*.bin" } } });
        if (path != null)
        {
            ImportBin(path);
            await ApplyOpcodes();
        }
    }
    public void ImportBin(string filename)
    {
        try
        {
            foreach (var opcode in new OpcodesReader().ReadOpcodeList(File.ReadAllBytes(filename)))
                Opcodes.Add(opcode);
            SelectedOpcode = Opcodes.LastOrDefault();
        }
        catch (Exception ex)
        {
            ShowError("Import Binary", $"Unable to import opcodes:\n{ex.Message}");
        }
    }
    [RelayCommand]
    async Task ExportBin()
    {
        var path = await PickSaveFileAsync("Export binary",
            new[] { new FilePickerFileType("Binary opcode list") { Patterns = new[] { "*.bin" } } },
            "opcodes.bin");
        if (path == null)
            return;
        try { File.WriteAllBytes(path, new OpcodesWriter().WriteOpcodeList(Opcodes)); }
        catch (Exception ex) { ShowError("Export Binary", $"Unable to export:\n{ex.Message}"); }
    }
    [RelayCommand]
    async Task ExportDng()
    {
        var path = await PickSaveFileAsync("Export to DNG",
            new[] { new FilePickerFileType("DNG files") { Patterns = new[] { "*.dng" } } },
            "");
        if (path == null)
            return;
        try
        {
            var tiff = File.ReadAllBytes(path);
            for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                var listOpcodes = Opcodes.Where(o => o.ListIndex == listIndex).ToArray();
                if (listOpcodes.Length == 0)
                    continue;
                var payload = new OpcodesWriter().WriteOpcodeList(listOpcodes);
                tiff = TiffFile.WriteOpcodeList(tiff, listIndex, payload);
            }
            File.WriteAllBytes(path, tiff);
        }
        catch (Exception ex)
        {
            ShowError("Export DNG", $"Unable to export opcodes to DNG:\n{ex.Message}");
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
    partial void OnDecodeGammaChanged(bool value) => _ = ApplyOpcodes();
    partial void OnEncodeGammaChanged(bool value) => _ = ApplyOpcodes();
    static void ApplyGamma(PixelBuffer img, float exponent)
    {
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
                img.ChangeRgb16Pixel(x, y, pixel => MathF.Pow(pixel / 65535.0f, exponent) * 65535.0f);
        });
    }
    static void ApplyOpcode(PixelBuffer img, Opcode opcode)
    {
        switch (opcode.header.id)
        {
            case OpcodeId.WarpRectilinear: OpcodesImplementation.WarpRectilinear(img, (OpcodeWarpRectilinear)opcode); break;
            case OpcodeId.FixVignetteRadial: OpcodesImplementation.FixVignetteRadial(img, (OpcodeFixVignetteRadial)opcode); break;
            case OpcodeId.FixBadPixelsConstant: OpcodesImplementation.FixBadPixelsConstant(img, (OpcodeFixBadPixelsConstant)opcode); break;
            case OpcodeId.FixBadPixelsList: OpcodesImplementation.FixBadPixelsList(img, (OpcodeFixBadPixelsList)opcode); break;
            case OpcodeId.TrimBounds: OpcodesImplementation.TrimBounds(img, (OpcodeTrimBounds)opcode); break;
            case OpcodeId.MapTable: OpcodesImplementation.MapTable(img, (OpcodeMapTable)opcode); break;
            case OpcodeId.MapPolynomial: OpcodesImplementation.MapPolynomial(img, (OpcodeMapPolynomial)opcode); break;
            case OpcodeId.GainMap: OpcodesImplementation.GainMap(img, (OpcodeGainMap)opcode); break;
            case OpcodeId.DeltaPerRow: OpcodesImplementation.DeltaPerRow(img, (OpcodeDeltaPerRow)opcode); break;
            case OpcodeId.DeltaPerColumn: OpcodesImplementation.DeltaPerColumn(img, (OpcodeDeltaPerColumn)opcode); break;
            case OpcodeId.ScalePerRow: OpcodesImplementation.ScalePerRow(img, (OpcodeScalePerRow)opcode); break;
            case OpcodeId.ScalePerColumn: OpcodesImplementation.ScalePerColumn(img, (OpcodeScalePerColumn)opcode); break;
            default: Debug.WriteLine($"\t{opcode.header.id} not implemented yet and skipped"); break;
        }
    }
    [RelayCommand]
    public async Task ApplyOpcodes()
    {
        if (ImgSrc == null)
            return;
        if (_applyRunning) { _applyPending = true; return; }
        _applyRunning = true;
        try
        {
            do
            {
                _applyPending = false;
                SetWaitCursor(true);
                var dst = ImgSrc.Clone();
                var ops = Opcodes.Where(o => o.Enabled).ToArray();
                bool decode = DecodeGamma, encode = EncodeGamma;
                string error = null;
                var sw = Stopwatch.StartNew();
                await Task.Run(() =>
                {
                    try
                    {
                        if (decode) ApplyGamma(dst, 2.2f);
                        foreach (var op in ops) ApplyOpcode(dst, op);
                        if (encode) ApplyGamma(dst, 1.0f / 2.2f);
                    }
                    catch (Exception ex) { error = ex.Message; Debug.WriteLine(ex); }
                });
                dst.Update();
                ImgDst = dst;
                Debug.WriteLine($"ApplyOpcodes executed in {sw.ElapsedMilliseconds}ms");
                if (error != null)
                    ShowError("Apply Opcodes", $"Error while applying opcodes:\n{error}");
            }
            while (_applyPending);
        }
        finally
        {
            _applyRunning = false;
            SetWaitCursor(false);
        }
    }
    // -- Avalonia-specific helpers -------------------------------------------
    async Task<string> PickOpenFileAsync(string title, FilePickerFileType[] filters)
    {
        if (Owner == null) return null;
        var topLevel = TopLevel.GetTopLevel(Owner);
        var startLocation = Directory.Exists(SAMPLES_DIR)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(SAMPLES_DIR)
            : null;
        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters,
            SuggestedStartLocation = startLocation
        });
        if (result.Count == 0) return null;
        return result[0].TryGetLocalPath();
    }
    async Task<string> PickSaveFileAsync(string title, FilePickerFileType[] filters, string suggestedFile)
    {
        if (Owner == null) return null;
        var topLevel = TopLevel.GetTopLevel(Owner);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = filters,
            SuggestedFileName = suggestedFile
        });
        return file?.TryGetLocalPath();
    }
    static FilePickerFileType[] AllFilesFilter() => new[]
    {
        new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
    };
    void SetWaitCursor(bool busy)
    {
        if (Owner == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            Owner.Cursor = busy ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
        });
    }
    void ShowError(string title, string message)
    {
        if (Owner == null) { Debug.WriteLine($"[{title}] {message}"); return; }
        Dispatcher.UIThread.Post(() => MessageDialog.Show(Owner, title, message));
    }
}
