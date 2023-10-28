using System;
using System.Buffers.Binary;
using System.IO;

public static class BigEndianStreamExtension
{
    public static UInt32 ReadUInt32(this Stream stream)
    {
        var buffer = new byte[4]; stream.Read(buffer); return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }
    public static Int64 ReadInt64(this Stream stream)
    {
        var buffer = new byte[8]; stream.Read(buffer); return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }
    public static UInt64 ReadUInt64(this Stream stream)
    {
        var buffer = new byte[8]; stream.Read(buffer); return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }
    public static float ReadFloat(this Stream stream)
    {
        var buffer = new byte[4]; stream.Read(buffer); return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }
    public static double ReadDouble(this Stream stream)
    {
        var buffer = new byte[8]; stream.Read(buffer); return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }
    public static void WriteUInt32(this Stream stream, UInt32 value)
    {
        byte[] _buffer = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(_buffer, value); stream.Write(_buffer);
    }
    public static void WriteInt64(this Stream stream, Int64 value)
    {
        byte[] _buffer = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(_buffer, value); stream.Write(_buffer);
    }
    public static void WriteUInt64(this Stream stream, UInt64 value)
    {
        byte[] _buffer = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(_buffer, value); stream.Write(_buffer);
    }
    public static void WriteFloat(this Stream stream, float value)
    {
        byte[] _buffer = new byte[4]; BinaryPrimitives.WriteSingleBigEndian(_buffer, value); stream.Write(_buffer);
    }
    public static void WriteDouble(this Stream stream, double value)
    {
        byte[] _buffer = new byte[8]; BinaryPrimitives.WriteDoubleBigEndian(_buffer, value); stream.Write(_buffer);
    }
}