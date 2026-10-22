using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// A connection to one algod node (and the indexer, when configured), owned by a single service operation. It holds the suggested
    /// parameters read when it opened, so every transaction of the operation uses the same fees and validity rounds.
    /// </summary>
    internal sealed class AlgodSession : IEndpointSession
    {
        private readonly RestClient _algod;
        private readonly RestClient? _indexer;
        private readonly CancellationToken _cancellationToken;
        private SuggestedParams? _params;

        private AlgodSession(RestClient algod, RestClient? indexer, CancellationToken cancellationToken) =>
            (_algod, _indexer, _cancellationToken) = (algod, indexer, cancellationToken);

        /// <summary>Fees, validity rounds and genesis of the network, read when the session opened.</summary>
        public SuggestedParams Params => _params ?? throw new InvalidOperationException("The session has not been verified.");

        public bool HasIndexer => _indexer != null;

        public CancellationToken CancellationToken => _cancellationToken;

        public static AlgodSession Open(string uri, AlgorandNetwork network, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var indexer = network.IndexerURI == null ? null : new RestClient(network.IndexerURI, timeout, cancellationToken, "X-Indexer-API-Token", network.IndexerToken);
            return new AlgodSession(new RestClient(uri, timeout, cancellationToken, "X-Algo-API-Token", network.Token), indexer, cancellationToken);
        }

        /// <summary>Reads the suggested parameters and returns the genesis ID of the node's network.</summary>
        public async Task<string> LoadAsync()
        {
            _params = SuggestedParams.FromJson((JsonObject)(await _algod.GetJsonAsync("v2/transactions/params").ConfigureAwait(false))!);
            return _params.GenesisId;
        }

        public async Task<JsonObject> GetAsync(string path) => (JsonObject)(await _algod.GetJsonAsync(path).ConfigureAwait(false))!;

        /// <summary>The response, or null when the node answers 404 Not Found.</summary>
        public async Task<JsonObject?> OptionalAsync(string path) => (JsonObject?)await _algod.OptionalJsonAsync(path).ConfigureAwait(false);

        public async Task<JsonObject?> OptionalIndexedAsync(string path) =>
            _indexer == null ? null : (JsonObject?)await _indexer.OptionalJsonAsync(path).ConfigureAwait(false);

        public async Task<JsonObject> PostAsync(string path, byte[] body, string contentType) =>
            (JsonObject)(await _algod.PostAsync(path, new ByteArrayContent(body), contentType).ConfigureAwait(false))!;

        public bool IsCancellation(Exception error) => Errors.IsCancellation(error, _cancellationToken);

        public void Dispose()
        {
            // HTTP requests hold no connection to close: the shared HttpClient pools them.
        }
    }
}
