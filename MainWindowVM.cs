using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DngOpcodesEditor
{
    public partial class MainWindowVM : ObservableObject
    {
        [ObservableProperty]
        ObservableCollection<Opcode> _opcodes = new ObservableCollection<Opcode>();
        //[ObservableProperty]
        //Dictionary<Opcode, int> _opcodeLists = new Dictionary<Opcode, int>();
        [ObservableProperty]
        Opcode _selectedOpcode;
        [ObservableProperty]
        Image _imgSrc, _imgDst;
        [ObservableProperty]
        bool _encodeGamma, _decodeGamma;

        public MainWindowVM()
        {
            _encodeGamma = true;
            _decodeGamma = true;
            SetWindowTitle();
            _opcodes.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    foreach (Opcode item in e.NewItems)
                    {
                        item.PropertyChanged += (ps, pe) => ApplyOpcodes();
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

            App.Current.MainWindow.Title = $"DNG Opcodes Editor v{Assembly.GetExecutingAssembly().GetName().Version.ToString()}";
            if (!string.IsNullOrWhiteSpace(filename))
            {
                App.Current.MainWindow.Title += $" - {Path.GetFileName(filename)}";
            }
        }
        public void OpenImage()
        {
            var dialog = new OpenFileDialog() { Filter = "All files (*.*)|*.*" };
            dialog.InitialDirectory = Environment.CurrentDirectory;
            if (dialog.ShowDialog() == true)
            {
                OpenImage(dialog.FileName);
            }
        }
        public void OpenImage(string filename)
        {
            ImgSrc = new Image();
            int bpp = ImgSrc.Open(filename);
            EncodeGamma = bpp > 32 ? true : false;
            ImgDst = ImgSrc.Clone();
            SetWindowTitle(filename);
        }
        public void SaveImage()
        {
            var dialog = new SaveFileDialog() { Filter = "Tiff image (*.tiff)|*.tiff" };
            dialog.FileName = "processed_" + DateTime.Now.ToString("hhmmss") + ".tiff";
            if (dialog.ShowDialog() == true)
            {
                SaveImage(dialog.FileName);
            }
        }
        public void SaveImage(string filename) => ImgDst.SaveImage(filename);
        public void AddOpcode(OpcodeId id)
        {
            var header = new OpcodeHeader() { id = id };
            switch (id)
            {
                case OpcodeId.WarpRectilinear:
                    Opcodes.Add(new OpcodeWarpRectilinear() { header = header });
                    break;
                case OpcodeId.FixVignetteRadial:
                    Opcodes.Add(new OpcodeFixVignetteRadial() { header = header });
                    break;
                case OpcodeId.TrimBounds:
                    Opcodes.Add(new OpcodeTrimBounds() { header = header });
                    break;
                default:
                    Opcodes.Add(new Opcode());
                    break;
            }
        }
        public void ImportDng()
        {
            var dialog = new OpenFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
            dialog.InitialDirectory = Environment.CurrentDirectory;
            if (dialog.ShowDialog() == true)
            {
                ImportDng(dialog.FileName);
            }
        }
        public void ImportDng(string filename)
        {
            // Import OpcodeList2 and OpcodeList3
            for (int listIndex = 2; listIndex < 4; listIndex++)
            {
                //int listIndex = 3;  // TODO: Add support for additional lists
                var ms = new MemoryStream();
                // -IFD0:OpcodeList
                var exifProcess = Process.Start(new ProcessStartInfo("exiftool.exe", $"-b -OpcodeList{listIndex} \"{filename}\"")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                exifProcess.StandardOutput.BaseStream.CopyTo(ms);
                var bytes = ms.ToArray();
                if (bytes.Length > 0)
                {
                    foreach (Opcode opcode in ImportBin(bytes))
                    {
                        opcode.ListIndex = listIndex;
                        Opcodes.Add(opcode);
                    }
                    SelectedOpcode = Opcodes.Last();
                }
            }
        }
        public void ImportBin()
        {
            var dialog = new OpenFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
            dialog.InitialDirectory = Environment.CurrentDirectory;
            if (dialog.ShowDialog() == true)
            {
                ImportBin(dialog.FileName);
            }
        }
        public void ImportBin(string filename)
        {
            foreach (var opcode in ImportBin(File.ReadAllBytes(filename)))
            {
                Opcodes.Add(opcode);
            }
            SelectedOpcode = Opcodes.Last();
        }
        public void ExportBin()
        {
            var dialog = new SaveFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                File.WriteAllBytes(dialog.FileName, new OpcodesWriter().WriteOpcodeList(Opcodes));
            }
        }
        public void ExportDNG()
        {
            // OpcodeList1: applied as read directly from the file
            // OpcodeList2: applied after mapping to linear reference values
            // OpcodeList3: applied after demosaicing
            var dialog = new SaveFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
            dialog.InitialDirectory = Environment.CurrentDirectory;
            if (dialog.ShowDialog() == true)
            {
                string tmpFile = "tmpDngOpcodesEditor.bin";
                var bytes = new OpcodesWriter().WriteOpcodeList(Opcodes);
                File.WriteAllBytes(tmpFile, bytes);
                // TODO: Add support for all OpcodeList
                string tag = "OpcodeList3";         // default SubIFD
                //string tag = "IFD0:OpcodeList3";
                var exifProcess = Process.Start(new ProcessStartInfo("exiftool.exe", $"-overwrite_original \"-{tag}#<={tmpFile}\" \"{dialog.FileName}\"")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                exifProcess.WaitForExit();
                File.Delete(tmpFile);
            }
        }
        Opcode[] ImportBin(byte[] binaryData)
        {
            return new OpcodesReader().ReadOpcodeList(binaryData);
        }
        public void ApplyOpcodes()
        {
            Debug.WriteLine("ApplyOpcodes started");
            var sw = Stopwatch.StartNew();

            if (ImgSrc == null)
                return;

            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            ImgDst = ImgSrc.Clone();
            if (DecodeGamma)
            {
                var swGamma = Stopwatch.StartNew();
                // Apply gamma decoding
                Parallel.For(0, ImgDst.Height, (y) =>
                {
                    for (int x = 0; x < ImgDst.Width; x++)
                    {
                        ImgDst.ChangeRgb16Pixel(x, y, (pixel => MathF.Pow(pixel / 65535.0f, 2.2f) * 65535.0f));
                    }
                });
                Debug.WriteLine($"\tGamma decoding executed in {swGamma.ElapsedMilliseconds}ms");
            }
            foreach (var opcode in Opcodes)
            {
                if (!opcode.Enabled)
                    continue;
                switch (opcode.header.id)
                {
                    case OpcodeId.WarpRectilinear:
                        OpcodesImplementation.WarpRectilinear(ImgDst, opcode as OpcodeWarpRectilinear);
                        break;
                    case OpcodeId.FixVignetteRadial:
                        OpcodesImplementation.FixVignetteRadial(ImgDst, opcode as OpcodeFixVignetteRadial);
                        break;
                    case OpcodeId.TrimBounds:
                        OpcodesImplementation.TrimBounds(ImgDst, opcode as OpcodeTrimBounds);
                        break;
                    case OpcodeId.GainMap:
                        OpcodesImplementation.GainMap(ImgDst, opcode as OpcodeGainMap);
                        break;
                    default:
                        Debug.WriteLine($"\t{opcode.header.id} not implemented yet and skipped");
                        continue;
                }
            }
            if (EncodeGamma)
            {
                var swGamma = Stopwatch.StartNew();
                // Apply gamma encoding
                Parallel.For(0, ImgDst.Height, (y) =>
                {
                    for (int x = 0; x < ImgDst.Width; x++)
                    {
                        ImgDst.ChangeRgb16Pixel(x, y, (pixel => MathF.Pow(pixel / 65535.0f, 1.0f / 2.2f) * 65535.0f));
                    }
                });
                Debug.WriteLine($"\tGamma encoding executed in {swGamma.ElapsedMilliseconds}ms");
            }
            ImgDst.Update();
            Debug.WriteLine($"ApplyOpcodes executed in {sw.ElapsedMilliseconds}ms");
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
        }
    }
}