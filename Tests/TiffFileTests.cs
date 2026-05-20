using System;
using System.Buffers.Binary;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class TiffFileTests
{
    [Fact]
    public void WriteAndReadOpcodeList_OnEmptyTiff_AddsToIfd0()
    {
        // A minimal valid TIFF with an empty IFD0.
        var tiff = MinimalTiff(isLE: true);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        var modified = TiffFile.WriteOpcodeList(tiff, 3, payload);
        var read = TiffFile.ReadOpcodeList(modified, 3);

        Assert.Equal(payload, read);
    }

    [Fact]
    public void WriteOpcodeList_ReplacesExistingTagInPlace()
    {
        var tiff = MinimalTiff(isLE: true);
        var first = new byte[] { 1, 2, 3, 4, 5 };
        var second = new byte[] { 10, 20, 30, 40, 50, 60 };

        var step1 = TiffFile.WriteOpcodeList(tiff, 3, first);
        Assert.Equal(first, TiffFile.ReadOpcodeList(step1, 3));

        var step2 = TiffFile.WriteOpcodeList(step1, 3, second);
        Assert.Equal(second, TiffFile.ReadOpcodeList(step2, 3));
    }

    [Fact]
    public void WriteOpcodeList_HandlesMultipleListsIndependently()
    {
        var tiff = MinimalTiff(isLE: true);
        var p1 = new byte[] { 1, 1, 1, 1 };
        var p2 = new byte[] { 2, 2, 2, 2, 2 };
        var p3 = new byte[] { 3, 3, 3, 3, 3, 3 };

        var t = TiffFile.WriteOpcodeList(tiff, 1, p1);
        t = TiffFile.WriteOpcodeList(t, 2, p2);
        t = TiffFile.WriteOpcodeList(t, 3, p3);

        Assert.Equal(p1, TiffFile.ReadOpcodeList(t, 1));
        Assert.Equal(p2, TiffFile.ReadOpcodeList(t, 2));
        Assert.Equal(p3, TiffFile.ReadOpcodeList(t, 3));
    }

    [Fact]
    public void ReadOpcodeList_FindsTagInSubIfd()
    {
        // Build a TIFF where IFD0 has no OpcodeList and a single SubIFD does.
        var payload = new byte[] { 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48 };
        var tiff = TiffWithSubIfdOpcodeList(isLE: true, listIndex: 3, payload: payload);

        var read = TiffFile.ReadOpcodeList(tiff, 3);

        Assert.Equal(payload, read);
    }

    [Fact]
    public void WriteOpcodeList_ReplacesExistingSubIfdTag()
    {
        var initial = new byte[] { 0xAA, 0xBB, 0xCC };
        var replacement = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };
        var tiff = TiffWithSubIfdOpcodeList(isLE: true, listIndex: 3, payload: initial);

        var modified = TiffFile.WriteOpcodeList(tiff, 3, replacement);

        Assert.Equal(replacement, TiffFile.ReadOpcodeList(modified, 3));
    }

    [Fact]
    public void ReadOpcodeList_ReturnsNullWhenMissing()
    {
        var tiff = MinimalTiff(isLE: true);
        Assert.Null(TiffFile.ReadOpcodeList(tiff, 3));
    }

    [Fact]
    public void Works_With_BigEndianTiff()
    {
        var tiff = MinimalTiff(isLE: false);
        var payload = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        var modified = TiffFile.WriteOpcodeList(tiff, 2, payload);

        Assert.Equal(payload, TiffFile.ReadOpcodeList(modified, 2));
    }

    // Returns a TIFF with header, an empty IFD0 (count 0, next 0).
    static byte[] MinimalTiff(bool isLE)
    {
        var data = new byte[8 + 2 + 4];
        if (isLE) { data[0] = (byte)'I'; data[1] = (byte)'I'; }
        else { data[0] = (byte)'M'; data[1] = (byte)'M'; }
        Write16(data, 2, 42, isLE);   // magic
        Write32(data, 4, 8, isLE);    // first IFD offset
        Write16(data, 8, 0, isLE);    // IFD entry count
        Write32(data, 10, 0, isLE);   // next IFD = none
        return data;
    }

    // Returns a TIFF with IFD0 holding one SubIFDs entry (tag 330) pointing to a
    // SubIFD that contains the OpcodeList tag.
    static byte[] TiffWithSubIfdOpcodeList(bool isLE, int listIndex, byte[] payload)
    {
        // Layout:
        //   [0..8)   header
        //   [8..)    IFD0: count=1, entry(tag=330, type=4, count=1, value=<subIfd offset>), next=0
        //   subIfd:  count=1, entry(tag=opcode, type=7, count=N, value=<payload offset>), next=0
        //   payload: N bytes
        ushort opTag = TiffFile.OpcodeListTag(listIndex);

        // We pre-compute offsets.
        int ifd0Offset = 8;
        int ifd0Size = 2 + 12 + 4;             // count + 1 entry + next
        int subIfdOffset = ifd0Offset + ifd0Size;
        int subIfdSize = 2 + 12 + 4;
        int payloadOffset = subIfdOffset + subIfdSize;

        var data = new byte[payloadOffset + payload.Length];
        if (isLE) { data[0] = (byte)'I'; data[1] = (byte)'I'; }
        else { data[0] = (byte)'M'; data[1] = (byte)'M'; }
        Write16(data, 2, 42, isLE);
        Write32(data, 4, (uint)ifd0Offset, isLE);

        // IFD0
        Write16(data, ifd0Offset, 1, isLE);
        // Entry: tag=330, type=LONG(4), count=1, value=subIfdOffset
        Write16(data, ifd0Offset + 2, 330, isLE);
        Write16(data, ifd0Offset + 4, 4, isLE);
        Write32(data, ifd0Offset + 6, 1, isLE);
        Write32(data, ifd0Offset + 10, (uint)subIfdOffset, isLE);
        Write32(data, ifd0Offset + 14, 0, isLE);

        // SubIFD
        Write16(data, subIfdOffset, 1, isLE);
        // Entry: tag=opTag, type=UNDEFINED(7), count=payload.Length, value=payloadOffset
        Write16(data, subIfdOffset + 2, opTag, isLE);
        Write16(data, subIfdOffset + 4, 7, isLE);
        Write32(data, subIfdOffset + 6, (uint)payload.Length, isLE);
        // If payload <= 4 bytes the value would be inline; tests use longer payloads.
        Write32(data, subIfdOffset + 10, (uint)payloadOffset, isLE);
        Write32(data, subIfdOffset + 14, 0, isLE);

        Array.Copy(payload, 0, data, payloadOffset, payload.Length);
        return data;
    }

    static void Write16(byte[] data, int offset, ushort value, bool isLE)
    {
        if (isLE) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);
    }
    static void Write32(byte[] data, int offset, uint value, bool isLE)
    {
        if (isLE) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
    }
}
