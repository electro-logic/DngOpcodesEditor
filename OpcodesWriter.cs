using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DngOpcodesEditor;

public class OpcodesWriter
{
    MemoryStream _ms = new MemoryStream();
    public byte[] WriteOpcodeList(IList<Opcode> opcodes)
    {
        WriteOpcodeListHeader((UInt32)opcodes.Count);
        foreach (Opcode op in opcodes)
        {
            switch (op.header.id)
            {
                case OpcodeId.WarpRectilinear:
                    WarpRectilinear(op as OpcodeWarpRectilinear);
                    break;
                case OpcodeId.FixVignetteRadial:
                    FixVignetteRadial(op as OpcodeFixVignetteRadial);
                    break;
                case OpcodeId.TrimBounds:
                    TrimBounds(op as OpcodeTrimBounds);
                    break;
                case OpcodeId.GainMap:
                    GainMap(op as OpcodeGainMap);
                    break;
                default:
                    WriteOpcodeHeader(op.header);
                    _ms.Write(new byte[op.header.bytesCount]);
                    Debug.WriteLine($"{op.header.id} write not implemented. Filled with zeros.");
                    break;
            }
        }
        return _ms.ToArray();
    }
    public void WriteOpcodeListHeader(UInt32 opcodesCount) => _ms.WriteUInt32(opcodesCount);
    public void WriteOpcodeHeader(OpcodeHeader header)
    {
        _ms.WriteUInt32((UInt32)header.id);
        _ms.WriteUInt32((UInt32)header.dngVersion);
        _ms.WriteUInt32((UInt32)header.flags);
        _ms.WriteUInt32(header.bytesCount);
    }
    public void WriteOpcodeHeader(OpcodeId opcode, DngVersion dngVersion, OpcodeFlag flags, UInt32 bytesCount)
    {
        _ms.WriteUInt32((UInt32)opcode);
        _ms.WriteUInt32((UInt32)dngVersion);
        _ms.WriteUInt32((UInt32)flags);
        _ms.WriteUInt32(bytesCount);
    }
    public void WarpRectilinear(UInt32 planes, double[] coefficients, double cx, double cy)
    {
        var dataLenght = 20 + (8 * (uint)coefficients.Length);
        WriteOpcodeHeader(OpcodeId.WarpRectilinear, DngVersion.DNG_VERSION_1_3_0_0, OpcodeFlag.OptionalPreview, dataLenght);
        _ms.WriteUInt32(planes);
        foreach (var coefficient in coefficients)
        {
            _ms.WriteDouble(coefficient);
        }
        _ms.WriteDouble(cx);
        _ms.WriteDouble(cy);
    }
    public void WarpRectilinear(OpcodeWarpRectilinear p) => WarpRectilinear(p.planes, p.coefficients, p.cx, p.cy);
    public void FixVignetteRadial(double k0, double k1, double k2, double k3, double k4, double cx, double cy)
    {
        WriteOpcodeHeader(OpcodeId.FixVignetteRadial, DngVersion.DNG_VERSION_1_3_0_0, OpcodeFlag.OptionalPreview, 56);
        _ms.WriteDouble(k0);
        _ms.WriteDouble(k1);
        _ms.WriteDouble(k2);
        _ms.WriteDouble(k3);
        _ms.WriteDouble(k4);
        _ms.WriteDouble(cx);
        _ms.WriteDouble(cy);
    }
    public void FixVignetteRadial(OpcodeFixVignetteRadial p) => FixVignetteRadial(p.k0, p.k1, p.k2, p.k3, p.k4, p.cx, p.cy);
    public void TrimBounds(UInt32 top, UInt32 left, UInt32 bottom, UInt32 right)
    {
        WriteOpcodeHeader(OpcodeId.TrimBounds, DngVersion.DNG_VERSION_1_3_0_0, OpcodeFlag.OptionalPreview, 16);
        _ms.WriteUInt32(top);
        _ms.WriteUInt32(left);
        _ms.WriteUInt32(bottom);
        _ms.WriteUInt32(right);
    }
    public void TrimBounds(OpcodeTrimBounds p) => TrimBounds(p.top, p.left, p.bottom, p.right);
    public void GainMap(UInt32 top, UInt32 left, UInt32 bottom, UInt32 right,
        UInt32 plane, UInt32 planes, UInt32 rowPitch, UInt32 colPitch,
        UInt32 mapPointsV, UInt32 mapPointsH, double mapSpacingV, double mapSpacingH,
        double mapOriginV, double mapOriginH, UInt32 mapPlanes, float[] mapGains)
    {
        uint dataLenght = (uint)(76 + 4 * mapGains.Length);
        WriteOpcodeHeader(OpcodeId.GainMap, DngVersion.DNG_VERSION_1_3_0_0, OpcodeFlag.OptionalPreview, dataLenght);
        _ms.WriteUInt32(top);
        _ms.WriteUInt32(left);
        _ms.WriteUInt32(bottom);
        _ms.WriteUInt32(right);
        _ms.WriteUInt32(plane);
        _ms.WriteUInt32(planes);
        _ms.WriteUInt32(rowPitch);
        _ms.WriteUInt32(colPitch);
        _ms.WriteUInt32(mapPointsV);
        _ms.WriteUInt32(mapPointsH);
        _ms.WriteDouble(mapSpacingV);
        _ms.WriteDouble(mapSpacingH);
        _ms.WriteDouble(mapOriginV);
        _ms.WriteDouble(mapOriginH);
        _ms.WriteUInt32(mapPlanes);
        foreach (var mapGain in mapGains)
        {
            _ms.WriteFloat(mapGain);
        }
    }
    public void GainMap(OpcodeGainMap p) => GainMap(p.top, p.left, p.bottom, p.right, 
        p.plane, p.planes, p.rowPitch, p.colPitch, 
        p.mapPointsV, p.mapPointsH, p.mapSpacingV, p.mapSpacingH, 
        p.mapOriginV, p.mapOriginH, p.mapPlanes, p.mapGains);
}