using System;
using System.Collections.Generic;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class LosslessJpegDecoderTests
{
    [Fact]
    public void DecodesAllZeroDifferences()
    {
        // 2x2, 8-bit, 1 component, predictor=1, Huffman code "0" -> category 0
        // (zero difference). Every decoded sample collapses to the initial
        // predictor value 2^(P-Pt-1) = 2^7 = 128.
        var stream = BuildAllZeroLosslessJpeg2x2();

        var samples = LosslessJpegDecoder.Decode(stream, out int w, out int h, out int p, out int comps);

        Assert.Equal(2, w);
        Assert.Equal(2, h);
        Assert.Equal(8, p);
        Assert.Equal(1, comps);
        Assert.Equal(new ushort[] { 128, 128, 128, 128 }, samples);
    }

    [Fact]
    public void DecodesMixedDifferences()
    {
        // Same 2x2/8-bit/1-component shape but using a tiny Huffman table:
        //   code "0"  (1 bit) -> category 0  (difference = 0)
        //   code "10" (2 bits)-> category 1  (difference = +1 if bit=1, -1 if bit=0)
        // Sample plan: differences [0, +1, -1, 0] applied with predictor 1 (A=left).
        //   pixel(0,0): predictor = 128, diff = 0  -> 128
        //   pixel(1,0): predictor = 128, diff = +1 -> 129
        //   pixel(0,1): predictor = previous row's value at column 0 = 128, diff = -1 -> 127
        //   pixel(1,1): predictor = A = 127, diff = 0 -> 127
        var stream = BuildMixedLosslessJpeg();

        var samples = LosslessJpegDecoder.Decode(stream, out _, out _, out _, out _);

        Assert.Equal(new ushort[] { 128, 129, 127, 127 }, samples);
    }

    // Builds a complete lossless JPEG stream for a 2x2/8-bit/1-component image
    // with all zero differences (every sample becomes the initial predictor 128).
    static byte[] BuildAllZeroLosslessJpeg2x2()
    {
        var b = new List<byte>();
        // SOI
        b.AddRange(new byte[] { 0xFF, 0xD8 });
        // SOF3
        b.AddRange(new byte[] { 0xFF, 0xC3, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x02, 0x01, 0x00, 0x11, 0x00 });
        // DHT (single code of length 1 -> value 0)
        b.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x14, 0x00,
                                0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                                0x00 });
        // SOS (1 component, predictor=1)
        b.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00 });
        // Entropy data: 4 codes "0" = 4 bits, padded with 1s -> 0x0F
        b.Add(0x0F);
        // EOI
        b.AddRange(new byte[] { 0xFF, 0xD9 });
        return b.ToArray();
    }

    // Differences [0, +1, -1, 0] with the two-code table described above.
    static byte[] BuildMixedLosslessJpeg()
    {
        // Codes:
        //   value 0 (length 1) -> "0"
        //   value 1 (length 2) -> "10"
        // Sequence to encode (per sample: category bits, then magnitude bits):
        //   diff  0: "0"
        //   diff +1: "10" then magnitude 1 (1 bit) = "1"   -> "101"
        //   diff -1: "10" then magnitude 1 (1 bit) = "0"   -> "100"
        //   diff  0: "0"
        // Concatenated bits: 0 101 100 0 = "01011000" -> 0x58
        var b = new List<byte>();
        b.AddRange(new byte[] { 0xFF, 0xD8 });
        // SOF3: same as above
        b.AddRange(new byte[] { 0xFF, 0xC3, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x02, 0x01, 0x00, 0x11, 0x00 });
        // DHT: BITS[1]=1, BITS[2]=1, rest 0; HUFFVAL = 0, 1
        // Length: 2 (segLen) + 1 (TcTh) + 16 (BITS) + 2 (HUFFVAL) = 21 bytes
        b.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x15, 0x00,
                                0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                                0x00, 0x01 });
        // SOS
        b.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00 });
        // Entropy data
        b.Add(0x58);
        // EOI
        b.AddRange(new byte[] { 0xFF, 0xD9 });
        return b.ToArray();
    }
}
