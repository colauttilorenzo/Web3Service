using Certifie.Web3.Algorand;
using Certifie.Web3.Bitcoin;
using Certifie.Web3.Evm;

namespace Certifie.Web3
{
    /// <summary>Chooses the blockchain implementation from the type of the provider.</summary>
    internal static class Blockchains
    {
        /// <summary><see cref="IAvmProvider"/> → AlgorandChain, <see cref="IBitcoinProvider"/> → BitcoinChain, <see cref="IEvmProvider"/> → EvmChain.</summary>
        public static IWeb3Service Create(IBlockchainProvider? provider, Web3ServiceOptions? options) => provider switch
        {
            IAvmProvider avm => new AlgorandChain(avm, options),
            IBitcoinProvider bitcoin => new BitcoinChain(bitcoin, options),
            IEvmProvider evm => new EvmChain(evm, options),
            null => throw Errors.InvalidArgument("An rpcURI is required."),
            _ => throw Errors.InvalidArgument("The provider must be an IEvmProvider, an IAvmProvider or an IBitcoinProvider."),
        };
    }
}
