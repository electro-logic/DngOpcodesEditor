using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class ColorTransformTests
{
    [Fact]
    public void HighlightDesaturationBlendsSaturatedChannelsTowardWhite()
    {
        // Identity matrix so we can read the desaturation effect directly.
        var identity = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        // AsShotNeutral exaggerated so the WB step pushes blue well above 1.0:
        //   neutral = (1, 1, 0.5) means a raw pixel of (0, 0, 32768) maps to
        //   WB'd (0, 0, 1.0) — at the saturation knee.
        var neutral = new[] { 1.0, 1.0, 0.5 };
        // Pure-blue pixel with B = 65535 reads WB'd as (0, 0, 2.0) — well into
        // the saturated zone — so the desat should drag R and G toward 2.0
        // (then mapped back to camera-native by *neutral*65535).
        var pixels = new ulong[] { 0UL | (0UL << 16) | (65535UL << 32) | (65535UL << 48) };
        var buf = new PixelBuffer(pixels, 1, 1);

        ColorTransform.Apply(buf, identity, neutral);

        var px = buf.GetRgb16Pixel(0, 0);
        // With desat fully engaged (t=1), R and G end up at maxC*nR*65535 and
        // maxC*nG*65535 — which is 2.0 * 65535 -> clamped to 65535 by the
        // matrix's per-channel clip. So a saturating blue pixel comes out
        // white, not magenta-cyan.
        Assert.Equal(65535, px[0]);
        Assert.Equal(65535, px[1]);
        Assert.Equal(65535, px[2]);
    }

    [Fact]
    public void HighlightDesaturationLeavesInRangePixelsAlone()
    {
        var identity = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var neutral = new[] { 1.0, 1.0, 1.0 };
        // 50% grey camera reading -> well below the DESAT_START threshold.
        var pixels = new ulong[] { 30000UL | (30000UL << 16) | (30000UL << 32) | (65535UL << 48) };
        var buf = new PixelBuffer(pixels, 1, 1);

        ColorTransform.Apply(buf, identity, neutral);

        var px = buf.GetRgb16Pixel(0, 0);
        Assert.Equal(30000, px[0]);
        Assert.Equal(30000, px[1]);
        Assert.Equal(30000, px[2]);
    }

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
    public void BaselineExposureBrightensTheMatrixByTwoPowStops()
    {
        var neutral = new double[] { 0.42899, 1.0, 0.540726 };
        var matrix = new double[,]
        {
            { 0.8575, -0.3219, -0.0868 },
            { -0.3351, 1.1451, 0.1593 },
            { 0.0207, 0.0468, 0.4876 },
        };
        var noBoost = ColorTransform.BuildCameraToSrgb(neutral, matrix);
        var withBoost = ColorTransform.BuildCameraToSrgb(neutral, matrix, baselineExposureStops: 0.86);

        // Every cell should be brighter by exactly 2^0.86 ≈ 1.815.
        double expectedScale = Math.Pow(2, 0.86);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                Assert.Equal(noBoost[r, c] * expectedScale, withBoost[r, c], 6);
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
