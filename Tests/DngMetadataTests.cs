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

    [Fact]
    public void PrefersRawSubIfdForImageShapeTags()
    {
        // Build a TIFF that mimics the FiveK DNGs: IFD0 is a tiny RGB thumbnail,
        // a SubIFD holds the actual CFA raw image. Camera tags belong to IFD0;
        // image-shape tags must come from the raw SubIFD.
        var tiff = BuildTiffWithRawSubIfd();

        var entries = DngMetadata.Read(tiff);
        var byName = new Dictionary<string, string>();
        foreach (var e in entries) byName[e.Name] = e.Value;

        Assert.Equal("ACME", byName["Make"]);
        Assert.Equal("Camera X", byName["Model"]);
        // Raw image is 1000x2000, thumbnail in IFD0 is 10x20 — must report raw.
        Assert.Equal("1000", byName["Image Width"]);
        Assert.Equal("2000", byName["Image Length"]);
        Assert.Equal("Lossless JPEG", byName["Compression"]);
        Assert.Equal("CFA", byName["Photometric Interpretation"]);
    }

    static byte[] BuildTiffWithRawSubIfd()
    {
        const int IFD0_OFFSET = 8;
        const int IFD0_ENTRIES = 7;
        int ifd0Size = 2 + 12 * IFD0_ENTRIES + 4;
        int subIfdOffset = IFD0_OFFSET + ifd0Size;
        const int SUBIFD_ENTRIES = 4;
        int subIfdSize = 2 + 12 * SUBIFD_ENTRIES + 4;
        int makeOffset = subIfdOffset + subIfdSize;
        const string make = "ACME\0";
        const string model = "Camera X\0";
        int modelOffset = makeOffset + make.Length;
        int totalSize = modelOffset + model.Length;

        var data = new byte[totalSize];
        data[0] = (byte)'I'; data[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), IFD0_OFFSET);

        // IFD0: thumbnail-style entries plus a SubIFDs pointer.
        var ifd0 = new[]
        {
            MakeEntry(256, 3, 1, 10),                                       // ImageWidth (thumbnail)
            MakeEntry(257, 3, 1, 20),                                       // ImageLength (thumbnail)
            MakeEntry(259, 3, 1, 1),                                        // Compression = uncompressed
            MakeEntry(262, 3, 1, 2),                                        // Photometric = RGB
            MakeAsciiEntry(271, make.Length, makeOffset),                   // Make
            MakeAsciiEntry(272, model.Length, modelOffset),                 // Model
            MakeEntry(330, 4, 1, (uint)subIfdOffset),                       // SubIFDs
        };
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(IFD0_OFFSET), (ushort)ifd0.Length);
        for (int i = 0; i < ifd0.Length; i++)
            Buffer.BlockCopy(ifd0[i], 0, data, IFD0_OFFSET + 2 + 12 * i, 12);

        // SubIFD: the actual raw image.
        var sub = new[]
        {
            MakeEntry(256, 3, 1, 1000),                                     // raw ImageWidth
            MakeEntry(257, 3, 1, 2000),                                     // raw ImageLength
            MakeEntry(259, 3, 1, 7),                                        // Compression = Lossless JPEG
            MakeEntry(262, 3, 1, 32803),                                    // Photometric = CFA
        };
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(subIfdOffset), (ushort)sub.Length);
        for (int i = 0; i < sub.Length; i++)
            Buffer.BlockCopy(sub[i], 0, data, subIfdOffset + 2 + 12 * i, 12);

        for (int i = 0; i < make.Length; i++) data[makeOffset + i] = (byte)make[i];
        for (int i = 0; i < model.Length; i++) data[modelOffset + i] = (byte)model[i];
        return data;
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
