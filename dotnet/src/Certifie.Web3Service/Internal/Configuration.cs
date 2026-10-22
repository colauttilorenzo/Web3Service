using System;
using System.Collections.Generic;
using System.Linq;

namespace Certifie.Web3
{
    /// <summary>Applies the Ethereum defaults to the service configuration and validates it once, at construction.</summary>
    internal static class Configuration
    {
        private const string DefaultChainId = "0x1";
        private const string DefaultChainName = "Ethereum";
        private const string DefaultCurrencyName = "Ether";
        private const string DefaultCurrencySymbol = "ETH";
        private const int DefaultCurrencyDecimals = 18;
        private const int MaxCurrencyDecimals = 255;
        private static readonly string[] RpcSchemes = { "http", "https", "ws", "wss" };

        public static EthereumProvider ResolveProvider(EvmProvider? input)
        {
            if (input == null || string.IsNullOrEmpty(input.RpcURI)) throw Errors.InvalidArgument("An rpcURI is required.");
            var (name, symbol, decimals) = ResolveCurrency(input.NativeCurrency);
            foreach (var uri in Endpoints(input.RpcURI, input.RpcPublicURI)) ValidateEndpoint(uri);
            var chainId = Integers.NormalizeChainId(input.ChainId ?? DefaultChainId);
            return new EthereumProvider(chainId, input.RpcURI, input.RpcPublicURI, input.ChainName ?? DefaultChainName,
                name, symbol, decimals, input.BlockExplorerURI);
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

        private static (string Name, string Symbol, int Decimals) ResolveCurrency(NativeCurrency? currency)
        {
            var symbol = currency?.Symbol ?? DefaultCurrencySymbol;
            var decimals = currency?.Decimals ?? DefaultCurrencyDecimals;
            if (symbol.Length == 0) throw Errors.InvalidArgument("nativeCurrency.symbol must not be empty.");
            if (decimals < 0 || decimals > MaxCurrencyDecimals)
                throw Errors.InvalidArgument($"nativeCurrency.decimals must be an integer between 0 and {MaxCurrencyDecimals}.");
            return (currency?.Name ?? DefaultCurrencyName, symbol, decimals);
        }

        private static void ValidateEndpoint(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) throw Errors.InvalidArgument("RPC endpoints must be valid HTTP(S) or WS(S) URLs.");
            if (!RpcSchemes.Contains(parsed.Scheme)) throw Errors.InvalidArgument("RPC endpoints must use HTTP(S) or WS(S).");
        }

        private static void RequireTimeout(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
                throw Errors.InvalidArgument($"{name} must be positive and at most {int.MaxValue} ms.");
        }
    }
}
