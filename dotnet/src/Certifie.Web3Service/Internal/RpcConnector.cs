using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>Opens one verified session per operation and falls back to the next endpoint when one is down or on the wrong chain.</summary>
    internal sealed class RpcConnector
    {
        private readonly string _chainId;
        private readonly IReadOnlyList<string> _endpoints;
        private readonly TimeSpan _timeout;

        public RpcConnector(EthereumProvider provider, TimeSpan timeout)
        {
            _chainId = provider.ChainId;
            _endpoints = Configuration.Endpoints(provider.RpcURI, provider.RpcPublicURI);
            _timeout = timeout;
        }

        /// <summary>Runs an operation on a verified session and always closes it. Unexpected failures become <c>RPC_ERROR</c>.</summary>
        public async Task<T> UseAsync<T>(Func<RpcSession, Task<T>> operation, CancellationToken cancellationToken)
        {
            using var session = await OpenAsync(cancellationToken).ConfigureAwait(false);
            try { return await operation(session).ConfigureAwait(false); }
            catch (Exception error) when (!(error is Web3Exception) && !session.IsCancellation(error))
            {
                throw new Web3Exception("RPC_ERROR", "The blockchain operation failed.", error);
            }
        }

        private async Task<RpcSession> OpenAsync(CancellationToken cancellationToken)
        {
            var failures = new List<Exception>();
            foreach (var uri in _endpoints)
            {
                try { return await OpenVerifiedAsync(uri, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (!Errors.IsCancellation(error, cancellationToken)) { failures.Add(error); }
            }
            throw SelectionError(failures);
        }

        private async Task<RpcSession> OpenVerifiedAsync(string uri, CancellationToken cancellationToken)
        {
            var session = RpcSession.Open(uri, _timeout, cancellationToken);
            try { await VerifyChainIdAsync(session).ConfigureAwait(false); }
            catch { session.Dispose(); throw; }
            return session;
        }

        private async Task VerifyChainIdAsync(RpcSession session)
        {
            var actual = Integers.ToHex(await session.QuantityAsync(session.Eth.ChainId.SendRequestAsync()).ConfigureAwait(false));
            if (actual != _chainId) throw new Web3Exception("CHAIN_ID_MISMATCH", $"Expected chain {_chainId}, received {actual}.");
        }

        /// <summary>An endpoint on the wrong chain is more useful to report than an unreachable one.</summary>
        private Web3Exception SelectionError(List<Exception> failures) =>
            failures.OfType<Web3Exception>().LastOrDefault(error => error.Code == "CHAIN_ID_MISMATCH")
            ?? new Web3Exception("PROVIDER_UNAVAILABLE", $"No RPC endpoint is available for chain {_chainId}.", failures.LastOrDefault());
    }
}
