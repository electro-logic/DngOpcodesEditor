using System;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// Camera-native-RGB to linear-sRGB colour transform driven by the DNG's
// AsShotNeutral (50728) + ColorMatrix2 (50722) tags.
//
// Theory: the DNG colour matrix M maps reference XYZ -> camera native RGB,
// so the inverse maps camera RGB -> XYZ. After white-balancing by dividing
// each channel by AsShotNeutral, multiplying by inv(M) gives an XYZ value at
// the calibration illuminant. A standard XYZ -> sRGB matrix then yields
// linear sRGB. We pre-multiply the three matrices into a single 3x3 so the
// per-pixel work is just one matrix-vector product.
//
// This is "good enough" colour: it ignores DNG's tone-curve, chromatic
// adaptation between illuminants, and the linearisation table. It gets DJI
// drone DNGs from a green-cast look to roughly accurate colour, which is
// what the preview needs.
public static class ColorTransform
{
    // Standard XYZ (D65) -> linear sRGB matrix.
    static readonly double[,] XyzToSrgbD65 =
    {
        {  3.2406, -1.5372, -0.4986 },
        { -0.9689,  1.8758,  0.0415 },
        {  0.0557, -0.2040,  1.0570 },
    };

    // Bradford chromatic-adaptation matrix from D50 to D65. DNG's PCS is
    // D50, sRGB is D65, so XYZ values out of the inverted ColorMatrix need
    // adapting before going through the sRGB primaries matrix.
    static readonly double[,] BradfordD50ToD65 =
    {
        {  0.9555766, -0.0230393,  0.0631636 },
        { -0.0282895,  1.0099416,  0.0210077 },
        {  0.0122982, -0.0204830,  1.3299098 },
    };

