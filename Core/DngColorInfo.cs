using System.IO;

namespace DngOpcodesEditor;

// Snapshot of the colour-conversion tags a DNG carries, so callers can take
// a camera-native-RGB PixelBuffer (the output of DngRawReader) and convert
// it to linear sRGB via ColorTransform.
public class DngColorInfo
{
    public double[] AsShotNeutral { get; init; }
    public double[,] ColorMatrix { get; init; }
    // DNG tag 50730 — recommended additional exposure adjustment in stops.
    public double BaselineExposure { get; init; }
    // Pre-built camera-native-RGB -> linear-sRGB matrix (white-balance
    // + colour matrix + baseline exposure all baked in).
    public double[,] CameraToSrgb { get; init; }
    // DNG tag 50940 ProfileToneCurve, pre-baked into a 16-bit LUT. Null if
    // the DNG doesn't ship one.
    public DngToneCurve ToneCurve { get; init; }

    // Reads the relevant tags. Returns null if the file isn't a TIFF / DNG
    // or if the colour-calibration tags aren't present.
    public static DngColorInfo TryRead(byte[] tiff)
    {
        try
        {
            var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);
            int neutralEntry = -1;
            int matrixEntry = -1;
            int baselineEntry = -1;
            int toneCurveEntry = -1;
            foreach (var ifd in TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd))
            {
                if (neutralEntry < 0)
                    neutralEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50728);
                // Prefer ColorMatrix2 (D65); fall back to ColorMatrix1 (Standard A).
                if (matrixEntry < 0)
                    matrixEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50722);
                if (baselineEntry < 0)
                    baselineEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50730);
                if (toneCurveEntry < 0)
                    toneCurveEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50940);
                if (neutralEntry >= 0 && matrixEntry >= 0 && baselineEntry >= 0 && toneCurveEntry >= 0) break;
            }
            if (matrixEntry < 0)
            {
                foreach (var ifd in TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd))
                {
                    matrixEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50721);
                    if (matrixEntry >= 0) break;
                }
            }
            if (neutralEntry < 0 || matrixEntry < 0)
                return null;

            var neutral = TiffFile.ReadEntryAsDoubleArray(tiff, isLE, neutralEntry);
            var matrixFlat = TiffFile.ReadEntryAsDoubleArray(tiff, isLE, matrixEntry);
            if (neutral.Length < 3 || matrixFlat.Length < 9)
                return null;
            var matrix = new double[3, 3];
            for (int i = 0; i < 9; i++) matrix[i / 3, i % 3] = matrixFlat[i];
            double baseline = 0;
            if (baselineEntry >= 0)
            {
                var v = TiffFile.ReadEntryAsDoubleArray(tiff, isLE, baselineEntry);
                if (v.Length > 0) baseline = v[0];
            }
            DngToneCurve toneCurve = null;
            if (toneCurveEntry >= 0)
            {
                var xy = TiffFile.ReadEntryAsDoubleArray(tiff, isLE, toneCurveEntry);
                toneCurve = DngToneCurve.FromControlPoints(xy);
            }
            return new DngColorInfo
            {
                AsShotNeutral = neutral,
                ColorMatrix = matrix,
                BaselineExposure = baseline,
                CameraToSrgb = ColorTransform.BuildCameraToSrgb(neutral, matrix, baseline),
                ToneCurve = toneCurve,
            };
        }
        catch
        {
            return null;
        }
    }
}
