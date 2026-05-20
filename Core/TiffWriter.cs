using System;
using System.Buffers.Binary;
using System.IO;

namespace DngOpcodesEditor;

// Writes a PixelBuffer as a minimal uncompressed 16-bit-per-channel RGB TIFF.
//
// The output is the simplest layout a TIFF reader is required to handle:
// little-endian, single strip, chunky planar configuration, unsigned 16-bit
// samples. No compression, no tiling, no extra alpha channel — just the
// minimum a standard image viewer needs to open the file.
public static class TiffWriter
{
    public static void WriteRgb16(PixelBuffer buffer, string filename) =>
        File.WriteAllBytes(filename, WriteRgb16(buffer));

    public static byte[] WriteRgb16(PixelBuffer buffer)
    {
        int W = buffer.Width;
        int H = buffer.Height;
        int imageBytes = W * H * 6;                   // 3 channels * 2 bytes
        int imageOffset = 8;                          // right after the TIFF header
        int bpsOffset = imageOffset + imageBytes;     // BitsPerSample array (3 × SHORT = 6 bytes)
        int xresOffset = bpsOffset + 6;               // XResolution RATIONAL (8 bytes)
        int yresOffset = xresOffset + 8;              // YResolution RATIONAL (8 bytes)
        int sampleFormatOffset = yresOffset + 8;      // SampleFormat array (3 × SHORT = 6 bytes)
        int ifdOffset = sampleFormatOffset + 6;
        const int ENTRY_COUNT = 14;
        int ifdSize = 2 + ENTRY_COUNT * 12 + 4;
        int totalSize = ifdOffset + ifdSize;

        var output = new byte[totalSize];

        // TIFF header.
        output[0] = (byte)'I';
        output[1] = (byte)'I';
        W16(output, 2, 42);
        W32(output, 4, (uint)ifdOffset);

        // Image data: R, G, B 16-bit samples per pixel in row-major order.
        var pixels = buffer.Pixels;
        for (int i = 0; i < W * H; i++)
        {
            ulong p = pixels[i];
            int o = imageOffset + i * 6;
            W16(output, o + 0, (ushort)(p & 0xFFFF));
            W16(output, o + 2, (ushort)((p >> 16) & 0xFFFF));
            W16(output, o + 4, (ushort)((p >> 32) & 0xFFFF));
        }

        // BitsPerSample (16, 16, 16).
        W16(output, bpsOffset + 0, 16);
        W16(output, bpsOffset + 2, 16);
        W16(output, bpsOffset + 4, 16);

        // 96 dpi resolutions (just a sensible default).
        W32(output, xresOffset + 0, 96);
        W32(output, xresOffset + 4, 1);
        W32(output, yresOffset + 0, 96);
        W32(output, yresOffset + 4, 1);

        // SampleFormat (1, 1, 1 — unsigned integer per channel).
        W16(output, sampleFormatOffset + 0, 1);
        W16(output, sampleFormatOffset + 2, 1);
        W16(output, sampleFormatOffset + 4, 1);

        // IFD.
        W16(output, ifdOffset, ENTRY_COUNT);
        int entry = ifdOffset + 2;

        void Add(ushort tag, ushort type, uint count, uint valueOrOffset)
        {
            W16(output, entry + 0, tag);
            W16(output, entry + 2, type);
            W32(output, entry + 4, count);
            W32(output, entry + 8, valueOrOffset);
            entry += 12;
        }

        // Entries must appear in ascending tag order.
        Add(256, 4, 1, (uint)W);                   // ImageWidth (LONG)
        Add(257, 4, 1, (uint)H);                   // ImageLength
        Add(258, 3, 3, (uint)bpsOffset);           // BitsPerSample (SHORT[3])
        Add(259, 3, 1, 1);                         // Compression = uncompressed
        Add(262, 3, 1, 2);                         // PhotometricInterpretation = RGB
        Add(273, 4, 1, (uint)imageOffset);         // StripOffsets
        Add(277, 3, 1, 3);                         // SamplesPerPixel
        Add(278, 4, 1, (uint)H);                   // RowsPerStrip (single strip)
        Add(279, 4, 1, (uint)imageBytes);          // StripByteCounts
        Add(282, 5, 1, (uint)xresOffset);          // XResolution (RATIONAL)
        Add(283, 5, 1, (uint)yresOffset);          // YResolution
        Add(284, 3, 1, 1);                         // PlanarConfiguration = chunky
        Add(296, 3, 1, 2);                         // ResolutionUnit = inch
        Add(339, 3, 3, (uint)sampleFormatOffset);  // SampleFormat (SHORT[3])

        // Next IFD = none.
        W32(output, ifdOffset + 2 + ENTRY_COUNT * 12, 0);

        return output;
    }

    static void W16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
    static void W32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
}
