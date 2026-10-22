using System;
using System.Threading.Tasks;

namespace Certifie.Web3.Evm
{
    /// <summary>Opens one verified JSON-RPC session per operation and falls back to the next endpoint when one is down or on the wrong chain.</summary>
    internal static class RpcConnector
    {
        public static Connector<RpcSession> Create(EthereumProvider provider, TimeSpan timeout) =>
            new Connector<RpcSession>(provider, (uri, cancellationToken) => RpcSession.Open(uri, timeout, cancellationToken),
                session => VerifyChainIdAsync(session, provider.ChainId));

        private static async Task VerifyChainIdAsync(RpcSession session, string chainId)
        {
            var actual = Integers.ToHex(await session.QuantityAsync(session.Eth.ChainId.SendRequestAsync()).ConfigureAwait(false));
            if (actual != chainId) throw Errors.ChainMismatch(chainId, actual);
        }
    }
}
