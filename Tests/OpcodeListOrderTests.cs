using System.Collections.Generic;
using System.Linq;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

// Regression tests that pin the order in which OpcodeList1 / 2 / 3 are
// imported and applied. Per DNG spec the lists run in numeric order
// (1 -> 2 -> 3) and within a list the opcodes are applied in stored
// order.
public class OpcodeListOrderTests
{
    [Fact]
    public void TiffWithAllThreeListsReportsEachByItself()
    {
        var trim = MakeTrim(1, 2, 3, 4);
        trim.header.id = OpcodeId.TrimBounds;
        var fix = new OpcodeFixVignetteRadial { k0 = -0.3, cx = 0.5, cy = 0.5 };
        fix.header.id = OpcodeId.FixVignetteRadial;
        var warp = new OpcodeWarpRectilinear { planes = 1, coefficients = new double[] { 1, 0, 0, 0, 0, 0 }, cx = 0.5, cy = 0.5 };
        warp.header.id = OpcodeId.WarpRectilinear;

        var payload1 = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)trim });
        var payload2 = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)fix });
        var payload3 = new OpcodesWriter().WriteOpcodeList(new[] { (Opcode)warp });

        // Start from a tiny synthetic TIFF and inject all three OpcodeList
        // tags one at a time — the same flow MainWindowVM.ExportDng uses.
        var tiff = TiffFile.WriteOpcodeList(MinimalTiff(), 1, payload1);
        tiff = TiffFile.WriteOpcodeList(tiff, 2, payload2);
        tiff = TiffFile.WriteOpcodeList(tiff, 3, payload3);

        // Each list reads back independently as the matching single opcode.
        var ops1 = new OpcodesReader().ReadOpcodeList(TiffFile.ReadOpcodeList(tiff, 1));
        var ops2 = new OpcodesReader().ReadOpcodeList(TiffFile.ReadOpcodeList(tiff, 2));
        var ops3 = new OpcodesReader().ReadOpcodeList(TiffFile.ReadOpcodeList(tiff, 3));

        Assert.Equal(OpcodeId.TrimBounds, ops1[0].header.id);
        Assert.Equal(OpcodeId.FixVignetteRadial, ops2[0].header.id);
        Assert.Equal(OpcodeId.WarpRectilinear, ops3[0].header.id);
    }

    [Fact]
    public void ImportFlowAddsOpcodesInListThenStoredOrder()
    {
        // Mirror MainWindowVM.ImportDng: iterate lists 1..3 and within each
        // list append every parsed opcode while tagging it with the list it
        // came from. The resulting collection drives ApplyOpcodes, so its
        // order *is* the apply order.

        // Three OpcodeList tags, each with a distinct opcode set so we can
        // assert the final order by id + ListIndex.
        var l1 = new OpcodesWriter().WriteOpcodeList(new[]
        {
            (Opcode)TaggedTrim(10, 20, 30, 40),
        });
        var l2 = new OpcodesWriter().WriteOpcodeList(new[]
        {
            (Opcode)TaggedVignette(0.1),
            (Opcode)TaggedVignette(0.2),
        });
        var l3 = new OpcodesWriter().WriteOpcodeList(new[]
        {
            (Opcode)TaggedWarp(1, 2, 3),
        });

        var tiff = TiffFile.WriteOpcodeList(MinimalTiff(), 1, l1);
        tiff = TiffFile.WriteOpcodeList(tiff, 2, l2);
        tiff = TiffFile.WriteOpcodeList(tiff, 3, l3);

        // Replay MainWindowVM.ImportDng's logic.
        var collected = new List<Opcode>();
        for (int listIndex = 1; listIndex < 4; listIndex++)
        {
            var payload = TiffFile.ReadOpcodeList(tiff, listIndex);
            if (payload == null || payload.Length == 0) continue;
            foreach (var op in new OpcodesReader().ReadOpcodeList(payload))
            {
                op.ListIndex = listIndex;
                collected.Add(op);
            }
        }

        // Across-list order is L1, L2, L3 (DNG spec).
        // Within-list order is the order written.
        Assert.Equal(4, collected.Count);
        Assert.Equal(OpcodeId.TrimBounds, collected[0].header.id);
        Assert.Equal(1, collected[0].ListIndex);
        Assert.Equal(OpcodeId.FixVignetteRadial, collected[1].header.id);
        Assert.Equal(2, collected[1].ListIndex);
        Assert.Equal(0.1, ((OpcodeFixVignetteRadial)collected[1]).k0);
        Assert.Equal(OpcodeId.FixVignetteRadial, collected[2].header.id);
        Assert.Equal(2, collected[2].ListIndex);
        Assert.Equal(0.2, ((OpcodeFixVignetteRadial)collected[2]).k0);
        Assert.Equal(OpcodeId.WarpRectilinear, collected[3].header.id);
        Assert.Equal(3, collected[3].ListIndex);
    }

    [Fact]
    public void ExportRoundTripPreservesListAssignment()
    {
        // Build a mixed Opcodes collection (manually-assigned ListIndex values)
        // and verify that MainWindowVM.ExportDng's per-list filter writes each
        // opcode back to the matching tag.
        var opcodes = new List<Opcode>
        {
            TaggedTrim(0, 0, 100, 100, listIndex: 1),
            TaggedVignette(0.5, listIndex: 3),
            TaggedWarp(1, 0, 0, listIndex: 3),
        };

        var tiff = MinimalTiff();
        for (int listIndex = 1; listIndex < 4; listIndex++)
        {
            var listOpcodes = opcodes.Where(o => o.ListIndex == listIndex).ToArray();
            if (listOpcodes.Length == 0) continue;
            var payload = new OpcodesWriter().WriteOpcodeList(listOpcodes);
            tiff = TiffFile.WriteOpcodeList(tiff, listIndex, payload);
        }

        // L1 has the TrimBounds, L2 is empty, L3 has the two others in order.
        var l1 = new OpcodesReader().ReadOpcodeList(TiffFile.ReadOpcodeList(tiff, 1));
        Assert.Single(l1);
        Assert.Equal(OpcodeId.TrimBounds, l1[0].header.id);

        Assert.Null(TiffFile.ReadOpcodeList(tiff, 2));

        var l3 = new OpcodesReader().ReadOpcodeList(TiffFile.ReadOpcodeList(tiff, 3));
        Assert.Equal(2, l3.Length);
        Assert.Equal(OpcodeId.FixVignetteRadial, l3[0].header.id);
        Assert.Equal(OpcodeId.WarpRectilinear, l3[1].header.id);
    }

    static OpcodeTrimBounds MakeTrim(uint top, uint left, uint bottom, uint right)
    {
        var t = new OpcodeTrimBounds { top = top, left = left, bottom = bottom, right = right };
        t.header.id = OpcodeId.TrimBounds;
        return t;
    }
    static OpcodeTrimBounds TaggedTrim(uint top, uint left, uint bottom, uint right, int listIndex = 1)
    {
        var t = MakeTrim(top, left, bottom, right);
        t.ListIndex = listIndex;
        return t;
    }
    static OpcodeFixVignetteRadial TaggedVignette(double k0, int listIndex = 2)
    {
        var v = new OpcodeFixVignetteRadial { k0 = k0, cx = 0.5, cy = 0.5 };
        v.header.id = OpcodeId.FixVignetteRadial;
        v.ListIndex = listIndex;
        return v;
    }
    static OpcodeWarpRectilinear TaggedWarp(double r0, double r1, double r2, int listIndex = 3)
    {
        var w = new OpcodeWarpRectilinear
        {
            planes = 1,
            coefficients = new double[] { r0, r1, r2, 0, 0, 0 },
            cx = 0.5,
            cy = 0.5,
        };
        w.header.id = OpcodeId.WarpRectilinear;
        w.ListIndex = listIndex;
        return w;
    }

    static byte[] MinimalTiff()
    {
        // 'II', 42, IFD0 at offset 8; IFD0 has count=0 and no next.
        var t = new byte[14];
        t[0] = (byte)'I'; t[1] = (byte)'I';
        t[2] = 42; t[3] = 0;
        t[4] = 8; t[5] = 0; t[6] = 0; t[7] = 0;
        // IFD0: count = 0
        t[8] = 0; t[9] = 0;
        // next IFD = 0
        return t;
    }
}
