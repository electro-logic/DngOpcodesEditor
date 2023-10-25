using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public MainWindowVM()
        {
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
        public void OpenImage()
        {
            var dialog = new OpenFileDialog() { Filter = "All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                OpenImage(dialog.FileName);
            }
        }
        public void OpenImage(string filename)
        {
            ImgSrc = new Image(filename);
            ImgDst = ImgSrc.Clone();
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
        public void SaveImage(string filename)
        {
            using (var stream = new FileStream(filename, FileMode.Create))
            {
                var encoder = new TiffBitmapEncoder() { Compression = TiffCompressOption.Lzw };
                BitmapFrame bmpFrame = BitmapFrame.Create(ImgDst.Bmp, null, null, null);
                encoder.Frames.Add(bmpFrame);
                encoder.Save(stream);
            }
        }
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
        public void ApplyOpcodes()
        {
            Debug.WriteLine("ApplyOpcodes started");
            var sw = Stopwatch.StartNew();
            ImgDst = ImgSrc.Clone();
            foreach (var opcode in Opcodes)
            {
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
                    default:
                        Debug.WriteLine($"\t{opcode.header.id} not implemented yet and skipped");
                        continue;
                }
            }
            ImgDst.Update(); 
            Debug.WriteLine($"ApplyOpcodes executed in {sw.ElapsedMilliseconds}ms");
        }
        public void ImportDng()
        {
            var dialog = new OpenFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                ImportDng(dialog.FileName);
            }
        }
        public void ImportDng(string filename)
        {
            // Import OpcodeList1, OpcodeList2, OpcodeList3
            //for (int listIndex = 1; listIndex < 4; listIndex++)
            {
                int listIndex = 3;  // TODO: Add support for additional lists
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
    }
}