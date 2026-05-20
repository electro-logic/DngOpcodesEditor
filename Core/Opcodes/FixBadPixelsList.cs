namespace DngOpcodesEditor;

// =============================================================================
// FixBadPixelsList (DNG opcode 5)
// =============================================================================
//
// Repairs an explicit list of bad pixels and bad rectangles, interpolating
// each from its neighbours. Cameras ship per-unit factory bad-pixel maps
// inside this opcode.
//
// Parameters (OpcodeFixBadPixelsList)
//   bayerPhase  CFA phase (0..3) — relevant for raw CFA data, ignored here.
//   badPoints   flat uint32[] of (row, col) pairs.
//   badRects    flat uint32[] of (top, left, bottom, right) quads.
//
// Pipeline position
//   OpcodeList1 by spec.
//
// Implementation notes
//   The DJI Phantom 4 ships ~6 kB of bad-point data per frame inside this
//   opcode. Like FixBadPixelsConstant, the proper algorithm interpolates on
//   the CFA grid; on the demosaiced preview we average the four
//   axis-aligned neighbours of each listed pixel for each channel. Source is
//   snapshotted before the pass so neighbour reads are consistent.

public static partial class OpcodesImplementation
{
    public static void FixBadPixelsList(PixelBuffer img, OpcodeFixBadPixelsList p)
    {
        var src = img.ClonePixels();
        for (int i = 0; i + 1 < p.badPoints.Length; i += 2)
        {
            FixPixel(img, src, (int)p.badPoints[i + 1], (int)p.badPoints[i]);
        }
        for (int i = 0; i + 3 < p.badRects.Length; i += 4)
        {
            int top = (int)p.badRects[i], left = (int)p.badRects[i + 1];
            int bottom = (int)p.badRects[i + 2], right = (int)p.badRects[i + 3];
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                    FixPixel(img, src, x, y);
        }
    }
}
