using System.IO;
using System.IO.Compression;

namespace DngOpcodesEditor;

// Inflates a zlib-wrapped deflate stream. TIFF uses this for both
// Compression = 8 (Adobe Deflate) and the older Compression = 32946.
// .NET's ZLibStream understands the zlib header (vs DeflateStream which
// expects raw deflate without the header).
public static class DeflateDecoder
{
    public static byte[] Decode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(data.Length * 2);
        inflater.CopyTo(output);
        return output.ToArray();
    }
}
