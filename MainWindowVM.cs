using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace DngOpcodesEditor
{
    public partial class MainWindowVM : ObservableObject
    {
        [ObservableProperty]
        ObservableCollection<Opcode> _opcodes = new ObservableCollection<Opcode>();
        [ObservableProperty]
        Opcode _selectedOpcode;
        [ObservableProperty]
        Image _imgSrc, _imgDst;
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
                default:
                    Opcodes.Add(new Opcode());
                    break;
            }
        }
        public void ApplyOpcodes()
        {
            ImgDst = ImgSrc.Clone();
            foreach (var opcode in Opcodes)
            {
                switch (opcode.header.id)
                {
                    case OpcodeId.WarpRectilinear:
                        OpcodeImplementation.WarpRectilinear(ImgDst, opcode as OpcodeWarpRectilinear);
                        break;
                    case OpcodeId.FixVignetteRadial:
                        OpcodeImplementation.FixVignetteRadial(ImgDst, opcode as OpcodeFixVignetteRadial);
                        break;
                    default:
                        Debug.WriteLine($"{opcode.header.id} not implemented yet and skipped");
                        continue;
                }
            }
            ImgDst.Update();
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
                }
            }
        }
        public void ImportBin()
        {
            var dialog = new OpenFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
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
            // OpcodeList1: applied to the raw image, as read directly from the file
            // OpcodeList2: applied to the raw image, just after it has been mapped to linear reference values
            // OpcodeList3: applied to the raw image, just after it has been demosaiced
            //Process.Start("exiftool.exe", "-v \"-IFD0:OpcodeList3#<=data.bin\" mod.dng").WaitForExit();
            //Process.Start("dng_validate.exe", "mod.dng").WaitForExit();

            var dialog = new SaveFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
            dialog.InitialDirectory = Environment.CurrentDirectory;
            if (dialog.ShowDialog() == true)
            {
                string tmpFile = "tmpDngOpcodesEditor.bin";
                File.WriteAllBytes(tmpFile, new OpcodesWriter().WriteOpcodeList(Opcodes));
                // TODO: Add support for additional lists
                var exifProcess = Process.Start(new ProcessStartInfo("exiftool.exe", $"\"-IFD0:OpcodeList3#<={tmpFile}\" \"{dialog.FileName}\"")
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