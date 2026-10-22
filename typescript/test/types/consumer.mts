import { Web3Service, type ITransaction, type IEthereumProvider, type IEvmProvider, type EthereumProviderConfig, type Eip1193Provider } from '@certifie/web3-service';
const provider: IEthereumProvider = { chainId: 1337, rpcURI: 'http://localhost:8545', nativeCurrency: { symbol: 'ETH', decimals: 18 } };
const service = new Web3Service(provider, { signer: { privateKey: '0x1234' } });
const defaultConfig: EthereumProviderConfig = { rpcURI: 'http://localhost:8545' };
new Web3Service(defaultConfig);
const evmConfig: IEvmProvider = { rpcURI: 'wss://node.example', nativeCurrency: { symbol: 'ETH' } };
new Web3Service(evmConfig);
new Web3Service('https://node.example');
new Web3Service('wss://node.example');
new Web3Service({ ...defaultConfig, nativeCurrency: { symbol: 'CUSTOM' } });
// @ts-expect-error An RPC endpoint is still required.
new Web3Service({ chainId: '0x1' });
async function read(): Promise<string | undefined> {
  const result: ITransaction = await service.getTransactionReceipt('0x1234');
  const status: string | undefined = result.tx?.status;
  // @ts-expect-error Blockchain output numbers are integer strings.
  const incorrect: number | undefined = result.tx?.gasUsed;
  return status;
}
void read;
// @ts-expect-error Choose exactly one signer credential.
new Web3Service(provider, { signer: { privateKey: '0x1234', mnemonic: 'example' } });
const wallet: Eip1193Provider = { request: async () => null };
new Web3Service(provider, { signer: { wallet, switchChain: false } });
// @ts-expect-error A wallet signer cannot be combined with a private key.
new Web3Service(provider, { signer: { wallet, privateKey: '0x1234' } });
