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
    public void Read_ThrowsClearErrorForCompressedDng()
    {
        var tiff = BuildMinimalCfaDng(new ushort[] { 0, 0, 0, 0 }, 2, 2,
            cfaPattern: new byte[] { 0, 1, 1, 2 }, blackLevel: 0, whiteLevel: 65535,
            compression: 7);

        var ex = Assert.Throws<NotSupportedException>(() => DngRawReader.Read(tiff));
        Assert.Contains("uncompressed", ex.Message, StringComparison.OrdinalIgnoreCase);
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
}
