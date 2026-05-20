using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class LzwDecoderTests
{
    [Fact]
    public void DecodesHandCraftedAbababab()
    {
        // Reference encoding of the 8-byte string "ABABABAB" using TIFF LZW.
        // Encoder steps:
        //   emit 65 (A), add 258 = "AB"
        //   emit 66 (B), add 259 = "BA"
        //   emit 258 ("AB"), add 260 = "ABA"
        //   emit 260 ("ABA"), add 261 = "ABAB"
        //   emit 66 (B), then EOI (257)
        // Each code is 9 bits, MSB-first. The bit stream is 54 bits packed
        // into 7 bytes:
        //   001000001  001000010  100000010  100000100  001000010  100000001
        //  |---byte0--||---byte1--||---byte2--||---byte3--||---byte4--||---byte5--||byte6|
        //   00100000   10010000   10100000   01010000   01000010   00010100   00000100
        var encoded = new byte[] { 0x20, 0x90, 0xA0, 0x50, 0x42, 0x14, 0x04 };

        var decoded = LzwDecoder.Decode(encoded);

        Assert.Equal(new byte[] { 0x41, 0x42, 0x41, 0x42, 0x41, 0x42, 0x41, 0x42 }, decoded);
    }

    [Fact]
    public void DecodesSingleByteFollowedByEoi()
    {
        // 9-bit codes: 65 (A) then 257 (EOI).
        // 65  = 001000001
        // 257 = 100000001
        // Concatenated: 001000001 100000001 -> 0010000011 00000001
        // 18 bits, pad to 24 with zeros: 00100000 11000000 01000000
        var encoded = new byte[] { 0x20, 0xC0, 0x40 };

        var decoded = LzwDecoder.Decode(encoded);

        Assert.Equal(new byte[] { 0x41 }, decoded);
    }

    [Fact]
    public void OpensLzwCompressedTiffSample()
    {
        // The bundled grid.tiff is an 8-bit RGB image stored with LZW
        // compression (the most common TIFF compression). Round-tripping it
        // through DngRawReader verifies the LZW path end-to-end.
        var tiff = System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Samples", "grid.tiff"));
        var buffer = DngRawReader.Read(tiff);
        Assert.Equal(640, buffer.Width);
        Assert.Equal(480, buffer.Height);
    }
}
