using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class ColorTransformTests
{
    [Fact]
    public void IdentityMatrixLeavesPixelsAlone()
    {
        var identity = new double[,]
        {
            { 1, 0, 0 },
            { 0, 1, 0 },
            { 0, 0, 1 },
        };
        var pixels = new ulong[]
        {
            10000UL | (20000UL << 16) | (30000UL << 32) | (65535UL << 48),
            65000UL | (40000UL << 16) | (55555UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 2, 1);

        ColorTransform.Apply(buf, identity);

        var p0 = buf.GetRgb16Pixel(0, 0);
        Assert.Equal(10000, p0[0]);
        Assert.Equal(20000, p0[1]);
        Assert.Equal(30000, p0[2]);
    }

    [Fact]
    public void Invert3x3RoundTrips()
    {
        var m = new double[,]
        {
            { 0.8575, -0.3219, -0.0868 },
            { -0.3351, 1.1451, 0.1593 },
            { 0.0207, 0.0468, 0.4876 },
        };
        var inv = ColorTransform.Invert3x3(m);
        var product = ColorTransform.Multiply3x3(m, inv);
        // m * m^-1 should be the identity matrix (within FP epsilon).
        Assert.Equal(1.0, product[0, 0], 6);
        Assert.Equal(1.0, product[1, 1], 6);
        Assert.Equal(1.0, product[2, 2], 6);
        Assert.Equal(0.0, product[0, 1], 6);
        Assert.Equal(0.0, product[1, 0], 6);
    }

    [Fact]
    public void BuiltCameraToSrgbMapsAsShotWhiteToWhite()
    {
        // With the DJI Mavic 3 Pro Hasselblad matrix + AsShotNeutral, a
        // camera pixel equal to AsShotNeutral represents the as-shot scene
        // white. The transform should produce a pixel where the max channel
        // is 1.0 (we normalise so it doesn't clip).
        var neutral = new double[] { 0.42899, 1.0, 0.540726 };
        var matrix = new double[,]
        {
            { 0.8575, -0.3219, -0.0868 },
            { -0.3351, 1.1451, 0.1593 },
            { 0.0207, 0.0468, 0.4876 },
        };
        var m = ColorTransform.BuildCameraToSrgb(neutral, matrix);

        var white = ColorTransform.MultiplyVec(m, neutral);
        // After row-normalisation the as-shot scene white maps exactly to
        // (1, 1, 1) linear sRGB regardless of camera quirks.
        Assert.Equal(1.0, white[0], 6);
        Assert.Equal(1.0, white[1], 6);
        Assert.Equal(1.0, white[2], 6);
    }
}
