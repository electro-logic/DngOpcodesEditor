using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class PixelBufferResizeTests
{
    [Fact]
    public void ReturnsCopyWhenAlreadyWithinBudget()
    {
        var buf = new PixelBuffer(new ulong[4 * 4], 4, 4);
        var resized = buf.Resize(100, 100);
        Assert.Equal(4, resized.Width);
        Assert.Equal(4, resized.Height);
        // The returned buffer is a copy, not the same instance.
        Assert.NotSame(buf, resized);
        Assert.NotSame(buf.Pixels, resized.Pixels);
    }

    [Fact]
    public void DownsamplesToFitWidthBudget()
    {
        // 4000x100 -> max 1920 wide -> 1920 x 48.
        var buf = new PixelBuffer(new ulong[4000 * 100], 4000, 100);
        var resized = buf.Resize(1920, 1080);
        Assert.Equal(1920, resized.Width);
        Assert.Equal(48, resized.Height);
    }

    [Fact]
    public void DownsamplesToFitHeightBudget()
    {
        var buf = new PixelBuffer(new ulong[100 * 4000], 100, 4000);
        var resized = buf.Resize(1920, 1080);
        Assert.Equal(27, resized.Width);
        Assert.Equal(1080, resized.Height);
    }

    [Fact]
    public void BoxFilterAveragesPixelValues()
    {
        // 4x4 image: top-left 2x2 = red 30000, top-right 2x2 = green 30000,
        // bottom-left = blue 30000, bottom-right = white 30000.
        var src = new ulong[16];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            ushort r = (ushort)(x < 2 && y < 2 || (x >= 2 && y >= 2) ? 30000 : 0);
            ushort g = (ushort)(x >= 2 && y < 2 || (x >= 2 && y >= 2) ? 30000 : 0);
            ushort b = (ushort)(y >= 2 ? 30000 : 0);
            src[x + y * 4] = r | ((ulong)g << 16) | ((ulong)b << 32) | (65535UL << 48);
        }
        var buf = new PixelBuffer(src, 4, 4);
        var resized = buf.Resize(2, 2);
        Assert.Equal(2, resized.Width);
        Assert.Equal(2, resized.Height);
        // Each output pixel = average of the 2x2 input block above it.
        var tl = resized.GetRgb16Pixel(0, 0);
        Assert.Equal(30000, tl[0]); // pure red
        var tr = resized.GetRgb16Pixel(1, 0);
        Assert.Equal(30000, tr[1]); // pure green
    }
}
