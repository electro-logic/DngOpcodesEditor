using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DngOpcodesEditorLegacy
{
    /*
     new OpcodesReader().ReadOpcodeList(File.ReadAllBytes(@"C:\Users\leona\OneDrive\Desktop\DNG\Files\extracted.bin"));


return;

var writer = new OpcodesWriter();
writer.WriteOpcodeListHeader(1);
writer.FixVignetteRadial(2.154732, 0, 0, 0, 0, 0.5, 0.5);
//writer.WarpRectilinear(1, new double[] { 1.0, k1, k2, k3, p1, p2 }, cx, cy);

var bytes = writer.ReadOpcodeList();
new OpcodesReader().ReadOpcodeList(bytes);

// Extract OpCodeList3 from DNG file
// exiftool -b -OpCodeList3 FILE > extracted.bin
//Process.Start("exiftool.exe", "-b -IFD0:OpcodeList3 file.dng > extracted.bin").WaitForExit();
// dng_validate.exe -v file.dng

File.WriteAllBytes("data.bin", bytes);
Console.WriteLine($"Written {bytes.Length} bytes");
File.Copy("orig.dng", "mod.dng", true);
     */


}
