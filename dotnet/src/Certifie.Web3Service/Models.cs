using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Certifie.Web3
{
    /// <summary>Gas token of the network. Decimals are informational: fees are always returned in the smallest unit.</summary>
    public sealed class NativeCurrency
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("decimals")] public int? Decimals { get; set; }
    }

    /// <summary>Initialization input. Only <see cref="RpcURI"/> is required; omitted fields use Ethereum defaults.</summary>
    public sealed class EvmProvider
    {
        /// <summary>Decimal (<c>"1337"</c>) or hex (<c>"0x539"</c>) chain ID. Default: <c>0x1</c>.</summary>
        [JsonPropertyName("chainId")] public string? ChainId { get; set; }
        /// <summary>HTTP(S) or WS(S) node URI.</summary>
        [JsonPropertyName("rpcURI")] public string RpcURI { get; set; } = "";
        /// <summary>Fallback node, used when the primary is unavailable or on the wrong chain.</summary>
        [JsonPropertyName("rpcPublicURI")] public string? RpcPublicURI { get; set; }
        [JsonPropertyName("nativeCurrency")] public NativeCurrency? NativeCurrency { get; set; }
        [JsonPropertyName("chainName")] public string? ChainName { get; set; }
        [JsonPropertyName("blockExplorerURI")] public string? BlockExplorerURI { get; set; }
    }

    /// <summary>Configuration with defaults applied, as exposed by <see cref="Web3Service.Provider"/>.</summary>
    public sealed class EthereumProvider
    {
        internal EthereumProvider(string chainId, string rpcURI, string? rpcPublicURI, string chainName, string currencyName, string symbol, int decimals, string? blockExplorerURI)
        {
            ChainId = chainId; RpcURI = rpcURI; RpcPublicURI = rpcPublicURI; ChainName = chainName;
            CurrencyName = currencyName; Symbol = symbol; Decimals = decimals; BlockExplorerURI = blockExplorerURI;
        }

        public string ChainId { get; }
        public string RpcURI { get; }
        public string? RpcPublicURI { get; }
        public string ChainName { get; }
        public string CurrencyName { get; }
        public string Symbol { get; }
        public int Decimals { get; }
        public string? BlockExplorerURI { get; }
    }

    /// <summary>Signing credentials and timeouts. Decrypting and storing secrets belongs to the application.</summary>
    public sealed class Web3ServiceOptions
    {
        /// <summary>Hex private key. Use either this or <see cref="Mnemonic"/>.</summary>
        public string? PrivateKey { get; set; }
        /// <summary>BIP-39 phrase; the key is derived at <c>m/44'/60'/0'/0/{WalletIndex}</c>.</summary>
        public string? Mnemonic { get; set; }
        public string MnemonicPassword { get; set; } = "";
        /// <summary>Default: 1, as in the original CERTIFIE service.</summary>
        public int WalletIndex { get; set; } = 1;
        /// <summary>Timeout for each RPC request, including provider selection. Default: 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>Time to wait for mining after broadcast. Default: 120 seconds.</summary>
        public TimeSpan TransactionTimeout { get; set; } = TimeSpan.FromSeconds(120);
        /// <summary>Receipt polling interval. Default: 1 second.</summary>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>Added to the estimated gas limit. Default: 10000.</summary>
        public BigInteger GasBuffer { get; set; } = 10_000;

        internal Web3ServiceOptions Copy() => (Web3ServiceOptions)MemberwiseClone();
    }

    public sealed class TransactionOptions
    {
        public BigInteger? Gas { get; set; }
        public BigInteger? GasPrice { get; set; }
        public BigInteger? Nonce { get; set; }
        public BigInteger? Value { get; set; }
    }

    public sealed class NotarizationHash
    {
        [JsonPropertyName("data")] public string Data { get; set; } = "";
        [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "";
    }

    public sealed class NotarizationDocument
    {
        [JsonPropertyName("hash")] public NotarizationHash Hash { get; set; } = new NotarizationHash();
        [JsonPropertyName("metadata")] public string? Metadata { get; set; }
    }

    public sealed class WalletInfo
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("publicKey")] public string PublicKey { get; set; } = "";
        [JsonPropertyName("privateKey")] public string PrivateKey { get; set; } = "";
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("index")] public string Index { get; set; } = "";
    }

    public sealed class CheckResult
    {
        [JsonPropertyName("blockNumber")] public string BlockNumber { get; set; } = "";
        [JsonPropertyName("gasPrice")] public string GasPrice { get; set; } = "";
    }

    /// <summary>All numeric RPC fields are decimal integer strings, including zero.</summary>
    public sealed class TransactionLog
    {
        [JsonPropertyName("address")] public string? Address { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
        [JsonPropertyName("topics")] public List<string>? Topics { get; set; }
        [JsonPropertyName("logIndex")] public string? LogIndex { get; set; }
        [JsonPropertyName("transactionIndex")] public string? TransactionIndex { get; set; }
        [JsonPropertyName("transactionHash")] public string? TransactionHash { get; set; }
        [JsonPropertyName("blockHash")] public string? BlockHash { get; set; }
        [JsonPropertyName("blockNumber")] public string? BlockNumber { get; set; }
        [JsonPropertyName("removed")] public bool? Removed { get; set; }
    }

    /// <summary>All numeric RPC fields are decimal integer strings, including zero.</summary>
    public sealed class TransactionReceipt
    {
        [JsonPropertyName("transactionHash")] public string TransactionHash { get; set; } = "";
        [JsonPropertyName("blockHash")] public string BlockHash { get; set; } = "";
        [JsonPropertyName("blockNumber")] public string BlockNumber { get; set; } = "";
        [JsonPropertyName("transactionIndex")] public string TransactionIndex { get; set; } = "";
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("contractAddress")] public string? ContractAddress { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("cumulativeGasUsed")] public string CumulativeGasUsed { get; set; } = "";
        [JsonPropertyName("gasUsed")] public string GasUsed { get; set; } = "";
        [JsonPropertyName("effectiveGasPrice")] public string? EffectiveGasPrice { get; set; }
        [JsonPropertyName("logsBloom")] public string LogsBloom { get; set; } = "";
        [JsonPropertyName("logs")] public List<TransactionLog> Logs { get; set; } = new List<TransactionLog>();
    }

    /// <summary>Exact gas cost in the native token's smallest unit (wei on Ethereum).</summary>
    public sealed class NativeFee
    {
        [JsonPropertyName("amount")] public string Amount { get; set; } = "";
        [JsonPropertyName("symbol")] public string Symbol { get; set; } = "";
        [JsonPropertyName("decimals")] public string Decimals { get; set; } = "";
    }

    /// <summary>Same shape and JSON names as <c>ITransaction</c> in the JavaScript package.</summary>
    public sealed class TransactionResult
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("requestedAt")] public string? RequestedAt { get; set; }
        [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
        [JsonPropertyName("rejectedAt")] public string? RejectedAt { get; set; }
        [JsonPropertyName("tx")] public TransactionReceipt? Tx { get; set; }
        [JsonPropertyName("error")] public Web3ErrorInfo? Error { get; set; }
        [JsonPropertyName("nativeFee")] public NativeFee? NativeFee { get; set; }

        internal TransactionResult Copy() => (TransactionResult)MemberwiseClone();
    }
}
