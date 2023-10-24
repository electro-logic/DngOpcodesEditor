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
        OpcodeWarpRectilinear ReadWrapRectilinear(OpcodeHeader header)
        {
            var result = new OpcodeWarpRectilinear();
            result.header = header;
            result.planes = _ms.ReadUInt32();
            var coefficients = new List<double>();
            for (int planeIndex = 0; planeIndex < result.planes * 6; planeIndex++)
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
                default:
                    _ms.Seek(header.bytesCount, SeekOrigin.Current);
                    return new Opcode() { header = header };
            }
        }
    }
}