    // Builds the 3x3 matrix that takes a camera-native-RGB triple to
    // linear sRGB. `baselineExposureStops` (DNG tag 50730) is folded in as a
    // uniform 2^stops gain so a `BaselineExposure` of +0.86 stops, for
    // example, brightens the result by ~1.81x — what the DNG spec asks raw
    // converters to do by default.
    public static double[,] BuildCameraToSrgb(double[] asShotNeutral, double[,] colorMatrix, double baselineExposureStops = 0.0)
    {
        if (asShotNeutral == null || asShotNeutral.Length < 3)
            throw new ArgumentException("AsShotNeutral must have 3 components.", nameof(asShotNeutral));
        var invColor = Invert3x3(colorMatrix);
        var combined = Multiply3x3(XyzToSrgbD65, Multiply3x3(BradfordD50ToD65, invColor));
        // Right-multiply by diag(1 / AsShotNeutral) to bake the white balance in.
        var m = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r, c] = combined[r, c] / asShotNeutral[c];
        // Row-normalise so the scene white (a camera reading equal to
        // AsShotNeutral) maps exactly to (1, 1, 1) linear sRGB. This is a
        // common "white-balance after matrix" approximation used by simple
        // raw converters — it produces correct white even when the colour
        // matrix doesn't perfectly satisfy the DNG calibration assumptions
        // (e.g. for some drone / Hasselblad combinations). Highly saturated
        // colours can shift slightly as a side-effect, which is the trade.
        double[] white = MultiplyVec(m, asShotNeutral);
        for (int r = 0; r < 3; r++)
        {
            if (Math.Abs(white[r]) > 1e-9)
                for (int c = 0; c < 3; c++)
                    m[r, c] /= white[r];
        }
        // BaselineExposure (in stops) applied last as a uniform 2^stops gain.
        if (baselineExposureStops != 0.0)
        {
            double scale = Math.Pow(2.0, baselineExposureStops);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    m[r, c] *= scale;
        }
        return m;
    }

    // Applies the camera-to-sRGB matrix in place over every pixel of the
    // buffer. Each output channel is clamped to [0, 65535].
    //
    // When `asShotNeutralForDesat` is supplied, each pixel is first checked
    // in white-balanced camera space: if any channel exceeds 1.0 the pixel
    // is blended toward neutral white before the matrix runs. This kills
    // the magenta cast that otherwise appears in clipped highlights, where
    // a channel reaches its sensor maximum before the other two.
    public static void Apply(PixelBuffer buffer, double[,] cameraToSrgb, double[] asShotNeutralForDesat = null)
    {
        int W = buffer.Width, H = buffer.Height;
        double m00 = cameraToSrgb[0, 0], m01 = cameraToSrgb[0, 1], m02 = cameraToSrgb[0, 2];
        double m10 = cameraToSrgb[1, 0], m11 = cameraToSrgb[1, 1], m12 = cameraToSrgb[1, 2];
        double m20 = cameraToSrgb[2, 0], m21 = cameraToSrgb[2, 1], m22 = cameraToSrgb[2, 2];
        bool desat = asShotNeutralForDesat != null && asShotNeutralForDesat.Length >= 3;
        double nR = desat ? asShotNeutralForDesat[0] : 1.0;
        double nG = desat ? asShotNeutralForDesat[1] : 1.0;
        double nB = desat ? asShotNeutralForDesat[2] : 1.0;
        // Below this WB'd max channel the pixel is untouched; above it the
        // desaturation ramps up to a full blend at maxC = 1 + DESAT_RANGE.
        const double DESAT_START = 0.95;
        const double DESAT_RANGE = 0.10;
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                var px = buffer.GetRgb16Pixel(x, y);
                double r = px[0], g = px[1], b = px[2];
                if (desat)
                {
                    double wr = r / (65535.0 * nR);
                    double wg = g / (65535.0 * nG);
                    double wb = b / (65535.0 * nB);
                    double maxC = Math.Max(wr, Math.Max(wg, wb));
                    if (maxC > DESAT_START)
                    {
                        double t = Math.Clamp((maxC - DESAT_START) / DESAT_RANGE, 0.0, 1.0);
                        // Blend toward a neutral grey at the same maximum
                        // brightness — pure highlights become white instead
                        // of taking on the cast of whichever channel clipped
                        // first.
                        wr = wr * (1 - t) + maxC * t;
                        wg = wg * (1 - t) + maxC * t;
                        wb = wb * (1 - t) + maxC * t;
                        r = wr * nR * 65535.0;
                        g = wg * nG * 65535.0;
                        b = wb * nB * 65535.0;
                    }
                }
                double rOut = m00 * r + m01 * g + m02 * b;
                double gOut = m10 * r + m11 * g + m12 * b;
                double bOut = m20 * r + m21 * g + m22 * b;
                px[0] = (ushort)Math.Clamp(Math.Round(rOut), 0, 65535);
                px[1] = (ushort)Math.Clamp(Math.Round(gOut), 0, 65535);
                px[2] = (ushort)Math.Clamp(Math.Round(bOut), 0, 65535);
            }
        });
    }

    // 3x3 matrix inverse via the cofactor / adjugate formula.
    public static double[,] Invert3x3(double[,] m)
    {
        double a = m[0, 0], b = m[0, 1], c = m[0, 2];
        double d = m[1, 0], e = m[1, 1], f = m[1, 2];
        double g = m[2, 0], h = m[2, 1], i = m[2, 2];
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (Math.Abs(det) < 1e-12)
            throw new InvalidOperationException("3x3 matrix is singular.");
        double inv = 1.0 / det;
        var r = new double[3, 3];
        r[0, 0] = (e * i - f * h) * inv;
        r[0, 1] = (c * h - b * i) * inv;
        r[0, 2] = (b * f - c * e) * inv;
        r[1, 0] = (f * g - d * i) * inv;
        r[1, 1] = (a * i - c * g) * inv;
        r[1, 2] = (c * d - a * f) * inv;
        r[2, 0] = (d * h - e * g) * inv;
        r[2, 1] = (b * g - a * h) * inv;
        r[2, 2] = (a * e - b * d) * inv;
        return r;
    }

    public static double[,] Multiply3x3(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double s = 0;
                for (int k = 0; k < 3; k++) s += a[i, k] * b[k, j];
                r[i, j] = s;
            }
        return r;
    }

    public static double[] MultiplyVec(double[,] m, double[] v)
    {
        return new[]
        {
            m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
            m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
            m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
        };
    }
}
