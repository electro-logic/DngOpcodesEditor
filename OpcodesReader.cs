using System.Collections.Generic;
using System.IO;

namespace DngOpcodesEditor
{
    public class OpcodesReader
    {
        MemoryStream _ms;
        public Opcode[] ReadOpcodeList(byte[] bytes)
        {
            _ms = new MemoryStream();
            _ms.Write(bytes);
            _ms.Position = 0;
            var opcodesCount = _ms.ReadUInt32();
            var opcodes = new List<Opcode>((int)opcodesCount);
            for (int opcodeIndex = 0; opcodeIndex < opcodesCount; opcodeIndex++)
            {
                opcodes.Add(ReadOpcode());
            }
            return opcodes.ToArray();
        }
        OpcodeGainMap ReadGainMap(OpcodeHeader header)
        {
            var result = new OpcodeGainMap();
            result.header = header;
            result.top = _ms.ReadUInt32();
            result.left = _ms.ReadUInt32();
            result.bottom = _ms.ReadUInt32();
            result.right = _ms.ReadUInt32();
            result.plane = _ms.ReadUInt32();
            result.planes = _ms.ReadUInt32();
            result.rowPitch = _ms.ReadUInt32();
            result.colPitch = _ms.ReadUInt32();
            result.mapPointsV = _ms.ReadUInt32();
            result.mapPointsH = _ms.ReadUInt32();
            result.mapSpacingV = _ms.ReadDouble();
            result.mapSpacingH = _ms.ReadDouble();
            result.mapOriginV = _ms.ReadDouble();
            result.mapOriginH = _ms.ReadDouble();
            result.mapPlanes = _ms.ReadUInt32();
            int mapGainsCount = (int)(result.mapPointsV * result.mapPointsH * result.mapPlanes);
            var mapGains = new List<float>(mapGainsCount);
            for (int mapGainsIndex = 0; mapGainsIndex < mapGainsCount; mapGainsIndex++)
            {
                mapGains.Add(_ms.ReadFloat());
            }
            result.mapGains = mapGains.ToArray();
            return result;
        }
        OpcodeWarpRectilinear ReadWrapRectilinear(OpcodeHeader header)
        {
            var result = new OpcodeWarpRectilinear();
            result.header = header;
            result.planes = _ms.ReadUInt32();
            int coefficientsCount = (int)(result.planes * 6);
            var coefficients = new List<double>(coefficientsCount);
            for (int planeIndex = 0; planeIndex < coefficientsCount; planeIndex++)
            {
                coefficients.Add(_ms.ReadDouble());
            }
            result.coefficients = coefficients.ToArray();
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
        OpcodeHeader ReadOpcodeHeader()
        {
            var opcode = new OpcodeHeader();
            opcode.id = (OpcodeId)_ms.ReadUInt32();
            opcode.dngVersion = (DngVersion)_ms.ReadUInt32();
            opcode.flags = (OpcodeFlag)_ms.ReadUInt32();
            opcode.bytesCount = _ms.ReadUInt32();
            return opcode;
        }
        Opcode ReadOpcode()
        {
            var header = ReadOpcodeHeader();
            switch (header.id)
            {
                case OpcodeId.WarpRectilinear:
                    return ReadWrapRectilinear(header);
                case OpcodeId.FixVignetteRadial:
                    return ReadFixVignetteRadial(header);
                case OpcodeId.TrimBounds:
                    return ReadTrimBounds(header);
                case OpcodeId.GainMap: 
                    return ReadGainMap(header);
                default:
                    _ms.Seek(header.bytesCount, SeekOrigin.Current);
                    return new Opcode() { header = header };
            }
        }
    }
}