using System;
using System.IO;
using DngOpcodesEditor;
using Xunit;

namespace DngOpcodesEditor.Tests;

public class OpcodesRoundTripTests
{
    static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    [Theory]
    [InlineData("FixVignetteRadial.bin")]
    [InlineData("WarpRectilinear.bin")]
    [InlineData("GainMap.bin")]
    [InlineData("TrimsBound.bin")]
    public void SampleBin_ReadWrite_IsRoundTripStable(string name)
    {
        var original = File.ReadAllBytes(SamplePath(name));

        var opcodes1 = new OpcodesReader().ReadOpcodeList(original);
        var bytes2 = new OpcodesWriter().WriteOpcodeList(opcodes1);
        var opcodes2 = new OpcodesReader().ReadOpcodeList(bytes2);
        var bytes3 = new OpcodesWriter().WriteOpcodeList(opcodes2);

        // The opcode list survives a read/write cycle unchanged.
        Assert.Equal(opcodes1.Length, opcodes2.Length);
        for (int i = 0; i < opcodes1.Length; i++)
        {
            Assert.Equal(opcodes1[i].header.id, opcodes2[i].header.id);
            Assert.Equal(opcodes1[i].header.dngVersion, opcodes2[i].header.dngVersion);
            Assert.Equal(opcodes1[i].header.flags, opcodes2[i].header.flags);
        }
        // Writing is idempotent: a second pass produces identical bytes.
        Assert.Equal(bytes2, bytes3);
    }

    [Fact]
    public void FixVignetteRadial_RoundTrips()
    {
        var op = new OpcodeFixVignetteRadial { k0 = -0.3, k1 = 0.1, k2 = -0.05, k3 = 0.02, k4 = -0.01, cx = 0.51, cy = 0.49 };
        op.header.id = OpcodeId.FixVignetteRadial;

        var result = (OpcodeFixVignetteRadial)RoundTrip(op);

        Assert.Equal(op.k0, result.k0);
        Assert.Equal(op.k4, result.k4);
        Assert.Equal(op.cx, result.cx);
        Assert.Equal(op.cy, result.cy);
    }

    [Fact]
    public void MapPolynomial_RoundTrips()
    {
        var op = new OpcodeMapPolynomial
        {
            top = 0, left = 0, bottom = 100, right = 200,
            plane = 0, planes = 3, rowPitch = 1, colPitch = 1,
            coefficients = new double[] { 0.01, 0.98, 0.05 }
        };
        op.header.id = OpcodeId.MapPolynomial;

        var result = (OpcodeMapPolynomial)RoundTrip(op);

        Assert.Equal(op.right, result.right);
        Assert.Equal(op.planes, result.planes);
        Assert.Equal(op.coefficients, result.coefficients);
    }

    [Fact]
    public void MapTable_RoundTrips()
    {
        var op = new OpcodeMapTable
        {
            top = 1, left = 2, bottom = 3, right = 4,
            table = new ushort[] { 0, 100, 40000, 65535 }
        };
        op.header.id = OpcodeId.MapTable;

        var result = (OpcodeMapTable)RoundTrip(op);

        Assert.Equal(op.table, result.table);
        Assert.Equal(op.left, result.left);
    }

    [Fact]
    public void GainMap_RoundTrips()
    {
        var op = new OpcodeGainMap
        {
            top = 0, left = 0, bottom = 64, right = 64,
            plane = 0, planes = 1, rowPitch = 1, colPitch = 1,
            mapPointsV = 2, mapPointsH = 2,
            mapSpacingV = 0.5, mapSpacingH = 0.5,
            mapOriginV = 0.0, mapOriginH = 0.0,
            mapPlanes = 1,
            mapGains = new float[] { 1.1f, 1.2f, 1.3f, 1.4f }
        };
        op.header.id = OpcodeId.GainMap;

        var result = (OpcodeGainMap)RoundTrip(op);

        Assert.Equal(op.mapGains, result.mapGains);
        Assert.Equal(op.mapPointsH, result.mapPointsH);
    }

    [Fact]
    public void DeltaPerRow_RoundTrips()
    {
        var op = new OpcodeDeltaPerRow
        {
            top = 0, left = 0, bottom = 4, right = 8,
            plane = 0, planes = 3, rowPitch = 1, colPitch = 1,
            deltas = new float[] { 0.01f, -0.02f, 0.03f, -0.04f }
        };
        op.header.id = OpcodeId.DeltaPerRow;

        var result = (OpcodeDeltaPerRow)RoundTrip(op);

        Assert.Equal(op.deltas, result.deltas);
    }

    [Fact]
    public void UnknownOpcode_IsSkippedWithoutDesync()
    {
        // Build a list with an unknown opcode (id 999) followed by a known one.
        var writer = new MemoryStream();
        writer.WriteUInt32(2);
        // Unknown opcode with a 12-byte body.
        writer.WriteUInt32(999);
        writer.WriteUInt32((uint)DngVersion.DNG_VERSION_1_3_0_0);
        writer.WriteUInt32((uint)OpcodeFlag.Optional);
        writer.WriteUInt32(12);
        writer.Write(new byte[12]);
        // Known TrimBounds opcode.
        writer.WriteUInt32((uint)OpcodeId.TrimBounds);
        writer.WriteUInt32((uint)DngVersion.DNG_VERSION_1_3_0_0);
        writer.WriteUInt32((uint)OpcodeFlag.OptionalPreview);
        writer.WriteUInt32(16);
        writer.WriteUInt32(10);
        writer.WriteUInt32(20);
        writer.WriteUInt32(30);
        writer.WriteUInt32(40);

        var opcodes = new OpcodesReader().ReadOpcodeList(writer.ToArray());

        Assert.Equal(2, opcodes.Length);
        // The stream stayed aligned: the TrimBounds opcode parsed correctly.
        var trim = Assert.IsType<OpcodeTrimBounds>(opcodes[1]);
        Assert.Equal(10u, trim.top);
        Assert.Equal(40u, trim.right);
    }

    static Opcode RoundTrip(Opcode op)
    {
        var bytes = new OpcodesWriter().WriteOpcodeList(new[] { op });
        return new OpcodesReader().ReadOpcodeList(bytes)[0];
    }
}
