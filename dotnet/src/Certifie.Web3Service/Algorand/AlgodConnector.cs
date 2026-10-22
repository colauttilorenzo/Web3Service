using System;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>Opens one verified algod session per operation and falls back to the next node when one is down or on another network.</summary>
    internal static class AlgodConnector
    {
        public static Connector<AlgodSession> Create(AlgorandNetwork network, TimeSpan timeout) =>
            new Connector<AlgodSession>(network, (uri, cancellationToken) => AlgodSession.Open(uri, network, timeout, cancellationToken),
                session => VerifyGenesisAsync(session, network.ChainId));

        /// <summary>The genesis ID identifies the network, as the chain ID does on EVM networks.</summary>
        private static async Task VerifyGenesisAsync(AlgodSession session, string genesisId)
        {
            var actual = await session.LoadAsync().ConfigureAwait(false);
            if (actual != genesisId) throw Errors.ChainMismatch(genesisId, actual);
        }
    }
}
