using System;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class PixelBufferOrientationTests
{
    // Tiny 2x3 test image laid out as:
    //   A B
    //   C D
    //   E F
    // Each letter is a unique pixel value so we can spot how rotations rearrange them.
    static PixelBuffer Build2x3()
    {
        var p = new ulong[] {
            Pack(1), Pack(2),
            Pack(3), Pack(4),
            Pack(5), Pack(6),
        };
        return new PixelBuffer(p, 2, 3);
    }
    static ulong Pack(int n) => (ulong)n;

    [Fact]
    public void Orientation1IsIdentity()
    {
        var src = Build2x3();
        var dst = src.ApplyOrientation(1);
        Assert.Equal(2, dst.Width);
        Assert.Equal(3, dst.Height);
        for (int i = 0; i < 6; i++) Assert.Equal((ulong)(i + 1), dst.Pixels[i]);
    }

    [Fact]
    public void Orientation3Rotates180()
    {
        var dst = Build2x3().ApplyOrientation(3);
        // F E
        // D C
        // B A
        Assert.Equal(2, dst.Width);
        Assert.Equal(3, dst.Height);
        Assert.Equal((ulong)6, dst.Pixels[0]);
        Assert.Equal((ulong)5, dst.Pixels[1]);
        Assert.Equal((ulong)4, dst.Pixels[2]);
        Assert.Equal((ulong)3, dst.Pixels[3]);
        Assert.Equal((ulong)2, dst.Pixels[4]);
        Assert.Equal((ulong)1, dst.Pixels[5]);
    }

    [Fact]
    public void Orientation6Rotates90Cw()
    {
        var dst = Build2x3().ApplyOrientation(6);
        // Source:    Target (3 wide, 2 tall):
        //   A B        E C A
        //   C D        F D B
        //   E F
        Assert.Equal(3, dst.Width);
        Assert.Equal(2, dst.Height);
        Assert.Equal((ulong)5, dst.Pixels[0]);
        Assert.Equal((ulong)3, dst.Pixels[1]);
        Assert.Equal((ulong)1, dst.Pixels[2]);
        Assert.Equal((ulong)6, dst.Pixels[3]);
        Assert.Equal((ulong)4, dst.Pixels[4]);
        Assert.Equal((ulong)2, dst.Pixels[5]);
    }

    [Fact]
    public void Orientation8Rotates90Ccw()
    {
        var dst = Build2x3().ApplyOrientation(8);
        // Source:    Target (3 wide, 2 tall):
        //   A B        B D F
        //   C D        A C E
        //   E F
        Assert.Equal(3, dst.Width);
        Assert.Equal(2, dst.Height);
        Assert.Equal((ulong)2, dst.Pixels[0]);
        Assert.Equal((ulong)4, dst.Pixels[1]);
        Assert.Equal((ulong)6, dst.Pixels[2]);
        Assert.Equal((ulong)1, dst.Pixels[3]);
        Assert.Equal((ulong)3, dst.Pixels[4]);
        Assert.Equal((ulong)5, dst.Pixels[5]);
    }

    [Fact]
    public void OrientationOutOfRangeIsIdentity()
    {
        var dst = Build2x3().ApplyOrientation(99);
        Assert.Equal(2, dst.Width);
        Assert.Equal(3, dst.Height);
        for (int i = 0; i < 6; i++) Assert.Equal((ulong)(i + 1), dst.Pixels[i]);
    }
}
