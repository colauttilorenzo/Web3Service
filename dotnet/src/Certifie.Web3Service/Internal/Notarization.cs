namespace Certifie.Web3
{
    /// <summary>The notarization call shared by every blockchain: <c>mint(address,string,string,string)</c> with the contract's own address first.</summary>
    internal static class Notarization
    {
        /// <summary>The arguments of mint: the contract's address, the hash, its algorithm and the metadata.</summary>
        public static string[] MintArguments(string contractAddress, NotarizationDocument? document)
        {
            if (string.IsNullOrEmpty(document?.Hash?.Data) || string.IsNullOrEmpty(document!.Hash.Algorithm))
                throw Errors.InvalidArgument("A document hash and algorithm are required.");
            return new[] { contractAddress, document.Hash.Data, document.Hash.Algorithm, document.Metadata ?? "" };
        }

        /// <summary>A copy of the caller's event, named after the operation unless the caller named it.</summary>
        public static TransactionResult Named(TransactionResult? onChainEvent, string name)
        {
            var copy = onChainEvent?.Copy() ?? new TransactionResult();
            copy.Name ??= name;
            return copy;
        }
    }
}
