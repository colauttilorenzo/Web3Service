using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Util;

namespace Certifie.Web3
{
    /// <summary>A function of the ABI: its public description, its 4-byte selector and its ABI entry.</summary>
    internal sealed class AbiFunction
    {
        public AbiFunction(ContractMethod method, string selector, JsonObject fragment)
        {
            Method = method;
            Selector = selector;
            Fragment = fragment;
        }

        public ContractMethod Method { get; }
        public string Selector { get; }
        public JsonObject Fragment { get; }
    }

    /// <summary>Describes the functions of an ABI, and turns a JSON object of named parameters into validated arguments in ABI order.</summary>
    internal static class AbiMethods
    {
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            ["pure"] = "read", ["view"] = "read", ["nonpayable"] = "write", ["payable"] = "payable",
        };
        private static readonly Regex ArraySuffix = new Regex(@"\[\d*\]$");
        private static readonly Regex IntegerType = new Regex(@"^u?int\d*$");
        private static readonly Regex IntegerArgument = new Regex("^-?(?:0x[0-9a-f]+|[0-9]+)$", RegexOptions.IgnoreCase);
        private static readonly Regex Address = new Regex("^0x[0-9a-f]{40}$", RegexOptions.IgnoreCase);
        private static readonly Regex HexBytes = new Regex("^0x(?:[0-9a-f]{2})*$", RegexOptions.IgnoreCase);
        private static readonly JsonSerializerOptions ParameterOptions = new JsonSerializerOptions { Converters = { new BigIntegerConverter() } };

        public static List<AbiFunction> Describe(string abi)
        {
            var fragments = JsonNode.Parse(abi) as JsonArray ?? throw Errors.InvalidArgument("The ABI must be a JSON array.");
            return fragments.OfType<JsonObject>().Where(fragment => (Text(fragment["type"]) ?? "function") == "function").Select(ToFunction).ToList();
        }

        /// <summary>Finds a function by name or signature; a name shared by several functions is resolved by the parameter names.</summary>
        public static AbiFunction Find(IReadOnlyList<AbiFunction> functions, string method, JsonObject parameters)
        {
            var candidates = functions.Where(fn => fn.Method.Signature == method || fn.Method.Name == method).ToList();
            if (candidates.Count == 0) throw Errors.MethodNotFound(method);
            var matching = candidates.Count == 1 ? candidates : candidates.Where(fn => HasParameters(fn, parameters)).ToList();
            if (matching.Count == 1) return matching[0];
            throw Errors.InvalidArgument($"Several functions are named {method}; call one of {Signatures(candidates)}.");
        }

        /// <summary>True when the parameter names are exactly those of the function.</summary>
        public static bool HasParameters(AbiFunction fn, JsonObject parameters)
        {
            var names = fn.Method.Parameters.Keys.ToList();
            var given = parameters.Select(item => item.Key).ToList();
            return names.Count == given.Count && given.All(names.Contains);
        }

        public static string Signatures(IEnumerable<AbiFunction> functions) => string.Join(", ", functions.Select(fn => fn.Method.Signature));

        /// <summary>The arguments in ABI order. Unknown or missing parameters and values of the wrong type fail with <c>INVALID_ARGUMENT</c>.</summary>
        public static object[] ToArguments(AbiFunction fn, JsonObject parameters)
        {
            var unknown = parameters.Select(item => item.Key).FirstOrDefault(name => !fn.Method.Parameters.ContainsKey(name));
            if (unknown != null) throw Errors.InvalidArgument($"{fn.Method.Signature} has no parameter {unknown}.");
            return InputsOf(fn.Fragment).Select((input, index) => Convert(input!, ValueOf(parameters, ParameterName(input!, index)))).ToArray();
        }

        /// <summary>The name of a parameter, or arg0, arg1, … (result0, result1, … for outputs) when the ABI leaves it unnamed.</summary>
        public static string ParameterName(JsonNode parameter, int index, string prefix = "arg")
        {
            var name = Text(parameter["name"]);
            return string.IsNullOrEmpty(name) ? $"{prefix}{index}" : name!;
        }

        public static JsonArray InputsOf(JsonObject fragment) => fragment["inputs"] as JsonArray ?? new JsonArray();

        /// <summary>Named parameters as a JSON object: a <see cref="JsonObject"/>, a dictionary or an anonymous object such as <c>new { to, amount = "1000" }</c>.</summary>
        public static JsonObject ToParameters(object? parameters) => parameters switch
        {
            null => new JsonObject(),
            JsonObject json => json,
            _ => JsonSerializer.SerializeToNode(parameters, ParameterOptions) as JsonObject
                ?? throw Errors.InvalidArgument("The parameters must be an object with one property per parameter."),
        };

        private static AbiFunction ToFunction(JsonObject fragment)
        {
            var signature = $"{Text(fragment["name"])}({string.Join(",", InputsOf(fragment).Select(input => CanonicalType(input!)))})";
            var selector = "0x" + Sha3Keccack.Current.CalculateHash(signature).Substring(0, 8);
            return new AbiFunction(DescribeMethod(fragment, signature), selector, fragment);
        }

        private static ContractMethod DescribeMethod(JsonObject fragment, string signature)
        {
            var stateMutability = StateMutabilityOf(fragment);
            return new ContractMethod
            {
                Name = Text(fragment["name"]) ?? "", Signature = signature, Kind = Kinds[stateMutability], StateMutability = stateMutability,
                Parameters = NamedTypes(InputsOf(fragment), "arg", parameter => JsonType(Text(parameter["type"])!)),
                Inputs = NamedTypes(InputsOf(fragment), "arg", CanonicalType),
                Outputs = NamedTypes(fragment["outputs"] as JsonArray ?? new JsonArray(), "result", CanonicalType),
            };
        }

