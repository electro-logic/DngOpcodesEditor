using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class DngRawReaderTests
{
    [Fact]
    public void DemosaicsMinimalRggbImage()
    {
        // 2x2 RGGB CFA. Black = 0, white = 65535, so raw values pass through
        // the linearization unchanged.
        //   y=0: R G   (raw = 1000, 2000)
        //   y=1: G B   (raw = 3000, 4000)
        ushort[] samples = { 1000, 2000, 3000, 4000 };
        var tiff = BuildMinimalCfaDng(samples, width: 2, height: 2,
            cfaPattern: new byte[] { 0, 1, 1, 2 }, blackLevel: 0, whiteLevel: 65535);

        var buffer = DngRawReader.Read(tiff);

        Assert.Equal(2, buffer.Width);
        Assert.Equal(2, buffer.Height);
        // (0,0) is R: R sampled, G is hor/vert average, B is diagonal average.
        // With edge clamping the missing neighbors reuse the existing samples.
        var rgb00 = buffer.GetRgb16Pixel(0, 0);
        Assert.Equal(1000, rgb00[0]);
        Assert.Equal(1750, rgb00[1]);
        Assert.Equal(2500, rgb00[2]);
        // (1,1) is B: B sampled.
        var rgb11 = buffer.GetRgb16Pixel(1, 1);
        Assert.Equal(4000, rgb11[2]);
    }

    [Fact]
    public void OpensCompressedDngWithLosslessJpegTile()
    {
        // 2x2 CFA DNG with one tile encoded as Lossless JPEG. The LJPEG stream
        // decodes to four samples of value 128 (P=8). With black=0/white=255,
        // each sample normalises to (128 * 65535 / 255) = 32896 across all
        // channels (every neighbour is also 32896 so demosaic is identity).
        var ljpegTile = AllZeroLosslessJpeg2x2();
        var tiff = BuildMinimalTiledCfaDng(
            new byte[][] { ljpegTile },
            width: 2, height: 2, tileWidth: 2, tileHeight: 2,
            cfaPattern: new byte[] { 0, 1, 1, 2 },
            blackLevel: 0, whiteLevel: 255,
            compression: 7, bitsPerSample: 8);

        var buffer = DngRawReader.Read(tiff);

        Assert.Equal(2, buffer.Width);
        Assert.Equal(2, buffer.Height);
        var rgb = buffer.GetRgb16Pixel(0, 0);
        Assert.Equal(32896, rgb[0]);
        Assert.Equal(32896, rgb[1]);
        Assert.Equal(32896, rgb[2]);
    }

    [Fact]
    public void Read_ThrowsClearErrorForUnsupportedCompression()
    {
        // Compression 8 = Adobe Deflate, not supported by this reader.
        var tiff = BuildMinimalCfaDng(new ushort[] { 0, 0, 0, 0 }, 2, 2,
            cfaPattern: new byte[] { 0, 1, 1, 2 }, blackLevel: 0, whiteLevel: 65535,
            compression: 8);

        var ex = Assert.Throws<NotSupportedException>(() => DngRawReader.Read(tiff));
        Assert.Contains("Compression", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpensLinearRawDng()
    {
        // 2x2 LinearRaw image: each pixel has 3 interleaved RGB 16-bit samples.
        // R=10000, G=20000, B=30000 for (0,0); etc.
        ushort[] samples =
        {
            10000, 20000, 30000,
            40000, 50000, 60000,
            11111, 22222, 33333,
            44444, 55555, 65535,
        };
        var tiff = BuildMinimalLinearRawDng(samples, width: 2, height: 2, blackLevel: 0, whiteLevel: 65535);

        var buffer = DngRawReader.Read(tiff);

        Assert.Equal(2, buffer.Width);
        Assert.Equal(2, buffer.Height);
        var rgb00 = buffer.GetRgb16Pixel(0, 0);
        Assert.Equal(10000, rgb00[0]);
        Assert.Equal(20000, rgb00[1]);
        Assert.Equal(30000, rgb00[2]);
        var rgb11 = buffer.GetRgb16Pixel(1, 1);
        Assert.Equal(44444, rgb11[0]);
        Assert.Equal(55555, rgb11[1]);
        Assert.Equal(65535, rgb11[2]);
    }

    [Fact]
    public void OpensTiledUncompressedDng()
    {
        // 4x4 image split into four 2x2 tiles, RGGB CFA. Each tile holds a
        // unique flat value so we can verify the tiles end up at the right
        // position in the assembled image.
        //   tile (0,0) = 1000   tile (1,0) = 2000
        //   tile (0,1) = 3000   tile (1,1) = 4000
        var tiles = new ushort[][]
        {
            new ushort[] { 1000, 1000, 1000, 1000 },
            new ushort[] { 2000, 2000, 2000, 2000 },
            new ushort[] { 3000, 3000, 3000, 3000 },
            new ushort[] { 4000, 4000, 4000, 4000 },
        };
        var tiff = BuildMinimalTiledCfaDng(tiles, width: 4, height: 4, tileWidth: 2, tileHeight: 2,
            cfaPattern: new byte[] { 0, 1, 1, 2 }, blackLevel: 0, whiteLevel: 65535);

        var buffer = DngRawReader.Read(tiff);

        Assert.Equal(4, buffer.Width);
        Assert.Equal(4, buffer.Height);
        // Pixel (0,0): R sampled from tile (0,0). All neighbours are also 1000,
        // so R = G = B = 1000.
        var rgb00 = buffer.GetRgb16Pixel(0, 0);
        Assert.Equal(1000, rgb00[0]);
        Assert.Equal(1000, rgb00[1]);
        Assert.Equal(1000, rgb00[2]);
        // Pixel (3,3): inside tile (1,1) with value 4000.
        var rgb33 = buffer.GetRgb16Pixel(3, 3);
        Assert.Equal(4000, rgb33[2]); // B at this CFA position
    }

    // Builds a tiny CFA DNG by hand: TIFF header, single IFD with the minimum
    // tags the reader needs, then the raw 16-bit sample data.
    static byte[] BuildMinimalCfaDng(ushort[] samples, int width, int height, byte[] cfaPattern,
        uint blackLevel, uint whiteLevel, uint compression = 1)
    {
        var entries = new List<byte[]>
        {
            MakeEntry(256, type: 3, count: 1, value: (uint)width),
            MakeEntry(257, type: 3, count: 1, value: (uint)height),
            MakeEntry(258, type: 3, count: 1, value: 16),
            MakeEntry(259, type: 3, count: 1, value: compression),
            MakeEntry(262, type: 3, count: 1, value: 32803),
            // 273 StripOffsets — value (data offset) filled in below.
            null,
            MakeEntry(277, type: 3, count: 1, value: 1),
            MakeEntry(278, type: 3, count: 1, value: (uint)height),
            MakeEntry(279, type: 4, count: 1, value: (uint)(samples.Length * 2)),
            MakeEntryInlineBytes(33422, cfaPattern),
            MakeEntry(50714, type: 3, count: 1, value: blackLevel),
            MakeEntry(50717, type: 3, count: 1, value: whiteLevel),
        };

        // IFD0 starts at offset 8. Size = 2 (count) + 12*count + 4 (next ptr).
        int ifdSize = 2 + 12 * entries.Count + 4;
        int stripOffset = 8 + ifdSize;
        entries[5] = MakeEntry(273, type: 4, count: 1, value: (uint)stripOffset);

        int totalSize = stripOffset + samples.Length * 2;
        var data = new byte[totalSize];
        // Header
        data[0] = (byte)'I'; data[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        // IFD
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)entries.Count);
        for (int i = 0; i < entries.Count; i++)
            Buffer.BlockCopy(entries[i], 0, data, 8 + 2 + 12 * i, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8 + 2 + 12 * entries.Count), 0);
        // Raw samples
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(stripOffset + i * 2), samples[i]);
        return data;
    }

    static byte[] MakeEntry(ushort tag, ushort type, uint count, uint value)
    {
        var e = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(4), count);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), value);
        return e;
    }

    // A minimal lossless JPEG stream: 2x2, 8-bit, 1 component, all four pixels
    // decode to 128 (the initial predictor for P=8).
    static byte[] AllZeroLosslessJpeg2x2() => new byte[]
    {
        0xFF, 0xD8,                                                                                  // SOI
        0xFF, 0xC3, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x02, 0x01, 0x00, 0x11, 0x00,                 // SOF3
        0xFF, 0xC4, 0x00, 0x14, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00,                                                                                         // DHT
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00,                                   // SOS
        0x0F,                                                                                         // entropy
        0xFF, 0xD9                                                                                    // EOI
    };

    static byte[] MakeEntryInlineBytes(ushort tag, byte[] data)
    {
        // type 7 UNDEFINED, count = data.Length (must be <= 4 for inline storage).
        var e = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(4), (uint)data.Length);
        for (int i = 0; i < Math.Min(4, data.Length); i++)
            e[8 + i] = data[i];
        return e;
    }

    // Build a minimal LinearRaw (photometric 34892) DNG with 3 interleaved
    // 16-bit samples per pixel and a single strip.
    static byte[] BuildMinimalLinearRawDng(ushort[] samples, int width, int height,
        uint blackLevel, uint whiteLevel)
    {
        var entries = new List<byte[]>
        {
            MakeEntry(256, 3, 1, (uint)width),
            MakeEntry(257, 3, 1, (uint)height),
            MakeEntry(258, 3, 1, 16),
            MakeEntry(259, 3, 1, 1),                 // uncompressed
            MakeEntry(262, 3, 1, 34892),             // LinearRaw
            null,                                    // 273 StripOffsets (filled below)
            MakeEntry(277, 3, 1, 3),                 // SamplesPerPixel = 3
            MakeEntry(278, 3, 1, (uint)height),      // RowsPerStrip
            MakeEntry(279, 4, 1, (uint)(samples.Length * 2)), // StripByteCounts
            MakeEntry(50714, 3, 1, blackLevel),
            MakeEntry(50717, 3, 1, whiteLevel),
        };
        int ifdSize = 2 + 12 * entries.Count + 4;
        int stripOffset = 8 + ifdSize;
        entries[5] = MakeEntry(273, 4, 1, (uint)stripOffset);

        int totalSize = stripOffset + samples.Length * 2;
        var data = new byte[totalSize];
        data[0] = (byte)'I'; data[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)entries.Count);
        for (int i = 0; i < entries.Count; i++)
            Buffer.BlockCopy(entries[i], 0, data, 8 + 2 + 12 * i, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8 + 2 + 12 * entries.Count), 0);
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(stripOffset + i * 2), samples[i]);
        return data;
    }

    // Build a minimal tiled uncompressed CFA DNG. `tiles` is row-major
    // (tilesAcross then tilesDown); each tile is tileWidth*tileHeight samples.
    static byte[] BuildMinimalTiledCfaDng(ushort[][] tiles, int width, int height,
        int tileWidth, int tileHeight, byte[] cfaPattern, uint blackLevel, uint whiteLevel)
    {
        // Serialize each tile's samples to little-endian byte payload and
        // delegate to the byte-payload variant.
        var tileBytePayloads = new byte[tiles.Length][];
        for (int t = 0; t < tiles.Length; t++)
        {
            var payload = new byte[tiles[t].Length * 2];
            for (int i = 0; i < tiles[t].Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(i * 2), tiles[t][i]);
            tileBytePayloads[t] = payload;
        }
        return BuildMinimalTiledCfaDng(tileBytePayloads, width, height, tileWidth, tileHeight,
            cfaPattern, blackLevel, whiteLevel, compression: 1, bitsPerSample: 16);
    }

    // Same as above but takes pre-encoded tile byte payloads (so callers can
    // pass Lossless JPEG bytes for compression=7 tests).
    static byte[] BuildMinimalTiledCfaDng(byte[][] tileBytePayloads, int width, int height,
        int tileWidth, int tileHeight, byte[] cfaPattern, uint blackLevel, uint whiteLevel,
        uint compression, uint bitsPerSample)
    {
        int tilesAcross = (width + tileWidth - 1) / tileWidth;
        int tilesDown = (height + tileHeight - 1) / tileHeight;
        int tileCount = tileBytePayloads.Length;

        var entries = new List<byte[]>
        {
            MakeEntry(256, 3, 1, (uint)width),
            MakeEntry(257, 3, 1, (uint)height),
            MakeEntry(258, 3, 1, bitsPerSample),
            MakeEntry(259, 3, 1, compression),
            MakeEntry(262, 3, 1, 32803),
            MakeEntry(277, 3, 1, 1),
            MakeEntry(322, 3, 1, (uint)tileWidth),
            MakeEntry(323, 3, 1, (uint)tileHeight),
            null,                                  // 324 TileOffsets (filled below)
            null,                                  // 325 TileByteCounts (filled below)
            MakeEntryInlineBytes(33422, cfaPattern),
            MakeEntry(50714, 3, 1, blackLevel),
            MakeEntry(50717, 3, 1, whiteLevel),
        };

        int ifdSize = 2 + 12 * entries.Count + 4;
        int tileDataBlock = 8 + ifdSize;

        // Per-tile absolute offsets and sizes inside the file.
        var tileOffsetsInFile = new int[tileCount];
        int cursor = tileDataBlock;
        for (int t = 0; t < tileCount; t++)
        {
            tileOffsetsInFile[t] = cursor;
            cursor += tileBytePayloads[t].Length;
        }

        int tileOffsetsArrayOffset = cursor;
        int tileByteCountsArrayOffset = tileOffsetsArrayOffset + tileCount * 4;
        int totalSize = tileByteCountsArrayOffset + tileCount * 4;
        var data = new byte[totalSize];

        if (tileCount == 1)
        {
            entries[8] = MakeEntry(324, 4, 1, (uint)tileOffsetsInFile[0]);
            entries[9] = MakeEntry(325, 4, 1, (uint)tileBytePayloads[0].Length);
        }
        else
        {
            entries[8] = MakeEntry(324, 4, (uint)tileCount, (uint)tileOffsetsArrayOffset);
            entries[9] = MakeEntry(325, 4, (uint)tileCount, (uint)tileByteCountsArrayOffset);
        }

        data[0] = (byte)'I'; data[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)entries.Count);
        for (int i = 0; i < entries.Count; i++)
            Buffer.BlockCopy(entries[i], 0, data, 8 + 2 + 12 * i, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8 + 2 + 12 * entries.Count), 0);

        for (int t = 0; t < tileCount; t++)
        {
            Buffer.BlockCopy(tileBytePayloads[t], 0, data, tileOffsetsInFile[t], tileBytePayloads[t].Length);
            if (tileCount > 1)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tileOffsetsArrayOffset + t * 4), (uint)tileOffsetsInFile[t]);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tileByteCountsArrayOffset + t * 4), (uint)tileBytePayloads[t].Length);
            }
        }
        return data;
    }
}
