using System;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// FixBadPixelsConstant (DNG opcode 4)
// =============================================================================
//
// Replaces every pixel whose value equals a sentinel "constant" with an
// interpolated value reconstructed from its neighbours. The sentinel is the
// camera's way of marking dead / stuck pixels in raw data.
//
// Parameters (OpcodeFixBadPixelsConstant)
//   constant    raw sample value that flags a bad pixel.
//   bayerPhase  CFA phase (0..3) — relevant for raw CFA data, ignored on the
//               demosaiced RGB preview.
//
// Pipeline position
//   OpcodeList1, by spec — applied to raw CFA before linearisation.
//
// Implementation notes
//   This opcode is designed for raw CFA data. On the demosaiced RGB buffer the
//   editor previews against, we approximate by averaging the four
//   axis-aligned neighbours of any pixel where *any* channel equals the
//   constant — a useful sanity check, not a faithful CFA repair. The source
//   buffer is snapshotted before the pass so neighbour reads are consistent.

public static partial class OpcodesImplementation
{
    public static void FixBadPixelsConstant(PixelBuffer img, OpcodeFixBadPixelsConstant p)
    {
        var src = img.ClonePixels();
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                var pixel = img.GetRgb16Pixel(x, y);
                for (int channel = 0; channel < 3; channel++)
                {
                    if (pixel[channel] == p.constant)
                        pixel[channel] = NeighborAverage(src, x, y, channel);
                }
            }
        });
    }
}
