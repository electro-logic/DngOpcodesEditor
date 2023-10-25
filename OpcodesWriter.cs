using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DngOpcodesEditor
{
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
        public void WarpRectilinear(OpcodeWarpRectilinear parameters) => WarpRectilinear(parameters.planes, parameters.coefficients, parameters.cx, parameters.cy);
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
        public void FixVignetteRadial(OpcodeFixVignetteRadial parameters) => FixVignetteRadial(parameters.k0, parameters.k1, parameters.k2, parameters.k3, parameters.k4, parameters.cx, parameters.cy);
        public void TrimBounds(UInt32 top, UInt32 left, UInt32 bottom, UInt32 right)
        {        
            WriteOpcodeHeader(OpcodeId.TrimBounds, DngVersion.DNG_VERSION_1_3_0_0, OpcodeFlag.OptionalPreview, 16);
            _ms.WriteUInt32(top);
            _ms.WriteUInt32(left);
            _ms.WriteUInt32(bottom);
            _ms.WriteUInt32(right);
        }
        public void TrimBounds(OpcodeTrimBounds parameters) => TrimBounds(parameters.top, parameters.left, parameters.bottom, parameters.right);
    }
}