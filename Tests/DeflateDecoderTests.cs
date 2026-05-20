using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class DeflateDecoderTests
{
    [Fact]
    public void RoundTripsZlibStream()
    {
        var original = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog "
            + "the quick brown fox jumps over the lazy dog");
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(original, 0, original.Length);
            compressed = ms.ToArray();
        }
        // Compression actually shrank the input (sanity check).
        Assert.True(compressed.Length < original.Length);

        var decoded = DeflateDecoder.Decode(compressed);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void OpensDeflateCompressedTiffSample()
    {
        // solid64.tiff is an RGB image stored with Adobe Deflate (compression 8).
        var tiff = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "solid64.tiff"));
        var buffer = DngRawReader.Read(tiff);
        Assert.Equal(640, buffer.Width);
        Assert.Equal(480, buffer.Height);
    }
}
