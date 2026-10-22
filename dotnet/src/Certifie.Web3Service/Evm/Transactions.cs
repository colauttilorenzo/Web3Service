using System.Collections.Generic;
using System.Threading.Tasks;
using RpcTransaction = Nethereum.RPC.Eth.DTOs.Transaction;

namespace Certifie.Web3.Evm
{
    /// <summary>Transactions read by hash: the RPC object as JSON-safe <see cref="TransactionDetails"/>, with the called function decoded from the ABI.</summary>
    internal static class Transactions
    {
        public static async Task<RpcTransaction> GetAsync(RpcSession session, string transactionHash) =>
            await session.WithTimeoutAsync(session.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionHash)).ConfigureAwait(false)
                ?? throw Errors.TransactionNotFound(transactionHash);

        public static TransactionDetails FromRpc(RpcTransaction transaction, IReadOnlyList<AbiFunction> functions) => new TransactionDetails
        {
            Hash = transaction.TransactionHash, From = transaction.From, To = transaction.To, Value = Integers.ToDecimalString(transaction.Value.Value),
            Nonce = Integers.ToDecimalString(transaction.Nonce.Value), Gas = Integers.ToDecimalString(transaction.Gas.Value), GasPrice = Integers.ToOptionalDecimalString(transaction.GasPrice),
            MaxFeePerGas = Integers.ToOptionalDecimalString(transaction.MaxFeePerGas), MaxPriorityFeePerGas = Integers.ToOptionalDecimalString(transaction.MaxPriorityFeePerGas),
            Input = transaction.Input, Type = Integers.ToOptionalDecimalString(transaction.Type), BlockHash = transaction.BlockHash,
            BlockNumber = Integers.ToOptionalDecimalString(transaction.BlockNumber), TransactionIndex = Integers.ToOptionalDecimalString(transaction.TransactionIndex),
            Pending = transaction.BlockNumber == null, Method = ContractCalls.DecodeCall(functions, transaction.Input),
        };
    }
}
