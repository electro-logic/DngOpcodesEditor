using System;
using System.Collections.Generic;
using System.IO;

namespace DngOpcodesEditor;

// Lossless JPEG (ITU-T T.81 Annex H, SOF3) decoder.
//
// DNG files with Compression = 7 wrap each strip or tile in its own Lossless
// JPEG bitstream. The decoder returns the raw samples as a flat ushort[] in
// row-major, component-interleaved order:
//     samples[(y * width + x) * components + c]
// which is what DngRawReader's strip/tile loader expects.
//
// Scope:
//   - All seven predictors (Ss 1..7).
//   - Up to 4 components per scan (DNG never needs more than 3).
//   - Precision 8..16 bits per sample.
//   - Standard Huffman tables defined in the stream.
//   - Byte-stuffing (0xFF 0x00).
//   - Restart intervals are NOT supported (DNG rarely uses them).
public static class LosslessJpegDecoder
{
    const int MARKER_SOI = 0xD8;
    const int MARKER_SOF3 = 0xC3;
    const int MARKER_DHT = 0xC4;
    const int MARKER_SOS = 0xDA;
    const int MARKER_EOI = 0xD9;
    const int MARKER_DRI = 0xDD;

    public static ushort[] Decode(byte[] data, out int width, out int height, out int precision, out int components)
    {
        var reader = new JpegReader(data);
        if (reader.ReadMarker() != MARKER_SOI)
            throw new InvalidDataException("Not a JPEG stream (missing SOI marker).");

        int P = 0, Y = 0, X = 0;
        var huffmanTables = new HuffmanTable[16];
        int[] frameComponentIds = Array.Empty<int>();
        int[] scanComponentTd = Array.Empty<int>();
        int Ss = 1, Pt = 0;
        int restartInterval = 0;

        while (true)
        {
            int marker = reader.ReadMarker();
            if (marker == MARKER_EOI)
                throw new InvalidDataException("EOI before SOS — no image data.");

            switch (marker)
            {
                case MARKER_SOF3:
                {
                    int segLen = reader.ReadUInt16BE();
                    P = reader.ReadByte();
                    Y = reader.ReadUInt16BE();
                    X = reader.ReadUInt16BE();
                    int Nf = reader.ReadByte();
                    frameComponentIds = new int[Nf];
                    for (int i = 0; i < Nf; i++)
                    {
                        frameComponentIds[i] = reader.ReadByte();
                        reader.ReadByte(); // Hi/Vi (unused, assumed 1/1)
                        reader.ReadByte(); // Tqi (always 0 for lossless)
                    }
                    if (segLen != 8 + 3 * Nf)
                        throw new InvalidDataException("SOF3 segment length mismatch.");
                    break;
                }
                case MARKER_DHT:
                {
                    int segLen = reader.ReadUInt16BE();
                    int consumed = 2;
                    while (consumed < segLen)
                    {
                        int tcTh = reader.ReadByte();
                        int th = tcTh & 0xF;
                        var bitCounts = new int[17];
                        for (int i = 1; i <= 16; i++)
                            bitCounts[i] = reader.ReadByte();
                        int total = 0;
                        for (int i = 1; i <= 16; i++) total += bitCounts[i];
                        var values = new byte[total];
                        for (int i = 0; i < total; i++) values[i] = (byte)reader.ReadByte();
                        huffmanTables[th] = HuffmanTable.Build(bitCounts, values);
                        consumed += 1 + 16 + total;
                    }
                    break;
                }
                case MARKER_DRI:
                {
                    reader.ReadUInt16BE(); // segment length (always 4)
                    restartInterval = reader.ReadUInt16BE();
                    if (restartInterval != 0)
                        throw new NotSupportedException("Restart intervals are not supported.");
                    break;
                }
                case MARKER_SOS:
                {
                    int segLen = reader.ReadUInt16BE();
                    int Ns = reader.ReadByte();
                    scanComponentTd = new int[Ns];
                    for (int i = 0; i < Ns; i++)
                    {
                        reader.ReadByte(); // Csj (component selector — assumed in same order as frame)
                        int tdTa = reader.ReadByte();
                        scanComponentTd[i] = tdTa >> 4;
                    }
                    Ss = reader.ReadByte();
                    reader.ReadByte(); // Se (unused for lossless)
                    int ahAl = reader.ReadByte();
                    Pt = ahAl & 0xF;
                    if (segLen != 6 + 2 * Ns)
                        throw new InvalidDataException("SOS segment length mismatch.");
                    goto endOfHeader;
                }
                default:
                    // Skip unknown marker segments (length-prefixed). Standalone
                    // markers (TEM, RSTn) don't carry a length.
                    if (marker >= 0xC0 && marker <= 0xFE && marker != 0xD8 && marker != 0xD9
                        && !(marker >= 0xD0 && marker <= 0xD7))
                    {
                        int segLen = reader.ReadUInt16BE();
                        reader.Skip(segLen - 2);
                    }
                    break;
            }
        }
        endOfHeader:

        if (P == 0 || X == 0 || Y == 0 || scanComponentTd.Length == 0)
            throw new InvalidDataException("Lossless JPEG header is incomplete.");

        width = X;
        height = Y;
        precision = P;
        components = scanComponentTd.Length;

        var samples = new ushort[Y * X * components];
        var bits = reader.IntoBitReader();
        int initialPredictor = 1 << (P - Pt - 1);
        int sampleMask = (1 << P) - 1;

        // Per-component rolling row buffers used by the predictors. Only the
        // previous row and the in-progress row are needed.
        var prevRow = new int[components][];
        var currRow = new int[components][];
        for (int c = 0; c < components; c++)
        {
            prevRow[c] = new int[X];
            currRow[c] = new int[X];
        }

        for (int y = 0; y < Y; y++)
        {
            for (int x = 0; x < X; x++)
            {
                for (int c = 0; c < components; c++)
                {
                    var huff = huffmanTables[scanComponentTd[c]];
                    if (huff == null)
                        throw new InvalidDataException($"Huffman table {scanComponentTd[c]} not defined.");
                    int diff = DecodeDifference(bits, huff);
                    int predictor;
                    if (y == 0 && x == 0)
                        predictor = initialPredictor;
                    else if (y == 0)
                        predictor = currRow[c][x - 1];                              // A
                    else if (x == 0)
                        predictor = prevRow[c][0];                                  // B
                    else
                        predictor = ApplyPredictor(Ss,
                            currRow[c][x - 1],                                       // A
                            prevRow[c][x],                                           // B
                            prevRow[c][x - 1]);                                      // C
                    int value = (predictor + diff) & sampleMask;
                    currRow[c][x] = value;
                    samples[(y * X + x) * components + c] = (ushort)value;
                }
            }
            // Rotate buffers: this row becomes "previous" for the next y.
            (prevRow, currRow) = (currRow, prevRow);
        }

        return samples;
    }

