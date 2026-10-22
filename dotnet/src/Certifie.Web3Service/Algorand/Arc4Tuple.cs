using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// A tuple: static values in place, dynamic values after the head with their 2-byte offsets in place, and consecutive bools packed into bits.
    /// Arrays are encoded as tuples of their items.
    /// </summary>
    internal sealed class Arc4Tuple : Arc4Type
    {
        private const int OffsetBytes = 2;
        private const int ByteBits = 8;

        public Arc4Tuple(IReadOnlyList<Arc4Type> children) => Children = children;

        public IReadOnlyList<Arc4Type> Children { get; }
        public override bool IsDynamic => Children.Any(child => child.IsDynamic);
        public override int StaticSize => Segments().Sum(segment => segment.Bools > 0 ? BoolBytes(segment.Bools) : Children[segment.Start].StaticSize);
        public override string ToString() => $"({string.Join(",", Children)})";

        /// <summary>A 2-byte big-endian length or offset.</summary>
        public static byte[] Length(int value) => new[] { (byte)(value >> ByteBits), (byte)value };

        public override byte[] Encode(object value)
        {
            var items = ((IEnumerable<object>)value).ToList();
            var parts = Segments().Select(segment => EncodeSegment(segment, items)).ToList();
            var (head, offset) = (new List<byte>(), parts.Sum(part => part.Dynamic ? OffsetBytes : part.Bytes.Length));
            foreach (var part in parts.Where(part => part.Dynamic)) (part.Offset, offset) = (offset, offset + part.Bytes.Length);
            foreach (var part in parts) head.AddRange(part.Dynamic ? Length(part.Offset) : part.Bytes);
            return head.Concat(parts.Where(part => part.Dynamic).SelectMany(part => part.Bytes)).ToArray();
        }

        public override object Decode(byte[] encoded)
        {
            var (values, position, offsets) = (new object[Children.Count], 0, new List<(int Index, int Offset)>());
            foreach (var segment in Segments()) position += DecodeSegment(segment, encoded, position, values, offsets);
            for (var item = 0; item < offsets.Count; item++)
            {
                var end = item + 1 < offsets.Count ? offsets[item + 1].Offset : encoded.Length;
                values[offsets[item].Index] = Children[offsets[item].Index].Decode(Slice(encoded, offsets[item].Offset, end - offsets[item].Offset));
            }
            return values.ToList();
        }

        public static byte[] Slice(byte[] bytes, int start, int length)
        {
            if (start < 0 || length < 0 || start + length > bytes.Length) throw Errors.InvalidArgument("The ARC-4 value is too short for its type.");
            var slice = new byte[length];
            Array.Copy(bytes, start, slice, 0, length);
            return slice;
        }

        /// <summary>The children grouped as the encoding lays them out: each run of bools is one segment, every other child its own.</summary>
        private IEnumerable<(int Start, int Bools)> Segments()
        {
            for (var index = 0; index < Children.Count;)
            {
                var bools = Children.Skip(index).TakeWhile(child => child is Arc4Bool).Count();
                yield return (index, bools);
                index += Math.Max(bools, 1);
            }
        }

        private Part EncodeSegment((int Start, int Bools) segment, List<object> items)
        {
            if (segment.Bools > 0) return new Part(PackBools(items.Skip(segment.Start).Take(segment.Bools).Cast<bool>().ToList()), false);
            var child = Children[segment.Start];
            return new Part(child.Encode(items[segment.Start]), child.IsDynamic);
        }

        /// <summary>Reads one segment at the position and returns its size in the head; dynamic children are read after the head.</summary>
        private int DecodeSegment((int Start, int Bools) segment, byte[] encoded, int position, object[] values, List<(int, int)> offsets)
        {
            for (var bit = 0; bit < segment.Bools; bit++) values[segment.Start + bit] = (encoded[position + bit / ByteBits] & (Arc4Bool.True >> (bit % ByteBits))) != 0;
            if (segment.Bools > 0) return BoolBytes(segment.Bools);
            var child = Children[segment.Start];
            if (child.IsDynamic) offsets.Add((segment.Start, (encoded[position] << ByteBits) | encoded[position + 1]));
            else values[segment.Start] = child.Decode(Slice(encoded, position, child.StaticSize));
            return child.IsDynamic ? OffsetBytes : child.StaticSize;
        }

        private static byte[] PackBools(List<bool> values)
        {
            var packed = new byte[BoolBytes(values.Count)];
            for (var bit = 0; bit < values.Count; bit++) packed[bit / ByteBits] |= values[bit] ? (byte)(Arc4Bool.True >> (bit % ByteBits)) : (byte)0;
            return packed;
        }

        private static int BoolBytes(int count) => (count + ByteBits - 1) / ByteBits;

        /// <summary>The encoding of one segment; dynamic parts get their offset once the head size is known.</summary>
        private sealed class Part
        {
            public Part(byte[] bytes, bool dynamic) => (Bytes, Dynamic) = (bytes, dynamic);

            public byte[] Bytes { get; }
            public bool Dynamic { get; }
            public int Offset { get; set; }
        }
    }

    /// <summary>T[N]: N items without a length; byte[N] values are byte arrays.</summary>
    internal sealed class Arc4StaticArray : Arc4Type
    {
        public Arc4StaticArray(Arc4Type child, int length) => (Child, Length) = (child, length);

        public Arc4Type Child { get; }
        public int Length { get; }
        public override bool IsDynamic => Child.IsDynamic;
        public override int StaticSize => Items().StaticSize;
        public override string ToString() => $"{Child}[{Length}]";
        public override byte[] Encode(object value) => Arc4Arrays.Encode(Items(), Child, value);
        public override object Decode(byte[] encoded) => Arc4Arrays.Decode(Items(), Child, encoded);

        private Arc4Tuple Items() => new Arc4Tuple(Enumerable.Repeat(Child, Length).ToList());
    }

    /// <summary>T[]: a 2-byte item count followed by the items; byte[] values are byte arrays.</summary>
    internal sealed class Arc4DynamicArray : Arc4Type
    {
        public Arc4DynamicArray(Arc4Type child) => Child = child;

        public Arc4Type Child { get; }
        public override bool IsDynamic => true;
        public override int StaticSize => throw new InvalidOperationException($"{this} has no static size.");
        public override string ToString() => $"{Child}[]";

        public override byte[] Encode(object value)
        {
            var count = value is byte[] bytes ? bytes.Length : ((IEnumerable<object>)value).Count();
            return Arc4Tuple.Length(count).Concat(Arc4Arrays.Encode(Items(count), Child, value)).ToArray();
        }

        public override object Decode(byte[] encoded) =>
            Arc4Arrays.Decode(Items((encoded[0] << 8) | encoded[1]), Child, Arc4Tuple.Slice(encoded, 2, encoded.Length - 2));

        private Arc4Tuple Items(int count) => new Arc4Tuple(Enumerable.Repeat(Child, count).ToList());
    }

    /// <summary>Arrays as tuples of their items; arrays of bytes take and return byte arrays.</summary>
    internal static class Arc4Arrays
    {
        public static bool IsBytes(Arc4Type child) => child.ToString() == "byte";

        public static byte[] Encode(Arc4Tuple items, Arc4Type child, object value) =>
            value is byte[] bytes && IsBytes(child) ? bytes : items.Encode(value);

        public static object Decode(Arc4Tuple items, Arc4Type child, byte[] encoded)
        {
            var values = (List<object>)items.Decode(encoded);
            return IsBytes(child) ? values.Select(value => (byte)(BigInteger)value).ToArray() : values;
        }
    }
}
