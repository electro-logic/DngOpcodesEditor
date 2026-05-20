using System;
using System.IO;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class CliIntegrationTests
{
    [Fact]
    public void Preview_RoundTripsUncompressedTiff()
    {
        // Use TiffWriter to build a small uncompressed RGB TIFF on disk, then
        // ask the CLI to apply an (empty) opcode list and write a new TIFF.
        var pixels = new ulong[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = 30000ul | (30000ul << 16) | (30000ul << 32) | (65535ul << 48);
        var inputBytes = TiffWriter.WriteRgb16(new PixelBuffer(pixels, 16, 16));

        var tmp = Path.Combine(Path.GetTempPath(), "DngCliTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var inputPath = Path.Combine(tmp, "in.tiff");
            var listPath = Path.Combine(tmp, "ops.bin");
            var outputPath = Path.Combine(tmp, "out.tiff");

            File.WriteAllBytes(inputPath, inputBytes);
            // Empty opcode list = a no-op pipeline; just exercises the dispatch
            // and the writer.
            File.WriteAllBytes(listPath, new OpcodesWriter().WriteOpcodeList(Array.Empty<Opcode>()));

            // Suppress console chatter from the CLI for clean test output.
            var stdoutBackup = Console.Out;
            Console.SetOut(TextWriter.Null);
            int exit;
            try
            {
                exit = DngOpcodesEditor.Cli.Program.Main(new[]
                {
                    "preview", inputPath, listPath, outputPath, "--no-encode-gamma"
                });
            }
            finally
            {
                Console.SetOut(stdoutBackup);
            }

            Assert.Equal(0, exit);
            Assert.True(File.Exists(outputPath));
            var output = File.ReadAllBytes(outputPath);
            // Quick sanity: II header + TIFF magic.
            Assert.Equal((byte)'I', output[0]);
            Assert.Equal((byte)'I', output[1]);
            Assert.True(output.Length > 1000); // 16x16x6 image + tags
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void List_ReportsOpcodesInFile()
    {
        // Build a TIFF whose IFD0 contains an OpcodeList3 tag.
        var pixels = new ulong[1];
        var baseTiff = TiffWriter.WriteRgb16(new PixelBuffer(pixels, 1, 1));
        var trim = new OpcodeTrimBounds { top = 0, left = 0, bottom = 1, right = 1 };
        trim.header.id = OpcodeId.TrimBounds;
        var payload = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)trim });
        var tiff = TiffFile.WriteOpcodeList(baseTiff, 3, payload);

        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, tiff);
            var stdoutBackup = Console.Out;
            using var capture = new StringWriter();
            Console.SetOut(capture);
            int exit;
            try { exit = DngOpcodesEditor.Cli.Program.Main(new[] { "list", tmp }); }
            finally { Console.SetOut(stdoutBackup); }

            Assert.Equal(0, exit);
            Assert.Contains("OpcodeList3", capture.ToString());
            Assert.Contains("TrimBounds", capture.ToString());
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
