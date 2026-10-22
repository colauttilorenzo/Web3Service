using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3.Bitcoin
{
    /// <summary>An Esplora REST API (Blockstream, mempool.space or a self-hosted electrs), bounded by the configured timeout.</summary>
    internal sealed class EsploraSession : IEndpointSession
    {
        private readonly RestClient _api;
        private readonly CancellationToken _cancellationToken;

        public EsploraSession(string uri, TimeSpan timeout, CancellationToken cancellationToken) =>
            (_api, _cancellationToken) = (new RestClient(uri, timeout, cancellationToken), cancellationToken);

        /// <summary>A JSON resource; null when the API answers 404.</summary>
        public Task<JsonNode?> JsonAsync(string path) => _api.OptionalJsonAsync(path);

        public async Task<string> TextAsync(string path)
        {
            try { return (await _api.GetTextAsync(path).ConfigureAwait(false)).Trim(); }
            catch (HttpStatusException error) when (error.Status == System.Net.HttpStatusCode.NotFound) { throw new Web3Exception("RPC_ERROR", $"{path} was not found."); }
        }

        public bool IsCancellation(Exception error) => Errors.IsCancellation(error, _cancellationToken);

        public void Dispose()
        {
            // HTTP requests hold no connection to close: the shared HttpClient pools them.
        }
    }

    /// <summary>Opens one verified Esplora session per operation and falls back to the next API when one is down or on another network.</summary>
    internal static class EsploraConnector
    {
        /// <summary>Genesis block hashes of the public networks; another network name skips the check.</summary>
        private static readonly Dictionary<string, string> Genesis = new Dictionary<string, string>
        {
            ["mainnet"] = "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f",
            ["testnet"] = "000000000933ea01ad0ee984209779baaef3ee6c8af2eac1fd6a6b17ed2e4943",
            ["testnet4"] = "00000000da84f2bafbbc53dee25a72ae507ff4914b867c565be350b0da8bf043",
            ["signet"] = "00000008819873e925422c1ff0f99f7cc9bbb232af63a077a480a3633bee1ef6",
            ["regtest"] = "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206",
        };

        public static Connector<EsploraSession> Create(BitcoinNetwork network, TimeSpan timeout) =>
            new Connector<EsploraSession>(network, (uri, cancellationToken) => new EsploraSession(uri, timeout, cancellationToken),
                session => VerifyGenesisAsync(session, network.ChainId));

        /// <summary>The genesis block identifies the network, as the chain ID does on EVM networks.</summary>
        private static async Task VerifyGenesisAsync(EsploraSession session, string network)
        {
            if (!Genesis.TryGetValue(network, out var expected)) return;
            var actual = await session.TextAsync("/block-height/0").ConfigureAwait(false);
            if (actual != expected) throw Errors.ChainMismatch(network, actual);
        }
    }
}
