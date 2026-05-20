using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class SrgbGammaTests
{
    [Fact]
    public void EncodeDecodeRoundTrips()
    {
        // Spot-check a handful of values across the curve.
        var pixels = new ulong[]
        {
            0UL                | (16384UL << 16) | (32768UL << 32) | (65535UL << 48),
            8192UL             | (49152UL << 16) | (65535UL << 32) | (65535UL << 48),
            1000UL             | (10000UL << 16) | (60000UL << 32) | (65535UL << 48),
        };
        var original = new PixelBuffer((ulong[])pixels.Clone(), 3, 1);
        var buf = new PixelBuffer(pixels, 3, 1);

        OpcodesImplementation.ApplySrgbEncode(buf);
        OpcodesImplementation.ApplySrgbDecode(buf);

        for (int x = 0; x < 3; x++)
        {
            var got = buf.GetRgb16Pixel(x, 0);
            var want = original.GetRgb16Pixel(x, 0);
            // Allow a small tolerance for FP round-trip + ushort quantisation.
            Assert.InRange(got[0], want[0] - 3, want[0] + 3);
            Assert.InRange(got[1], want[1] - 3, want[1] + 3);
            Assert.InRange(got[2], want[2] - 3, want[2] + 3);
        }
    }

    [Fact]
    public void EncodeBlackStaysBlackAndWhiteStaysWhite()
    {
        var pixels = new ulong[]
        {
            0UL | (0UL << 16) | (0UL << 32) | (65535UL << 48),
            65535UL | (65535UL << 16) | (65535UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 2, 1);

        OpcodesImplementation.ApplySrgbEncode(buf);

        var black = buf.GetRgb16Pixel(0, 0);
        var white = buf.GetRgb16Pixel(1, 0);
        Assert.Equal(0, black[0]);
        Assert.Equal(0, black[1]);
        Assert.Equal(0, black[2]);
        Assert.Equal(65535, white[0]);
        Assert.Equal(65535, white[1]);
        Assert.Equal(65535, white[2]);
    }

    [Fact]
    public void EncodeDarkValueUsesLinearSegment()
    {
        // Below 0.0031308 the OETF is linear with slope 12.92, so the input
        // and output (both as fractions in [0,1]) should satisfy out ≈ 12.92*in.
        ushort input = 66; // ≈ 0.001007 in normalised space
        double inputFrac = input / 65535.0;
        var pixels = new ulong[] { (ulong)input | ((ulong)input << 16) | ((ulong)input << 32) | (65535UL << 48) };
        var buf = new PixelBuffer(pixels, 1, 1);

        OpcodesImplementation.ApplySrgbEncode(buf);

        ushort expected = (ushort)Math.Round(12.92 * inputFrac * 65535);
        var got = buf.GetRgb16Pixel(0, 0);
        Assert.InRange(got[0], expected - 1, expected + 1);
    }

    [Fact]
    public void MidGreyMapsToExpectedSrgbValue()
    {
        // Linear 0.5 -> sRGB 1.055 * 0.5^(1/2.4) - 0.055 ≈ 0.7354
        var pixels = new ulong[] { 32768UL | (32768UL << 16) | (32768UL << 32) | (65535UL << 48) };
        var buf = new PixelBuffer(pixels, 1, 1);

        OpcodesImplementation.ApplySrgbEncode(buf);

        ushort expected = (ushort)Math.Round(0.7354 * 65535);
        var got = buf.GetRgb16Pixel(0, 0);
        Assert.InRange(got[0], expected - 50, expected + 50);
    }
}