    static int ApplyPredictor(int Ss, int A, int B, int C) => Ss switch
    {
        1 => A,
        2 => B,
        3 => C,
        4 => A + B - C,
        5 => A + ((B - C) >> 1),
        6 => B + ((A - C) >> 1),
        7 => (A + B) >> 1,
        _ => throw new InvalidDataException($"Invalid lossless JPEG predictor {Ss}")
    };

    static int DecodeDifference(BitReader r, HuffmanTable h)
    {
        int category = h.Decode(r);
        if (category == 0)
            return 0;
        if (category == 16)
            // Special-case: a magnitude of 32768 with no extra bits.
            return 32768;
        int magnitude = r.ReadBits(category);
        // If the top bit is set the value is positive; otherwise extend the
        // sign by adding (1 - 2^category).
        int signBit = magnitude >> (category - 1);
        if (signBit == 1)
            return magnitude;
        return magnitude + 1 - (1 << category);
    }

    // --- JPEG marker / byte reader ------------------------------------------

    sealed class JpegReader
    {
        readonly byte[] _data;
        int _pos;
        public JpegReader(byte[] data) { _data = data; }
        public int Position => _pos;
        public int ReadByte()
        {
            if (_pos >= _data.Length) throw new EndOfStreamException();
            return _data[_pos++];
        }
        public int ReadUInt16BE()
        {
            int hi = ReadByte();
            int lo = ReadByte();
            return (hi << 8) | lo;
        }
        public void Skip(int n) { _pos += n; }
        public int ReadMarker()
        {
            while (_pos < _data.Length && _data[_pos] == 0xFF
                   && _pos + 1 < _data.Length && _data[_pos + 1] == 0xFF)
                _pos++; // fill padding
            if (_pos >= _data.Length || _data[_pos] != 0xFF)
                throw new InvalidDataException($"Expected JPEG marker at offset {_pos}.");
            _pos++;
            return ReadByte();
        }
        public BitReader IntoBitReader() => new BitReader(_data, _pos);
    }

