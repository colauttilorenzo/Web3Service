using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Certifie.Web3
{
    /// <summary>A contract method with its public description: an EVM ABI function or an ARC-4 method.</summary>
    internal interface IDescribedMethod
    {
        ContractMethod Method { get; }
    }

    /// <summary>Finds a contract method by name or signature and reads its named parameters, whatever the blockchain.</summary>
    internal static class ContractMethods
    {
        private static readonly JsonSerializerOptions ParameterOptions = new JsonSerializerOptions { Converters = { new BigIntegerConverter() } };

        /// <summary>An EVM ABI (a JSON array) or an Algorand contract description (a JSON object).</summary>
        public static JsonNode? ParseAbi(string abi)
        {
            try { return JsonNode.Parse(abi); }
            catch (JsonException) { throw Errors.InvalidArgument("The ABI must be valid JSON."); }
        }

        /// <summary>Finds a method by name or signature; a name shared by several methods is resolved by the parameter names.</summary>
        public static T Find<T>(IReadOnlyList<T> methods, string method, JsonObject parameters) where T : IDescribedMethod
        {
            var candidates = methods.Where(fn => fn.Method.Signature == method || fn.Method.Name == method).ToList();
            if (candidates.Count == 0) throw Errors.MethodNotFound(method);
            var matching = candidates.Count == 1 ? candidates : candidates.Where(fn => HasParameters(fn, parameters)).ToList();
            if (matching.Count == 1) return matching[0];
            throw Errors.InvalidArgument($"Several functions are named {method}; call one of {Signatures(candidates.Cast<IDescribedMethod>())}.");
        }

        /// <summary>True when the parameter names are exactly those of the method.</summary>
        public static bool HasParameters(IDescribedMethod fn, JsonObject parameters)
        {
            var names = fn.Method.Parameters.Keys.ToList();
            var given = parameters.Select(item => item.Key).ToList();
            return names.Count == given.Count && given.All(names.Contains);
        }

        public static string Signatures(IEnumerable<IDescribedMethod> methods) => string.Join(", ", methods.Select(fn => fn.Method.Signature));

        /// <summary>Fails when a parameter is not one of the method's.</summary>
        public static void RejectUnknown(IDescribedMethod fn, JsonObject parameters)
        {
            var unknown = parameters.Select(item => item.Key).FirstOrDefault(name => !fn.Method.Parameters.ContainsKey(name));
            if (unknown != null) throw Errors.InvalidArgument($"{fn.Method.Signature} has no parameter {unknown}.");
        }

        /// <summary>The value of a required parameter.</summary>
        public static JsonNode ValueOf(JsonObject values, string name, string? label = null)
        {
            if (!values.TryGetPropertyValue(name, out var value) || value == null) throw Errors.InvalidArgument($"The parameter {label ?? name} is required.");
            return value;
        }

        /// <summary>Positional arguments, as CallContractMethodAsync and DeployAsync receive them, mapped to the method's parameter names.</summary>
        public static JsonObject FromPositional(IDescribedMethod fn, object?[] values)
        {
            var names = fn.Method.Parameters.Keys.ToList();
            if (values.Length != names.Count) throw Errors.InvalidArgument($"{fn.Method.Signature} expects {names.Count} arguments.");
            return new JsonObject(names.Select((name, index) => new KeyValuePair<string, JsonNode?>(name, ToJson(values[index]))));
        }

        /// <summary>Named parameters as a JSON object: a <see cref="JsonObject"/>, a dictionary or an anonymous object such as <c>new { to, amount = "1000" }</c>.</summary>
        public static JsonObject ToParameters(object? parameters) => parameters switch
        {
            null => new JsonObject(),
            JsonObject json => json,
            _ => JsonSerializer.SerializeToNode(parameters, ParameterOptions) as JsonObject
                ?? throw Errors.InvalidArgument("The parameters must be an object with one property per parameter."),
        };

        private static JsonNode? ToJson(object? value) => value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value, ParameterOptions);

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
