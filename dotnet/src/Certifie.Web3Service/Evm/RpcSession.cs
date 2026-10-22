using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.Contracts.Services;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.JsonRpc.WebSocketClient;

namespace Certifie.Web3.Evm
{
    /// <summary>
    /// A connection to one RPC endpoint, owned by a single service operation.
    /// Every request is bounded by the configured timeout and by the caller's cancellation token.
    /// </summary>
    internal sealed class RpcSession : IEndpointSession
    {
        private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);
        private readonly IDisposable _transport;
        private readonly TimeSpan _timeout;

        private RpcSession(IClient client, IDisposable transport, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var web3 = new Nethereum.Web3.Web3(client);
            Eth = web3.Eth;
            Client = web3.Client;
            CancellationToken = cancellationToken;
            _transport = transport;
            _timeout = timeout;
        }

        public IEthApiContractService Eth { get; }

        public IClient Client { get; }

        public CancellationToken CancellationToken { get; }

        /// <summary>Opens a WebSocket session for <c>ws://</c> and <c>wss://</c> URIs, an HTTP session otherwise.</summary>
        public static RpcSession Open(string rpcURI, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var uri = new Uri(rpcURI);
            if (uri.Scheme == "ws" || uri.Scheme == "wss")
            {
                var socket = new WebSocketClient(rpcURI);
                return new RpcSession(socket, socket, timeout, cancellationToken);
            }
            var http = new HttpClient { Timeout = timeout };
            return new RpcSession(new RpcClient(uri, http), http, timeout, cancellationToken);
        }

        /// <summary>Awaits a request within the timeout. Nethereum's own timeouts are process-wide, so they cannot be used per service.</summary>
        public async Task<T> WithTimeoutAsync<T>(Task<T> request)
        {
            using var delay = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            var completed = await Task.WhenAny(request, Task.Delay(_timeout, delay.Token)).ConfigureAwait(false);
            if (completed != request)
            {
                CancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("The RPC request timed out.");
            }
            delay.Cancel();
            return await request.ConfigureAwait(false);
        }

        /// <summary>Awaits a request that returns a JSON-RPC quantity (a hex integer).</summary>
        public async Task<BigInteger> QuantityAsync(Task<HexBigInteger> request) =>
            (await WithTimeoutAsync(request).ConfigureAwait(false)).Value;

        public bool IsCancellation(Exception error) => Errors.IsCancellation(error, CancellationToken);

        public void Dispose()
        {
            if (_transport is WebSocketClient socket) _ = Task.Run(() => CloseAsync(socket));
            else _transport.Dispose();
        }

        /// <summary>Closes a socket in the background with a bounded handshake, so a peer that never answers cannot delay the caller.</summary>
        private static async Task CloseAsync(WebSocketClient socket)
        {
            using var timeout = new CancellationTokenSource(CloseHandshakeTimeout);
            try { await socket.StopAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token).ConfigureAwait(false); }
            catch (Exception) { /* Already closed or timed out. */ }
            finally { socket.Dispose(); }
        }
    }
}
