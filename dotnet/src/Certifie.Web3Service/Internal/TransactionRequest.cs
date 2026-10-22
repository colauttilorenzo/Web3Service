using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;

namespace Certifie.Web3
{
    /// <summary>An encoded contract call or deployment, ready to be signed.</summary>
    internal sealed class TransactionRequest
    {
        public TransactionRequest(string data, string? to, Func<string, HexBigInteger, Task<HexBigInteger>> estimateGas, TransactionOptions? options)
        {
            Data = data;
            To = to;
            EstimateGas = estimateGas;
            Options = options ?? new TransactionOptions();
        }

        public string Data { get; }

        /// <summary>Recipient contract; null for deployments.</summary>
        public string? To { get; }

        /// <summary>Estimates the gas for a sender address and a value.</summary>
        public Func<string, HexBigInteger, Task<HexBigInteger>> EstimateGas { get; }

        public TransactionOptions Options { get; }

        /// <summary>The explicit gas limit, or the node's estimate plus the configured buffer.</summary>
        public async Task<BigInteger> GasLimitAsync(RpcSession session, string from, BigInteger value, BigInteger gasBuffer)
        {
            if (Options.Gas.HasValue) return Integers.RequireNonNegative(Options.Gas.Value, "gas");
            var estimate = await session.QuantityAsync(EstimateGas(from, new HexBigInteger(value))).ConfigureAwait(false);
            return estimate + gasBuffer;
        }
    }
}
