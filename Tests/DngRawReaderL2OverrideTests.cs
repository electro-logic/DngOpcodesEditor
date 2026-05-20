using System.Collections.Generic;
using System.IO;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

// Pins the new DngRawReader.Read(bytes, l2Override) overload that lets the
// editor re-decode a cached DNG with the user's current OpcodeList2 — the
// path that makes live editing of L2 opcodes visible in the preview.
public class DngRawReaderL2OverrideTests
{
    // ---- A minimal synthetic 4x4 CFA DNG --------------------------------------

    // Builds a 4x4 little-endian TIFF with one CFA IFD (photometric 32803,
    // 16-bit samples, single strip, uniform value `fill`). No L2/L3 opcode
    // tags. Just enough to drive DngRawReader through its CFA path so the
    // override can replace its (empty) L2 list.
    static byte[] BuildSyntheticCfaTiff(ushort fill)
    {
        const int W = 4, H = 4;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        // TIFF header: II*, IFD0 offset.
        bw.Write((byte)'I'); bw.Write((byte)'I');
        bw.Write((ushort)42);
        bw.Write((uint)8); // IFD0 at offset 8

        // IFD0: we need ImageWidth(256), ImageLength(257), BitsPerSample(258),
        // Compression(259)=1, PhotometricInterp(262)=32803, StripOffsets(273),
        // SamplesPerPixel(277)=1, RowsPerStrip(278), StripByteCounts(279),
        // CFAPattern(33422)=R,G,G,B (= 0,1,1,2), CFARepeatPatternDim(33421)=2,2,
        // PlanarConfiguration(284)=1, BlackLevel(50714)=0, WhiteLevel(50717)=65535.
        // Keep it minimal — DngRawReader tolerates missing optional tags.
        int entryCount = 9;
        bw.Write((ushort)entryCount);
        int afterIfdOffset = 8 + 2 + entryCount * 12 + 4;
        // ImageWidth (256, SHORT, 1) -> W
        WriteEntry(bw, 256, 3, 1, W);
        // ImageLength (257, SHORT, 1) -> H
        WriteEntry(bw, 257, 3, 1, H);
        // BitsPerSample (258, SHORT, 1) -> 16
        WriteEntry(bw, 258, 3, 1, 16);
        // Compression (259, SHORT, 1) -> 1
        WriteEntry(bw, 259, 3, 1, 1);
        // PhotometricInterpretation (262, SHORT, 1) -> 32803
        WriteEntry(bw, 262, 3, 1, 32803);
        // StripOffsets (273, LONG, 1) -> afterIfdOffset (image data right after IFD)
        WriteEntry(bw, 273, 4, 1, (uint)afterIfdOffset);
        // SamplesPerPixel (277, SHORT, 1) -> 1
        WriteEntry(bw, 277, 3, 1, 1);
        // RowsPerStrip (278, SHORT, 1) -> H
        WriteEntry(bw, 278, 3, 1, H);
        // StripByteCounts (279, LONG, 1) -> W*H*2
        WriteEntry(bw, 279, 4, 1, (uint)(W * H * 2));
        // Next IFD = 0
        bw.Write((uint)0);
        // Image data: uniform `fill`.
        for (int i = 0; i < W * H; i++) bw.Write(fill);
        return ms.ToArray();
    }

    static void WriteEntry(BinaryWriter bw, ushort tag, ushort type, uint count, uint value)
    {
        bw.Write(tag);
        bw.Write(type);
        bw.Write(count);
        // Value/offset field: type 3 = SHORT (2 bytes left-aligned), type 4 = LONG.
        if (type == 3)
        {
            bw.Write((ushort)value);
            bw.Write((ushort)0);
        }
        else
        {
            bw.Write(value);
        }
    }

    // ---- Tests ----------------------------------------------------------------

    [Fact]
    public void NoOverride_DecodesAsBefore()
    {
        var tiff = BuildSyntheticCfaTiff(fill: 20000);
        var buffer = DngRawReader.Read(tiff); // single-arg path unchanged

        Assert.Equal(4, buffer.Width);
        Assert.Equal(4, buffer.Height);
        // Centre pixel passed through the linearise + bilinear-demosaic path;
        // with all samples equal, the demosaic produces a uniform image.
        var px = buffer.GetRgb16Pixel(2, 2);
        Assert.InRange((int)px[0], 19990, 20010);
    }

