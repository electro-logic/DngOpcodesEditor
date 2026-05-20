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
        _ms.WriteUInt32((UInt32)opcodes.Count);
        foreach (Opcode op in opcodes)
        {
            switch (op.header.id)
            {
                case OpcodeId.WarpRectilinear:
                    WarpRectilinear(op as OpcodeWarpRectilinear);
                    break;
                case OpcodeId.WarpFisheye:
                    WarpFisheye(op as OpcodeWarpFisheye);
                    break;
                case OpcodeId.FixVignetteRadial:
                    FixVignetteRadial(op as OpcodeFixVignetteRadial);
                    break;
                case OpcodeId.FixBadPixelsConstant:
                    FixBadPixelsConstant(op as OpcodeFixBadPixelsConstant);
                    break;
                case OpcodeId.FixBadPixelsList:
                    FixBadPixelsList(op as OpcodeFixBadPixelsList);
                    break;
                case OpcodeId.TrimBounds:
                    TrimBounds(op as OpcodeTrimBounds);
                    break;
                case OpcodeId.MapTable:
                    MapTable(op as OpcodeMapTable);
                    break;
                case OpcodeId.MapPolynomial:
                    MapPolynomial(op as OpcodeMapPolynomial);
                    break;
                case OpcodeId.GainMap:
                    GainMap(op as OpcodeGainMap);
                    break;
                case OpcodeId.DeltaPerRow:
                    AreaFloats(op, ((OpcodeDeltaPerRow)op).deltas);
                    break;
                case OpcodeId.DeltaPerColumn:
                    AreaFloats(op, ((OpcodeDeltaPerColumn)op).deltas);
                    break;
                case OpcodeId.ScalePerRow:
                    AreaFloats(op, ((OpcodeScalePerRow)op).scales);
                    break;
                case OpcodeId.ScalePerColumn:
                    AreaFloats(op, ((OpcodeScalePerColumn)op).scales);
                    break;
                default:
                    WriteOpcodeHeader(op.header, op.header.bytesCount);
                    _ms.Write(new byte[op.header.bytesCount]);
                    Debug.WriteLine($"{op.header.id} write not implemented. Filled with zeros.");
                    break;
            }
        }
        return _ms.ToArray();
    }
    // Writes the header, preserving the opcode's DNG version and flags while
    // using the freshly computed body size.
    void WriteOpcodeHeader(OpcodeHeader header, UInt32 bytesCount)
    {
        _ms.WriteUInt32((UInt32)header.id);
        _ms.WriteUInt32((UInt32)header.dngVersion);
        _ms.WriteUInt32((UInt32)header.flags);
        _ms.WriteUInt32(bytesCount);
    }
    void WriteArea(OpcodeArea p)
    {
        _ms.WriteUInt32(p.top);
        _ms.WriteUInt32(p.left);
        _ms.WriteUInt32(p.bottom);
        _ms.WriteUInt32(p.right);
        _ms.WriteUInt32(p.plane);
        _ms.WriteUInt32(p.planes);
        _ms.WriteUInt32(p.rowPitch);
        _ms.WriteUInt32(p.colPitch);
    }
    public void WarpRectilinear(OpcodeWarpRectilinear p)
    {
        WriteOpcodeHeader(p.header, (UInt32)(20 + 8 * p.coefficients.Length));
        _ms.WriteUInt32(p.planes);
        foreach (var coefficient in p.coefficients)
            _ms.WriteDouble(coefficient);
        _ms.WriteDouble(p.cx);
        _ms.WriteDouble(p.cy);
    }
    public void WarpFisheye(OpcodeWarpFisheye p)
    {
        WriteOpcodeHeader(p.header, (UInt32)(20 + 8 * p.coefficients.Length));
        _ms.WriteUInt32(p.planes);
        foreach (var coefficient in p.coefficients)
            _ms.WriteDouble(coefficient);
        _ms.WriteDouble(p.cx);
        _ms.WriteDouble(p.cy);
    }
    public void FixVignetteRadial(OpcodeFixVignetteRadial p)
    {
        WriteOpcodeHeader(p.header, 56);
        _ms.WriteDouble(p.k0);
        _ms.WriteDouble(p.k1);
        _ms.WriteDouble(p.k2);
        _ms.WriteDouble(p.k3);
        _ms.WriteDouble(p.k4);
        _ms.WriteDouble(p.cx);
        _ms.WriteDouble(p.cy);
    }
    public void TrimBounds(OpcodeTrimBounds p)
    {
        WriteOpcodeHeader(p.header, 16);
        _ms.WriteUInt32(p.top);
        _ms.WriteUInt32(p.left);
        _ms.WriteUInt32(p.bottom);
        _ms.WriteUInt32(p.right);
    }
    public void GainMap(OpcodeGainMap p)
    {
        WriteOpcodeHeader(p.header, (UInt32)(76 + 4 * p.mapGains.Length));
        WriteArea(p);
        _ms.WriteUInt32(p.mapPointsV);
        _ms.WriteUInt32(p.mapPointsH);
        _ms.WriteDouble(p.mapSpacingV);
        _ms.WriteDouble(p.mapSpacingH);
        _ms.WriteDouble(p.mapOriginV);
        _ms.WriteDouble(p.mapOriginH);
        _ms.WriteUInt32(p.mapPlanes);
        foreach (var mapGain in p.mapGains)
            _ms.WriteFloat(mapGain);
    }
    public void MapTable(OpcodeMapTable p)
    {
        WriteOpcodeHeader(p.header, (UInt32)(36 + 2 * p.table.Length));
        WriteArea(p);
        _ms.WriteUInt32((UInt32)p.table.Length);
        foreach (var entry in p.table)
            _ms.WriteUInt16(entry);
    }
    public void MapPolynomial(OpcodeMapPolynomial p)
    {
        WriteOpcodeHeader(p.header, (UInt32)(36 + 8 * p.coefficients.Length));
        WriteArea(p);
        _ms.WriteUInt32((UInt32)(p.coefficients.Length - 1));
        foreach (var coefficient in p.coefficients)
            _ms.WriteDouble(coefficient);
    }
    void AreaFloats(Opcode op, float[] values)
    {
        var p = (OpcodeArea)op;
        WriteOpcodeHeader(p.header, (UInt32)(36 + 4 * values.Length));
        WriteArea(p);
        _ms.WriteUInt32((UInt32)values.Length);
        foreach (var value in values)
            _ms.WriteFloat(value);
    }
    public void FixBadPixelsConstant(OpcodeFixBadPixelsConstant p)
    {
        WriteOpcodeHeader(p.header, 8);
        _ms.WriteUInt32(p.constant);
        _ms.WriteUInt32(p.bayerPhase);
    }
    public void FixBadPixelsList(OpcodeFixBadPixelsList p)
    {
        WriteOpcodeHeader(p.header, (UInt32)(12 + 4 * p.badPoints.Length + 4 * p.badRects.Length));
        _ms.WriteUInt32(p.bayerPhase);
        _ms.WriteUInt32((UInt32)(p.badPoints.Length / 2));
        _ms.WriteUInt32((UInt32)(p.badRects.Length / 4));
        foreach (var value in p.badPoints)
            _ms.WriteUInt32(value);
        foreach (var value in p.badRects)
            _ms.WriteUInt32(value);
    }
}
