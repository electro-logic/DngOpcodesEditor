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
    public OpcodeId[] OpcodeIds { get; } = Enum.GetValues<OpcodeId>();
    readonly string SAMPLES_DIR = Path.Combine(AppContext.BaseDirectory, "Samples");
    readonly string EXIFTOOL_PATH = Path.Combine(AppContext.BaseDirectory, "exiftool.exe");
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
            int bpp = img.Open(filename);
            ImgSrc = img;
            DecodeGamma = bpp <= 32;
            ImgDst = ImgSrc.Clone();
            SetWindowTitle(filename);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open image:\n{filename}\n\n{ex.Message}", "Open Image",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        if (!File.Exists(EXIFTOOL_PATH))
        {
            MessageBox.Show($"ExifTool was not found at:\n{EXIFTOOL_PATH}\n\nIt is required to import opcodes from DNG files.",
                "ExifTool missing", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            bool found = false;
            // Import OpcodeList1, OpcodeList2 and OpcodeList3
            for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                var bytes = RunExifTool($"-b -OpcodeList{listIndex} \"{filename}\"");
                if (bytes.Length > 0)
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
        if (!File.Exists(EXIFTOOL_PATH))
        {
            MessageBox.Show($"ExifTool was not found at:\n{EXIFTOOL_PATH}\n\nIt is required to export opcodes to DNG files.",
                "ExifTool missing", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
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
            // Each OpcodeList tag is written from the opcodes that were tagged
            // with the matching ListIndex.
            for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                var listOpcodes = Opcodes.Where(o => o.ListIndex == listIndex).ToArray();
                if (listOpcodes.Length == 0)
                    continue;
                string tmpFile = Path.Combine(AppContext.BaseDirectory, $"tmpOpcodeList{listIndex}.bin");
                File.WriteAllBytes(tmpFile, new OpcodesWriter().WriteOpcodeList(listOpcodes));
                RunExifTool($"-overwrite_original \"-OpcodeList{listIndex}#<={tmpFile}\" \"{dialog.FileName}\"");
                File.Delete(tmpFile);
            }
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
    byte[] RunExifTool(string arguments)
    {
        using var ms = new MemoryStream();
        var process = Process.Start(new ProcessStartInfo(EXIFTOOL_PATH, arguments)
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process.StandardOutput.BaseStream.CopyTo(ms);
        process.WaitForExit();
        return ms.ToArray();
    }
    Opcode[] ImportBin(byte[] binaryData) => new OpcodesReader().ReadOpcodeList(binaryData);
    static void ApplyGamma(Image img, float exponent)
    {
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                img.ChangeRgb16Pixel(x, y, pixel => MathF.Pow(pixel / 65535.0f, exponent) * 65535.0f);
            }
        });
    }
    static void ApplyOpcode(Image img, Opcode opcode)
    {
        switch (opcode.header.id)
        {
            case OpcodeId.WarpRectilinear:
                OpcodesImplementation.WarpRectilinear(img, (OpcodeWarpRectilinear)opcode);
                break;
            case OpcodeId.FixVignetteRadial:
                OpcodesImplementation.FixVignetteRadial(img, (OpcodeFixVignetteRadial)opcode);
                break;
            case OpcodeId.FixBadPixelsConstant:
                OpcodesImplementation.FixBadPixelsConstant(img, (OpcodeFixBadPixelsConstant)opcode);
                break;
            case OpcodeId.FixBadPixelsList:
                OpcodesImplementation.FixBadPixelsList(img, (OpcodeFixBadPixelsList)opcode);
                break;
            case OpcodeId.TrimBounds:
                OpcodesImplementation.TrimBounds(img, (OpcodeTrimBounds)opcode);
                break;
            case OpcodeId.MapTable:
                OpcodesImplementation.MapTable(img, (OpcodeMapTable)opcode);
                break;
            case OpcodeId.MapPolynomial:
                OpcodesImplementation.MapPolynomial(img, (OpcodeMapPolynomial)opcode);
                break;
            case OpcodeId.GainMap:
                OpcodesImplementation.GainMap(img, (OpcodeGainMap)opcode);
                break;
            case OpcodeId.DeltaPerRow:
                OpcodesImplementation.DeltaPerRow(img, (OpcodeDeltaPerRow)opcode);
                break;
            case OpcodeId.DeltaPerColumn:
                OpcodesImplementation.DeltaPerColumn(img, (OpcodeDeltaPerColumn)opcode);
                break;
            case OpcodeId.ScalePerRow:
                OpcodesImplementation.ScalePerRow(img, (OpcodeScalePerRow)opcode);
                break;
            case OpcodeId.ScalePerColumn:
                OpcodesImplementation.ScalePerColumn(img, (OpcodeScalePerColumn)opcode);
                break;
            default:
                Debug.WriteLine($"\t{opcode.header.id} not implemented yet and skipped");
                break;
        }
    }
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
                            ApplyGamma(dst, 2.2f);
                        foreach (var op in ops)
                            ApplyOpcode(dst, op);
                        if (encode)
                            ApplyGamma(dst, 1.0f / 2.2f);
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
