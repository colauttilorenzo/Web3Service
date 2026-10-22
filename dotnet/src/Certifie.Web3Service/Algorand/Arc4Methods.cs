using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>An argument of an ARC-4 method: an ABI value, a reference to an account, asset or application, or a transaction such as pay.</summary>
    internal sealed class Arc4Argument
    {
        private static readonly string[] References = { "account", "asset", "application" };
        private static readonly string[] TransactionTypes = { "txn", "pay", "keyreg", "acfg", "axfer", "afrz", "appl" };

        public Arc4Argument(string name, string type)
        {
            Name = name;
            Type = References.Contains(type) || TransactionTypes.Contains(type) ? null : Arc4Type.Parse(type);
            TypeName = Type?.ToString() ?? type;
        }

        public string Name { get; }
        /// <summary>The canonical type, as the method signature spells it.</summary>
        public string TypeName { get; }
        /// <summary>The ABI type of a value argument; null for references and transactions.</summary>
        public Arc4Type? Type { get; }
        public bool IsReference => References.Contains(TypeName);
        public bool IsTransaction => TransactionTypes.Contains(TypeName);
    }

    /// <summary>A method argument ready to encode: a value, a reference (<c>account</c>, <c>asset</c>, <c>application</c>) or a <c>payment</c>.</summary>
    internal sealed class CallArgument
    {
        public CallArgument(string kind, Arc4Type? type = null, object? value = null) => (Kind, Type, Value) = (kind, type, value);

        public string Kind { get; }
        public Arc4Type? Type { get; }
        public object? Value { get; }
    }

    /// <summary>A method of the contract: its public description, arguments, return type, 4-byte selector, ARC-4 entry and the contract's error messages.</summary>
    internal sealed class Arc4Function : IDescribedMethod
    {
        public Arc4Function(JsonObject spec, IReadOnlyDictionary<int, string> errors)
        {
            Spec = spec;
            Errors = errors;
            Args = JsonValues.Items(spec["args"]).Select((arg, index) => new Arc4Argument(ArgumentName(arg, index), JsonValues.Text(arg["type"]) ?? "")).ToList();
            var returns = JsonValues.Text(spec["returns"]?["type"]) ?? "void";
            Returns = returns == "void" ? null : Arc4Type.Parse(returns);
            Method = Arc4Methods.DescribeMethod(this, JsonValues.Text(spec["name"]) ?? "");
            Selector = AvmCrypto.Sha512_256(Encoding.UTF8.GetBytes(Method.Signature)).Take(4).ToArray();
        }

        public ContractMethod Method { get; }
        public IReadOnlyList<Arc4Argument> Args { get; }
        /// <summary>Null for void methods.</summary>
        public Arc4Type? Returns { get; }
        public byte[] Selector { get; }
        public JsonObject Spec { get; }
        public IReadOnlyDictionary<int, string> Errors { get; }

        private static string ArgumentName(JsonNode arg, int index) => JsonValues.Text(arg["name"]) is { Length: > 0 } name ? name : $"arg{index}";
    }

    /// <summary>Describes the methods of an ARC-4 contract, and turns a JSON object of named parameters into ARC-4 arguments in method order.</summary>
    internal static class Arc4Methods
    {
        /// <summary>ARC-4 passes at most 15 arguments; from the 15th on they are packed into one tuple.</summary>
        private const int MaxAppArguments = 15;
        private static readonly byte[] ReturnPrefix = { 0x15, 0x1f, 0x7c, 0x75 };

        /// <summary>The contract description; ARC-4 and ARC-56 list the methods at the top level, ARC-32 under contract.</summary>
        public static (JsonObject Contract, JsonArray Methods) RequireArc4(string abi)
        {
            var contract = ContractMethods.ParseAbi(abi) as JsonObject
                ?? throw Errors.InvalidArgument("An Algorand contract must be described by ARC-4, ARC-32 or ARC-56 JSON, not by an EVM ABI.");
            var methods = contract["methods"] as JsonArray ?? contract["contract"]?["methods"] as JsonArray;
            return (contract, methods ?? throw Errors.InvalidArgument("The contract description has no methods."));
        }

        public static List<Arc4Function> Describe(string abi)
        {
            var (contract, methods) = RequireArc4(abi);
            var errors = ProgramErrors.Of(contract);
            return methods.OfType<JsonObject>().Select(spec => new Arc4Function(spec, errors)).ToList();
        }

        /// <summary>The arguments in method order. Unknown or missing parameters and values of the wrong type fail with <c>INVALID_ARGUMENT</c>.</summary>
        public static List<CallArgument> ToArguments(Arc4Function fn, JsonObject parameters)
        {
            ContractMethods.RejectUnknown(fn, parameters);
            return fn.Args.Select(arg => ToArgument(arg, parameters)).ToList();
        }

        /// <summary>The application arguments: the selector, then each value; from the 15th on the values are packed into one tuple.</summary>
        public static List<byte[]> Encode(Arc4Function fn, IReadOnlyList<(Arc4Type Type, object Value)> values)
        {
            var single = values.Count <= MaxAppArguments ? values : values.Take(MaxAppArguments - 1).ToList();
            var encoded = new List<byte[]> { fn.Selector };
            encoded.AddRange(single.Select(item => item.Type.Encode(item.Value)));
            if (single.Count < values.Count) encoded.Add(Pack(values.Skip(MaxAppArguments - 1).ToList()));
            return encoded;
        }

        /// <summary>Application arguments concatenated as 0x hex: the method selector followed by the encoded arguments.</summary>
        public static string ToHex(IEnumerable<byte[]> appArgs) => appArgs.SelectMany(arg => arg).ToArray().ToHex(true);

        /// <summary>The return value, read from the last log that starts with the ARC-4 return prefix; null for void methods.</summary>
        public static JsonNode? ReturnValue(Arc4Function fn, IReadOnlyList<byte[]> logs)
        {
            if (fn.Returns == null) return null;
            var log = logs.LastOrDefault(entry => entry.Take(ReturnPrefix.Length).SequenceEqual(ReturnPrefix))
                ?? throw Errors.NotSupported($"{fn.Method.Signature} did not log a return value.");
            return Arc4Values.ToJson(fn.Returns, fn.Returns.Decode(Arc4Tuple.Slice(log, ReturnPrefix.Length, log.Length - ReturnPrefix.Length)));
        }

        /// <summary>The method a transaction called, with its arguments; null when no method of the contract has its selector.</summary>
        public static DecodedCall? DecodeCall(IReadOnlyList<Arc4Function> functions, IReadOnlyList<byte[]> appArgs)
        {
            var fn = appArgs.Count == 0 ? null : functions.FirstOrDefault(candidate => candidate.Selector.SequenceEqual(appArgs[0]));
            if (fn == null) return null;
            var types = ValueTypes(fn);
            var values = Unpack(types, appArgs.Skip(1).ToList());
            var parameters = new JsonObject(types.Select((item, index) =>
                new KeyValuePair<string, JsonNode?>(item.Name, Arc4Values.ToJson(item.Type, item.Type.Decode(values[index])))));
            return new DecodedCall { Name = fn.Method.Name, Signature = fn.Method.Signature, Parameters = parameters };
        }

        /// <summary>Read-only methods (ARC-22) are reads; a method that takes a payment is payable.</summary>
        internal static ContractMethod DescribeMethod(Arc4Function fn, string name)
        {
            var kind = JsonValues.Flag(fn.Spec["readonly"]) ? "read" : fn.Args.Any(arg => arg.TypeName == "pay") ? "payable" : "write";
            var returns = fn.Returns?.ToString() ?? "void";
            return new ContractMethod
            {
                Name = name, Signature = $"{name}({string.Join(",", fn.Args.Select(arg => arg.TypeName))}){returns}", Kind = kind,
                StateMutability = kind == "read" ? "view" : kind == "write" ? "nonpayable" : "payable",
                Parameters = fn.Args.Where(arg => !arg.IsTransaction).ToDictionary(arg => arg.Name, JsonType),
                Inputs = fn.Args.ToDictionary(arg => arg.Name, arg => arg.TypeName),
                Outputs = fn.Returns == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["result0"] = returns },
            };
        }

        private static string JsonType(Arc4Argument arg) => arg.Type != null ? Arc4Values.JsonType(arg.Type) : arg.TypeName == "account" ? "string" : "number";

        private static CallArgument ToArgument(Arc4Argument arg, JsonObject parameters)
        {
            if (arg.TypeName == "pay") return new CallArgument("payment");
            if (arg.IsTransaction) throw Errors.NotSupported($"Methods that take a {arg.TypeName} transaction cannot be called; only pay is supported.");
            var value = ContractMethods.ValueOf(parameters, arg.Name);
            if (arg.TypeName == "account") return new CallArgument("account", value: Arc4Values.ToAbiValue(new Arc4Address(), value, arg.Name));
            if (arg.IsReference) return new CallArgument(arg.TypeName, value: JsonValues.ParameterInteger(value, arg.Name));
            return new CallArgument("value", arg.Type, Arc4Values.ToAbiValue(arg.Type!, value, arg.Name));
        }

        /// <summary>The encoded types of the application arguments after the selector: references are uint8 indexes, transactions are not passed.</summary>
        private static List<(string Name, Arc4Type Type)> ValueTypes(Arc4Function fn) =>
            fn.Args.Where(arg => !arg.IsTransaction).Select(arg => (arg.Name, arg.Type ?? new Arc4Uint(8, "uint8"))).ToList();

        private static byte[] Pack(IReadOnlyList<(Arc4Type Type, object Value)> values) =>
            new Arc4Tuple(values.Select(item => item.Type).ToList()).Encode(values.Select(item => item.Value).ToList());

        /// <summary>The encoded value of each argument; from the 15th on they are fields of the last application argument.</summary>
        private static List<byte[]> Unpack(List<(string Name, Arc4Type Type)> types, List<byte[]> appArgs)
        {
            if (types.Count <= MaxAppArguments) return appArgs;
            var packed = new Arc4Tuple(types.Skip(MaxAppArguments - 1).Select(item => item.Type).ToList());
            var rest = ((List<object>)packed.Decode(appArgs[MaxAppArguments - 1])).Select((value, index) => packed.Children[index].Encode(value));
            return appArgs.Take(MaxAppArguments - 1).Concat(rest).ToList();
        }
    }
}
