using System;

namespace DngOpcodesEditor;

// =============================================================================
// MapTable (DNG opcode 7)
// =============================================================================
//
// Per-channel 1-D look-up table applied to a rectangular region of the image.
// The raw sample value indexes directly into the table; values beyond the
// table clamp to the last entry.
//
// Parameters (OpcodeMapTable)
//   top/left/bottom/right   region to operate on (pixels).
//   plane / planes          channel range; per-pixel pitch lives in rowPitch /
//                            colPitch (typically 1 / 1).
//   table                   ushort[N] of output values for input 0..N-1.
//
// Pipeline position
//   OpcodeList2 typically (raw linearisation / tone shaping).
//
// Implementation notes
//   The region-iteration boilerplate (rectangle + row/col pitch + plane
//   range) lives in the shared ApplyArea helper.

public static partial class OpcodesImplementation
{
    public static void MapTable(PixelBuffer img, OpcodeMapTable p)
    {
        if (p.table.Length == 0)
            return;
        int last = p.table.Length - 1;
        ApplyArea(img, p, (x, y, plane, v) => p.table[Math.Clamp((int)v, 0, last)]);
    }
}