    // --- Bit-level reader with 0xFF byte-stuffing ---------------------------

    sealed class BitReader
    {
        readonly byte[] _data;
        int _pos;
        int _bitBuffer;
        int _bitsInBuffer;
        public BitReader(byte[] data, int pos) { _data = data; _pos = pos; }
        public int ReadBit()
        {
            if (_bitsInBuffer == 0) FillByte();
            int bit = (_bitBuffer >> 7) & 1;
            _bitBuffer = (_bitBuffer << 1) & 0xFF;
            _bitsInBuffer--;
            return bit;
        }
        public int ReadBits(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++)
                v = (v << 1) | ReadBit();
            return v;
        }
        void FillByte()
        {
            if (_pos >= _data.Length)
                throw new EndOfStreamException("Unexpected end of lossless JPEG entropy data.");
            int b = _data[_pos++];
            if (b == 0xFF)
            {
                if (_pos >= _data.Length)
                    throw new EndOfStreamException("Unexpected end after 0xFF in entropy data.");
                int next = _data[_pos];
                if (next == 0x00)
                    _pos++; // stuffed: consume the padding 0x00, real data byte is 0xFF
                else
                    throw new EndOfStreamException($"Unexpected marker 0xFF{next:X2} inside entropy data.");
            }
            _bitBuffer = b;
            _bitsInBuffer = 8;
        }
    }

    // --- Huffman table (T.81 Annex C, decoding per F.16) --------------------

    sealed class HuffmanTable
    {
        readonly int[] _minCode = new int[17];
        readonly int[] _maxCode = new int[17];
        readonly int[] _valPtr = new int[17];
        readonly byte[] _huffVal;
        HuffmanTable(byte[] huffVal) { _huffVal = huffVal; }

        public static HuffmanTable Build(int[] bits, byte[] values)
        {
            var table = new HuffmanTable(values);
            // Generate per-code size list HUFFSIZE
            var sizes = new List<int>();
            for (int L = 1; L <= 16; L++)
                for (int i = 0; i < bits[L]; i++)
                    sizes.Add(L);
            // Generate canonical Huffman codes HUFFCODE
            var codes = new int[sizes.Count];
            if (sizes.Count > 0)
            {
                int code = 0;
                int si = sizes[0];
                for (int k = 0; k < sizes.Count; k++)
                {
                    while (sizes[k] != si)
                    {
                        code <<= 1;
                        si++;
                    }
                    codes[k] = code;
                    code++;
                }
            }
            // Build MINCODE / MAXCODE / VALPTR per length.
            int j = 0;
            for (int L = 1; L <= 16; L++)
            {
                if (bits[L] == 0)
                {
                    table._maxCode[L] = -1;
                    continue;
                }
                table._valPtr[L] = j;
                table._minCode[L] = codes[j];
                j += bits[L];
                table._maxCode[L] = codes[j - 1];
            }
            return table;
        }

        public int Decode(BitReader r)
        {
            int code = r.ReadBit();
            int len = 1;
            while (code > _maxCode[len])
            {
                len++;
                if (len > 16)
                    throw new InvalidDataException("Bad Huffman code in lossless JPEG stream.");
                code = (code << 1) | r.ReadBit();
            }
            int idx = _valPtr[len] + (code - _minCode[len]);
            return _huffVal[idx];
        }
    }
}