        private static string StateMutabilityOf(JsonObject fragment)
        {
            var stateMutability = Text(fragment["stateMutability"]);
            if (!string.IsNullOrEmpty(stateMutability)) return stateMutability!;
            if (Flag(fragment["constant"])) return "view";
            return Flag(fragment["payable"]) ? "payable" : "nonpayable";
        }

        private static Dictionary<string, string> NamedTypes(JsonArray parameters, string prefix, Func<JsonNode, string> typeOf) =>
            parameters.Select((parameter, index) => (Name: ParameterName(parameter!, index, prefix), Type: typeOf(parameter!)))
                .ToDictionary(item => item.Name, item => item.Type);

        /// <summary>The type as signatures spell it: a tuple becomes <c>(type1,type2)</c>, followed by its array suffix.</summary>
        private static string CanonicalType(JsonNode parameter)
        {
            var type = Text(parameter["type"])!;
            if (!type.StartsWith("tuple", StringComparison.Ordinal)) return type;
            var components = parameter["components"] as JsonArray ?? new JsonArray();
            return $"({string.Join(",", components.Select(component => CanonicalType(component!)))}){type.Substring("tuple".Length)}";
        }

        /// <summary>The JSON type a caller passes: number, boolean, string, array or object.</summary>
        private static string JsonType(string type)
        {
            if (ArraySuffix.IsMatch(type)) return "array";
            if (type == "tuple") return "object";
            if (type == "bool") return "boolean";
            return IntegerType.IsMatch(type) ? "number" : "string";
        }

        private static object Convert(JsonNode parameter, JsonNode? value, string? name = null)
        {
            var (type, label) = (Text(parameter["type"])!, name ?? Text(parameter["name"]) ?? "value");
            if (ArraySuffix.IsMatch(type)) return ConvertArray(parameter, type, value, label);
            if (type == "tuple") return ConvertTuple(parameter, value, label);
            if (IntegerType.IsMatch(type)) return ConvertInteger(type, value, label);
            if (type != "bool") return ConvertText(type, value, label);
            return value is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? flag.GetValue<bool>() : throw Errors.InvalidArgument($"{label} must be true or false.");
        }

        private static List<object> ConvertArray(JsonNode parameter, string type, JsonNode? value, string name)
        {
            if (value is not JsonArray items) throw Errors.InvalidArgument($"{name} must be an array.");
            var element = parameter.DeepClone().AsObject();
            element["type"] = ArraySuffix.Replace(type, "");
            return items.Select(item => Convert(element, item, name)).ToList();
        }

        /// <summary>Tuples are passed as objects with one property per field.</summary>
        private static object[] ConvertTuple(JsonNode parameter, JsonNode? value, string name)
        {
            if (value is not JsonObject fields) throw Errors.InvalidArgument($"{name} must be an object.");
            return (parameter["components"] as JsonArray ?? new JsonArray()).Select((component, index) =>
            {
                var field = ParameterName(component!, index);
                return Convert(component!, ValueOf(fields, field, $"{name}.{field}"), $"{name}.{field}");
            }).ToArray();
        }

        /// <summary>Negative values only for signed types.</summary>
        private static BigInteger ConvertInteger(string type, JsonNode? value, string name)
        {
            var text = value is JsonValue number ? IntegerText(number) : null;
            if (text == null || !IntegerArgument.IsMatch(text)) throw Errors.InvalidArgument($"{name} must be an integer; use a string for large values.");
            if (!text.StartsWith("-", StringComparison.Ordinal)) return Integers.Parse(text, name);
            if (type.StartsWith("u", StringComparison.Ordinal)) throw Errors.InvalidArgument($"{name} must not be negative.");
            return -Integers.Parse(text.Substring(1), name);
        }

        private static string? IntegerText(JsonValue value) => value.GetValueKind() switch
        {
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.String => value.GetValue<string>(),
            _ => null,
        };

        /// <summary>Strings, addresses and bytes. Addresses are lowercased, so any checksum casing is accepted.</summary>
        private static object ConvertText(string type, JsonNode? value, string name)
        {
            var text = value is JsonValue item && item.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : throw Errors.InvalidArgument($"{name} must be a string.");
            if (type == "address" && !Address.IsMatch(text)) throw Errors.InvalidArgument($"{name} must be an address: 0x followed by 40 hex digits.");
            if (type.StartsWith("bytes", StringComparison.Ordinal) && !HexBytes.IsMatch(text)) throw Errors.InvalidArgument($"{name} must be 0x-prefixed hex bytes.");
            if (type.StartsWith("bytes", StringComparison.Ordinal)) return text.HexToByteArray();
            return type == "address" ? text.ToLowerInvariant() : text;
        }

        private static JsonNode? ValueOf(JsonObject values, string name, string? label = null)
        {
            if (!values.TryGetPropertyValue(name, out var value) || value == null) throw Errors.InvalidArgument($"The parameter {label ?? name} is required.");
            return value;
        }

        private static string? Text(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

        private static bool Flag(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

        /// <summary>Writes <see cref="BigInteger"/> parameters as decimal strings.</summary>
        private sealed class BigIntegerConverter : JsonConverter<BigInteger>
        {
            public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                Integers.Parse(reader.GetString(), "value");

            public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) =>
                writer.WriteStringValue(Integers.ToDecimalString(value));
        }
    }
}
