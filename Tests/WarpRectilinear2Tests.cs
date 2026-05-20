using System.Collections.Generic;
using System.Linq;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class WarpRectilinear2Tests
{
    // ----- binary round-trip ---------------------------------------------------

    [Fact]
    public void RoundTripPreservesAllFields_SinglePlane()
    {
        var op = NewIdentity(planes: 1);
        op.radialCoefficients[0] = 1.0;
        op.radialCoefficients[3] = -0.05;
        op.radialCoefficients[7] = 0.001;
        op.tangentialCoefficients[0] = 1e-4;
        op.tangentialCoefficients[1] = -2e-4;
        op.validRadiusRange[0] = 0.1;
        op.validRadiusRange[1] = 0.95;
        op.cx = 0.51;
        op.cy = 0.49;
        op.useReciprocal = true;

        var bytes = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)op });
        var roundTripped = (OpcodeWarpRectilinear2)new OpcodesReader().ReadOpcodeList(bytes).Single();

        Assert.Equal(op.planes, roundTripped.planes);
        Assert.Equal(op.radialCoefficients, roundTripped.radialCoefficients);
        Assert.Equal(op.tangentialCoefficients, roundTripped.tangentialCoefficients);
        Assert.Equal(op.validRadiusRange, roundTripped.validRadiusRange);
        Assert.Equal(op.cx, roundTripped.cx);
        Assert.Equal(op.cy, roundTripped.cy);
        Assert.Equal(op.useReciprocal, roundTripped.useReciprocal);
    }

    [Fact]
    public void RoundTripPreservesAllFields_ThreePlanes()
    {
        var op = NewIdentity(planes: 3);
        for (int p = 0; p < 3; p++)
        {
            for (int k = 0; k < OpcodeWarpRectilinear2.RadialTermsPerPlane; k++)
                op.radialCoefficients[p * OpcodeWarpRectilinear2.RadialTermsPerPlane + k] = (p + 1) * 0.001 * k;
            op.tangentialCoefficients[p * 2 + 0] = p * 1e-5;
            op.tangentialCoefficients[p * 2 + 1] = -p * 2e-5;
            op.validRadiusRange[p * 2 + 0] = 0.0 + p * 0.05;
            op.validRadiusRange[p * 2 + 1] = 1.0 - p * 0.05;
        }
        op.cx = 0.50;
        op.cy = 0.50;
        op.useReciprocal = false;

        var bytes = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)op });
        var roundTripped = (OpcodeWarpRectilinear2)new OpcodesReader().ReadOpcodeList(bytes).Single();

        Assert.Equal(op.planes, roundTripped.planes);
        Assert.Equal(op.radialCoefficients, roundTripped.radialCoefficients);
        Assert.Equal(op.tangentialCoefficients, roundTripped.tangentialCoefficients);
        Assert.Equal(op.validRadiusRange, roundTripped.validRadiusRange);
    }

    [Fact]
    public void ByteCountMatchesSpecFormula()
    {
        // Spec: 4 (planes) + planes*(15+2+2)*8 + 16 (cx,cy) + 4 (useReciprocal)
        var op = NewIdentity(planes: 1);
        var bytes = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)op });
        // List header (4 bytes count) + opcode header (16 bytes) + body
        int expectedBody = 4 + 1 * OpcodeWarpRectilinear2.TermsPerPlane * 8 + 16 + 4; // 196
        Assert.Equal(4 + 16 + expectedBody, bytes.Length);
    }

    // ----- preview: identity polynomial leaves the image alone ----------------

    [Fact]
    public void IdentityWarpIsAnApproximateNoOp()
    {
        // k0=1, others=0 -> ratio(r) = 1 -> every pixel resamples from its
        // original position. Bicubic resampling at integer coords is a noop
        // for our Catmull-Rom kernel.
        var img = SolidGrey(64, 48, 30000);
        var op = NewIdentity(planes: 1);
        op.radialCoefficients[0] = 1.0;
        op.header.id = OpcodeId.WarpRectilinear2;

        OpcodesImplementation.WarpRectilinear2(img, op);

        // Centre pixel survives unchanged (sample at integer src coords with
        // a kernel that sums to 1).
        var c = img.GetRgb16Pixel(32, 24);
        Assert.Equal(30000, c[0]);
        Assert.Equal(30000, c[1]);
        Assert.Equal(30000, c[2]);
    }

    // ----- skip rule ----------------------------------------------------------

    [Fact]
    public void SkipRule_OptionalWR2SuppressesNextWarpRectilinear()
    {
        var wr2 = NewIdentity(planes: 1);
        wr2.header.id = OpcodeId.WarpRectilinear2;
        wr2.header.flags = OpcodeFlag.Optional;

        var wr = new OpcodeWarpRectilinear { planes = 1, coefficients = new double[] { 1, 0, 0, 0, 0, 0 } };
        wr.header.id = OpcodeId.WarpRectilinear;

        var gain = new OpcodeGainMap { mapPointsV = 1, mapPointsH = 1, mapPlanes = 1, mapGains = new[] { 1.0f } };
        gain.header.id = OpcodeId.GainMap;

        var result = OpcodesImplementation.ApplyWarpRectilinear2SkipRule(new List<Opcode> { wr2, wr, gain });
        // WarpRectilinear should be filtered out; the GainMap survives.
        Assert.Equal(2, result.Count);
        Assert.Equal(OpcodeId.WarpRectilinear2, result[0].header.id);
        Assert.Equal(OpcodeId.GainMap, result[1].header.id);
    }

    [Fact]
    public void SkipRule_NonOptionalWR2DoesNotSuppressAnything()
    {
        var wr2 = NewIdentity(planes: 1);
        wr2.header.id = OpcodeId.WarpRectilinear2;
        wr2.header.flags = OpcodeFlag.OptionalPreview; // NOT Optional

        var wr = new OpcodeWarpRectilinear { planes = 1, coefficients = new double[] { 1, 0, 0, 0, 0, 0 } };
        wr.header.id = OpcodeId.WarpRectilinear;

        var result = OpcodesImplementation.ApplyWarpRectilinear2SkipRule(new List<Opcode> { wr2, wr });
        Assert.Equal(2, result.Count);
        Assert.Equal(OpcodeId.WarpRectilinear, result[1].header.id);
    }

    [Fact]
    public void SkipRule_OptionalWR2DoesNotSuppressNonWarpOpcodes()
    {
        // From the spec: only WarpRectilinear / WarpFisheye get skipped.
        var wr2 = NewIdentity(planes: 1);
        wr2.header.id = OpcodeId.WarpRectilinear2;
        wr2.header.flags = OpcodeFlag.Optional;

        var gain = new OpcodeGainMap { mapPointsV = 1, mapPointsH = 1, mapPlanes = 1, mapGains = new[] { 1.0f } };
        gain.header.id = OpcodeId.GainMap;

        var result = OpcodesImplementation.ApplyWarpRectilinear2SkipRule(new List<Opcode> { wr2, gain });
        // Both opcodes pass through — the immediately following opcode isn't a
        // warp, so the skip rule doesn't fire.
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SkipRule_OptionalWR2SuppressesNextWarpFisheye()
    {
        var wr2 = NewIdentity(planes: 1);
        wr2.header.id = OpcodeId.WarpRectilinear2;
        wr2.header.flags = OpcodeFlag.Optional;

        var fish = new OpcodeWarpFisheye { planes = 1, coefficients = new double[] { 1, 0, 0, 0 } };
        fish.header.id = OpcodeId.WarpFisheye;

        var result = OpcodesImplementation.ApplyWarpRectilinear2SkipRule(new List<Opcode> { wr2, fish });
        // The fisheye fallback is filtered out.
        Assert.Single(result);
        Assert.Equal(OpcodeId.WarpRectilinear2, result[0].header.id);
    }

    // ----- helpers ------------------------------------------------------------

    static OpcodeWarpRectilinear2 NewIdentity(int planes)
    {
        var op = new OpcodeWarpRectilinear2 { planes = (uint)planes };
        op.header.id = OpcodeId.WarpRectilinear2;
        op.radialCoefficients     = new double[planes * OpcodeWarpRectilinear2.RadialTermsPerPlane];
        op.tangentialCoefficients = new double[planes * OpcodeWarpRectilinear2.TangentialTermsPerPlane];
        op.validRadiusRange       = new double[planes * OpcodeWarpRectilinear2.ValidRangeTermsPerPlane];
        // Identity ratio: k0=1 for every plane; valid range covers all r.
        for (int p = 0; p < planes; p++)
        {
            op.radialCoefficients[p * OpcodeWarpRectilinear2.RadialTermsPerPlane + 0] = 1.0;
            op.validRadiusRange[p * 2 + 1] = 1.0;
        }
        return op;
    }

    static PixelBuffer SolidGrey(int w, int h, ushort v)
    {
        var pixels = new ulong[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = v | ((ulong)v << 16) | ((ulong)v << 32) | (65535UL << 48);
        return new PixelBuffer(pixels, w, h);
    }
}
