using System;
using System.Collections.Generic;
using System.IO;

namespace DngOpcodesEditor;

public class OpcodesReader
{
    MemoryStream _ms;
    public Opcode[] ReadOpcodeList(byte[] bytes)
    {
        using (_ms = new MemoryStream(bytes, writable: false))
        {
            var opcodesCount = _ms.ReadUInt32();
            var opcodes = new List<Opcode>((int)opcodesCount);
            for (int opcodeIndex = 0; opcodeIndex < opcodesCount; opcodeIndex++)
            {
                opcodes.Add(ReadOpcode());
            }
            return opcodes.ToArray();
        }
    }
    void ReadArea(OpcodeArea result)
    {
        result.top = _ms.ReadUInt32();
        result.left = _ms.ReadUInt32();
        result.bottom = _ms.ReadUInt32();
        result.right = _ms.ReadUInt32();
        result.plane = _ms.ReadUInt32();
        result.planes = _ms.ReadUInt32();
        result.rowPitch = _ms.ReadUInt32();
        result.colPitch = _ms.ReadUInt32();
    }
    OpcodeGainMap ReadGainMap(OpcodeHeader header)
    {
        var result = new OpcodeGainMap();
        result.header = header;
        ReadArea(result);
        result.mapPointsV = _ms.ReadUInt32();
        result.mapPointsH = _ms.ReadUInt32();
        result.mapSpacingV = _ms.ReadDouble();
        result.mapSpacingH = _ms.ReadDouble();
        result.mapOriginV = _ms.ReadDouble();
        result.mapOriginH = _ms.ReadDouble();
        result.mapPlanes = _ms.ReadUInt32();
        int mapGainsCount = (int)(result.mapPointsV * result.mapPointsH * result.mapPlanes);
        var mapGains = new float[mapGainsCount];
        for (int mapGainsIndex = 0; mapGainsIndex < mapGainsCount; mapGainsIndex++)
        {
            mapGains[mapGainsIndex] = _ms.ReadFloat();
        }
        result.mapGains = mapGains;
        return result;
    }
    OpcodeWarpRectilinear ReadWarpRectilinear(OpcodeHeader header)
    {
        var result = new OpcodeWarpRectilinear();
        result.header = header;
        result.planes = _ms.ReadUInt32();
        int coefficientsCount = (int)(result.planes * 6);
        result.coefficients = new double[coefficientsCount];
        for (int i = 0; i < coefficientsCount; i++)
        {
            result.coefficients[i] = _ms.ReadDouble();
        }
        result.cx = _ms.ReadDouble();
        result.cy = _ms.ReadDouble();
        return result;
    }
    OpcodeWarpFisheye ReadWarpFisheye(OpcodeHeader header)
    {
        var result = new OpcodeWarpFisheye();
        result.header = header;
        result.planes = _ms.ReadUInt32();
        int coefficientsCount = (int)(result.planes * 4);
        result.coefficients = new double[coefficientsCount];
        for (int i = 0; i < coefficientsCount; i++)
        {
            result.coefficients[i] = _ms.ReadDouble();
        }
        result.cx = _ms.ReadDouble();
        result.cy = _ms.ReadDouble();
        return result;
    }
    OpcodeFixVignetteRadial ReadFixVignetteRadial(OpcodeHeader header)
    {
        var result = new OpcodeFixVignetteRadial();
        result.header = header;
        result.k0 = _ms.ReadDouble();
        result.k1 = _ms.ReadDouble();
        result.k2 = _ms.ReadDouble();
        result.k3 = _ms.ReadDouble();
        result.k4 = _ms.ReadDouble();
        result.cx = _ms.ReadDouble();
        result.cy = _ms.ReadDouble();
        return result;
    }
    OpcodeTrimBounds ReadTrimBounds(OpcodeHeader header)
    {
        var result = new OpcodeTrimBounds();
        result.header = header;
        result.top = _ms.ReadUInt32();
        result.left = _ms.ReadUInt32();
        result.bottom = _ms.ReadUInt32();
        result.right = _ms.ReadUInt32();
        return result;
    }
    OpcodeMapTable ReadMapTable(OpcodeHeader header)
    {
        var result = new OpcodeMapTable();
        result.header = header;
        ReadArea(result);
        int tableSize = (int)_ms.ReadUInt32();
        result.table = new UInt16[tableSize];
        for (int i = 0; i < tableSize; i++)
        {
            result.table[i] = _ms.ReadUInt16();
        }
        return result;
    }
    OpcodeMapPolynomial ReadMapPolynomial(OpcodeHeader header)
    {
        var result = new OpcodeMapPolynomial();
        result.header = header;
        ReadArea(result);
        int degree = (int)_ms.ReadUInt32();
        result.coefficients = new double[degree + 1];
        for (int i = 0; i <= degree; i++)
        {
            result.coefficients[i] = _ms.ReadDouble();
        }
        return result;
    }
    float[] ReadAreaFloats(OpcodeArea result)
    {
        ReadArea(result);
        int count = (int)_ms.ReadUInt32();
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = _ms.ReadFloat();
        }
        return values;
    }
    OpcodeFixBadPixelsConstant ReadFixBadPixelsConstant(OpcodeHeader header)
    {
        var result = new OpcodeFixBadPixelsConstant();
        result.header = header;
        result.constant = _ms.ReadUInt32();
        result.bayerPhase = _ms.ReadUInt32();
        return result;
    }
    OpcodeFixBadPixelsList ReadFixBadPixelsList(OpcodeHeader header)
    {
        var result = new OpcodeFixBadPixelsList();
        result.header = header;
        result.bayerPhase = _ms.ReadUInt32();
        int pointCount = (int)_ms.ReadUInt32();
        int rectCount = (int)_ms.ReadUInt32();
        result.badPoints = new UInt32[pointCount * 2];
        for (int i = 0; i < result.badPoints.Length; i++)
        {
            result.badPoints[i] = _ms.ReadUInt32();
        }
        result.badRects = new UInt32[rectCount * 4];
        for (int i = 0; i < result.badRects.Length; i++)
        {
            result.badRects[i] = _ms.ReadUInt32();
        }
        return result;
    }
    OpcodeHeader ReadOpcodeHeader()
    {
        var header = new OpcodeHeader();
        header.id = (OpcodeId)_ms.ReadUInt32();
        header.dngVersion = (DngVersion)_ms.ReadUInt32();
        header.flags = (OpcodeFlag)_ms.ReadUInt32();
        header.bytesCount = _ms.ReadUInt32();
        return header;
    }
    Opcode ReadOpcode()
    {
        var header = ReadOpcodeHeader();
        long bodyStart = _ms.Position;
        Opcode result;
        switch (header.id)
        {
            case OpcodeId.WarpRectilinear:
                result = ReadWarpRectilinear(header);
                break;
            case OpcodeId.WarpFisheye:
                result = ReadWarpFisheye(header);
                break;
            case OpcodeId.FixVignetteRadial:
                result = ReadFixVignetteRadial(header);
                break;
            case OpcodeId.FixBadPixelsConstant:
                result = ReadFixBadPixelsConstant(header);
                break;
            case OpcodeId.FixBadPixelsList:
                result = ReadFixBadPixelsList(header);
                break;
            case OpcodeId.TrimBounds:
                result = ReadTrimBounds(header);
                break;
            case OpcodeId.MapTable:
                result = ReadMapTable(header);
                break;
            case OpcodeId.MapPolynomial:
                result = ReadMapPolynomial(header);
                break;
            case OpcodeId.GainMap:
                result = ReadGainMap(header);
                break;
            case OpcodeId.DeltaPerRow:
                {
                    var op = new OpcodeDeltaPerRow { header = header };
                    op.deltas = ReadAreaFloats(op);
                    result = op;
                    break;
                }
            case OpcodeId.DeltaPerColumn:
                {
                    var op = new OpcodeDeltaPerColumn { header = header };
                    op.deltas = ReadAreaFloats(op);
                    result = op;
                    break;
                }
            case OpcodeId.ScalePerRow:
                {
                    var op = new OpcodeScalePerRow { header = header };
                    op.scales = ReadAreaFloats(op);
                    result = op;
                    break;
                }
            case OpcodeId.ScalePerColumn:
                {
                    var op = new OpcodeScalePerColumn { header = header };
                    op.scales = ReadAreaFloats(op);
                    result = op;
                    break;
                }
            default:
                result = new Opcode() { header = header };
                break;
        }
        // Resync to the next opcode using the declared size. This keeps the
        // stream aligned even if an opcode body is partially or not parsed.
        _ms.Position = bodyStart + header.bytesCount;
        return result;
    }
}
