using System.IO;

namespace DngOpcodesEditor;

// Snapshot of the colour-conversion tags a DNG carries, so callers can take
// a camera-native-RGB PixelBuffer (the output of DngRawReader) and convert
// it to linear sRGB via ColorTransform.
public class DngColorInfo
{
    public double[] AsShotNeutral { get; init; }
    public double[,] ColorMatrix { get; init; }
    // Pre-built camera-native-RGB -> linear-sRGB matrix.
    public double[,] CameraToSrgb { get; init; }

    // Reads the relevant tags. Returns null if the file isn't a TIFF / DNG
    // or if the colour-calibration tags aren't present.
    public static DngColorInfo TryRead(byte[] tiff)
    {
        try
        {
            var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);
            int neutralEntry = -1;
            int matrixEntry = -1;
            foreach (var ifd in TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd))
            {
                if (neutralEntry < 0)
                    neutralEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50728);
                // Prefer ColorMatrix2 (D65); fall back to ColorMatrix1 (Standard A).
                if (matrixEntry < 0)
                    matrixEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 50722);
                if (neutralEntry >= 0 && matrixEntry >= 0) break;
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
            return new DngColorInfo
            {
                AsShotNeutral = neutral,
                ColorMatrix = matrix,
                CameraToSrgb = ColorTransform.BuildCameraToSrgb(neutral, matrix),
            };
        }
        catch
        {
            return null;
        }
    }
}
