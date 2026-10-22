using System;
using System.Collections.Generic;
using System.Linq;

namespace Certifie.Web3
{
    /// <summary>Chain ID, chain name and native currency used when the provider omits them.</summary>
    internal sealed class NetworkDefaults
    {
        public NetworkDefaults(string chainId, string chainName, (string Name, string Symbol, int Decimals) currency)
        {
            ChainId = chainId;
            ChainName = chainName;
            Currency = currency;
        }

        public string ChainId { get; }
        public string ChainName { get; }
        public (string Name, string Symbol, int Decimals) Currency { get; }
    }

    /// <summary>Applies the defaults of each blockchain to the service configuration and validates it once, at construction.</summary>
    internal static class Configuration
    {
        private const int MaxCurrencyDecimals = 255;
        private static readonly NetworkDefaults EvmDefaults = new NetworkDefaults("0x1", "Ethereum", ("Ether", "ETH", 18));
        private static readonly string[] EvmSchemes = { "http", "https", "ws", "wss" };
        private static readonly string[] HttpSchemes = { "http", "https" };

        public static EthereumProvider ResolveProvider(IEvmProvider? input)
        {
            var (chainName, currency) = ResolveNetwork(input, EvmDefaults, EvmSchemes);
            return new EthereumProvider(input!, Integers.NormalizeChainId(input!.ChainId ?? EvmDefaults.ChainId), chainName, currency);
        }

        /// <summary>Validates the endpoints and the native currency, and returns the chain name and currency with the defaults applied. HTTP(S) only, unless schemes says otherwise.</summary>
        public static (string ChainName, (string Name, string Symbol, int Decimals) Currency) ResolveNetwork(IBlockchainProvider? input, NetworkDefaults defaults,
            string[]? schemes = null)
        {
            if (input == null || string.IsNullOrEmpty(input.RpcURI)) throw Errors.InvalidArgument("An rpcURI is required.");
            var currency = ResolveCurrency(input.NativeCurrency, defaults.Currency);
            foreach (var uri in Endpoints(input.RpcURI, input.RpcPublicURI)) ValidateEndpoint(uri, schemes ?? HttpSchemes);
            return (input.ChainName ?? defaults.ChainName, currency);
        }

        /// <summary>A network name or genesis ID: the configured one, or the default.</summary>
        public static string RequireChainName(string? chainId, string fallback)
        {
            var value = chainId ?? fallback;
            if (value.Trim().Length == 0) throw Errors.InvalidArgument("chainId must be a non-empty string.");
            return value;
        }

        /// <summary>A validated copy, so later changes to the caller's object cannot bypass validation.</summary>
        public static Web3ServiceOptions ResolveOptions(Web3ServiceOptions? options)
        {
            var resolved = options?.Copy() ?? new Web3ServiceOptions();
            RequireTimeout(resolved.Timeout, nameof(resolved.Timeout));
            RequireTimeout(resolved.TransactionTimeout, nameof(resolved.TransactionTimeout));
            RequireTimeout(resolved.PollingInterval, nameof(resolved.PollingInterval));
            if (resolved.GasBuffer < 0) throw Errors.InvalidArgument("GasBuffer must be a non-negative integer.");
            return resolved;
        }

        /// <summary>The configured RPC endpoints without duplicates: the primary first, then the public fallback.</summary>
        public static IReadOnlyList<string> Endpoints(string rpcURI, string? rpcPublicURI) =>
            new[] { rpcURI, rpcPublicURI }.OfType<string>().Where(uri => uri.Length > 0).Distinct().ToList();

        private static (string Name, string Symbol, int Decimals) ResolveCurrency(NativeCurrency? currency, (string Name, string Symbol, int Decimals) defaults)
        {
            var symbol = currency?.Symbol ?? defaults.Symbol;
            var decimals = currency?.Decimals ?? defaults.Decimals;
            if (symbol.Length == 0) throw Errors.InvalidArgument("nativeCurrency.symbol must not be empty.");
            if (decimals < 0 || decimals > MaxCurrencyDecimals)
                throw Errors.InvalidArgument($"nativeCurrency.decimals must be an integer between 0 and {MaxCurrencyDecimals}.");
            return (currency?.Name ?? defaults.Name, symbol, decimals);
        }

        private static void ValidateEndpoint(string uri, string[] schemes)
        {
            var allowed = schemes.Contains("ws") ? "HTTP(S) or WS(S)" : "HTTP(S)";
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) throw Errors.InvalidArgument($"RPC endpoints must be valid {allowed} URLs.");
            if (!schemes.Contains(parsed.Scheme)) throw Errors.InvalidArgument($"RPC endpoints must use {allowed}.");
        }

        private static void RequireTimeout(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
                throw Errors.InvalidArgument($"{name} must be positive and at most {int.MaxValue} ms.");
        }
    }
}
