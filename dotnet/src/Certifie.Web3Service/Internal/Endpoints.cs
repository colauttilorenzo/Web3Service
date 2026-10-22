using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>A connection to one endpoint, owned by a single service operation and closed when it ends.</summary>
    internal interface IEndpointSession : IDisposable
    {
        /// <summary>True when the caller cancelled the operation; such errors are rethrown unchanged.</summary>
        bool IsCancellation(Exception error);
    }

    /// <summary>
    /// Endpoint selection shared by every blockchain: opens one verified session per operation and falls back to the next endpoint
    /// when one is down or on another network.
    /// </summary>
    internal sealed class Connector<TSession> where TSession : IEndpointSession
    {
        private readonly string _chainId;
        private readonly IReadOnlyList<string> _endpoints;
        private readonly Func<string, CancellationToken, TSession> _open;
        private readonly Func<TSession, Task> _verify;

        public Connector(ResolvedProvider provider, Func<string, CancellationToken, TSession> open, Func<TSession, Task> verify)
        {
            _chainId = provider.ChainId;
            _endpoints = Configuration.Endpoints(provider.RpcURI, provider.RpcPublicURI);
            _open = open;
            _verify = verify;
        }

        /// <summary>Runs an operation on a verified session and always closes it. Unexpected failures become <c>RPC_ERROR</c>.</summary>
        public async Task<T> UseAsync<T>(Func<TSession, Task<T>> operation, CancellationToken cancellationToken)
        {
            using var session = await OpenAsync(cancellationToken).ConfigureAwait(false);
            try { return await operation(session).ConfigureAwait(false); }
            catch (Exception error) when (!(error is Web3Exception) && !session.IsCancellation(error))
            {
                throw new Web3Exception("RPC_ERROR", "The blockchain operation failed.", error);
            }
        }

        private async Task<TSession> OpenAsync(CancellationToken cancellationToken)
        {
            var failures = new List<Exception>();
            foreach (var uri in _endpoints)
            {
                try { return await OpenVerifiedAsync(uri, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (!Errors.IsCancellation(error, cancellationToken)) { failures.Add(error); }
            }
            throw SelectionError(failures);
        }

        private async Task<TSession> OpenVerifiedAsync(string uri, CancellationToken cancellationToken)
        {
            var session = _open(uri, cancellationToken);
            try { await _verify(session).ConfigureAwait(false); }
            catch { session.Dispose(); throw; }
            return session;
        }

        /// <summary>An endpoint on the wrong network is more useful to report than an unreachable one.</summary>
        private Web3Exception SelectionError(List<Exception> failures) =>
            failures.OfType<Web3Exception>().LastOrDefault(error => error.Code == "CHAIN_ID_MISMATCH")
            ?? new Web3Exception("PROVIDER_UNAVAILABLE", $"No RPC endpoint is available for chain {_chainId}.", failures.LastOrDefault());
    }
}
