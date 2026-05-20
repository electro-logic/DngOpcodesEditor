using System;

namespace DngOpcodesEditor;

// =============================================================================
// DeltaPerColumn (DNG opcode 11)
// =============================================================================
//
// Adds a per-column offset (in normalised [0, 1] sample space) to every
// pixel of a rectangular region. Column-banding counterpart to DeltaPerRow.
//
// Parameters (OpcodeDeltaPerColumn)
//   top/left/bottom/right   affected region.
//   plane / planes          channel range.
//   rowPitch / colPitch     pitch (often 1 / 1).
//   deltas                  float[] of per-column deltas; indexed by
//                            (x - left) / colPitch.
//
// Pipeline position
//   OpcodeList2 typically.

public static partial class OpcodesImplementation
{
    public static void DeltaPerColumn(PixelBuffer img, OpcodeDeltaPerColumn p)
    {
        int left = (int)p.left, colPitch = (int)Math.Max(1, p.colPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (x - left) / colPitch;
            if (index < 0 || index >= p.deltas.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round((v / 65535.0 + p.deltas[index]) * 65535.0), 0, 65535);
        });
    }
}
