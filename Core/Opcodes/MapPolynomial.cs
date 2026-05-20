using System;

namespace DngOpcodesEditor;

// =============================================================================
// MapPolynomial (DNG opcode 8)
// =============================================================================
//
// Per-channel polynomial mapping over a rectangular region. The input is
// normalised to [0, 1], the polynomial evaluated, the result rescaled back to
// 16-bit and clamped.
//
//   out = clamp((c0 + c1·v + c2·v² + … + cN·vᴺ) · 65535)
//
// Parameters (OpcodeMapPolynomial)
//   top/left/bottom/right   region to operate on (pixels).
//   plane / planes          channel range.
//   coefficients            double[N+1] of polynomial coefficients (low to high).
//
// Pipeline position
//   OpcodeList2 typically.

public static partial class OpcodesImplementation
{
    public static void MapPolynomial(PixelBuffer img, OpcodeMapPolynomial p)
    {
        ApplyArea(img, p, (x, y, plane, v) =>
        {
            double n = v / 65535.0;
            double acc = 0.0, power = 1.0;
            for (int i = 0; i < p.coefficients.Length; i++)
            {
                acc += p.coefficients[i] * power;
                power *= n;
            }
            return (UInt16)Math.Clamp(Math.Round(acc * 65535.0), 0, 65535);
        });
    }
}
