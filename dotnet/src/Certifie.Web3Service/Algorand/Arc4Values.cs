using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>Converts JSON values to ARC-4 values and back: integers as decimal strings, byte arrays as 0x hex, tuples as arrays.</summary>
    internal static class Arc4Values
    {
        private static readonly Regex HexBytes = new Regex("^0x(?:[0-9a-f]{2})*$", RegexOptions.IgnoreCase);

        /// <summary>The JSON type a caller passes: number, boolean, string or array. Byte arrays are 0x hex strings.</summary>
        public static string JsonType(Arc4Type type) => type switch
        {
            Arc4Uint _ => "number",
            Arc4Bool _ => "boolean",
            Arc4StaticArray array when !Arc4Arrays.IsBytes(array.Child) => "array",
            Arc4DynamicArray array when !Arc4Arrays.IsBytes(array.Child) => "array",
            Arc4Tuple _ => "array",
            _ => "string",
        };

        /// <summary>A JSON value as an ARC-4 value. Values of the wrong type fail with <c>INVALID_ARGUMENT</c>.</summary>
        public static object ToAbiValue(Arc4Type type, JsonNode? value, string name) => type switch
        {
            Arc4Uint integer => ToInteger(value, integer.Bits, name),
            Arc4Bool _ => value is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? flag.GetValue<bool>() : throw Errors.InvalidArgument($"{name} must be true or false."),
            Arc4StaticArray array => ToArray(array.Child, value, name, array.Length),
            Arc4DynamicArray array => ToArray(array.Child, value, name, null),
            Arc4Tuple tuple => Items(value, name, tuple.Children.Count).Select((item, index) => ToAbiValue(tuple.Children[index], item, name)).ToList(),
            _ => ToText(type, value, name),
        };

        /// <summary>A decoded ARC-4 value as JSON: integers become decimal strings, byte arrays 0x hex.</summary>
        public static JsonNode? ToJson(Arc4Type type, object value) => value switch
        {
            byte[] bytes => bytes.ToHex(true),
            BigInteger integer => Integers.ToDecimalString(integer),
            bool flag => flag,
            string text => text,
            _ => new JsonArray(((List<object>)value).Select((item, index) => ToJson(ChildOf(type, index), item)).ToArray()),
        };

        /// <summary>UTF-8 text of a byte array, without the trailing zero bytes that pad fixed-size values such as arc200_name.</summary>
        public static string BytesToText(string bytesHex) => Encoding.UTF8.GetString(bytesHex.HexToByteArray()).TrimEnd('\0');

        private static BigInteger ToInteger(JsonNode? value, int bits, string name)
        {
            var integer = JsonValues.ParameterInteger(value, name);
            return integer < BigInteger.One << bits ? integer : throw Errors.InvalidArgument($"{name} must be below 2^{bits}.");
        }

        private static object ToArray(Arc4Type child, JsonNode? value, string name, int? length) => Arc4Arrays.IsBytes(child)
            ? ToBytes(value, name, length)
            : Items(value, name, length).Select(item => ToAbiValue(child, item, name)).ToList();

        private static byte[] ToBytes(JsonNode? value, string name, int? length)
        {
            var text = JsonValues.Text(value);
            if (text == null || !HexBytes.IsMatch(text)) throw Errors.InvalidArgument($"{name} must be 0x-prefixed hex bytes.");
            var bytes = text.HexToByteArray();
            return length == null || bytes.Length == length ? bytes : throw Errors.InvalidArgument($"{name} must be {length} bytes.");
        }

        private static List<JsonNode?> Items(JsonNode? value, string name, int? length)
        {
            if (value is not JsonArray items) throw Errors.InvalidArgument($"{name} must be an array.");
            return length == null || items.Count == length ? items.ToList() : throw Errors.InvalidArgument($"{name} must have {length} items.");
        }

        /// <summary>Strings, and addresses: 58-character Algorand addresses.</summary>
        private static string ToText(Arc4Type type, JsonNode? value, string name)
        {
            var text = JsonValues.Text(value) ?? throw Errors.InvalidArgument($"{name} must be a string.");
            return type is Arc4Address && !AlgorandAddress.IsValid(text) ? throw Errors.InvalidArgument($"{name} must be an Algorand address.") : text;
        }

        private static Arc4Type ChildOf(Arc4Type type, int index) => type switch
        {
            Arc4StaticArray array => array.Child,
            Arc4DynamicArray array => array.Child,
            _ => ((Arc4Tuple)type).Children[index],
        };
    }
}
