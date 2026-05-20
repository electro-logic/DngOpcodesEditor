using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class ProfileHueSatMapTests
{
    [Fact]
    public void IdentityMapLeavesPixelsUnchanged()
    {
        // 2×2×1 table, every cell = (0° hue shift, 1.0 sat, 1.0 value) → no-op.
        var dims = new uint[] { 2, 2, 1 };
        var data = new double[]
        {
            0, 1, 1,  0, 1, 1,
            0, 1, 1,  0, 1, 1,
        };
        var map = ProfileHueSatMap.TryBuild(dims, data);
        Assert.NotNull(map);

        var pixels = new ulong[]
        {
            10000UL | (30000UL << 16) | (60000UL << 32) | (65535UL << 48),
            65000UL | (40000UL << 16) | (15000UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 2, 1);

        map.Apply(buf);

        var p0 = buf.GetRgb16Pixel(0, 0);
        Assert.InRange(p0[0], 9990, 10010);
        Assert.InRange(p0[1], 29990, 30010);
        Assert.InRange(p0[2], 59990, 60010);
    }

    [Fact]
    public void SaturationBoostPushesGreyPixelStillGrey()
    {
        // Doubling sat scale on a fully grey pixel should still be grey because
        // its saturation is 0 — only colour pixels gain saturation.
        var dims = new uint[] { 2, 2, 1 };
        var data = new double[]
        {
            0, 2, 1,  0, 2, 1,
            0, 2, 1,  0, 2, 1,
        };
        var map = ProfileHueSatMap.TryBuild(dims, data);
        var pixels = new ulong[]
        {
            32768UL | (32768UL << 16) | (32768UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 1, 1);

        map.Apply(buf);

        var p = buf.GetRgb16Pixel(0, 0);
        Assert.Equal(32768, p[0]);
        Assert.Equal(32768, p[1]);
        Assert.Equal(32768, p[2]);
    }

    [Fact]
    public void SaturationBoostIncreasesSaturationOfColouredPixel()
    {
        // A pure-blue pixel through a sat=2.0 boost should stay pure-blue
        // (already max saturation, can't go higher) but a pixel that's
        // partly desaturated should land closer to pure colour.
        var dims = new uint[] { 2, 2, 1 };
        var data = new double[]
        {
            0, 2, 1,  0, 2, 1,
            0, 2, 1,  0, 2, 1,
        };
        var map = ProfileHueSatMap.TryBuild(dims, data);
        // Pale blue: R=30000 G=40000 B=60000 → S ≈ (60000-30000)/60000 = 0.5
        var pixels = new ulong[]
        {
            30000UL | (40000UL << 16) | (60000UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 1, 1);

        map.Apply(buf);

        var p = buf.GetRgb16Pixel(0, 0);
        // After 2x sat boost, R should be near 0 (no red), B remains 60000, G between.
        // Saturation after: clamp(0.5 * 2, 0, 1) = 1.0 → pure blue.
        Assert.InRange(p[0], 0, 200);
        Assert.InRange(p[2], 59500, 60500);
    }

    [Fact]
    public void HueShiftRotatesColors()
    {
        // 120° hue shift across the whole map. Pure red should become pure green.
        var dims = new uint[] { 2, 2, 1 };
        var data = new double[]
        {
            120, 1, 1,  120, 1, 1,
            120, 1, 1,  120, 1, 1,
        };
        var map = ProfileHueSatMap.TryBuild(dims, data);
        var pixels = new ulong[]
        {
            65535UL | (0UL << 16) | (0UL << 32) | (65535UL << 48), // pure red
        };
        var buf = new PixelBuffer(pixels, 1, 1);

        map.Apply(buf);

        var p = buf.GetRgb16Pixel(0, 0);
        // Pure red (hue 0) + 120° = pure green (hue 120).
        Assert.InRange(p[0], 0, 200);
        Assert.InRange(p[1], 65300, 65535);
        Assert.InRange(p[2], 0, 200);
    }

    [Fact]
    public void TryBuildReturnsNullForBadInput()
    {
        Assert.Null(ProfileHueSatMap.TryBuild(null, null));
        Assert.Null(ProfileHueSatMap.TryBuild(new uint[] { 2, 2 }, new double[12]));     // wrong dim count
        Assert.Null(ProfileHueSatMap.TryBuild(new uint[] { 2, 2, 1 }, new double[10])); // wrong data length
        Assert.Null(ProfileHueSatMap.TryBuild(new uint[] { 0, 2, 1 }, new double[0]));  // zero division
    }
}
