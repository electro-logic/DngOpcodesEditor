using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// =============================================================================
// GainMap (DNG opcode 9)
// =============================================================================
//
// Multiplicative shading correction: a 2-D gain field, bilinearly
// interpolated, applied to a rectangular region of the image. This is how
// drone cameras correct lens vignetting + sensor non-uniformity in a single
// pre-computed table.
//
// Parameters (OpcodeGainMap)
//   top/left/bottom/right        affected pixel rectangle.
//   plane / planes               image channels the gain map covers.
//   rowPitch / colPitch          step within the rectangle (typically 1 / 1).
//   mapPointsV / mapPointsH      grid resolution of the gain field.
//   mapSpacingV / mapSpacingH    normalised step between grid points (in
//                                 [0,1] image coordinates).
//   mapOriginV / mapOriginH      normalised origin of the grid.
//   mapPlanes                    number of gain planes packed in mapGains
//                                 (1 = shared by every channel; otherwise
//                                 per-channel).
//   mapGains                     float[mapPointsH * mapPointsV * mapPlanes]
//                                 in plane-interleaved order.
//
// Pipeline position
//   OpcodeList3 typically — applied after demosaic. The DJI Mavic 3 Pro DNGs
//   in the test corpus ship a 16×16-ish three-plane map here.
//
// Implementation notes
//   - Map values are bilinearly interpolated.
//   - Pixels outside the rectangle are left alone.
//   - Coordinates outside the gain field (after origin/spacing) clamp to the
//     edge of the map, which is the spec's "edge replication" behaviour.

public static partial class OpcodesImplementation
{
    public static void GainMap(PixelBuffer img, OpcodeGainMap p)
    {
        var sw = Stopwatch.StartNew();
        if (p.mapGains.Length == 0 || p.mapPointsH == 0 || p.mapPointsV == 0)
            return;
        // Split p.mapGains by p.mapPlanes and transform to a 2D array.
        // Ex. float[3072] -> 3x float[32,32]
        int mapPlanes = (int)Math.Max(1, p.mapPlanes);
        var mapGainsPlanes = new float[mapPlanes][,];
        for (int planeIndex = 0; planeIndex < mapPlanes; planeIndex++)
        {
            var planeChannel = p.mapGains.Where((f, i) => i % mapPlanes == planeIndex).ToArray();
            mapGainsPlanes[planeIndex] = MathHelper.ArrayToArray2D(planeChannel, (int)p.mapPointsH);
        }
        int planeEnd = (int)Math.Min(p.plane + p.planes, 3u);
        Parallel.For(0, img.Height, (y) =>
        {
            for (int x = 0; x < img.Width; x++)
            {
                // The opcode only affects pixels inside its rectangle.
                if (x >= p.left && x <= p.right && y >= p.top && y <= p.bottom)
                {
                    // Convert x,y into [0,1] image range.
                    double xRel = x / (img.Width - 1.0);
                    double yRel = y / (img.Height - 1.0);
                    // Convert from image [0,1]x[0,1] to map [0,mapPointsH]x[0,mapPointsV].
                    // Clamping replicates the gain values from the edge of the map.
                    double xMap = Math.Clamp((xRel - p.mapOriginH) / p.mapSpacingH, 0.0, p.mapPointsH - 1.0);
                    double yMap = Math.Clamp((yRel - p.mapOriginV) / p.mapSpacingV, 0.0, p.mapPointsV - 1.0);
                    var pixel = img.GetRgb16Pixel(x, y);
                    for (int planeIndex = (int)p.plane; planeIndex < planeEnd; planeIndex++)
                    {
                        // Use the last gain map plane if there are fewer map planes than image planes.
                        int mapPlane = Math.Min(planeIndex - (int)p.plane, mapGainsPlanes.Length - 1);
                        var gain = MathHelper.BilinearInterpolation(mapGainsPlanes[mapPlane], xMap, yMap);
                        pixel[planeIndex] = (UInt16)Math.Clamp(Math.Round(pixel[planeIndex] * gain), 0, 65535);
                    }
                }
            }
        });
        Debug.WriteLine($"\tGainMap executed in {sw.ElapsedMilliseconds}ms");
    }
}
