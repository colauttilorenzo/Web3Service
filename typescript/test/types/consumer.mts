import {
  Web3Service, type Web3Contract, type ITransaction, type IEthereumProvider, type IEvmProvider, type EthereumProviderConfig, type Eip1193Provider,
  type IContractMethod, type INftMint, type ITokenInfo, type ITransactionDetails,
} from '@certifie/web3-service';
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
async function contracts(): Promise<string[]> {
  const contract: Web3Contract = service.getContract([], '0x00000000000000000000000000000000000000aa');
  const method: IContractMethod | undefined = contract.methods[0];
  const kind: 'read' | 'write' | 'payable' | undefined = method?.kind;
  const balance = await contract.call<string>('balanceOf', { owner: '0x00000000000000000000000000000000000000aa' });
  const sent: ITransaction = await contract.send('transfer', { to: '0x00000000000000000000000000000000000000aa', amount: 1 }, { value: '0' });
  const minted: INftMint = await contract.mintNft({ to: '0x00000000000000000000000000000000000000aa', uri: 'ipfs://example' });
  const info: ITokenInfo = await contract.getTokenInfo(1n);
  const details: ITransactionDetails = await service.getTransaction('0x1234', contract.abi);
  // @ts-expect-error Token supplies are integer strings.
  const supply: number | undefined = info.totalSupply;
  return [balance, kind ?? '', sent.name ?? '', ...minted.tokenIds, details.value, ...Web3Service.detectStandards(contract.abi)];
}
void contracts;
