using System;
using System.Collections.Generic;

namespace DngOpcodesEditor;

// Extracts a friendly Name/Value list of the most useful TIFF/EXIF/DNG tags
// from a DNG (or TIFF) file. Used by the metadata viewer panel.
//
// Per-tag IFD preference:
//   - Image-shape tags (Image Width/Length, BitsPerSample, Compression,
//     PhotometricInterpretation, CFAPattern, BlackLevel, WhiteLevel) prefer
//     the raw image SubIFD (photometric 32803 / 34892) when one exists,
//     because IFD0 in a DNG usually holds a small embedded thumbnail and
//     reporting its shape would be misleading.
//   - Everything else (Make / Model / EXIF / DNG colour-calibration tags)
//     prefers IFD0 + its EXIF sub-IFD, falling back to other IFDs.
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

    // Tags whose value belongs to the raw image, not the thumbnail.
    static readonly HashSet<ushort> _rawPreferredTags = new()
    {
        256, 257, 258, 259, 262, 33422, 50714, 50717,
    };

    public static List<Entry> Read(byte[] tiff)
    {
        var result = new List<Entry>();
        try
        {
            var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);

            // All IFDs reachable from the file (IFD chain + SubIFDs).
            var allIfds = new List<uint>(TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd));

            // The raw image IFD = the first IFD whose photometric interpretation
            // is CFA (32803) or LinearRaw (34892).
            uint? rawIfd = null;
            foreach (var ifd in allIfds)
            {
                int photoEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)ifd, 262);
                if (photoEntry < 0) continue;
                var photo = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, photoEntry);
                if (photo.Length > 0 && (photo[0] == 32803 || photo[0] == 34892))
                {
                    rawIfd = ifd;
                    break;
                }
            }

            // The EXIF SubIFD is linked from IFD0 via tag 34665.
            uint? exifIfd = null;
            int exifLink = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 34665);
            if (exifLink >= 0)
            {
                var v = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, exifLink);
                if (v.Length > 0) exifIfd = v[0];
            }

            var rawFirst = BuildSearchOrder(rawIfd, firstIfd, exifIfd, allIfds, preferRaw: true);
            var ifd0First = BuildSearchOrder(rawIfd, firstIfd, exifIfd, allIfds, preferRaw: false);

            foreach (var (tag, name) in _knownTags)
            {
                var order = _rawPreferredTags.Contains(tag) ? rawFirst : ifd0First;
                foreach (var ifd in order)
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

    static List<uint> BuildSearchOrder(uint? rawIfd, uint firstIfd, uint? exifIfd, List<uint> allIfds, bool preferRaw)
    {
        var order = new List<uint>();
        var seen = new HashSet<uint>();
        void Add(uint ifd)
        {
            if (seen.Add(ifd)) order.Add(ifd);
        }
        if (preferRaw)
        {
            if (rawIfd.HasValue) Add(rawIfd.Value);
            Add(firstIfd);
        }
        else
        {
            Add(firstIfd);
            if (exifIfd.HasValue) Add(exifIfd.Value);
        }
        foreach (var ifd in allIfds) Add(ifd);
        if (exifIfd.HasValue) Add(exifIfd.Value);
        if (rawIfd.HasValue) Add(rawIfd.Value);
        return order;
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
                "5" => "LZW",
                "7" => "Lossless JPEG",
                "8" or "32946" => "Adobe Deflate",
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
