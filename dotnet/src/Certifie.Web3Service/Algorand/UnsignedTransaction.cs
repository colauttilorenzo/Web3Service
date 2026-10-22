using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// An Algorand transaction before signing: the common header (sender, fee, validity, genesis, group) and the fields of its type.
    /// Its ID and signature cover its canonical MessagePack encoding prefixed with "TX".
    /// </summary>
    internal sealed class UnsignedTransaction
    {
        private static readonly byte[] TransactionPrefix = Encoding.ASCII.GetBytes("TX");
        private static readonly byte[] GroupPrefix = Encoding.ASCII.GetBytes("TG");
        private readonly SuggestedParams _params;
        private readonly MsgpackMap _fields;

        /// <summary>A transaction of a type (pay, appl, acfg) with a flat fee and the fields of that type.</summary>
        public UnsignedTransaction(string type, string sender, BigInteger fee, SuggestedParams parameters, MsgpackMap fields)
        {
            (Type, Sender, Fee, _params, _fields) = (type, sender, fee, parameters, fields);
        }

        public string Type { get; }
        public string Sender { get; }
        public BigInteger Fee { get; }

        /// <summary>The group ID, the same for every transaction of a group.</summary>
        public byte[]? Group { get; private set; }

        /// <summary>The 52-character transaction ID.</summary>
        public string Id => Base32.Encode(RawId());

        /// <summary>Gives every transaction the ID of the group: the hash of "TG" and the list of their raw IDs. algosdk does so for a single transaction too.</summary>
        public static List<UnsignedTransaction> AssignGroup(List<UnsignedTransaction> transactions)
        {
            var ids = transactions.Select(transaction => (object)transaction.RawId()).ToList();
            var group = AvmCrypto.Sha512_256(GroupPrefix, Msgpack.Encode(new MsgpackMap().Add("txlist", ids)));
            foreach (var transaction in transactions) transaction.Group = group;
            return transactions;
        }

        public byte[] Encode() => Msgpack.Encode(ToMap());

        /// <summary>The signed transaction as the node receives it: the Ed25519 signature and the transaction.</summary>
        public byte[] Sign(byte[] seed) =>
            Msgpack.Encode(new MsgpackMap().Add("sig", AvmCrypto.Sign(seed, TransactionPrefix.Concat(Encode()).ToArray())).Add("txn", ToMap()));

        /// <summary>The transaction without a signature, as simulation takes it.</summary>
        public MsgpackMap ToUnsignedMap() => new MsgpackMap().Add("txn", ToMap());

        private byte[] RawId() => AvmCrypto.Sha512_256(TransactionPrefix, Encode());

        private MsgpackMap ToMap() => new MsgpackMap()
            .Add("type", Type).Add("snd", AlgorandAddress.Decode(Sender)).Add("fee", Fee).Add("fv", _params.FirstValid).Add("lv", _params.LastValid)
            .Add("gen", _params.GenesisId).Add("gh", _params.GenesisHash).Add("grp", Group).AddAll(_fields);
    }
}
