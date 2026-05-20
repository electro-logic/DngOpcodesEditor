using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace DngOpcodesEditor;

// Minimal TIFF/DNG IFD reader+writer focused on the OpcodeList tags.
//
// DNG is a TIFF container. The OpcodeList1/2/3 byte payloads (the bytes that
// OpcodesReader/OpcodesWriter consume) live in tags 51008/51009/51022, type 7
// UNDEFINED. They may sit in IFD0 or in any SubIFD (typically the SubIFD that
// holds the raw image).
//
// This class walks the IFD chain (IFD0 -> next IFDs) and every SubIFD, so it
// finds and rewrites OpcodeList tags wherever they're stored without depending
// on exiftool.
public static class TiffFile
{
    const ushort TAG_SUBIFDS = 330;
    const ushort TYPE_UNDEFINED = 7;
    const ushort TYPE_LONG = 4;

    public static ushort OpcodeListTag(int listIndex) => listIndex switch
    {
        1 => 51008,
        2 => 51009,
        3 => 51022,
        _ => throw new ArgumentOutOfRangeException(nameof(listIndex), "Expected 1, 2 or 3")
    };

    // Returns the OpcodeList tag payload (the bytes consumed by OpcodesReader)
    // or null if the tag is not present.
    public static byte[] ReadOpcodeList(byte[] tiff, int listIndex)
    {
        ushort target = OpcodeListTag(listIndex);
        bool isLE = IsLittleEndian(tiff);
        uint firstIfd = ReadUInt32(tiff, 4, isLE);
        foreach (uint ifdOffset in EnumerateIfds(tiff, isLE, firstIfd))
        {
            int entry = FindEntry(tiff, isLE, (int)ifdOffset, target);
            if (entry >= 0)
                return ReadEntryBytes(tiff, isLE, entry);
        }
        return null;
    }

    // Returns a new TIFF byte array with the OpcodeList tag set to `payload`.
    //
    // - If the tag already exists (in any IFD or SubIFD), its entry is rewritten
    //   in place to point at the new payload appended at the end of the file.
    // - Otherwise the entry is added to IFD0 (the IFD0 itself is rewritten at
    //   the end of file and the TIFF header's first-IFD-offset is updated).
    //
    // Orphaned old payloads/IFDs are not reclaimed; the file grows slightly.
    public static byte[] WriteOpcodeList(byte[] tiff, int listIndex, byte[] payload)
    {
        ushort target = OpcodeListTag(listIndex);
        bool isLE = IsLittleEndian(tiff);
        uint firstIfd = ReadUInt32(tiff, 4, isLE);

        // Per TIFF spec, when the data fits in 4 bytes it is stored inline in
        // the value/offset field; otherwise the value field is an offset to it.
        bool inline = payload.Length <= 4;
        byte[] working;
        uint payloadOffset = 0;
        if (inline)
        {
            working = (byte[])tiff.Clone();
        }
        else
        {
            working = new byte[tiff.Length + payload.Length];
            Buffer.BlockCopy(tiff, 0, working, 0, tiff.Length);
            Buffer.BlockCopy(payload, 0, working, tiff.Length, payload.Length);
            payloadOffset = (uint)tiff.Length;
        }

        // Replace the existing tag in place when possible.
        foreach (uint ifdOffset in EnumerateIfds(working, isLE, firstIfd))
        {
            int entry = FindEntry(working, isLE, (int)ifdOffset, target);
            if (entry >= 0)
            {
                WriteUInt16(working, entry + 2, TYPE_UNDEFINED, isLE);
                WriteUInt32(working, entry + 4, (uint)payload.Length, isLE);
                WriteValueField(working, entry + 8, payload, inline, payloadOffset, isLE);
                return working;
            }
        }

        // Not found: rewrite IFD0 with the new entry appended.
        return AppendToIfd0(working, isLE, firstIfd, target, payload, inline, payloadOffset);
    }

