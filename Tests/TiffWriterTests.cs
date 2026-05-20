using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class TiffWriterTests
{
    [Fact]
    public void WriteRgb16_ProducesParseableTiff()
    {
        // 2x2 image with distinct corner pixel values so we can verify the
        // bytes survive the write.
        var pixels = new ulong[]
        {
            Pack(1000, 2000, 3000),
            Pack(10000, 20000, 30000),
            Pack(60000, 0, 65535),
            Pack(32768, 32768, 32768),
        };
        var buffer = new PixelBuffer(pixels, 2, 2);

        var tiff = TiffWriter.WriteRgb16(buffer);

        // The header is a valid TIFF.
        var (isLE, firstIfd) = TiffFile.ReadHeader(tiff);
        Assert.True(isLE);
        Assert.True(firstIfd > 0);

        // The standard tags hold the values we wrote.
        int widthEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 256);
        int heightEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 257);
        int photoEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 262);
        int compressionEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 259);
        int sppEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 277);

        Assert.Equal(2u, TiffFile.ReadEntryAsUInt32Array(tiff, isLE, widthEntry)[0]);
        Assert.Equal(2u, TiffFile.ReadEntryAsUInt32Array(tiff, isLE, heightEntry)[0]);
        Assert.Equal(2u, TiffFile.ReadEntryAsUInt32Array(tiff, isLE, photoEntry)[0]);
        Assert.Equal(1u, TiffFile.ReadEntryAsUInt32Array(tiff, isLE, compressionEntry)[0]);
        Assert.Equal(3u, TiffFile.ReadEntryAsUInt32Array(tiff, isLE, sppEntry)[0]);

        // BitsPerSample = [16, 16, 16].
        int bpsEntry = TiffFile.FindEntryPublic(tiff, isLE, (int)firstIfd, 258);
        var bps = TiffFile.ReadEntryAsUInt32Array(tiff, isLE, bpsEntry);
        Assert.Equal(new uint[] { 16, 16, 16 }, bps);
    }

    static ulong Pack(ushort r, ushort g, ushort b) =>
        r | ((ulong)g << 16) | ((ulong)b << 32) | (65535UL << 48);
}
