using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Nethereum.ABI.ABIDeserialisation;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.Model;
using Nethereum.Contracts;
using Nethereum.Contracts.Services;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Evm
{
    /// <summary>ABI lookups and conversion of contract outputs to JSON-safe values.</summary>
    internal static class ContractCalls
    {
        /// <summary>Looks a function up in the ABI by name or signature, and fails with <c>METHOD_NOT_FOUND</c> when it is missing.</summary>
        public static Function GetFunction(IEthApiContractService eth, string abi, string address, string method, string? missingMessage = null)
        {
            var contract = eth.GetContract(abi, address);
            var function = contract.ContractBuilder.ContractABI.Functions.FirstOrDefault(candidate => candidate.Name == method || candidate.Signature == method)
                ?? throw (missingMessage == null ? Errors.MethodNotFound(method) : new Web3Exception("METHOD_NOT_FOUND", missingMessage));
            return contract.GetFunctionBySignature(function.Sha3Signature);
        }

        /// <summary>The call data of a function call: its selector followed by the encoded arguments.</summary>
        public static string Encode(AbiFunction fn, object[] args)
        {
            var function = FunctionAbiOf(fn);
            return new FunctionCallEncoder().EncodeRequest(function.Sha3Signature, function.InputParameters, args);
        }

        /// <summary>The function a transaction input calls, with its arguments; null when no function of the ABI has its selector.</summary>
        public static DecodedCall? DecodeCall(IReadOnlyList<AbiFunction> functions, string input)
        {
            var fn = functions.FirstOrDefault(candidate => input.StartsWith(candidate.Selector, StringComparison.OrdinalIgnoreCase));
            if (fn == null) return null;
            var function = FunctionAbiOf(fn);
            var values = new FunctionCallDecoder().DecodeFunctionInput(function.Sha3Signature, input, function.InputParameters);
            var parameters = new JsonObject(AbiMethods.InputsOf(fn.Fragment).Select((parameter, index) =>
                new KeyValuePair<string, JsonNode?>(AbiMethods.ParameterName(parameter!, index), ToJson(values[index]))));
            return new DecodedCall { Name = fn.Method.Name, Signature = fn.Method.Signature, Parameters = parameters };
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

        /// <summary>Nethereum's description of the function; old ABIs may omit the entry type.</summary>
        private static FunctionABI FunctionAbiOf(AbiFunction fn)
        {
            var fragment = fn.Fragment.DeepClone().AsObject();
            fragment["type"] = "function";
            return new ABIJsonDeserialiser().DeserialiseContract(new JsonArray(fragment).ToJsonString()).Functions[0];
        }

        private static JsonArray ToJsonArray(IEnumerable<object?> values) => new JsonArray(values.Select(ToJson).ToArray());

        private static bool IsInteger(IConvertible value) => value.GetTypeCode() is >= TypeCode.SByte and <= TypeCode.UInt64;
    }
}
