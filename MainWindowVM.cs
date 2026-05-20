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
    [ObservableProperty]
    DngColorInfo _colorInfo;
    [ObservableProperty]
    bool _applyColorTransform = true;
    [ObservableProperty]
    bool _processAtFullResolution;
    [ObservableProperty]
    bool _ditherDisplay = Image.DitherDisplay;
    public OpcodeId[] OpcodeIds { get; } = Enum.GetValues<OpcodeId>();
    // File dialogs start here. After each successful Open / Save, the
    // dialog's folder is remembered for the next dialog. Initial value:
    // prefer the dedicated DNG sample folder, then the project's Samples,
    // then the bin output's linked copy for deployed builds.
    string _lastDialogDirectory = ResolveInitialDialogDirectory();
    static string ResolveInitialDialogDirectory()
    {
        // 1. The preferred DNG test corpus.
        const string DngSamples = @"D:\DngOpcodesEditor\Samples\dng";
        if (Directory.Exists(DngSamples)) return DngSamples;
        // 2. Current working directory (set by `dotnet run` / Visual Studio to the project root).
        var cwd = Path.Combine(Environment.CurrentDirectory, "Samples");
        if (Directory.Exists(cwd)) return cwd;
        // 3. Walk up from the binary location until we find the .csproj.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "DngOpcodesEditor.csproj")))
                return Path.Combine(dir, "Samples");
            dir = Path.GetDirectoryName(dir);
        }
        // 4. Last resort: the linked copy in the bin output.
        return Path.Combine(AppContext.BaseDirectory, "Samples");
    }
    void RememberFolder(string filename)
    {
        var folder = Path.GetDirectoryName(filename);
        if (!string.IsNullOrEmpty(folder)) _lastDialogDirectory = folder;
    }
    // Working previews are downsampled to at most this size unless
    // ProcessAtFullResolution is set, so editing 24 MP DNGs stays snappy.
    const int MaxPreviewWidth = 1920;
    const int MaxPreviewHeight = 1080;
    // Re-entrancy guard: rapid edits (ex. dragging a slider) coalesce into a
    // single trailing run instead of stacking concurrent passes.
    bool _applyRunning, _applyPending;
    Image _originalImage;
    string _openedFilename;
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
        var dialog = new OpenFileDialog() { Filter = "All files (*.*)|*.*", InitialDirectory = _lastDialogDirectory };
        if (dialog.ShowDialog() == true)
        {
            RememberFolder(dialog.FileName);
            OpenImage(dialog.FileName);
            _ = ApplyOpcodes();
        }
    }
    // Combined command: open a DNG, drop any existing opcodes, and import
    // the DNG's own OpcodeList tags so the editor immediately shows the
    // file's intended pipeline.
    [RelayCommand]
    async Task OpenDngWithOpcodes()
    {
        var dialog = new OpenFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*", InitialDirectory = _lastDialogDirectory };
        if (dialog.ShowDialog() != true)
            return;
        RememberFolder(dialog.FileName);
        OpenDngWithOpcodes(dialog.FileName);
        await ApplyOpcodes();
    }
    public void OpenDngWithOpcodes(string filename)
    {
        Opcodes.Clear();
        OpenImage(filename);
        if (Path.GetExtension(filename).Equals(".dng", StringComparison.OrdinalIgnoreCase))
            ImportDng(filename);
    }
    public void OpenImage(string filename)
    {
        try
        {
            var img = new Image();
            DngColorInfo colorInfo = null;
            // DNG files are routed through the built-in CFA reader so users no
            // longer need to develop the file with dcraw_emu first. Other
            // formats (TIFF, PNG, ...) go through WPF's BitmapDecoder.
            if (Path.GetExtension(filename).Equals(".dng", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = File.ReadAllBytes(filename);
                var buffer = DngRawReader.Read(bytes);
                img.LoadFromBuffer(buffer);
                colorInfo = DngColorInfo.TryRead(bytes);
                // Raw CFA data is linear; no input-gamma decode required.
                DecodeGamma = false;
            }
            else
            {
                int bpp = img.Open(filename);
                DecodeGamma = bpp <= 32;
            }
            _originalImage = img;
            _openedFilename = filename;
            ColorInfo = colorInfo;
            RebuildWorkingImage();
            Metadata = ReadFileMetadata(filename);
            SetWindowTitle(filename);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open image:\n{filename}\n\n{ex.Message}", "Open Image",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    // Initialises the editor with a pre-built reference image — used at
    // startup to show a synthetic grid so the first-launch UX doesn't
    // depend on any bundled sample file (relative paths were brittle —
    // they resolved correctly only when CWD happened to match the build
    // output dir).
    public void LoadReferenceBuffer(PixelBuffer buffer, string title = null)
    {
        var img = new Image();
        img.LoadFromBuffer(buffer);
        _originalImage = img;
        _openedFilename = null;
        ColorInfo = null;
        DecodeGamma = false;
        RebuildWorkingImage();
        Metadata = new System.Collections.Generic.List<DngMetadata.Entry>();
        SetWindowTitle(title ?? "");
    }

    // Produces the working ImgSrc — a clone of the loaded full-resolution
    // image, downsampled to <= 1920x1080 unless ProcessAtFullResolution is
    // set. The opcode chain runs on this working copy for responsiveness.
    void RebuildWorkingImage()
    {
        if (_originalImage == null) return;
        bool needsResize = !ProcessAtFullResolution
            && (_originalImage.Width > MaxPreviewWidth || _originalImage.Height > MaxPreviewHeight);
        if (needsResize)
        {
            var smaller = _originalImage.Resize(MaxPreviewWidth, MaxPreviewHeight);
            var working = new Image();
            working.LoadFromBuffer(smaller);
            ImgSrc = working;
        }
        else
        {
            ImgSrc = _originalImage.Clone();
        }
        ImgDst = ImgSrc.Clone();
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
            FileName = "processed_" + DateTime.Now.ToString("HHmmss") + ".tiff",
            InitialDirectory = _lastDialogDirectory
        };
        if (dialog.ShowDialog() == true)
        {
            RememberFolder(dialog.FileName);
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
        var dialog = new OpenFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*", InitialDirectory = _lastDialogDirectory };
        if (dialog.ShowDialog() == true)
        {
            RememberFolder(dialog.FileName);
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
        var dialog = new OpenFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*", InitialDirectory = _lastDialogDirectory };
        if (dialog.ShowDialog() == true)
        {
            RememberFolder(dialog.FileName);
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
        var dialog = new SaveFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*", InitialDirectory = _lastDialogDirectory };
        if (dialog.ShowDialog() == true)
        {
            RememberFolder(dialog.FileName);
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
            InitialDirectory = _lastDialogDirectory
        };
        if (dialog.ShowDialog() != true)
            return;
        RememberFolder(dialog.FileName);
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
    partial void OnApplyColorTransformChanged(bool value) => _ = ApplyOpcodes();
    partial void OnDitherDisplayChanged(bool value)
    {
        Image.DitherDisplay = value;
        // No need to rerun the full pipeline — just refresh the on-screen
        // bitmap from the existing 16-bit buffer.
        ImgSrc?.Update();
        ImgDst?.Update();
    }
    partial void OnProcessAtFullResolutionChanged(bool value)
    {
        if (_originalImage == null) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try { RebuildWorkingImage(); }
        finally { Mouse.OverrideCursor = null; }
        _ = ApplyOpcodes();
    }
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
                // OpcodeList2 opcodes target *linearised CFA* data per the DNG
                // spec and are applied by DngRawReader before demosaicing; we
                // skip them here so they don't get re-applied to the (already
                // corrected) demosaiced RGB buffer. Toggling an L2 opcode in
                // the UI therefore has no effect on the preview without
                // reloading the file — a known limitation, documented in the
                // README.
                // Filter to enabled non-L2 opcodes, then within each list apply
                // the DNG 1.6 skip rule: an optional WarpRectilinear2 silences
                // the WarpRectilinear / WarpFisheye that *immediately* follows
                // it. Group by list, apply per group, concat in 1 -> 3 order.
                var ops = Opcodes
                    .Where(o => o.Enabled && o.ListIndex != 2)
                    .GroupBy(o => o.ListIndex)
                    .OrderBy(g => g.Key)
                    .SelectMany(g => OpcodesImplementation.ApplyWarpRectilinear2SkipRule(g.ToList()))
                    .ToArray();
                bool decode = DecodeGamma, encode = EncodeGamma;
                bool doColorTransform = ApplyColorTransform && ColorInfo != null;
                var cameraToSrgb = ColorInfo?.CameraToSrgb;
                var asShotNeutral = ColorInfo?.AsShotNeutral;
                var toneCurve = ColorInfo?.ToneCurve;
                var hueSatMap = ColorInfo?.HueSatMap;
                string error = null;
                var sw = Stopwatch.StartNew();
                // The pixel work touches only the managed pixel buffer, so it
                // runs off the UI thread; the WPF bitmap is refreshed afterwards.
                await Task.Run(() =>
                {
                    try
                    {
                        // Opcodes operate on linear values, before gamma encoding.
                        // Inputs flagged as gamma-encoded (8-bit-per-channel
                        // TIFFs / PNGs etc.) are decoded with the proper sRGB
                        // EOTF rather than the 2.2 approximation we used before.
                        if (decode)
                            OpcodesImplementation.ApplySrgbDecode(dst);
                        foreach (var op in ops)
                            OpcodesImplementation.Apply(dst, op);
                        // DNG-spec opcodes run on camera-native RGB; convert
                        // to linear sRGB once the chain has finished. The
                        // AsShotNeutral is forwarded so saturating highlights
                        // get desaturated instead of taking on the cast of
                        // whichever channel clipped first.
                        if (doColorTransform)
                            ColorTransform.Apply(dst, cameraToSrgb, asShotNeutral);
                        // ProfileHueSatMap — per-hue saturation / value tweak
                        // the manufacturer ships to get "punchy" colour
                        // rendering (DJI saturates skies and oranges here).
                        // Applied between the colour matrix and the tone curve
                        // per the DNG spec ordering.
                        if (doColorTransform && hueSatMap != null)
                            hueSatMap.Apply(dst);
                        // ProfileToneCurve (DJI ships one) — applied in linear
                        // sRGB space, before gamma encoding.
                        if (doColorTransform && toneCurve != null)
                            toneCurve.Apply(dst);
                        // Output is encoded with the proper sRGB OETF so it
                        // matches what monitors and image viewers expect.
                        if (encode)
                            OpcodesImplementation.ApplySrgbEncode(dst);
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
