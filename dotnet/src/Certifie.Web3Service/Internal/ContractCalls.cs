using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts;
using Nethereum.Contracts.Services;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3
{
    /// <summary>ABI lookups and conversion of contract outputs to JSON-safe values.</summary>
    internal static class ContractCalls
    {
        /// <summary>Looks a function up in the ABI and fails with <c>METHOD_NOT_FOUND</c> when it is missing.</summary>
        public static Function GetFunction(IEthApiContractService eth, string abi, string address, string method, string? missingMessage = null)
        {
            var contract = eth.GetContract(abi, address);
            if (!contract.ContractBuilder.ContractABI.Functions.Any(function => function.Name == method))
                throw new Web3Exception("METHOD_NOT_FOUND", missingMessage ?? $"Contract method {method} is missing from the ABI.");
            return contract.GetFunction(method);
        }

        /// <summary>A single output is returned as its value, several outputs as a JSON array.</summary>
        public static JsonNode? OutputsToJson(List<ParameterOutput> outputs) => outputs.Count == 1 ? ToJson(outputs[0]) : ToJson(outputs);

        /// <summary>Converts decoded values at any depth. Strings and bytes are matched before the generic collection case.</summary>
        private static JsonNode? ToJson(object? value) => value switch
        {
            null => null,
            string text => text,
            byte[] bytes => bytes.ToHex(true),
            ParameterOutput output => ToJson(output.Result),
            IEnumerable items => ToJsonArray(items.Cast<object?>()),
            _ => ScalarToJson(value),
        };

        /// <summary>Integers become decimal strings, so values beyond 2^53 survive JSON.</summary>
        private static JsonNode? ScalarToJson(object value) => value switch
        {
            BigInteger integer => Integers.ToDecimalString(integer),
            bool flag => flag,
            IConvertible number when IsInteger(number) => Convert.ToDecimal(number, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        private static JsonArray ToJsonArray(IEnumerable<object?> values) => new JsonArray(values.Select(ToJson).ToArray());

        private static bool IsInteger(IConvertible value) => value.GetTypeCode() is >= TypeCode.SByte and <= TypeCode.UInt64;
    }
}
