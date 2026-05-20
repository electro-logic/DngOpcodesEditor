using System;

namespace DngOpcodesEditor;

// =============================================================================
// DeltaPerRow (DNG opcode 10)
// =============================================================================
//
// Adds a per-row offset (in normalised [0, 1] sample space) to every pixel
// of a rectangular region. Useful for correcting horizontal banding /
// per-row dark-frame bias in scientific or astrophotography cameras.
//
// Parameters (OpcodeDeltaPerRow)
//   top/left/bottom/right   affected region.
//   plane / planes          channel range.
//   rowPitch / colPitch     pitch within the region (often 1 / 1).
//   deltas                  float[] of per-row deltas in [-1, 1] normalised
//                            space; deltas[(y - top) / rowPitch] applies to
//                            the matching row.
//
// Pipeline position
//   OpcodeList2 typically.

public static partial class OpcodesImplementation
{
    public static void DeltaPerRow(PixelBuffer img, OpcodeDeltaPerRow p)
    {
        int top = (int)p.top, rowPitch = (int)Math.Max(1, p.rowPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (y - top) / rowPitch;
            if (index < 0 || index >= p.deltas.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round((v / 65535.0 + p.deltas[index]) * 65535.0), 0, 65535);
        });
    }
}
