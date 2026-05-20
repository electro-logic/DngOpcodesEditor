using System;

namespace DngOpcodesEditor;

// =============================================================================
// ScalePerRow (DNG opcode 12)
// =============================================================================
//
// Multiplies every pixel of a rectangular region by a per-row scale factor.
// Multiplicative counterpart to DeltaPerRow — corrects PRNU (per-pixel
// response non-uniformity) along rows.
//
// Parameters (OpcodeScalePerRow)
//   top/left/bottom/right   affected region.
//   plane / planes          channel range.
//   rowPitch / colPitch     pitch (often 1 / 1).
//   scales                  float[] of per-row gains; scales[(y - top) /
//                            rowPitch] multiplies the matching row.
//
// Pipeline position
//   OpcodeList2 typically.

public static partial class OpcodesImplementation
{
    public static void ScalePerRow(PixelBuffer img, OpcodeScalePerRow p)
    {
        int top = (int)p.top, rowPitch = (int)Math.Max(1, p.rowPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (y - top) / rowPitch;
            if (index < 0 || index >= p.scales.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round(v * p.scales[index]), 0, 65535);
        });
    }
}
