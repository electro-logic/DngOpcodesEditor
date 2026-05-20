using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// Reads raw DNG images and returns a 16-bit linear RGB PixelBuffer ready for
// the opcode pipeline. No external dependency on dcraw_emu is required.
//
// The reader is layered:
//   - LoadSamples() handles file layout (strip vs tile) and produces a flat
//     ushort[] of width * height * samplesPerPixel values in row-major order.
//   - Per-strip / per-tile bytes go through a decoder delegate that returns
//     ushort samples. Uncompressed and Lossless JPEG (compression 7) are
//     supported.
//   - The output formatter then either demosaics (CFA, photometric 32803)
//     or unpacks the interleaved RGB samples (LinearRaw, photometric 34892).
public static partial class DngRawReader
{
    delegate ushort[] SampleDecoder(byte[] file, int offset, int byteCount, int bitsPerSample, bool isLE);

    public static PixelBuffer Read(byte[] tiff)
    {
        var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);
        // EXIF Orientation lives in IFD0; apply it after demosaic so the
        // returned buffer is in display-canonical orientation.
        int orientation = ReadOrientation(tiff, isLE, firstIfd);
        // Walk every IFD once, picking the best image:
        //   1st choice: CFA (32803) or LinearRaw (34892) — the actual raw image.
        //   2nd choice: RGB (2) — useful for already-developed TIFFs and as a
        //   fall-back when no raw IFD is present.
        int? rgbFallback = null;
        foreach (var ifd in TiffFile.EnumerateIfdsPublic(tiff, isLE, firstIfd))
        {
            uint photometric = PhotometricOf(tiff, isLE, (int)ifd);
            if (photometric == 32803 || photometric == 34892)
                return ReadRawFromIfd(tiff, isLE, (int)ifd).ApplyOrientation(orientation);
            if (photometric == 2 && rgbFallback == null)
                rgbFallback = (int)ifd;
        }
        if (rgbFallback != null)
            return ReadRawFromIfd(tiff, isLE, rgbFallback.Value).ApplyOrientation(orientation);
        throw new InvalidDataException("No CFA, LinearRaw or RGB image IFD found in the file.");
    }

    static int ReadOrientation(byte[] tiff, bool isLE, uint firstIfd)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 274);
        if (e < 0) return 1;
        var v = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
        return v.Length > 0 ? (int)v[0] : 1;
    }

    static uint PhotometricOf(byte[] tiff, bool isLE, int ifd)
    {
        int entry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 262);
        if (entry < 0) return uint.MaxValue;
        var v = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, entry);
        return v.Length > 0 ? v[0] : uint.MaxValue;
    }

    static PixelBuffer ReadRawFromIfd(byte[] tiff, bool isLE, int ifd)
    {
        int width = (int)RequiredTag(tiff, isLE, ifd, 256);
        int height = (int)RequiredTag(tiff, isLE, ifd, 257);
        uint compression = OptionalTag(tiff, isLE, ifd, 259, 1);
        uint photometric = RequiredTag(tiff, isLE, ifd, 262);
        int bitsPerSample = (int)OptionalTag(tiff, isLE, ifd, 258, 16);
        // CFA = one sample per pixel; LinearRaw and RGB default to three interleaved.
        bool hasInterleavedRgb = photometric == 34892 || photometric == 2;
        int samplesPerPixel = (int)OptionalTag(tiff, isLE, ifd, 277, hasInterleavedRgb ? 3u : 1u);

        SampleDecoder decoder = compression switch
        {
            1 => DecodeUncompressed,
            5 => DecodeLzw,
            7 => DecodeLosslessJpeg,
            8 or 32946 => DecodeDeflate,
            _ => throw new NotSupportedException(
                $"Compression {compression} is not supported. " +
                "Handled values: 1 (uncompressed), 5 (LZW), 7 (Lossless JPEG), 8 / 32946 (Deflate).")
        };

        var samples = LoadSamples(tiff, isLE, ifd, width, height, samplesPerPixel, bitsPerSample, decoder);

        var blackLevels = ReadBlackLevels(tiff, isLE, ifd);
        uint whiteLevel = OptionalTag(tiff, isLE, ifd, 50717, (uint)((1u << bitsPerSample) - 1));

        var rgb = new UInt64[width * height];
        if (photometric == 32803)
        {
            var cfaPattern = ReadCfaPattern(tiff, isLE, ifd);
            // 1. Linearise CFA samples to 16-bit using per-Bayer-position
            //    black levels and the white level.
            LineariseCfaInPlace(samples, width, height, blackLevels, whiteLevel);
            // 2. Apply OpcodeList2 to the linearised CFA. Per DNG spec, L2
            //    runs after linearisation but *before* demosaicing — this is
            //    where lens-shading-correction GainMaps belong. Doing it on
            //    the demosaiced RGB buffer (what the editor used to do) is
            //    catastrophically wrong when the maps are per-Bayer-plane
            //    (e.g. Pixel 6: 4× GainMap, plane=0, rowPitch/colPitch=2)
            //    because they all collapse onto the R channel.
            ApplyOpcodeList2OnCfa(tiff, samples, width, height);
            // 3. Demosaic the linearised samples (no further black/white
            //    correction — already done in step 1).
            BilinearDemosaicLinearised(samples, rgb, width, height, cfaPattern);
        }
        else // 34892 LinearRaw or 2 RGB — both are interleaved R/G/B per pixel.
        {
            UnpackLinearRaw(samples, rgb, width, height, samplesPerPixel, blackLevels, whiteLevel);
        }
        return new PixelBuffer(rgb, width, height);
    }

    // --- Sample loading: strip vs tile ---------------------------------------

    static ushort[] LoadSamples(byte[] tiff, bool isLE, int ifd, int width, int height,
        int samplesPerPixel, int bitsPerSample, SampleDecoder decoder)
    {
        int totalSamples = width * height * samplesPerPixel;
        var samples = new ushort[totalSamples];

        bool tiled = TiffFile.FindEntryPublic(tiff, isLE, ifd, 322) >= 0;
        if (tiled)
            LoadFromTiles(tiff, isLE, ifd, width, height, samplesPerPixel, bitsPerSample, decoder, samples);
        else
            LoadFromStrips(tiff, isLE, ifd, width, height, samplesPerPixel, bitsPerSample, decoder, samples);
        return samples;
    }

    static void LoadFromStrips(byte[] tiff, bool isLE, int ifd, int width, int height,
        int spp, int bps, SampleDecoder decoder, ushort[] samples)
    {
        int stripOffsetsEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 273);
        int stripCountsEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 279);
        if (stripOffsetsEntry < 0 || stripCountsEntry < 0)
            throw new InvalidDataException("Strip-based DNG is missing StripOffsets/StripByteCounts.");
        uint rowsPerStrip = OptionalTag(tiff, isLE, ifd, 278, (uint)height);
        var stripOffsets = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, stripOffsetsEntry);
        var stripByteCounts = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, stripCountsEntry);

        int rowsRead = 0;
        for (int s = 0; s < stripOffsets.Length; s++)
        {
            int stripRows = Math.Min((int)rowsPerStrip, height - rowsRead);
            int byteOff = (int)stripOffsets[s];
            int byteCount = (int)stripByteCounts[s];
            var stripSamples = decoder(tiff, byteOff, byteCount, bps, isLE);
            int rowSamples = width * spp;
            int dstOff = rowsRead * rowSamples;
            int copyLength = Math.Min(stripSamples.Length, stripRows * rowSamples);
            Array.Copy(stripSamples, 0, samples, dstOff, copyLength);
            rowsRead += stripRows;
        }
    }

    static void LoadFromTiles(byte[] tiff, bool isLE, int ifd, int width, int height,
        int spp, int bps, SampleDecoder decoder, ushort[] samples)
    {
        int tileW = (int)RequiredTag(tiff, isLE, ifd, 322);
        int tileH = (int)RequiredTag(tiff, isLE, ifd, 323);
        int tileOffsetsEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 324);
        int tileCountsEntry = TiffFile.FindEntryPublic(tiff, isLE, ifd, 325);
        if (tileOffsetsEntry < 0 || tileCountsEntry < 0)
            throw new InvalidDataException("Tiled DNG is missing TileOffsets/TileByteCounts.");
        var tileOffsets = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, tileOffsetsEntry);
        var tileByteCounts = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, tileCountsEntry);

        int tilesAcross = (width + tileW - 1) / tileW;
        int tilesDown = (height + tileH - 1) / tileH;
        int tileRowSamples = tileW * spp;
        int dstRowSamples = width * spp;

        for (int ty = 0; ty < tilesDown; ty++)
        {
            for (int tx = 0; tx < tilesAcross; tx++)
            {
                int idx = ty * tilesAcross + tx;
                int byteOff = (int)tileOffsets[idx];
                int byteCount = (int)tileByteCounts[idx];
                var tileSamples = decoder(tiff, byteOff, byteCount, bps, isLE);

                int xStart = tx * tileW;
                int yStart = ty * tileH;
                int copyCols = Math.Min(tileW, width - xStart);
                int copyRows = Math.Min(tileH, height - yStart);
                for (int ly = 0; ly < copyRows; ly++)
                {
                    int srcOff = ly * tileRowSamples;
                    int dstOff = (yStart + ly) * dstRowSamples + xStart * spp;
                    Array.Copy(tileSamples, srcOff, samples, dstOff, copyCols * spp);
                }
            }
        }
    }

    // --- Per-strip / per-tile decoders --------------------------------------

    static ushort[] DecodeUncompressed(byte[] file, int offset, int byteCount, int bitsPerSample, bool isLE)
    {
        if (bitsPerSample == 16)
        {
            int count = byteCount / 2;
            var result = new ushort[count];
            for (int i = 0; i < count; i++)
                result[i] = isLE
                    ? BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(offset + i * 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(offset + i * 2));
            return result;
        }
        if (bitsPerSample == 8)
        {
            // Keep the samples at their native precision so the downstream
            // normalisation (using BlackLevel / WhiteLevel, which are in
            // BitsPerSample units) produces correct results.
            var result = new ushort[byteCount];
            for (int i = 0; i < byteCount; i++)
                result[i] = file[offset + i];
            return result;
        }
        throw new NotSupportedException($"Uncompressed BitsPerSample = {bitsPerSample} is not supported (8 or 16 only).");
    }

    static ushort[] DecodeLosslessJpeg(byte[] file, int offset, int byteCount, int bitsPerSample, bool isLE)
    {
        // The TIFF byte-order does not apply inside a JPEG stream; LJPEG is
        // big-endian by spec, the decoder handles that internally.
        var jpegBytes = new byte[byteCount];
        Buffer.BlockCopy(file, offset, jpegBytes, 0, byteCount);
        return LosslessJpegDecoder.Decode(jpegBytes, out _, out _, out _, out _);
    }

    static ushort[] DecodeLzw(byte[] file, int offset, int byteCount, int bitsPerSample, bool isLE)
    {
        // LZW inflates to raw uncompressed sample bytes, which we then read
        // through the standard uncompressed decoder.
        var compressed = new byte[byteCount];
        Buffer.BlockCopy(file, offset, compressed, 0, byteCount);
        var inflated = LzwDecoder.Decode(compressed);
        return DecodeUncompressed(inflated, 0, inflated.Length, bitsPerSample, isLE);
    }

    static ushort[] DecodeDeflate(byte[] file, int offset, int byteCount, int bitsPerSample, bool isLE)
    {
        var compressed = new byte[byteCount];
        Buffer.BlockCopy(file, offset, compressed, 0, byteCount);
        var inflated = DeflateDecoder.Decode(compressed);
        return DecodeUncompressed(inflated, 0, inflated.Length, bitsPerSample, isLE);
    }

    // --- Tag helpers ---------------------------------------------------------

    static uint RequiredTag(byte[] tiff, bool isLE, int ifd, ushort tag)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, tag);
        if (e < 0)
            throw new InvalidDataException($"Missing required TIFF tag {tag}.");
        var values = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
        if (values.Length == 0)
            throw new InvalidDataException($"TIFF tag {tag} has no value.");
        return values[0];
    }

    static uint OptionalTag(byte[] tiff, bool isLE, int ifd, ushort tag, uint defaultValue)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, tag);
        if (e < 0)
            return defaultValue;
        var values = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
        return values.Length > 0 ? values[0] : defaultValue;
    }

    static uint[] ReadBlackLevels(byte[] tiff, bool isLE, int ifd)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, 50714);
        if (e < 0)
            return new uint[] { 0 };
        return TiffFile.ReadEntryAsUInt32Array(tiff, isLE, e);
    }

    static uint[] ReadCfaPattern(byte[] tiff, bool isLE, int ifd)
    {
        int e = TiffFile.FindEntryPublic(tiff, isLE, ifd, 33422);
        if (e < 0)
            return new uint[] { 0, 1, 1, 2 }; // RGGB default
        var bytes = TiffFile.ReadEntryBytesPublic(tiff, isLE, e);
        if (bytes.Length < 4)
            return new uint[] { 0, 1, 1, 2 };
        return new uint[] { bytes[0], bytes[1], bytes[2], bytes[3] };
    }

    // --- Output formatters ---------------------------------------------------

    // LinearRaw photometric: the samples are already interleaved R, G, B per
    // pixel. Just normalise by black/white level and pack to Rgba64.
    static void UnpackLinearRaw(ushort[] samples, UInt64[] rgb, int W, int H, int spp, uint[] black, uint white)
    {
        double blackR = black.Length > 0 ? black[0] : 0;
        double blackG = black.Length > 1 ? black[1] : blackR;
        double blackB = black.Length > 2 ? black[2] : blackR;
        double scale = 65535.0 / Math.Max(1.0, white - blackR);

        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * spp;
                ushort r = Quantize(samples[i], blackR, scale);
                ushort g = spp > 1 ? Quantize(samples[i + 1], blackG, scale) : r;
                ushort b = spp > 2 ? Quantize(samples[i + 2], blackB, scale) : r;
                rgb[y * W + x] = r | ((UInt64)g << 16) | ((UInt64)b << 32) | ((UInt64)65535 << 48);
            }
        });
    }

    static ushort Quantize(ushort raw, double black, double scale) =>
        (ushort)Math.Clamp(Math.Round((raw - black) * scale), 0, 65535);

    // Linearise CFA samples in place: subtract per-Bayer-position black
    // level and scale so the white level lands at 65535.
    static void LineariseCfaInPlace(ushort[] samples, int W, int H, uint[] black, uint white)
    {
        double[] blackByPos = new double[4];
        for (int i = 0; i < 4; i++)
            blackByPos[i] = black.Length == 4 ? black[i] : (black.Length > 0 ? black[0] : 0);
        double whiteMinusBlack = Math.Max(1.0, white - blackByPos[0]);
        double scale = 65535.0 / whiteMinusBlack;
        Parallel.For(0, H, y =>
        {
            int rowOff = y * W;
            for (int x = 0; x < W; x++)
            {
                int pos = (y & 1) * 2 + (x & 1);
                double v = (samples[rowOff + x] - blackByPos[pos]) * scale;
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round(v), 0, 65535);
            }
        });
    }

    // Apply OpcodeList2 to the already-linearised CFA buffer. Currently
    // handles GainMap (the only L2 opcode in the corpus); other CFA-targeting
    // opcodes are skipped with a debug warning. Errors during opcode parsing
    // are swallowed — a malformed L2 should not block loading the image.
    static void ApplyOpcodeList2OnCfa(byte[] tiff, ushort[] samples, int W, int H)
    {
        var payload = TiffFile.ReadOpcodeList(tiff, 2);
        if (payload == null || payload.Length == 0)
            return;
        Opcode[] opcodes;
        try { opcodes = new OpcodesReader().ReadOpcodeList(payload); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"OpcodeList2 parse failed: {ex.Message}"); return; }
        foreach (var op in opcodes)
        {
            switch (op)
            {
                case OpcodeGainMap g:               ApplyGainMapToCfa(samples, W, H, g);            break;
                case OpcodeMapTable mt:             ApplyMapTableToCfa(samples, W, H, mt);          break;
                case OpcodeMapPolynomial mp:        ApplyMapPolynomialToCfa(samples, W, H, mp);     break;
                case OpcodeDeltaPerRow dr:          ApplyDeltaPerRowToCfa(samples, W, H, dr);       break;
                case OpcodeDeltaPerColumn dc:       ApplyDeltaPerColumnToCfa(samples, W, H, dc);    break;
                case OpcodeScalePerRow sr:          ApplyScalePerRowToCfa(samples, W, H, sr);       break;
                case OpcodeScalePerColumn sc:       ApplyScalePerColumnToCfa(samples, W, H, sc);    break;
                case OpcodeFixBadPixelsConstant c:  ApplyFixBadPixelsConstantToCfa(samples, W, H, c); break;
                case OpcodeFixBadPixelsList l:      ApplyFixBadPixelsListToCfa(samples, W, H, l);   break;
                default:
                    System.Diagnostics.Debug.WriteLine($"OpcodeList2 {op.header.id} on CFA not implemented; skipped.");
                    break;
            }
        }
    }

    // Per-Bayer-plane GainMap on the linearised CFA buffer. The opcode's
    // (top, left, rowPitch, colPitch) targets one Bayer subgrid (e.g.
    // top=0 left=0 pitch=2 picks every (even, even) pixel — one of the four
    // Bayer positions). The gain field itself is bilinearly interpolated
    // and clamped at the edges per spec.
    //
    // Public so tests can exercise the CFA-targeting math directly without
    // building a synthetic DNG.
    public static void ApplyGainMapToCfa(ushort[] samples, int W, int H, OpcodeGainMap p)
    {
        if (p.mapGains.Length == 0 || p.mapPointsH == 0 || p.mapPointsV == 0)
            return;
        int top = (int)Math.Clamp(p.top, 0u, (uint)H);
        int left = (int)Math.Clamp(p.left, 0u, (uint)W);
        int bottom = (int)Math.Clamp(p.bottom, 0u, (uint)H);
        int right = (int)Math.Clamp(p.right, 0u, (uint)W);
        int rowPitch = (int)Math.Max(1, p.rowPitch);
        int colPitch = (int)Math.Max(1, p.colPitch);
        int mapPointsH = (int)p.mapPointsH;
        int mapPointsV = (int)p.mapPointsV;
        int mapPlanes = (int)Math.Max(1, p.mapPlanes);
        // CFA gain maps are single-plane (mapPlanes typically 1). MathHelper
        // .BilinearInterpolation takes (array, x, y) and reads array[x, y],
        // so the map is built [H, V] (x-major) to match that convention.
        var map = new float[mapPointsH, mapPointsV];
        for (int v = 0; v < mapPointsV; v++)
            for (int h = 0; h < mapPointsH; h++)
                map[h, v] = p.mapGains[(v * mapPointsH + h) * mapPlanes];
        Parallel.For(top, bottom, y =>
        {
            if ((y - top) % rowPitch != 0) return;
            double yRel = y / (H - 1.0);
            double yMap = Math.Clamp((yRel - p.mapOriginV) / p.mapSpacingV, 0.0, mapPointsV - 1.0);
            int rowOff = y * W;
            for (int x = left; x < right; x++)
            {
                if ((x - left) % colPitch != 0) continue;
                double xRel = x / (W - 1.0);
                double xMap = Math.Clamp((xRel - p.mapOriginH) / p.mapSpacingH, 0.0, mapPointsH - 1.0);
                double gain = MathHelper.BilinearInterpolation(map, xMap, yMap);
                double v = samples[rowOff + x] * gain;
                samples[rowOff + x] = (ushort)Math.Clamp(Math.Round(v), 0, 65535);
            }
        });
    }

    // Bilinear demosaic on already-linearised CFA samples (i.e. after
    // LineariseCfaInPlace). Reconstructs RGB by:
    //   - keeping the value of the channel actually sampled at (x,y)
    //   - averaging horizontally/vertically for the channel that's sampled
    //     on the same row OR column as (x,y) in the CFA pattern
    //   - averaging diagonally for the channel sampled at neither.
    static void BilinearDemosaicLinearised(ushort[] samples, UInt64[] rgb, int W, int H, uint[] cfa)
    {
        double Sample(int sx, int sy)
        {
            sx = Math.Clamp(sx, 0, W - 1);
            sy = Math.Clamp(sy, 0, H - 1);
            return samples[sy * W + sx];
        }

        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int pos = (y & 1) * 2 + (x & 1);
                int color = (int)cfa[pos];
                double r = 0, g = 0, b = 0;
                double here = Sample(x, y);
                if (color == 0) // R pixel
                {
                    r = here;
                    g = (Sample(x - 1, y) + Sample(x + 1, y) + Sample(x, y - 1) + Sample(x, y + 1)) * 0.25;
                    b = (Sample(x - 1, y - 1) + Sample(x + 1, y - 1) + Sample(x - 1, y + 1) + Sample(x + 1, y + 1)) * 0.25;
                }
                else if (color == 2) // B pixel
                {
                    b = here;
                    g = (Sample(x - 1, y) + Sample(x + 1, y) + Sample(x, y - 1) + Sample(x, y + 1)) * 0.25;
                    r = (Sample(x - 1, y - 1) + Sample(x + 1, y - 1) + Sample(x - 1, y + 1) + Sample(x + 1, y + 1)) * 0.25;
                }
                else // G pixel — figure out whether R is horizontal or vertical
                {
                    g = here;
                    int leftPos = (y & 1) * 2 + ((x - 1) & 1);
                    int leftColor = (int)cfa[leftPos < 0 ? leftPos + 4 : leftPos];
                    if (leftColor == 0)
                    {
                        r = (Sample(x - 1, y) + Sample(x + 1, y)) * 0.5;
                        b = (Sample(x, y - 1) + Sample(x, y + 1)) * 0.5;
                    }
                    else
                    {
                        b = (Sample(x - 1, y) + Sample(x + 1, y)) * 0.5;
                        r = (Sample(x, y - 1) + Sample(x, y + 1)) * 0.5;
                    }
                }
                ushort R = (ushort)Math.Clamp(Math.Round(r), 0, 65535);
                ushort G = (ushort)Math.Clamp(Math.Round(g), 0, 65535);
                ushort B = (ushort)Math.Clamp(Math.Round(b), 0, 65535);
                rgb[y * W + x] = R | ((UInt64)G << 16) | ((UInt64)B << 32) | ((UInt64)65535 << 48);
            }
        });
    }
}
