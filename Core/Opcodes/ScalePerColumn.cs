using System;

namespace DngOpcodesEditor;

// =============================================================================
// ScalePerColumn (DNG opcode 13)
// =============================================================================
//
// Multiplies every pixel of a rectangular region by a per-column scale
// factor. Column counterpart to ScalePerRow.
//
// Parameters (OpcodeScalePerColumn)
//   top/left/bottom/right   affected region.
//   plane / planes          channel range.
//   rowPitch / colPitch     pitch (often 1 / 1).
//   scales                  float[] of per-column gains; indexed by
//                            (x - left) / colPitch.
//
// Pipeline position
//   OpcodeList2 typically.

public static partial class OpcodesImplementation
{
    public static void ScalePerColumn(PixelBuffer img, OpcodeScalePerColumn p)
    {
        int left = (int)p.left, colPitch = (int)Math.Max(1, p.colPitch);
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            int index = (x - left) / colPitch;
            if (index < 0 || index >= p.scales.Length)
                return v;
            return (UInt16)Math.Clamp(Math.Round(v * p.scales[index]), 0, 65535);
        });
    }
}
