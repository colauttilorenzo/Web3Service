using System.Numerics;
using System.Text.Json.Nodes;

namespace Certifie.Web3.Algorand
{
    /// <summary>Fees, validity rounds and genesis of the network, as algod suggests them for new transactions.</summary>
    internal sealed class SuggestedParams
    {
        /// <summary>Rounds a transaction stays valid, as algosdk sets it.</summary>
        private const int ValidRounds = 1000;

        public BigInteger MinFee { get; private set; }
        public BigInteger FirstValid { get; private set; }
        public BigInteger LastValid { get; private set; }
        public string GenesisId { get; private set; } = "";
        public byte[] GenesisHash { get; private set; } = new byte[0];

        /// <summary>From the response of <c>/v2/transactions/params</c>.</summary>
        public static SuggestedParams FromJson(JsonObject json)
        {
            var lastRound = JsonValues.Integer(json["last-round"]);
            return new SuggestedParams
            {
                MinFee = JsonValues.Integer(json["min-fee"]), FirstValid = lastRound, LastValid = lastRound + ValidRounds,
                GenesisId = JsonValues.Text(json["genesis-id"]) ?? "", GenesisHash = JsonValues.Base64(json["genesis-hash"]),
            };
        }
    }
}
