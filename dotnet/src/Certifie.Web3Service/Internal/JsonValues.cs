using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Certifie.Web3
{
    /// <summary>Reads the JSON of REST APIs (algod, indexer, Esplora) and of caller parameters: integers of any size, text and base64 bytes.</summary>
    internal static class JsonValues
    {
        /// <summary>An integer field; missing fields are zero. Integers are read from their text, so 64-bit values keep every digit.</summary>
        public static BigInteger Integer(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? BigInteger.Parse(value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture)
            : BigInteger.Zero;

        public static BigInteger? OptionalInteger(JsonNode? node) => node == null ? (BigInteger?)null : Integer(node);

        public static string? Text(JsonNode? node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

        public static bool Flag(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

        public static byte[] Base64(JsonNode? node) => Text(node) is string text ? Convert.FromBase64String(text) : Array.Empty<byte>();

        public static List<JsonNode> Items(JsonNode? node) => (node as JsonArray)?.OfType<JsonNode>().ToList() ?? new List<JsonNode>();

        /// <summary>A non-negative integer parameter: a JSON number, or a decimal or 0x hex string for large values.</summary>
        public static BigInteger ParameterInteger(JsonNode? node, string name) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? Integers.Parse(value.ToJsonString(), name)
            : Integers.Parse(Text(node), name);
    }
}
