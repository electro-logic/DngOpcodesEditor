using System;
using System.Collections.Generic;
using System.Linq;

namespace DngOpcodesEditor;

// Extracts a friendly Name/Value list of the most useful TIFF/EXIF/DNG tags
// from a DNG (or TIFF) file. Used by the metadata viewer panel.
//
// Tags are picked up from IFD0, every SubIFD, and the ExifIFD (linked from
// IFD0 via tag 34665). The first occurrence of each known tag wins, which is
// what you want when both IFD0 and the raw SubIFD declare the same tag.
public static class DngMetadata
{
    public class Entry
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public ushort Tag { get; set; }
    }

    // Order matters: this is the display order in the panel.
    static readonly (ushort Tag, string Name)[] _knownTags =
    {
        (271, "Make"),
        (272, "Model"),
        (305, "Software"),
        (306, "Modify Date"),
        (36867, "Date Taken"),
        (33434, "Exposure Time"),
        (33437, "F-Number"),
        (34855, "ISO"),
        (37386, "Focal Length"),
        (37380, "Exposure Bias"),
        (256, "Image Width"),
        (257, "Image Length"),
        (258, "Bits Per Sample"),
        (259, "Compression"),
        (262, "Photometric Interpretation"),
        (33422, "CFA Pattern"),
        (50706, "DNG Version"),
        (50708, "Unique Camera Model"),
        (50714, "Black Level"),
        (50717, "White Level"),
        (50721, "Color Matrix 1"),
        (50722, "Color Matrix 2"),
        (50727, "Analog Balance"),
        (50728, "As Shot Neutral"),
        (50730, "Baseline Exposure"),
        (50778, "Calibration Illuminant 1"),
        (50779, "Calibration Illuminant 2"),
    };

    public static List<Entry> Read(byte[] tiff)
    {
        var result = new List<Entry>();
        try
        {
            var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);

            // IFDs to scan: IFD chain + SubIFDs, plus the EXIF sub-IFD if linked.
            var ifds = new List<uint>(TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd));
            int exifLink = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 34665);
            if (exifLink >= 0)
            {
                var v = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, exifLink);
                if (v.Length > 0) ifds.Add(v[0]);
            }

            foreach (var (tag, name) in _knownTags)
            {
                foreach (var ifd in ifds)
                {
                    int entry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, tag);
                    if (entry < 0) continue;
                    string raw = TiffFile.FormatEntryValue(tiff, isLE, entry);
                    result.Add(new Entry { Tag = tag, Name = name, Value = DecorateValue(tag, raw) });
                    break;
                }
            }
        }
        catch
        {
            // Best-effort: a non-TIFF file simply yields no metadata.
        }
        return result;
    }

    // Light per-tag prettification: translate well-known numeric codes to
    // readable names, append units to scalars.
    static string DecorateValue(ushort tag, string raw)
    {
        return tag switch
        {
            259 => raw switch        // Compression
            {
                "1" => "Uncompressed",
                "7" => "Lossless JPEG",
                "8" => "Adobe Deflate",
                _ => raw
            },
            262 => raw switch        // PhotometricInterpretation
            {
                "32803" => "CFA",
                "34892" => "LinearRaw",
                "2" => "RGB",
                "1" => "BlackIsZero",
                _ => raw
            },
            50778 or 50779 => CalibrationIlluminantName(raw),
            33437 => $"f/{raw}",
            37386 => $"{raw} mm",
            33434 => $"{raw} s",
            _ => raw
        };
    }

    static string CalibrationIlluminantName(string raw) => raw switch
    {
        "1" => "Daylight",
        "2" => "Fluorescent",
        "3" => "Tungsten",
        "4" => "Flash",
        "9" => "Fine weather",
        "10" => "Cloudy",
        "11" => "Shade",
        "17" => "Standard A",
        "18" => "Standard B",
        "19" => "Standard C",
        "20" => "D55",
        "21" => "D65",
        "22" => "D75",
        "23" => "D50",
        _ => raw
    };
}
