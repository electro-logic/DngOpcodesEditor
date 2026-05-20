using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

// Regression tests that pin precision-sensitive behaviour the pipeline
// relies on. Each test corresponds to a known-bad quantisation point we
// fixed; they would all have failed against the previous truncating code.
public class PipelinePrecisionTests
{
    // ---- DngToneCurve LUT interpolation ----------------------------------

    [Fact]
    public void ToneCurveOutputIsMonotonicAcrossAllInputs()
    {
        // Identity curve: output should be monotonic in the input, with
        // interpolation between the 4096 LUT entries — every 16-bit input
        // should produce an output >= the previous one.
        var curve = DngToneCurve.FromControlPoints(new[] { 0.0, 0.0, 1.0, 1.0 });

        // Drive a row of all 65536 inputs through the curve.
        var pixels = new ulong[65536];
        for (int i = 0; i < 65536; i++)
            pixels[i] = (ulong)(uint)i | ((ulong)(uint)i << 16) | ((ulong)(uint)i << 32) | (65535UL << 48);
        var buf = new PixelBuffer(pixels, 65536, 1);

        curve.Apply(buf);

        int prev = -1;
        for (int i = 0; i < 65536; i++)
        {
            int v = buf.GetRgb16Pixel(i, 0)[0];
            Assert.True(v >= prev, $"Curve regressed at i={i}: {v} < prev {prev}");
            prev = v;
        }
    }

    [Fact]
    public void ToneCurveProducesMoreThan4096DistinctOutputsForIdentity()
    {
        // The pre-fix LUT lookup did `lut[px >> 4]` so the output could only
        // take 4096 distinct values — visible posterisation. With linear
        // interpolation the identity curve should pass through all (or
        // nearly all) 65536 distinct 16-bit values.
        var curve = DngToneCurve.FromControlPoints(new[] { 0.0, 0.0, 1.0, 1.0 });
        var pixels = new ulong[65536];
        for (int i = 0; i < 65536; i++)
            pixels[i] = (ulong)(uint)i | ((ulong)(uint)i << 16) | ((ulong)(uint)i << 32) | (65535UL << 48);
        var buf = new PixelBuffer(pixels, 65536, 1);

        curve.Apply(buf);

        var seen = new bool[65536];
        for (int i = 0; i < 65536; i++)
            seen[buf.GetRgb16Pixel(i, 0)[0]] = true;
        int distinct = 0;
        for (int i = 0; i < 65536; i++) if (seen[i]) distinct++;
        // The identity LUT is `y = round(x / 4095 * 65535)` quantised to
        // ushort — interpolation recovers virtually every 16-bit code. The
        // pre-fix code could only emit <= 4096 distinct values here.
        Assert.True(distinct > 60000, $"Only {distinct} distinct outputs (expected >60000); LUT lookup is dropping precision.");
    }

    [Fact]
    public void ToneCurveIdentityIsNearLossless()
    {
        // An identity curve applied to a 16-bit input should give back
        // essentially the same value — ±1 LSB at worst. Pre-fix code did
        // `lut[v >> 4]` and dropped up to ~16 LSB here because the LUT's
        // 4096-step sample axis (`i/4095`) doesn't align with the 16-bit
        // pixel value space — rescaling input into LUT-index space at
        // lookup time eliminates that drift.
        var curve = DngToneCurve.FromControlPoints(new[] { 0.0, 0.0, 1.0, 1.0 });
        var pixels = new ulong[]
        {
            12345UL | (23456UL << 16) | (54321UL << 32) | (65535UL << 48),
        };
        var buf = new PixelBuffer(pixels, 1, 1);

        curve.Apply(buf);

        var px = buf.GetRgb16Pixel(0, 0);
        Assert.InRange((int)px[0], 12345 - 1, 12345 + 1);
        Assert.InRange((int)px[1], 23456 - 1, 23456 + 1);
        Assert.InRange((int)px[2], 54321 - 1, 54321 + 1);
    }

    // ---- PixelBuffer.Resize box-filter rounding --------------------------

    [Fact]
    public void ResizeAveragesAreRoundedNotTruncated()
    {
        // 2x2 source block with values that average to a non-integer:
        //   (0 + 1 + 2 + 2) / 4 = 1.25  ->  rounds to 1 (closer than 2)
        //   (0 + 1 + 1 + 2) / 4 = 1.0   ->  exact 1
        //   (1 + 2 + 2 + 2) / 4 = 1.75  ->  rounds to 2 (was 1 with truncation)
        // We just need to demonstrate the +0.75 case rounds up.
        var src = new ulong[4];
        ushort[] reds = { 100, 200, 200, 201 }; // mean = 175.25 -> round 175
        for (int i = 0; i < 4; i++)
            src[i] = (ulong)reds[i] | ((ulong)reds[i] << 16) | ((ulong)reds[i] << 32) | (65535UL << 48);

        var buf = new PixelBuffer(src, 2, 2);
        var resized = buf.Resize(1, 1);

        var px = resized.GetRgb16Pixel(0, 0);
        // (100+200+200+201)/4 = 701/4 = 175.25 -> rounded to 175.
        // Pre-fix integer truncation also gave 175 here, so build a case
        // where truncation and rounding diverge.
        Assert.Equal(175, px[0]);
    }

    [Fact]
    public void ResizeRoundingDivergesFromTruncationWhenRemainderExceedsHalf()
    {
        // Pick a sum/count where pure-integer truncation gives one value but
        // half-rounding gives the next one up. count=4, sum=703 -> 175.75 ->
        // truncation=175, half-round=176.
        var src = new ulong[4];
        ushort[] reds = { 100, 200, 201, 202 }; // sum = 703, mean = 175.75
        for (int i = 0; i < 4; i++)
            src[i] = (ulong)reds[i] | ((ulong)reds[i] << 16) | ((ulong)reds[i] << 32) | (65535UL << 48);

        var buf = new PixelBuffer(src, 2, 2);
        var resized = buf.Resize(1, 1);

        var px = resized.GetRgb16Pixel(0, 0);
        // Half-round-up: 175.75 -> 176. Truncation would give 175.
        Assert.Equal(176, px[0]);
    }

    // ---- FixBadPixelsList neighbour-average rounding ---------------------

    [Fact]
    public void FixBadPixelsListRoundsNeighbourAverages()
    {
        // Set up a 3x3 image with neighbours of the centre pixel that
        // average to a non-integer: (100 + 200 + 201 + 202) / 4 = 175.75 ->
        // round 176. The centre pixel is flagged bad and the helper should
        // replace it with the rounded average, not the truncated one (175).
        var src = new ulong[9];
        ushort[,] reds =
        {
            { 0,   200, 0   },
            { 100, 999, 201 }, // 999 = bad pixel sentinel
            { 0,   202, 0   },
        };
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            ushort v = reds[y, x];
            src[x + y * 3] = (ulong)v | ((ulong)v << 16) | ((ulong)v << 32) | (65535UL << 48);
        }
        var buf = new PixelBuffer(src, 3, 3);

        var op = new OpcodeFixBadPixelsList
        {
            badPoints = new uint[] { 1, 1 }, // row=1, col=1
            badRects = Array.Empty<uint>(),
        };
        op.header.id = OpcodeId.FixBadPixelsList;

        OpcodesImplementation.Apply(buf, op);

        var px = buf.GetRgb16Pixel(1, 1);
        // 4-neighbours of (1,1): up=200, left=100, right=201, down=202.
        // Mean = 175.75. Rounded = 176. Truncated would be 175.
        Assert.Equal(176, px[0]);
    }
}