    static byte[] AppendToIfd0(byte[] file, bool isLE, uint firstIfd, ushort tag, byte[] payload, bool inline, uint payloadOffset)
    {
        ushort count = ReadUInt16(file, (int)firstIfd, isLE);
        int entriesStart = (int)firstIfd + 2;
        var entries = new List<byte[]>(count + 1);
        for (int i = 0; i < count; i++)
        {
            var e = new byte[12];
            Buffer.BlockCopy(file, entriesStart + 12 * i, e, 0, 12);
            entries.Add(e);
        }
        var newEntry = new byte[12];
        WriteUInt16(newEntry, 0, tag, isLE);
        WriteUInt16(newEntry, 2, TYPE_UNDEFINED, isLE);
        WriteUInt32(newEntry, 4, (uint)payload.Length, isLE);
        WriteValueField(newEntry, 8, payload, inline, payloadOffset, isLE);
        entries.Add(newEntry);
        // TIFF spec: IFD entries must be sorted by tag.
        entries.Sort((a, b) => ReadUInt16(a, 0, isLE).CompareTo(ReadUInt16(b, 0, isLE)));
        uint nextIfd = ReadUInt32(file, entriesStart + 12 * count, isLE);

        int newIfdSize = 2 + 12 * entries.Count + 4;
        var result = new byte[file.Length + newIfdSize];
        Buffer.BlockCopy(file, 0, result, 0, file.Length);
        int newIfdAt = file.Length;
        WriteUInt16(result, newIfdAt, (ushort)entries.Count, isLE);
        for (int i = 0; i < entries.Count; i++)
            Buffer.BlockCopy(entries[i], 0, result, newIfdAt + 2 + 12 * i, 12);
        WriteUInt32(result, newIfdAt + 2 + 12 * entries.Count, nextIfd, isLE);
        // Point the TIFF header at the new IFD0.
        WriteUInt32(result, 4, (uint)newIfdAt, isLE);
        return result;
    }

