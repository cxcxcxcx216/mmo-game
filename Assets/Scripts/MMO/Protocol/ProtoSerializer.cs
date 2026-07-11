using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MMO.Protocol
{
    /// <summary>
    /// 最小 Protobuf 线格式读写器，手写实现以匹配服务端 proto3 协议。
    /// 支持 varint、fixed32、fixed64、length-delimited 字段。
    /// 无外部依赖，确保与服务端 Protobuf 3.25.3 完全兼容。
    /// </summary>
    public static class ProtoSerializer
    {
        // ==================== 写入 ====================

        public static void WriteVarint(Stream s, ulong value)
        {
            while (value > 0x7F)
            {
                s.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }

        public static void WriteTag(Stream s, int fieldNumber, int wireType)
        {
            WriteVarint(s, ((ulong)fieldNumber << 3) | (uint)wireType);
        }

        public static void WriteInt32(Stream s, int fieldNumber, int value)
        {
            if (value == 0) return; // proto3 默认值不发送
            WriteTag(s, fieldNumber, 0);
            WriteVarint(s, (ulong)value);
        }

        public static void WriteInt64(Stream s, int fieldNumber, long value)
        {
            if (value == 0) return;
            WriteTag(s, fieldNumber, 0);
            WriteVarint(s, (ulong)value);
        }

        public static void WriteUInt32(Stream s, int fieldNumber, uint value)
        {
            if (value == 0) return;
            WriteTag(s, fieldNumber, 0);
            WriteVarint(s, value);
        }

        public static void WriteUInt64(Stream s, int fieldNumber, ulong value)
        {
            if (value == 0) return;
            WriteTag(s, fieldNumber, 0);
            WriteVarint(s, value);
        }

        public static void WriteFloat(Stream s, int fieldNumber, float value)
        {
            if (value == 0f) return;
            WriteTag(s, fieldNumber, 5);
            byte[] b = BitConverter.GetBytes(value);
            s.Write(b, 0, 4);
        }

        public static void WriteString(Stream s, int fieldNumber, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            byte[] b = Encoding.UTF8.GetBytes(value);
            WriteTag(s, fieldNumber, 2);
            WriteVarint(s, (ulong)b.Length);
            s.Write(b, 0, b.Length);
        }

        public static void WriteBytes(Stream s, int fieldNumber, byte[] value)
        {
            if (value == null || value.Length == 0) return;
            WriteTag(s, fieldNumber, 2);
            WriteVarint(s, (ulong)value.Length);
            s.Write(value, 0, value.Length);
        }

        /// <summary>写入嵌套 message（length-delimited）</summary>
        public static void WriteMessage<T>(Stream s, int fieldNumber, T value, Action<Stream, T> serializer)
        {
            if (value == null) return;
            using var ms = new MemoryStream();
            serializer(ms, value);
            byte[] data = ms.ToArray();
            if (data.Length == 0) return; // 空消息不发送
            WriteTag(s, fieldNumber, 2);
            WriteVarint(s, (ulong)data.Length);
            s.Write(data, 0, data.Length);
        }

        /// <summary>写入 repeated message 列表</summary>
        public static void WriteRepeatedMessage<T>(Stream s, int fieldNumber, List<T> values, Action<Stream, T> serializer)
        {
            if (values == null) return;
            foreach (var v in values)
            {
                WriteMessage(s, fieldNumber, v, serializer);
            }
        }

        // ==================== 读取 ====================

        public static ulong ReadVarint(byte[] data, ref int offset)
        {
            ulong result = 0;
            int shift = 0;
            while (offset < data.Length)
            {
                byte b = data[offset++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        public static int ReadTag(byte[] data, ref int offset)
        {
            return (int)ReadVarint(data, ref offset);
        }

        public static int ReadInt32(byte[] data, ref int offset)
        {
            return (int)ReadVarint(data, ref offset);
        }

        public static long ReadInt64(byte[] data, ref int offset)
        {
            return (long)ReadVarint(data, ref offset);
        }

        public static uint ReadUInt32(byte[] data, ref int offset)
        {
            return (uint)ReadVarint(data, ref offset);
        }

        public static ulong ReadUInt64(byte[] data, ref int offset)
        {
            return ReadVarint(data, ref offset);
        }

        public static float ReadFloat(byte[] data, ref int offset)
        {
            float v = BitConverter.ToSingle(data, offset);
            offset += 4;
            return v;
        }

        public static string ReadString(byte[] data, ref int offset)
        {
            int len = (int)ReadVarint(data, ref offset);
            string s = Encoding.UTF8.GetString(data, offset, len);
            offset += len;
            return s;
        }

        public static byte[] ReadBytes(byte[] data, ref int offset)
        {
            int len = (int)ReadVarint(data, ref offset);
            byte[] b = new byte[len];
            Buffer.BlockCopy(data, offset, b, 0, len);
            offset += len;
            return b;
        }

        /// <summary>读取嵌套 message 的子数组（length-delimited）</summary>
        public static byte[] ReadMessageBytes(byte[] data, ref int offset)
        {
            return ReadBytes(data, ref offset);
        }

        /// <summary>跳过未知字段</summary>
        public static void SkipField(byte[] data, ref int offset, int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(data, ref offset); break;
                case 1: offset += 8; break;
                case 2: int len = (int)ReadVarint(data, ref offset); offset += len; break;
                case 5: offset += 4; break;
                default: throw new InvalidDataException($"未知 wire type: {wireType}");
            }
        }

        /// <summary>将 message 序列化为字节数组</summary>
        public static byte[] Serialize(Action<Stream> writer)
        {
            using var ms = new MemoryStream();
            writer(ms);
            return ms.ToArray();
        }
    }
}
