using System;
using System.Linq;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

// Synthetic-only tests for the L2-on-CFA implementations of MapTable /
// MapPolynomial / Delta{Row,Col} / Scale{Row,Col} / FixBadPixels{Constant,List}.
// No real-world DNG in the corpus exercises these (only GainMap ships in
// real L2 lists), so this pins the spec semantics on hand-built buffers.
public class OpcodeList2CfaOpcodesTests
{
    const int W = 8, H = 4;

    static ushort[] Solid(ushort v)
    {
        var s = new ushort[W * H];
        for (int i = 0; i < s.Length; i++) s[i] = v;
        return s;
    }

    static OpcodeArea FullArea() => new OpcodeMapTable
    {
        top = 0, left = 0, bottom = (uint)H, right = (uint)W,
        plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
    };

    // ----- MapTable ----------------------------------------------------------

    [Fact]
    public void MapTable_LooksUpInTable()
    {
        var samples = Solid(3);
        var op = new OpcodeMapTable
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            table = new ushort[] { 100, 200, 300, 400, 500 },
        };
        DngRawReader.ApplyMapTableToCfa(samples, W, H, op);
        // Every sample was 3 -> table[3] = 400
        Assert.All(samples, v => Assert.Equal(400, v));
    }

    [Fact]
    public void MapTable_ClampsToLastEntry()
    {
        var samples = Solid(50000);
        var op = new OpcodeMapTable
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            table = new ushort[] { 11, 22, 33 },
        };
        DngRawReader.ApplyMapTableToCfa(samples, W, H, op);
        // Sample 50000 > last index 2 -> clamps to table[2] = 33
        Assert.All(samples, v => Assert.Equal(33, v));
    }

    // ----- MapPolynomial -----------------------------------------------------

    [Fact]
    public void MapPolynomial_LinearDoublesValues()
    {
        // poly(n) = 0 + 2·n -> half-grey 32768 (n ≈ 0.5) goes to ~65535.
        var samples = Solid(32768);
        var op = new OpcodeMapPolynomial
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            coefficients = new[] { 0.0, 2.0 },
        };
        DngRawReader.ApplyMapPolynomialToCfa(samples, W, H, op);
        Assert.All(samples, v => Assert.InRange((int)v, 65530, 65535));
    }

    [Fact]
    public void MapPolynomial_RegionAndPitchRespected()
    {
        var samples = Solid(30000);
        var op = new OpcodeMapPolynomial
        {
            top = 1, left = 2, bottom = 3, right = 6,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 2,
            coefficients = new[] { 0.0, 0.0 }, // zero everything in scope
        };
        DngRawReader.ApplyMapPolynomialToCfa(samples, W, H, op);
        // Pixels outside the region stay at 30000.
        Assert.Equal(30000, samples[0 * W + 0]); // (0,0)
        Assert.Equal(30000, samples[1 * W + 0]); // (0,1)  left of region
        Assert.Equal(30000, samples[3 * W + 2]); // (2,3)  below region
        // Inside the region, colPitch=2 only touches even-column offsets.
        Assert.Equal(0,     samples[1 * W + 2]); // (2,1)  in
        Assert.Equal(30000, samples[1 * W + 3]); // (3,1)  in region but skipped by pitch
        Assert.Equal(0,     samples[1 * W + 4]); // (4,1)  in
        Assert.Equal(30000, samples[1 * W + 5]); // (5,1)  pitch-skipped
    }

    // ----- DeltaPerRow / Column ----------------------------------------------

    [Fact]
    public void DeltaPerRow_AddsPerRowOffset()
    {
        var samples = Solid(10000);
        // Add 0.1 to row 0, 0.0 to row 1, -0.1 to row 2, 0.2 to row 3.
        var op = new OpcodeDeltaPerRow
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            deltas = new[] { 0.1f, 0.0f, -0.1f, 0.2f },
        };
        DngRawReader.ApplyDeltaPerRowToCfa(samples, W, H, op);
        // 10000/65535 ≈ 0.1526; + delta; * 65535.
        Assert.InRange(samples[0 * W + 0], 16550, 16560); // +0.1 -> ~16554
        Assert.InRange(samples[1 * W + 0], 9995,  10005); // unchanged
        Assert.InRange(samples[2 * W + 0], 3445,  3455);  // -0.1
        Assert.InRange(samples[3 * W + 0], 23100, 23120); // +0.2
    }

    [Fact]
    public void DeltaPerColumn_AddsPerColumnOffset()
    {
        var samples = Solid(10000);
        var op = new OpcodeDeltaPerColumn
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            deltas = new[] { 0.0f, 0.1f, 0.0f, -0.1f, 0.0f, 0.0f, 0.0f, 0.0f },
        };
        DngRawReader.ApplyDeltaPerColumnToCfa(samples, W, H, op);
        Assert.Equal(10000, samples[0 * W + 0]); // col 0
        Assert.InRange(samples[0 * W + 1], 16550, 16560); // col 1, +0.1
        Assert.InRange(samples[0 * W + 3], 3445,  3455);  // col 3, -0.1
    }

    [Fact]
    public void ScalePerRow_MultipliesPerRow()
    {
        var samples = Solid(10000);
        var op = new OpcodeScalePerRow
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            scales = new[] { 2.0f, 1.0f, 0.5f, 3.0f },
        };
        DngRawReader.ApplyScalePerRowToCfa(samples, W, H, op);
        Assert.Equal(20000, samples[0 * W + 0]);
        Assert.Equal(10000, samples[1 * W + 0]);
        Assert.Equal(5000,  samples[2 * W + 0]);
        Assert.Equal(30000, samples[3 * W + 0]);
    }

    [Fact]
    public void ScalePerColumn_ClampsOverflowAt65535()
    {
        var samples = Solid(40000);
        var op = new OpcodeScalePerColumn
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            scales = new[] { 1.0f, 2.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f },
        };
        DngRawReader.ApplyScalePerColumnToCfa(samples, W, H, op);
        // 40000 * 2 = 80000, clamped to 65535
        Assert.Equal(65535, samples[0 * W + 1]);
        Assert.Equal(40000, samples[0 * W + 0]);
    }

    // ----- FixBadPixelsConstant / List ---------------------------------------

    [Fact]
    public void FixBadPixelsConstant_ReplacesSamplesMatchingSentinel()
    {
        // Set up a 5×5 buffer with a "dead" pixel sentinel of 65535.
        // Same-colour neighbours of (2,2) are at (0,2), (4,2), (2,0), (2,4).
        const int w = 5, h = 5;
        var samples = new ushort[w * h];
        for (int i = 0; i < samples.Length; i++) samples[i] = 10000;
        samples[0 * w + 2] = 1000;
        samples[4 * w + 2] = 2000;
        samples[2 * w + 0] = 3000;
        samples[2 * w + 4] = 4000;
        samples[2 * w + 2] = 65535; // bad

        var op = new OpcodeFixBadPixelsConstant { constant = 65535 };
        DngRawReader.ApplyFixBadPixelsConstantToCfa(samples, w, h, op);

        // 4-neighbour average (skipping 1 px) = (1000 + 2000 + 3000 + 4000)/4 = 2500
        Assert.Equal(2500, samples[2 * w + 2]);
        // The same-colour neighbours themselves stay untouched.
        Assert.Equal(1000, samples[0 * w + 2]);
    }

    [Fact]
    public void FixBadPixelsList_FixesEachListedPoint()
    {
        const int w = 5, h = 5;
        var samples = new ushort[w * h];
        for (int i = 0; i < samples.Length; i++) samples[i] = 10000;
        samples[0 * w + 2] = 100;
        samples[4 * w + 2] = 200;
        samples[2 * w + 0] = 300;
        samples[2 * w + 4] = 400;

        var op = new OpcodeFixBadPixelsList
        {
            badPoints = new uint[] { 2, 2 }, // single (row=2, col=2)
            badRects  = Array.Empty<uint>(),
        };
        DngRawReader.ApplyFixBadPixelsListToCfa(samples, w, h, op);
        // (100 + 200 + 300 + 400) / 4 = 250
        Assert.Equal(250, samples[2 * w + 2]);
    }

    [Fact]
    public void FixBadPixelsList_FixesRectangles()
    {
        const int w = 6, h = 4;
        var samples = new ushort[w * h];
        for (int i = 0; i < samples.Length; i++) samples[i] = 20000;
        // The whole 1-pixel-tall rect [(row=1, col=2)..(row=2, col=4)] gets fixed.
        var op = new OpcodeFixBadPixelsList
        {
            badPoints = Array.Empty<uint>(),
            badRects  = new uint[] { 1, 2, 2, 4 },
        };
        DngRawReader.ApplyFixBadPixelsListToCfa(samples, w, h, op);
        // Source was uniform 20000, so the average is still 20000.
        Assert.Equal(20000, samples[1 * w + 2]);
        Assert.Equal(20000, samples[1 * w + 3]);
        // Outside the rect untouched (and equal to source).
        Assert.Equal(20000, samples[0 * w + 2]);
    }
}
