using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// TrimBounds (DNG opcode 6)
// =============================================================================
//
// Declares a sub-rectangle of the image that the rest of the pipeline should
// treat as the only valid image area; everything outside is cropped or marked
// as undefined.
//
// Parameters (OpcodeTrimBounds)
//   top, left, bottom, right  absolute pixel coordinates of the surviving rect.
//
// Pipeline position
//   Any OpcodeList; usually OpcodeList3.
//
// Implementation notes
//   To keep the editor's previewed image dimensions stable across slider
//   tweaks (and to make the trim region visible to the user), pixels outside
//   the rectangle are zeroed instead of removed. The dimensions of the
//   PixelBuffer never change.

public static partial class OpcodesImplementation
{
    public static void TrimBounds(PixelBuffer img, OpcodeTrimBounds p)
    {
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                if ((x <= p.left) || (x >= p.right) || (y <= p.top) || (y >= p.bottom))
                {
                    unchecked { img.SetPixel(x, y, 0); }
                }
            }
        });
    }
}
