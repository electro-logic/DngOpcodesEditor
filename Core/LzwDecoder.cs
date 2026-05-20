using System;
using System.IO;

namespace DngOpcodesEditor;

// TIFF-style LZW decoder (TIFF spec section 13, compression = 5).
//
// The dictionary is preloaded with codes 0..255 (single bytes), code 256 is
// the ClearCode (reset dictionary, reset width to 9 bits) and code 257 is
// EndOfInformation. New entries are added as `<previous-code-bytes>` followed
// by the first byte of the just-decoded code. Code width grows from 9 to 12.
//
// TIFF's only quirk vs. textbook LZW is that the code width is bumped one
// code earlier than the classic algorithm: when the next slot to write is
// 2^width - 1 the width is incremented. The decoder must do the same so the
// bit stream stays aligned with the encoder.
public static class LzwDecoder
{
    const int CLEAR_CODE = 256;
    const int EOI_CODE = 257;
    const int FIRST_DYNAMIC_CODE = 258;
    const int MAX_CODE = 4096;

    public static byte[] Decode(byte[] data)
    {
        var prefix = new short[MAX_CODE];
        var suffix = new byte[MAX_CODE];
        for (int i = 0; i < 256; i++) { prefix[i] = -1; suffix[i] = (byte)i; }
        prefix[CLEAR_CODE] = -1;
        prefix[EOI_CODE] = -1;

        // Output bytes that we collect by walking each entry's prefix chain
        // in reverse and then re-reversing into the output stream.
        var stack = new byte[MAX_CODE];
        var output = new MemoryStream(data.Length * 2);

        var reader = new BitReader(data);
        int width = 9;
        int nextCode = FIRST_DYNAMIC_CODE;
        int prevCode = -1;

        while (true)
        {
            int code = reader.ReadBits(width);
            if (code < 0 || code == EOI_CODE)
                break;
            if (code == CLEAR_CODE)
            {
                width = 9;
                nextCode = FIRST_DYNAMIC_CODE;
                prevCode = -1;
                continue;
            }

            int stackTop = 0;
            int chainStart;
            if (code < nextCode)
            {
                // Normal case: the code is already in the dictionary.
                chainStart = code;
            }
            else if (code == nextCode && prevCode >= 0)
            {
                // Special LZW case: the encoder used a code that the decoder
                // has not added yet. By LZW invariant the new entry is
                // <prev-bytes><first-byte-of-prev>, so we push the first
                // byte before walking the prev chain.
                int first = prevCode;
                while (prefix[first] >= 0) first = prefix[first];
                stack[stackTop++] = suffix[first];
                chainStart = prevCode;
            }
            else
            {
                throw new InvalidDataException($"Invalid LZW code {code} (nextCode = {nextCode}).");
            }

            int c = chainStart;
            while (c >= 0)
            {
                stack[stackTop++] = suffix[c];
                c = prefix[c];
            }
            while (stackTop > 0)
                output.WriteByte(stack[--stackTop]);

            if (prevCode >= 0 && nextCode < MAX_CODE)
            {
                // First byte of the just-decoded entry, which becomes the
                // suffix of the new dictionary entry.
                int first = (code == nextCode) ? prevCode : code;
                while (prefix[first] >= 0) first = prefix[first];
                prefix[nextCode] = (short)prevCode;
                suffix[nextCode] = suffix[first];
                nextCode++;
                // TIFF "early bump": increase the code width one slot before
                // the table is actually full at the current width.
                if (nextCode >= (1 << width) - 1 && width < 12)
                    width++;
            }
            prevCode = code;
        }

        return output.ToArray();
    }

    // MSB-first bit reader with no byte-stuffing — straight from the byte stream.
    sealed class BitReader
    {
        readonly byte[] _data;
        int _pos;
        int _buffer;
        int _bitsAvailable;
        public BitReader(byte[] data) { _data = data; }
        public int ReadBits(int n)
        {
            while (_bitsAvailable < n)
            {
                if (_pos >= _data.Length)
                    return -1;
                _buffer = (_buffer << 8) | _data[_pos++];
                _bitsAvailable += 8;
            }
            _bitsAvailable -= n;
            return (_buffer >> _bitsAvailable) & ((1 << n) - 1);
        }
    }
}