    public static (bool isLittleEndian, uint firstIfdOffset) ReadHeader(byte[] tiff)
    {
        bool isLE = IsLittleEndian(tiff);
        uint firstIfd = ReadUInt32(tiff, 4, isLE);
        return (isLE, firstIfd);
    }
    public static int FindEntryPublic(byte[] tiff, bool isLE, int ifdOffset, ushort tag) =>
        FindEntry(tiff, isLE, ifdOffset, tag);
    public static IEnumerable<uint> EnumerateIfdsPublic(byte[] tiff, bool isLE, uint firstIfd) =>
        EnumerateIfds(tiff, isLE, firstIfd);
    public static byte[] ReadEntryBytesPublic(byte[] tiff, bool isLE, int entryOffset) =>
        ReadEntryBytes(tiff, isLE, entryOffset);
    // Reads a TIFF entry's value as a double[] regardless of underlying type.
    // Useful for tags whose precision matters (RATIONAL, SRATIONAL, FLOAT,
    // DOUBLE) such as AsShotNeutral and ColorMatrix.
    public static double[] ReadEntryAsDoubleArray(byte[] tiff, bool isLE, int entryOffset)
    {
        ushort type = ReadUInt16(tiff, entryOffset + 2, isLE);
        uint count = ReadUInt32(tiff, entryOffset + 4, isLE);
        int elementSize = TypeSize(type);
        int totalBytes = (int)count * elementSize;
        int dataOffset = totalBytes <= 4 ? entryOffset + 8 : (int)ReadUInt32(tiff, entryOffset + 8, isLE);
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            switch (type)
            {
                case 1 or 7: result[i] = tiff[dataOffset + i]; break;
                case 3: result[i] = ReadUInt16(tiff, dataOffset + i * 2, isLE); break;
                case 4: result[i] = ReadUInt32(tiff, dataOffset + i * 4, isLE); break;
                case 8: result[i] = (short)ReadUInt16(tiff, dataOffset + i * 2, isLE); break;
                case 9: result[i] = (int)ReadUInt32(tiff, dataOffset + i * 4, isLE); break;
                case 5:
                {
                    uint num = ReadUInt32(tiff, dataOffset + i * 8, isLE);
                    uint den = ReadUInt32(tiff, dataOffset + i * 8 + 4, isLE);
                    result[i] = den != 0 ? (double)num / den : 0;
                    break;
                }
                case 10:
                {
                    int num = (int)ReadUInt32(tiff, dataOffset + i * 8, isLE);
                    int den = (int)ReadUInt32(tiff, dataOffset + i * 8 + 4, isLE);
                    result[i] = den != 0 ? (double)num / den : 0;
                    break;
                }
                case 11:
                    result[i] = isLE
                        ? System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tiff.AsSpan(dataOffset + i * 4))
                        : System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(tiff.AsSpan(dataOffset + i * 4));
                    break;
                case 12:
                    result[i] = isLE
                        ? System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(tiff.AsSpan(dataOffset + i * 8))
                        : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(tiff.AsSpan(dataOffset + i * 8));
                    break;
                default: result[i] = 0; break;
            }
        }
        return result;
    }
    public static uint[] ReadEntryAsUInt32Array(byte[] tiff, bool isLE, int entryOffset)
    {
        ushort type = ReadUInt16(tiff, entryOffset + 2, isLE);
        uint count = ReadUInt32(tiff, entryOffset + 4, isLE);
        var result = new uint[count];
        int elementSize = TypeSize(type);
        int totalBytes = (int)count * elementSize;
        int dataOffset = totalBytes <= 4 ? entryOffset + 8 : (int)ReadUInt32(tiff, entryOffset + 8, isLE);
        for (int i = 0; i < count; i++)
        {
            if (type == 3 || type == 8)
                result[i] = ReadUInt16(tiff, dataOffset + i * 2, isLE);
            else if (type == 4 || type == 9)
                result[i] = ReadUInt32(tiff, dataOffset + i * 4, isLE);
            else if (type == 5 || type == 10)
            {
                // RATIONAL: numerator/denominator, both uint32. Truncate to int.
                uint num = ReadUInt32(tiff, dataOffset + i * 8, isLE);
                uint denom = ReadUInt32(tiff, dataOffset + i * 8 + 4, isLE);
                result[i] = denom != 0 ? num / denom : 0;
            }
            else if (type == 1 || type == 7)
                result[i] = tiff[dataOffset + i];
            else
                result[i] = 0;
        }
        return result;
    }
    static bool IsLittleEndian(byte[] tiff)
    {
        if (tiff.Length < 8)
            throw new InvalidDataException("File is too small to be a TIFF.");
        if (tiff[0] == 'I' && tiff[1] == 'I')
            return true;
        if (tiff[0] == 'M' && tiff[1] == 'M')
            return false;
        throw new InvalidDataException("Not a TIFF file (missing II/MM byte-order marker).");
    }

    static IEnumerable<uint> EnumerateIfds(byte[] tiff, bool isLE, uint firstIfd)
    {
        var visited = new HashSet<uint>();
        var queue = new Queue<uint>();
        queue.Enqueue(firstIfd);
        while (queue.Count > 0)
        {
            uint ifd = queue.Dequeue();
            if (ifd == 0 || ifd >= tiff.Length || !visited.Add(ifd))
                continue;
            yield return ifd;
            ushort count = ReadUInt16(tiff, (int)ifd, isLE);
            int endOfEntries = (int)ifd + 2 + 12 * count;
            if (endOfEntries + 4 <= tiff.Length)
            {
                uint nextIfd = ReadUInt32(tiff, endOfEntries, isLE);
                if (nextIfd != 0)
                    queue.Enqueue(nextIfd);
            }
            // Follow SubIFDs (tag 330) — common for the raw-image IFD in DNG.
            int subEntry = FindEntry(tiff, isLE, (int)ifd, TAG_SUBIFDS);
            if (subEntry >= 0)
            {
                uint subCount = ReadUInt32(tiff, subEntry + 4, isLE);
                uint subValue = ReadUInt32(tiff, subEntry + 8, isLE);
                if (subCount == 1)
                {
                    queue.Enqueue(subValue);
                }
                else if (subCount > 1)
                {
                    for (int i = 0; i < subCount; i++)
                    {
                        uint subOff = ReadUInt32(tiff, (int)subValue + i * 4, isLE);
                        queue.Enqueue(subOff);
                    }
                }
            }
        }
    }

    static int FindEntry(byte[] tiff, bool isLE, int ifdOffset, ushort tag)
    {
        if (ifdOffset + 2 > tiff.Length)
            return -1;
        ushort count = ReadUInt16(tiff, ifdOffset, isLE);
        for (int i = 0; i < count; i++)
        {
            int entryOffset = ifdOffset + 2 + 12 * i;
            if (entryOffset + 12 > tiff.Length)
                return -1;
            if (ReadUInt16(tiff, entryOffset, isLE) == tag)
                return entryOffset;
        }
        return -1;
    }

    static byte[] ReadEntryBytes(byte[] tiff, bool isLE, int entryOffset)
    {
        ushort type = ReadUInt16(tiff, entryOffset + 2, isLE);
        uint count = ReadUInt32(tiff, entryOffset + 4, isLE);
        int elementSize = TypeSize(type);
        int totalBytes = (int)count * elementSize;
        var result = new byte[totalBytes];
        if (totalBytes <= 4)
        {
            // Inline value (the four value bytes hold the data directly).
            Buffer.BlockCopy(tiff, entryOffset + 8, result, 0, totalBytes);
        }
        else
        {
            uint valueOffset = ReadUInt32(tiff, entryOffset + 8, isLE);
            Buffer.BlockCopy(tiff, (int)valueOffset, result, 0, totalBytes);
        }
        return result;
    }

    // Formats the value of an IFD entry into a human-readable string.
    // Handles ASCII strings, integer arrays, rationals and floats — enough to
    // surface common EXIF / DNG tags in the metadata viewer.
    public static string FormatEntryValue(byte[] tiff, bool isLE, int entryOffset)
    {
        ushort type = ReadUInt16(tiff, entryOffset + 2, isLE);
        uint count = ReadUInt32(tiff, entryOffset + 4, isLE);
        int elementSize = TypeSize(type);
        int totalBytes = (int)count * elementSize;
        int dataOffset = totalBytes <= 4 ? entryOffset + 8 : (int)ReadUInt32(tiff, entryOffset + 8, isLE);

        if (type == 2) // ASCII (null-terminated)
        {
            int end = Math.Min((int)count, tiff.Length - dataOffset);
            int nul = Array.IndexOf(tiff, (byte)0, dataOffset, end);
            int len = nul >= 0 ? nul - dataOffset : end;
            return System.Text.Encoding.ASCII.GetString(tiff, dataOffset, len).Trim();
        }
        if (type == 7) // UNDEFINED — show as space-separated bytes
        {
            var bytes = new string[count];
            for (int i = 0; i < count; i++) bytes[i] = tiff[dataOffset + i].ToString();
            return count > 16
                ? string.Join(" ", bytes, 0, 16) + $" ... ({count} bytes)"
                : string.Join(" ", bytes);
        }
        if (type == 5 || type == 10) // RATIONAL / SRATIONAL
        {
            var parts = new string[count];
            for (int i = 0; i < count; i++)
            {
                uint num = ReadUInt32(tiff, dataOffset + i * 8, isLE);
                uint den = ReadUInt32(tiff, dataOffset + i * 8 + 4, isLE);
                if (type == 10)
                    parts[i] = den != 0 ? ((double)(int)num / (int)den).ToString("G6") : "0";
                else
                    parts[i] = den != 0 ? ((double)num / den).ToString("G6") : "0";
            }
            return string.Join(", ", parts);
        }
        if (type == 11 || type == 12) // FLOAT / DOUBLE
        {
            var parts = new string[count];
            for (int i = 0; i < count; i++)
            {
                if (type == 11)
                {
                    float f = isLE
                        ? System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tiff.AsSpan(dataOffset + i * 4))
                        : System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(tiff.AsSpan(dataOffset + i * 4));
                    parts[i] = f.ToString("G6");
                }
                else
                {
                    double d = isLE
                        ? System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(tiff.AsSpan(dataOffset + i * 8))
                        : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(tiff.AsSpan(dataOffset + i * 8));
                    parts[i] = d.ToString("G6");
                }
            }
            return string.Join(", ", parts);
        }
        // Default: integer types — read via the existing helper.
        var values = ReadEntryAsUInt32Array(tiff, isLE, entryOffset);
        if (values.Length > 16)
        {
            var head = new string[16];
            for (int i = 0; i < 16; i++) head[i] = values[i].ToString();
            return string.Join(", ", head) + $" ... ({values.Length} values)";
        }
        return string.Join(", ", values);
    }

    static void WriteValueField(byte[] data, int offset, byte[] payload, bool inline, uint payloadOffset, bool isLE)
    {
        if (inline)
        {
            // Spec: the 4-byte value field holds the data left-aligned, padded
            // with zeros. (Endianness only matters for typed numeric values;
            // for UNDEFINED bytes the order is the natural byte order.)
            for (int i = 0; i < 4; i++)
                data[offset + i] = i < payload.Length ? payload[i] : (byte)0;
        }
        else
        {
            WriteUInt32(data, offset, payloadOffset, isLE);
        }
    }

    static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 1
    };

    static ushort ReadUInt16(byte[] data, int offset, bool isLE) =>
        isLE ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
             : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    static uint ReadUInt32(byte[] data, int offset, bool isLE) =>
        isLE ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
             : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    static void WriteUInt16(byte[] data, int offset, ushort value, bool isLE)
    {
        if (isLE) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);
    }
    static void WriteUInt32(byte[] data, int offset, uint value, bool isLE)
    {
        if (isLE) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
    }
}
