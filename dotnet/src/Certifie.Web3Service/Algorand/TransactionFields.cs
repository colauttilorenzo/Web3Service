using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Certifie.Web3.Algorand
{
    /// <summary>Programs and state schema of a new application.</summary>
    internal sealed class Creation
    {
        public byte[] ApprovalProgram { get; set; } = new byte[0];
        public byte[] ClearProgram { get; set; } = new byte[0];
        public int GlobalInts { get; set; }
        public int GlobalBytes { get; set; }
        public int LocalInts { get; set; }
        public int LocalBytes { get; set; }
        public int ExtraPages { get; set; }
    }

    /// <summary>The asset parameters a token definition sets.</summary>
    internal sealed class AssetFields
    {
        public string AssetName { get; set; } = "";
        public string UnitName { get; set; } = "";
        public BigInteger Total { get; set; }
        public int Decimals { get; set; }
        public string? Url { get; set; }
        public byte[]? MetadataHash { get; set; }
    }

    /// <summary>The fields of each transaction type, with the MessagePack keys algod uses.</summary>
    internal static class TransactionFields
    {
        public static MsgpackMap Payment(string receiver, BigInteger amount) =>
            new MsgpackMap().Add("rcv", AlgorandAddress.Decode(receiver)).Add("amt", amount);

        /// <summary>A NoOp call; boxes of the called application have index 0, others the position of their application in the foreign apps plus one.</summary>
        public static MsgpackMap ApplicationCall(BigInteger appId, List<byte[]> appArgs, Resources resources, Creation? creation)
        {
            var boxes = resources.Boxes.Select(box => (object)new MsgpackMap()
                .Add("i", box.AppIndex == 0 || box.AppIndex == appId ? 0 : resources.Apps.IndexOf(box.AppIndex) + 1).Add("n", box.Name)).ToList();
            var call = new MsgpackMap().Add("apid", appId).Add("apaa", appArgs.Cast<object>().ToList()).Add("apbx", boxes)
                .Add("apat", resources.Accounts.Select(account => (object)AlgorandAddress.Decode(account)).ToList())
                .Add("apfa", resources.Apps.Cast<object>().ToList()).Add("apas", resources.Assets.Cast<object>().ToList());
            return creation == null ? call : call.AddAll(CreationFields(creation));
        }

        public static MsgpackMap AssetCreation(AssetFields asset, string manager) => new MsgpackMap().Add("apar", new MsgpackMap()
            .Add("an", asset.AssetName).Add("un", asset.UnitName).Add("t", asset.Total).Add("dc", asset.Decimals).Add("au", asset.Url)
            .Add("am", asset.MetadataHash).Add("m", AlgorandAddress.Decode(manager)));

        private static MsgpackMap CreationFields(Creation creation) => new MsgpackMap()
            .Add("apap", creation.ApprovalProgram).Add("apsu", creation.ClearProgram).Add("apep", creation.ExtraPages)
            .Add("apgs", new MsgpackMap().Add("nui", creation.GlobalInts).Add("nbs", creation.GlobalBytes))
            .Add("apls", new MsgpackMap().Add("nui", creation.LocalInts).Add("nbs", creation.LocalBytes));
    }
}
