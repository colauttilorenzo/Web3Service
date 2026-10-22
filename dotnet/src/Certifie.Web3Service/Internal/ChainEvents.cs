namespace Certifie.Web3
{
    /// <summary>The caller's event passed to every write, shared by every blockchain.</summary>
    internal static class ChainEvents
    {
        /// <summary>A copy of the caller's event, named after the operation unless the caller named it.</summary>
        public static TransactionResult Named(TransactionResult? onChainEvent, string name)
        {
            var copy = onChainEvent?.Copy() ?? new TransactionResult();
            copy.Name ??= name;
            return copy;
        }
    }
}
