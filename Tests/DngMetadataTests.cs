using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class DngMetadataTests
{
    [Fact]
    public void ReadsKnownTagsFromMinimalTiff()
    {
        var tiff = BuildTiffWithMetadata();

        var entries = DngMetadata.Read(tiff);

        var byName = new Dictionary<string, string>();
        foreach (var e in entries) byName[e.Name] = e.Value;

        Assert.Equal("Test Camera", byName["Make"]);
        Assert.Equal("DNG Model", byName["Model"]);
        Assert.Equal("100", byName["ISO"]);
        Assert.Equal("Uncompressed", byName["Compression"]);
        Assert.Equal("CFA", byName["Photometric Interpretation"]);
    }

    [Fact]
    public void NonTiffInputReturnsEmptyListWithoutThrowing()
    {
        var entries = DngMetadata.Read(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
        Assert.Empty(entries);
    }

    // Tiny TIFF with a handful of tags spanning ASCII, SHORT and LONG types so
    // FormatEntryValue and DecorateValue both get exercised.
    static byte[] BuildTiffWithMetadata()
    {
        // ASCII strings (must be null-terminated per TIFF spec).
        var make = "Test Camera\0";
        var model = "DNG Model\0";
        int makeOffset = 0;   // filled in
        int modelOffset = 0;

        // Pre-compute layout: header (8) + IFD then ASCII data after.
        var entryDefs = new List<byte[]>
        {
            MakeEntry(256, 3, 1, 2),                   // ImageWidth
            MakeEntry(257, 3, 1, 2),                   // ImageLength
            MakeEntry(259, 3, 1, 1),                   // Compression = 1
            MakeEntry(262, 3, 1, 32803),               // Photometric = CFA
            null,                                       // 271 Make (ASCII)
            null,                                       // 272 Model (ASCII)
            MakeEntry(34855, 3, 1, 100),               // ISO
        };
        int ifdSize = 2 + 12 * entryDefs.Count + 4;
        int asciiBlock = 8 + ifdSize;
        makeOffset = asciiBlock;
        modelOffset = makeOffset + make.Length;
        int totalSize = modelOffset + model.Length;

        entryDefs[4] = MakeAsciiEntry(271, make.Length, makeOffset);
        entryDefs[5] = MakeAsciiEntry(272, model.Length, modelOffset);

        var data = new byte[totalSize];
        data[0] = (byte)'I'; data[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)entryDefs.Count);
        for (int i = 0; i < entryDefs.Count; i++)
            System.Buffer.BlockCopy(entryDefs[i], 0, data, 8 + 2 + 12 * i, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8 + 2 + 12 * entryDefs.Count), 0);

        for (int i = 0; i < make.Length; i++) data[makeOffset + i] = (byte)make[i];
        for (int i = 0; i < model.Length; i++) data[modelOffset + i] = (byte)model[i];
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

    static byte[] MakeAsciiEntry(ushort tag, int length, int dataOffset)
    {
        var e = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), 2); // ASCII
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(4), (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), (uint)dataOffset);
        return e;
    }
}
