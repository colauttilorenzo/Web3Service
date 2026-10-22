using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// A MessagePack map in Algorand's canonical form: keys sorted, and empty values (0, false, "", no bytes, no items) left out.
    /// Transactions are signed and identified by this encoding, so it must match the node's byte for byte.
    /// </summary>
    internal sealed class MsgpackMap
    {
        private readonly SortedDictionary<string, object> _fields = new SortedDictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Adds a field unless its value is empty. Values: integers, bool, string, bytes, maps and lists of them.</summary>
        public MsgpackMap Add(string key, object? value)
        {
            if (!IsEmpty(value)) _fields[key] = value!;
            return this;
        }

        /// <summary>Adds the fields of another map.</summary>
        public MsgpackMap AddAll(MsgpackMap other)
        {
            foreach (var field in other._fields) _fields[field.Key] = field.Value;
            return this;
        }

        public IEnumerable<KeyValuePair<string, object>> Fields => _fields;

        private static bool IsEmpty(object? value) => value switch
        {
            null => true,
            bool flag => !flag,
            string text => text.Length == 0,
            byte[] bytes => bytes.Length == 0,
            MsgpackMap map => !map._fields.Any(),
            System.Collections.ICollection items => items.Count == 0,
            _ => ToInteger(value) == 0,
        };

        internal static BigInteger ToInteger(object value) => value switch
        {
            BigInteger integer => integer,
            int integer => integer,
            long integer => integer,
            ulong integer => integer,
            _ => throw new ArgumentException($"MessagePack cannot encode {value.GetType().Name}."),
        };
    }

    /// <summary>Writes MessagePack in the smallest form of each value, as Algorand's canonical encoding requires.</summary>
    internal static class Msgpack
    {
        public static byte[] Encode(object value)
        {
            using var stream = new MemoryStream();
            Write(stream, value);
            return stream.ToArray();
        }

        private static void Write(Stream stream, object value)
        {
            switch (value)
            {
                case MsgpackMap map: WriteMap(stream, map); break;
                case bool flag: stream.WriteByte(flag ? (byte)0xc3 : (byte)0xc2); break;
                case string text: WriteString(stream, Encoding.UTF8.GetBytes(text)); break;
                case byte[] bytes: WriteBytes(stream, bytes); break;
                case System.Collections.IEnumerable items: WriteArray(stream, items.Cast<object>().ToList()); break;
                default: WriteInteger(stream, MsgpackMap.ToInteger(value)); break;
            }
        }

        private static void WriteMap(Stream stream, MsgpackMap map)
        {
            var fields = map.Fields.ToList();
            WriteHeader(stream, fields.Count, 0x80, 0xde, 0xdf);
            foreach (var field in fields)
            {
                Write(stream, field.Key);
                Write(stream, field.Value);
            }
        }

        private static void WriteArray(Stream stream, List<object> items)
        {
            WriteHeader(stream, items.Count, 0x90, 0xdc, 0xdd);
            foreach (var item in items) Write(stream, item);
        }

        private static void WriteString(Stream stream, byte[] utf8)
        {
            if (utf8.Length < 32) stream.WriteByte((byte)(0xa0 | utf8.Length));
            else WriteLength(stream, utf8.Length, 0xd9, 0xda, 0xdb);
            stream.Write(utf8, 0, utf8.Length);
        }

        private static void WriteBytes(Stream stream, byte[] bytes)
        {
            WriteLength(stream, bytes.Length, 0xc4, 0xc5, 0xc6);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Non-negative integers below 2^64: a positive fixint, or uint 8, 16, 32 or 64.</summary>
        private static void WriteInteger(Stream stream, BigInteger value)
        {
            if (value < 0 || value > ulong.MaxValue) throw new ArgumentOutOfRangeException(nameof(value), "Algorand integers are unsigned 64-bit.");
            if (value < 0x80) stream.WriteByte((byte)value);
            else if (value <= byte.MaxValue) WriteBigEndian(stream, 0xcc, (ulong)value, 1);
            else if (value <= ushort.MaxValue) WriteBigEndian(stream, 0xcd, (ulong)value, 2);
            else if (value <= uint.MaxValue) WriteBigEndian(stream, 0xce, (ulong)value, 4);
            else WriteBigEndian(stream, 0xcf, (ulong)value, 8);
        }

        /// <summary>A map or array header: the count in the fix form when it fits in 4 bits, else in 16 or 32 bits.</summary>
        private static void WriteHeader(Stream stream, int count, byte fix, byte header16, byte header32)
        {
            if (count < 16) stream.WriteByte((byte)(fix | count));
            else if (count <= ushort.MaxValue) WriteBigEndian(stream, header16, (ulong)count, 2);
            else WriteBigEndian(stream, header32, (ulong)count, 4);
        }

        private static void WriteLength(Stream stream, int length, byte header8, byte header16, byte header32)
        {
            if (length <= byte.MaxValue) WriteBigEndian(stream, header8, (ulong)length, 1);
            else if (length <= ushort.MaxValue) WriteBigEndian(stream, header16, (ulong)length, 2);
            else WriteBigEndian(stream, header32, (ulong)length, 4);
        }

        private static void WriteBigEndian(Stream stream, byte header, ulong value, int size)
        {
            stream.WriteByte(header);
            for (var shift = (size - 1) * 8; shift >= 0; shift -= 8) stream.WriteByte((byte)(value >> shift));
        }
    }
}
