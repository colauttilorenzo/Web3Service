using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// An ARC-4 ABI type, which encodes and decodes its values. Values: <see cref="BigInteger"/> for integers, bool, string (also addresses),
    /// byte[] for byte arrays, and lists for other arrays and tuples.
    /// </summary>
    internal abstract class Arc4Type
    {
        private static readonly Regex UintPattern = new Regex(@"^uint(\d+)$");
        private static readonly Regex UfixedPattern = new Regex(@"^ufixed(\d+)x(\d+)$");
        private static readonly Regex ArrayPattern = new Regex(@"^(.+)\[(\d*)\]$");

        /// <summary>True when the encoding has no fixed size: strings, dynamic arrays and the types that contain them.</summary>
        public virtual bool IsDynamic => false;

        /// <summary>Bytes of the encoding of a static type; bools packed in a tuple take a bit each instead.</summary>
        public abstract int StaticSize { get; }

        public abstract byte[] Encode(object value);

        public abstract object Decode(byte[] encoded);

        /// <summary>The canonical name, as method signatures spell it.</summary>
        public abstract override string ToString();

        public static Arc4Type Parse(string type)
        {
            var array = ArrayPattern.Match(type);
            if (array.Success) return ParseArray(Parse(array.Groups[1].Value), array.Groups[2].Value);
            if (type.StartsWith("(", StringComparison.Ordinal) && type.EndsWith(")", StringComparison.Ordinal))
                return new Arc4Tuple(SplitTuple(type.Substring(1, type.Length - 2)).Select(Parse).ToList());
            return ParseBasic(type) ?? throw Errors.InvalidArgument($"Unknown ARC-4 type {type}.");
        }

        private static Arc4Type ParseArray(Arc4Type child, string length) =>
            length.Length == 0 ? new Arc4DynamicArray(child) : new Arc4StaticArray(child, int.Parse(length, CultureInfo.InvariantCulture));

        private static Arc4Type? ParseBasic(string type)
        {
            var uint_ = UintPattern.Match(type);
            if (uint_.Success) return new Arc4Uint(Bits(uint_.Groups[1].Value, type), $"uint{uint_.Groups[1].Value}");
            var ufixed = UfixedPattern.Match(type);
            if (ufixed.Success) return new Arc4Uint(Bits(ufixed.Groups[1].Value, type), type);
            return type switch
            {
                "byte" => new Arc4Uint(8, "byte"), "bool" => new Arc4Bool(), "address" => new Arc4Address(), "string" => new Arc4String(), _ => null,
            };
        }

        private static int Bits(string digits, string type)
        {
            var bits = int.Parse(digits, CultureInfo.InvariantCulture);
            return bits % 8 == 0 && bits >= 8 && bits <= 512 ? bits : throw Errors.InvalidArgument($"Unknown ARC-4 type {type}.");
        }

        /// <summary>The types of a tuple, split at the commas outside nested tuples.</summary>
        private static List<string> SplitTuple(string inner)
        {
            var (types, depth, start) = (new List<string>(), 0, 0);
            for (var index = 0; index < inner.Length; index++)
            {
                depth += inner[index] == '(' ? 1 : inner[index] == ')' ? -1 : 0;
                if (inner[index] == ',' && depth == 0) (types, start) = (Add(types, inner.Substring(start, index - start)), index + 1);
            }
            return inner.Length == 0 ? types : Add(types, inner.Substring(start));
        }

        private static List<string> Add(List<string> items, string item)
        {
            items.Add(item);
            return items;
        }
    }

    /// <summary>uintN, ufixedNxM and byte: N bits, big-endian.</summary>
    internal sealed class Arc4Uint : Arc4Type
    {
        private readonly string _name;

        public Arc4Uint(int bits, string name) => (Bits, _name) = (bits, name);

        public int Bits { get; }
        public override int StaticSize => Bits / 8;
        public override string ToString() => _name;

        public override byte[] Encode(object value)
        {
            var bytes = ((BigInteger)value).ToByteArray().Reverse().SkipWhile((item, index) => item == 0 && index == 0).ToArray();
            return new byte[StaticSize - bytes.Length].Concat(bytes).ToArray();
        }

        public override object Decode(byte[] encoded) => new BigInteger(encoded.Reverse().Append((byte)0).ToArray());
    }

    internal sealed class Arc4Bool : Arc4Type
    {
        public const byte True = 0x80;
        public override int StaticSize => 1;
        public override string ToString() => "bool";
        public override byte[] Encode(object value) => new[] { (bool)value ? True : (byte)0 };
        public override object Decode(byte[] encoded) => (encoded[0] & True) != 0;
    }

    /// <summary>A 32-byte public key; values are 58-character addresses.</summary>
    internal sealed class Arc4Address : Arc4Type
    {
        public override int StaticSize => AlgorandAddress.KeyBytes;
        public override string ToString() => "address";
        public override byte[] Encode(object value) => AlgorandAddress.Decode((string)value);
        public override object Decode(byte[] encoded) => AlgorandAddress.Encode(encoded);
    }

    /// <summary>UTF-8 text after a 2-byte length.</summary>
    internal sealed class Arc4String : Arc4Type
    {
        public override bool IsDynamic => true;
        public override int StaticSize => throw new InvalidOperationException("string has no static size.");
        public override string ToString() => "string";
        public override byte[] Encode(object value) => Arc4Tuple.Length(Encoding.UTF8.GetByteCount((string)value)).Concat(Encoding.UTF8.GetBytes((string)value)).ToArray();
        public override object Decode(byte[] encoded) => Encoding.UTF8.GetString(encoded, 2, encoded.Length - 2);
    }
}