    [Fact]
    public void EmptyOverride_SkipsL2Entirely()
    {
        // Even when the file had an L2 (this synthetic one doesn't), passing
        // an empty override means "no L2 to apply" — should match the unedited
        // result for a file with no L2.
        var tiff = BuildSyntheticCfaTiff(fill: 20000);
        var bufferA = DngRawReader.Read(tiff);
        var bufferB = DngRawReader.Read(tiff, new List<Opcode>());

        for (int y = 0; y < bufferA.Height; y++)
            for (int x = 0; x < bufferA.Width; x++)
            {
                var a = bufferA.GetRgb16Pixel(x, y);
                var b = bufferB.GetRgb16Pixel(x, y);
                Assert.Equal(a[0], b[0]);
                Assert.Equal(a[1], b[1]);
                Assert.Equal(a[2], b[2]);
            }
    }

    [Fact]
    public void OverrideWithScalePerRowAffectsTheCfaBeforeDemosaic()
    {
        // Apply ScalePerRow with row 0 -> 2x, others -> 1x. After CFA-stage
        // scaling, row 0 samples are 40000 (was 20000), rows 1..3 stay 20000.
        // Bilinear demosaic on a row-0 pixel ends up brighter than a row-3
        // pixel.
        var tiff = BuildSyntheticCfaTiff(fill: 20000);
        var scale = new OpcodeScalePerRow
        {
            top = 0, left = 0, bottom = 4, right = 4,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            scales = new[] { 2.0f, 1.0f, 1.0f, 1.0f },
        };
        scale.header.id = OpcodeId.ScalePerRow;
        scale.ListIndex = 2;
        scale.Enabled = true;

        var withL2 = DngRawReader.Read(tiff, new List<Opcode> { scale });
        var noL2  = DngRawReader.Read(tiff, new List<Opcode>());

        // Centre-of-row-0 pixel in the override-decoded buffer should be
        // brighter than the no-L2 baseline.
        var rowHotR  = withL2.GetRgb16Pixel(1, 0)[0];
        var rowColdR =  noL2.GetRgb16Pixel(1, 0)[0];
        Assert.True(rowHotR > rowColdR, $"row 0 R: with-L2 {rowHotR} should be > baseline {rowColdR}");
    }

    [Fact]
    public void OverrideIgnoresOpcodesWithNonTwoListIndex()
    {
        // ScalePerRow with ListIndex == 3 should NOT be applied via the L2
        // override path (only ListIndex == 2 + Enabled make it through).
        var tiff = BuildSyntheticCfaTiff(fill: 20000);
        var scale = new OpcodeScalePerRow
        {
            top = 0, left = 0, bottom = 4, right = 4,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            scales = new[] { 2.0f, 1.0f, 1.0f, 1.0f },
        };
        scale.header.id = OpcodeId.ScalePerRow;
        scale.ListIndex = 3; // <-- not L2
        scale.Enabled = true;

        var withL3InOverride = DngRawReader.Read(tiff, new List<Opcode> { scale });
        var noL2             = DngRawReader.Read(tiff, new List<Opcode>());

        // Identical — the L3-tagged opcode is filtered out by the override.
        for (int x = 0; x < 4; x++)
            Assert.Equal(noL2.GetRgb16Pixel(x, 0)[0], withL3InOverride.GetRgb16Pixel(x, 0)[0]);
    }

    [Fact]
    public void OverrideIgnoresDisabledOpcodes()
    {
        var tiff = BuildSyntheticCfaTiff(fill: 20000);
        var scale = new OpcodeScalePerRow
        {
            top = 0, left = 0, bottom = 4, right = 4,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            scales = new[] { 2.0f, 1.0f, 1.0f, 1.0f },
        };
        scale.header.id = OpcodeId.ScalePerRow;
        scale.ListIndex = 2;
        scale.Enabled = false; // <-- toggled off

        var disabled = DngRawReader.Read(tiff, new List<Opcode> { scale });
        var noL2     = DngRawReader.Read(tiff, new List<Opcode>());
        for (int x = 0; x < 4; x++)
            Assert.Equal(noL2.GetRgb16Pixel(x, 0)[0], disabled.GetRgb16Pixel(x, 0)[0]);
    }
}
