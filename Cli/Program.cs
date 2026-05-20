using System;
using System.IO;
using System.Linq;
using DngOpcodesEditor;

namespace DngOpcodesEditor.Cli;

// Headless command-line front-end. Reuses the Core library to expose every
// non-GUI operation: list / extract / inject opcode payloads, dump metadata,
// or apply a complete opcode chain to a raw DNG and write the result as a
// 16-bit RGB TIFF.
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "list" => List(args),
                "extract" => Extract(args),
                "inject" => Inject(args),
                "metadata" => Metadata(args),
                "preview" => Preview(args),
                "help" or "--help" or "-h" => Help(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    static int List(string[] args)
    {
        if (args.Length < 2) return Usage("list <dng> [--verbose]");
        bool verbose = Array.Exists(args, a => a == "--verbose" || a == "-v");
        var tiff = File.ReadAllBytes(args[1]);
        bool any = false;
        for (int listIndex = 1; listIndex < 4; listIndex++)
        {
            var bytes = TiffFile.ReadOpcodeList(tiff, listIndex);
            if (bytes == null || bytes.Length == 0) continue;
            any = true;
            Console.WriteLine($"OpcodeList{listIndex} ({bytes.Length} bytes):");
            var opcodes = new OpcodesReader().ReadOpcodeList(bytes);
            for (int i = 0; i < opcodes.Length; i++)
            {
                Console.WriteLine($"  [{i,2}] {opcodes[i].header.id}");
                if (verbose)
                {
                    foreach (var f in opcodes[i].GetType().GetFields(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance))
                    {
                        var v = f.GetValue(opcodes[i]);
                        if (v is float[] fa)
                            v = $"float[{fa.Length}] min={(fa.Length > 0 ? fa.Min() : 0):F4} max={(fa.Length > 0 ? fa.Max() : 0):F4} avg={(fa.Length > 0 ? fa.Average() : 0):F4}";
                        else if (v is double[] da)
                            v = $"double[{da.Length}] = [{string.Join(", ", da.Take(8).Select(x => x.ToString("F4")))}{(da.Length > 8 ? ", …" : "")}]";
                        else if (v is uint[] ua)
                            v = $"uint[{ua.Length}] = [{string.Join(", ", ua.Take(8))}{(ua.Length > 8 ? ", …" : "")}]";
                        else if (v is ushort[] sa)
                            v = $"ushort[{sa.Length}] min={(sa.Length > 0 ? sa.Min() : 0)} max={(sa.Length > 0 ? sa.Max() : 0)}";
                        Console.WriteLine($"        {f.Name,-20} = {v}");
                    }
                }
            }
        }
        if (!any)
            Console.WriteLine("No OpcodeList tags found.");
        return 0;
    }

    static int Extract(string[] args)
    {
        if (args.Length < 4) return Usage("extract <dng> <list:1|2|3> <output.bin>");
        var dng = args[1];
        int listIndex = int.Parse(args[2]);
        var output = args[3];
        var bytes = TiffFile.ReadOpcodeList(File.ReadAllBytes(dng), listIndex);
        if (bytes == null || bytes.Length == 0)
        {
            Console.Error.WriteLine($"OpcodeList{listIndex} not present in {dng}.");
            return 3;
        }
        File.WriteAllBytes(output, bytes);
        Console.WriteLine($"Wrote {bytes.Length} bytes of OpcodeList{listIndex} to {output}.");
        return 0;
    }

    static int Inject(string[] args)
    {
        if (args.Length < 4) return Usage("inject <dng> <input.bin> <list:1|2|3>");
        var dng = args[1];
        var binFile = args[2];
        int listIndex = int.Parse(args[3]);
        var payload = File.ReadAllBytes(binFile);
        var tiff = File.ReadAllBytes(dng);
        var updated = TiffFile.WriteOpcodeList(tiff, listIndex, payload);
        File.WriteAllBytes(dng, updated);
        Console.WriteLine($"Injected {payload.Length} bytes from {binFile} into OpcodeList{listIndex} of {dng}.");
        return 0;
    }

    static int Metadata(string[] args)
    {
        if (args.Length < 2) return Usage("metadata <file>");
        var entries = DngMetadata.Read(File.ReadAllBytes(args[1]));
        if (entries.Count == 0)
        {
            Console.WriteLine("No recognised metadata tags found.");
            return 0;
        }
        foreach (var e in entries)
            Console.WriteLine($"  {e.Name,-30}  {e.Value}");
        return 0;
    }

    static int Preview(string[] args)
    {
        if (args.Length < 4)
            return Usage("preview <input.dng|tiff> <list.bin> <output.tiff> [--decode-input-gamma] [--no-encode-gamma] [--raw-colors] [--max-dimension N]");
        var input = args[1];
        var listFile = args[2];
        var output = args[3];
        bool encodeGamma = !ArgsContain(args, "--no-encode-gamma");
        bool decodeGamma = ArgsContain(args, "--decode-input-gamma");
        bool rawColors = ArgsContain(args, "--raw-colors");
        int maxDimension = ParseIntArg(args, "--max-dimension", 0);

        Console.WriteLine($"Opening {input}…");
        var inputBytes = File.ReadAllBytes(input);
        var buffer = DngRawReader.Read(inputBytes);
        Console.WriteLine($"  {buffer.Width}x{buffer.Height} pixels");

        if (maxDimension > 0 && (buffer.Width > maxDimension || buffer.Height > maxDimension))
        {
            buffer = buffer.Resize(maxDimension, maxDimension);
            Console.WriteLine($"  resized to {buffer.Width}x{buffer.Height}");
        }

        var colorInfo = rawColors ? null : DngColorInfo.TryRead(inputBytes);
        if (colorInfo != null)
            Console.WriteLine("  found AsShotNeutral + ColorMatrix; will apply colour transform");

        if (decodeGamma)
        {
            Console.WriteLine("Decoding input gamma (sRGB EOTF)…");
            OpcodesImplementation.ApplySrgbDecode(buffer);
        }

        Console.WriteLine($"Loading opcodes from {listFile}…");
        var opcodes = new OpcodesReader().ReadOpcodeList(File.ReadAllBytes(listFile));
        Console.WriteLine($"  {opcodes.Length} opcode(s)");

        for (int i = 0; i < opcodes.Length; i++)
        {
            Console.WriteLine($"  applying [{i,2}] {opcodes[i].header.id}…");
            OpcodesImplementation.Apply(buffer, opcodes[i]);
        }
        if (colorInfo != null)
        {
            Console.WriteLine("Applying DNG colour transform…");
            ColorTransform.Apply(buffer, colorInfo.CameraToSrgb, colorInfo.AsShotNeutral);
            if (colorInfo.HueSatMap != null)
            {
                Console.WriteLine("Applying ProfileHueSatMap…");
                colorInfo.HueSatMap.Apply(buffer);
            }
            if (colorInfo.ToneCurve != null)
            {
                Console.WriteLine("Applying ProfileToneCurve…");
                colorInfo.ToneCurve.Apply(buffer);
            }
        }
        if (encodeGamma)
        {
            Console.WriteLine("Encoding output gamma (sRGB OETF)…");
            OpcodesImplementation.ApplySrgbEncode(buffer);
        }

        Console.WriteLine($"Writing {output}…");
        TiffWriter.WriteRgb16(buffer, output);
        Console.WriteLine("Done.");
        return 0;
    }

    static int ParseIntArg(string[] args, string flag, int defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out int v))
                return v;
        return defaultValue;
    }

    static int Help()
    {
        PrintHelp();
        return 0;
    }

    static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}\n");
        PrintHelp();
        return 1;
    }

    static int Usage(string usage)
    {
        Console.Error.WriteLine($"Usage: dng-opcodes {usage}");
        return 1;
    }

    static bool ArgsContain(string[] args, string flag)
    {
        foreach (var a in args)
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"dng-opcodes — headless DNG opcode tool

Usage:
  dng-opcodes <command> [arguments]

Commands:
  list      <dng>                                        List the opcodes contained in each OpcodeList tag.
  extract   <dng> <list:1|2|3> <output.bin>              Save the OpcodeListN payload to a binary file.
  inject    <dng> <input.bin> <list:1|2|3>               Replace OpcodeListN in the DNG with the payload (file modified in place).
  metadata  <file>                                       Print common EXIF / DNG tags from a TIFF or DNG file.
  preview   <input.dng|tiff> <list.bin> <output.tiff>    Apply an opcode list to an image and write a 16-bit RGB TIFF.
              [--decode-input-gamma]                       Apply the sRGB EOTF to the input first (use for gamma-encoded TIFF/PNG sources).
              [--no-encode-gamma]                          Skip the final sRGB OETF (leave the TIFF linear).
              [--raw-colors]                               Skip the DNG colour transform (keep camera-native RGB).
              [--max-dimension N]                          Downsample so the longest side fits within N pixels.
  help                                                   Show this help.
");
    }
}
