using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// Reads a raw Bayer-CFA DNG and produces a demosaiced 16-bit linear RGB
// PixelBuffer, removing the need to develop the file with dcraw_emu first.
//
// Scope of this initial implementation:
//   - Uncompressed (Compression = 1) DNGs only. Most modern DNGs use Lossless
//     JPEG (Compression = 7) which would require a Huffman decoder.
//   - Strip-based layout (StripOffsets / StripByteCounts). Tiled layouts
//     (TileOffsets / TileByteCounts) are not handled here.
//   - 16-bit storage per sample (the typical case for uncompressed DNG raw).
//   - 2x2 CFA pattern (RGGB / GRBG / GBRG / BGGR).
//   - Bilinear demosaic. Good enough for previewing opcodes; not a
//     production-quality demosaicer.
public static class DngRawReader
{
    public static PixelBuffer Read(byte[] tiff)
    {
        var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);
        foreach (var ifd in TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd))
        {
            if (IsRawIfd(tiff, isLE, (int)ifd))
                return ReadRawFromIfd(tiff, isLE, (int)ifd);
        }
        throw new InvalidDataException("No raw image IFD (PhotometricInterpretation = CFA) found in the file.");
    }

    static bool IsRawIfd(byte[] tiff, bool isLE, int ifd)
    {
        int photoEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 262);
        if (photoEntry < 0)
            return false;
        var photo = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, photoEntry);
        if (photo.Length == 0)
            return false;
        // 32803 = CFA, 34892 = LinearRaw (already demosaiced).
        return photo[0] == 32803 || photo[0] == 34892;
    }

    static PixelBuffer ReadRawFromIfd(byte[] tiff, bool isLE, int ifd)
    {
        int width = (int)RequiredTag(tiff, isLE, ifd, 256);
        int height = (int)RequiredTag(tiff, isLE, ifd, 257);
        uint compression = OptionalTag(tiff, isLE, ifd, 259, 1);
        if (compression != 1)
            throw new NotSupportedException(
                $"Compression {compression} is not supported. " +
                "Only uncompressed (compression=1) DNGs can be opened directly; " +
                "develop compressed DNGs to a linear TIFF first (for example with dcraw_emu).");
        uint photometric = RequiredTag(tiff, isLE, ifd, 262);
        if (photometric == 34892)
            throw new NotSupportedException("LinearRaw DNGs (already demosaiced) are not supported yet.");

        uint bitsPerSample = OptionalTag(tiff, isLE, ifd, 258, 16);

        int stripOffsetsEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 273);
        int stripCountsEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 279);
        if (stripOffsetsEntry < 0 || stripCountsEntry < 0)
            throw new NotSupportedException("Tiled raw layout is not supported yet — only strip-based DNGs.");
        uint rowsPerStrip = OptionalTag(tiff, isLE, ifd, 278, (uint)height);
        var stripOffsets = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, stripOffsetsEntry);

        // Black / white level. BlackLevel can be per CFA position (count = 4).
        var blackLevels = ReadBlackLevels(tiff, isLE, ifd);
        uint whiteLevel = OptionalTag(tiff, isLE, ifd, 50717, (uint)((1u << (int)bitsPerSample) - 1));

        // CFA pattern: 4-byte array, values 0=R, 1=G, 2=B. Default to RGGB.
        var cfaPattern = ReadCfaPattern(tiff, isLE, ifd);

        // Read the CFA samples as 16-bit values.
        var raw = new ushort[width * height];
        int rowsRead = 0;
        for (int s = 0; s < stripOffsets.Length; s++)
        {
            int stripRows = Math.Min((int)rowsPerStrip, height - rowsRead);
            int stripStart = (int)stripOffsets[s];
            for (int row = 0; row < stripRows; row++)
            {
                int rowOffset = stripStart + row * width * 2;
                for (int x = 0; x < width; x++)
                {
                    raw[(rowsRead + row) * width + x] = isLE
                        ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(rowOffset + x * 2))
                        : BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(rowOffset + x * 2));
                }
            }
            rowsRead += stripRows;
        }

        var rgb = new UInt64[width * height];
        BilinearDemosaic(raw, rgb, width, height, cfaPattern, blackLevels, whiteLevel);
        return new PixelBuffer(rgb, width, height);
    }

    static uint RequiredTag(byte[] tiff, bool isLE, int ifd, ushort tag)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, tag);
        if (e < 0)
            throw new InvalidDataException($"Missing required TIFF tag {tag}.");
        var values = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
        if (values.Length == 0)
            throw new InvalidDataException($"TIFF tag {tag} has no value.");
        return values[0];
    }

    static uint OptionalTag(byte[] tiff, bool isLE, int ifd, ushort tag, uint defaultValue)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, tag);
        if (e < 0)
            return defaultValue;
        var values = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
        return values.Length > 0 ? values[0] : defaultValue;
    }

    static uint[] ReadBlackLevels(byte[] tiff, bool isLE, int ifd)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, 50714);
        if (e < 0)
            return new uint[] { 0 };
        return TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
    }

    static uint[] ReadCfaPattern(byte[] tiff, bool isLE, int ifd)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, 33422);
        if (e < 0)
            return new uint[] { 0, 1, 1, 2 }; // RGGB default
        var bytes = TiffFile.ReadEntryBytesPublic(tiff, isLE, e);
        if (bytes.Length < 4)
            return new uint[] { 0, 1, 1, 2 };
        return new uint[] { bytes[0], bytes[1], bytes[2], bytes[3] };
    }

    // Bilinear demosaic. The output RGB pixel at (x,y) is reconstructed by:
    //   - keeping the value of the channel actually sampled at (x,y)
    //   - averaging horizontally/vertically the neighbors for the channel that
    //     is sampled on the same row OR column as (x,y) in the CFA pattern
    //   - averaging diagonally for the channel sampled at neither.
    static void BilinearDemosaic(ushort[] raw, UInt64[] rgb, int W, int H, uint[] cfa, uint[] black, uint white)
    {
        // Per-position black levels. Indexed by (y%2)*2 + (x%2).
        double[] blackByPos = new double[4];
        for (int i = 0; i < 4; i++)
            blackByPos[i] = black.Length == 4 ? black[i] : (black.Length > 0 ? black[0] : 0);
        double whiteMinusBlack = Math.Max(1.0, white - blackByPos[0]);
        double scale = 65535.0 / whiteMinusBlack;

        // Returns the 0..65535 linear value at (sx, sy), clamping to image bounds.
        double Sample(int sx, int sy)
        {
            sx = Math.Clamp(sx, 0, W - 1);
            sy = Math.Clamp(sy, 0, H - 1);
            int pos = (sy & 1) * 2 + (sx & 1);
            double v = (raw[sy * W + sx] - blackByPos[pos]) * scale;
            return Math.Clamp(v, 0.0, 65535.0);
        }

        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int pos = (y & 1) * 2 + (x & 1);
                int color = (int)cfa[pos];
                double r = 0, g = 0, b = 0;
                double here = Sample(x, y);
                if (color == 0) // R pixel
                {
                    r = here;
                    g = (Sample(x - 1, y) + Sample(x + 1, y) + Sample(x, y - 1) + Sample(x, y + 1)) * 0.25;
                    b = (Sample(x - 1, y - 1) + Sample(x + 1, y - 1) + Sample(x - 1, y + 1) + Sample(x + 1, y + 1)) * 0.25;
                }
                else if (color == 2) // B pixel
                {
                    b = here;
                    g = (Sample(x - 1, y) + Sample(x + 1, y) + Sample(x, y - 1) + Sample(x, y + 1)) * 0.25;
                    r = (Sample(x - 1, y - 1) + Sample(x + 1, y - 1) + Sample(x - 1, y + 1) + Sample(x + 1, y + 1)) * 0.25;
                }
                else // G pixel — figure out whether R is horizontal or vertical
                {
                    g = here;
                    int leftPos = (y & 1) * 2 + ((x - 1) & 1);
                    int leftColor = (int)cfa[leftPos < 0 ? leftPos + 4 : leftPos];
                    if (leftColor == 0)
                    {
                        r = (Sample(x - 1, y) + Sample(x + 1, y)) * 0.5;
                        b = (Sample(x, y - 1) + Sample(x, y + 1)) * 0.5;
                    }
                    else
                    {
                        b = (Sample(x - 1, y) + Sample(x + 1, y)) * 0.5;
                        r = (Sample(x, y - 1) + Sample(x, y + 1)) * 0.5;
                    }
                }
                ushort R = (ushort)Math.Clamp(Math.Round(r), 0, 65535);
                ushort G = (ushort)Math.Clamp(Math.Round(g), 0, 65535);
                ushort B = (ushort)Math.Clamp(Math.Round(b), 0, 65535);
                rgb[y * W + x] = R | ((UInt64)G << 16) | ((UInt64)B << 32) | ((UInt64)65535 << 48);
            }
        });
    }
}
