using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class DngToneCurveTests
{
    [Fact]
    public void IdentityCurveLeavesPixelsAlone()
    {
        // Two-point curve from (0,0) to (1,1) — the identity ramp.
        var curve = DngToneCurve.FromControlPoints(new[] { 0.0, 0.0, 1.0, 1.0 });
        var pixels = new ulong[]
        {
            10000UL | (30000UL << 16) | (60000UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 1, 1);

        curve.Apply(buf);

        var px = buf.GetRgb16Pixel(0, 0);
        // The LUT quantises to 12 bits, so allow ±16 of tolerance.
        Assert.InRange(px[0], 9984, 10016);
        Assert.InRange(px[1], 29984, 30016);
        Assert.InRange(px[2], 59984, 60016);
    }

    [Fact]
    public void HalvingCurveMapsMidGreyToQuarterGrey()
    {
        // (0, 0), (0.5, 0.25), (1, 1) — input 0.5 maps to output 0.25.
        var curve = DngToneCurve.FromControlPoints(new[] { 0.0, 0.0, 0.5, 0.25, 1.0, 1.0 });
        var pixels = new ulong[]
        {
            32768UL | (32768UL << 16) | (32768UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 1, 1);

        curve.Apply(buf);

        var px = buf.GetRgb16Pixel(0, 0);
        // Expect ~25% of 65535 = ~16384, ±32 LUT quantisation.
        Assert.InRange(px[0], 16352, 16416);
        Assert.InRange(px[1], 16352, 16416);
        Assert.InRange(px[2], 16352, 16416);
    }

    [Fact]
    public void FromControlPointsReturnsNullForInvalidInput()
    {
        Assert.Null(DngToneCurve.FromControlPoints(null));
        Assert.Null(DngToneCurve.FromControlPoints(Array.Empty<double>()));
        // Odd-length array isn't valid (x,y) pairs.
        Assert.Null(DngToneCurve.FromControlPoints(new[] { 0.0, 0.5, 1.0 }));
        // A single (x,y) pair isn't enough for a curve.
        Assert.Null(DngToneCurve.FromControlPoints(new[] { 0.5, 0.5 }));
    }
}
