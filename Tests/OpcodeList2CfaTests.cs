using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

// Pins the behaviour that OpcodeList2 GainMaps operate on the **linearised
// CFA buffer** before demosaicing — not on demosaiced RGB.
//
// Backstory: the Pixel 6's L2 ships four GainMaps with plane=0 and
// (rowPitch, colPitch) = (2, 2) plus per-Bayer-position (top, left) offsets,
// which is the spec idiom for addressing each of the four Bayer subpixels
// in a 2×2 CFA cell. Applied (incorrectly) to demosaiced RGB they all
// multiplied the R channel by ~2×, turning the whole image bright red.
// Once L2 is correctly applied to the CFA buffer, each GainMap touches
// only the Bayer subgrid it targets.
public class OpcodeList2CfaTests
{
    [Fact]
    public void GainMapWithPitch2OnlyTouchesOneBayerSubgrid()
    {
        // 4×4 linearised CFA buffer, every sample = 10 000.
        const int W = 4, H = 4;
        var samples = new ushort[W * H];
        for (int i = 0; i < samples.Length; i++) samples[i] = 10000;

        // Constant gain of 2.0 over the whole image, targeting only the
        // (top=0, left=0, rowPitch=2, colPitch=2) Bayer subgrid — i.e. the
        // four samples at (0,0), (0,2), (2,0), (2,2).
        var gain = new OpcodeGainMap
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1,
            rowPitch = 2, colPitch = 2,
            mapPointsH = 2, mapPointsV = 2,
            mapSpacingH = 1.0, mapSpacingV = 1.0,
            mapOriginH = 0, mapOriginV = 0,
            mapPlanes = 1,
            mapGains = new float[] { 2f, 2f, 2f, 2f }, // constant gain field
        };
        gain.header.id = OpcodeId.GainMap;

        DngRawReader.ApplyGainMapToCfa(samples, W, H, gain);

        // (0,0), (0,2), (2,0), (2,2) → multiplied by 2 → 20000.
        Assert.Equal(20000, samples[0 * W + 0]);
        Assert.Equal(20000, samples[0 * W + 2]);
        Assert.Equal(20000, samples[2 * W + 0]);
        Assert.Equal(20000, samples[2 * W + 2]);
        // Every other position (the other three Bayer subgrids) must be
        // untouched — this is what regressed when L2 was being applied on
        // demosaiced RGB.
        Assert.Equal(10000, samples[0 * W + 1]); // (1, 0): different col
        Assert.Equal(10000, samples[1 * W + 0]); // (0, 1): different row
        Assert.Equal(10000, samples[1 * W + 1]); // (1, 1): different both
        Assert.Equal(10000, samples[3 * W + 3]);
    }

    [Fact]
    public void GainMapClampsRatherThanWrappingAt65535()
    {
        // A sample at 50 000 multiplied by 2 → 100 000, which would wrap to
        // ~34 464 if the cast truncated, instead of clamping to 65 535.
        const int W = 2, H = 2;
        var samples = new ushort[] { 50000, 50000, 50000, 50000 };

        var gain = new OpcodeGainMap
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1,
            rowPitch = 1, colPitch = 1,
            mapPointsH = 2, mapPointsV = 2,
            mapSpacingH = 1.0, mapSpacingV = 1.0,
            mapOriginH = 0, mapOriginV = 0,
            mapPlanes = 1,
            mapGains = new float[] { 2f, 2f, 2f, 2f },
        };
        gain.header.id = OpcodeId.GainMap;

        DngRawReader.ApplyGainMapToCfa(samples, W, H, gain);

        Assert.Equal(65535, samples[0]);
        Assert.Equal(65535, samples[1]);
        Assert.Equal(65535, samples[2]);
        Assert.Equal(65535, samples[3]);
    }

    [Fact]
    public void GainMapBilinearlyInterpolatesAcrossTheField()
    {
        // 5×5 CFA, sample = 10 000 everywhere. Gain map is 2×2 spanning the
        // whole image with corners (1, 2, 2, 4). Mid-image (~xMap=0.5,
        // yMap=0.5) should land at the bilinear mean ≈ (1+2+2+4)/4 = 2.25.
        const int W = 5, H = 5;
        var samples = new ushort[W * H];
        for (int i = 0; i < samples.Length; i++) samples[i] = 10000;

        var gain = new OpcodeGainMap
        {
            top = 0, left = 0, bottom = (uint)H, right = (uint)W,
            plane = 0, planes = 1,
            rowPitch = 1, colPitch = 1,
            mapPointsH = 2, mapPointsV = 2,
            mapSpacingH = 1.0, mapSpacingV = 1.0,
            mapOriginH = 0, mapOriginV = 0,
            mapPlanes = 1,
            mapGains = new float[] { 1f, 2f, 2f, 4f },
        };
        gain.header.id = OpcodeId.GainMap;

        DngRawReader.ApplyGainMapToCfa(samples, W, H, gain);

        // Corner (0,0) → gain 1 → 10 000.
        Assert.InRange((int)samples[0], 9999, 10001);
        // Corner (W-1, H-1) → gain 4 → 40 000.
        Assert.InRange((int)samples[(H - 1) * W + (W - 1)], 39999, 40001);
        // Middle (2, 2) ≈ gain 2.25 → 22 500.
        Assert.InRange((int)samples[2 * W + 2], 22400, 22600);
    }
}
