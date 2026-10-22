// The same calls as dotnet/test/Certifie.Web3Service.Tests/Fixtures/AlgorandParityScenario.cs; the .NET AlgorandParityTests compare the outputs.
import { AlgorandWallets, Web3Service } from '@certifie/web3-service';
import { contracts } from './AlgorandNode.mjs';
import { vectors } from './TestVectors.mjs';

const FUNDING = { value: '1000000' };
const recipient = vectors.algorand.wallets[1].address;
const { Notarizer } = contracts;

export async function run(node) {
  const service = new Web3Service(node.provider(), node.signer());
  return { ...await notarization(service), ...await tokens(service) };
}

async function notarization(service) {
  const deployed = await service.deploy(Notarizer, '', ['Parity'], { name: 'parity', requestedAt: '123' }, FUNDING);
  const address = deployed.tx.contractAddress;
  const document = { recipient: AlgorandWallets.getApplicationAddress(address), hash: 'f'.repeat(64), algorithm: 'sha2', uri: 'ipfs://parity' };
  const minted = { ...await service.getContract(Notarizer, address).send('mint', document), requestedAt: 'now' };
  const receipt = await service.getTransactionReceipt(minted.tx.transactionHash, { name: 'lookup', requestedAt: '789' });
  return {
    deployed, minted, receipt,
    label: await service.callContractMethod('label', Notarizer, address),
    metadata: await service.callContractMethod('metadata', Notarizer, address, [document.hash]),
    numbers: await service.callContractMethod('numbers', Notarizer, address),
  };
}

/** Transactions sent through Web3Contract are requested now, so their requestedAt is replaced before comparing. */
async function tokens(service) {
  const token = await deployContract(service, 'TestToken', ['Parity', 'PAR', '1000']);
  await token.send('mintInitialSupply');
  const transfer = { ...await token.call('arc200_transfer', { to: recipient, value: '10' }), requestedAt: 'now' };
  const nft = await deployContract(service, 'TestNft', []);
  const nftMint = await nft.mintNft({ to: recipient, uri: 'ipfs://parity/1' });
  const asset = await service.createToken({ name: 'Parity Coin', symbol: 'PAR', totalSupply: '1000', decimals: 2 }, { requestedAt: '1' });
  return {
    notarizerStandards: Web3Service.detectStandards(Notarizer), tokenMethods: token.methods, tokenStandards: token.standards,
    tokenOwner: await token.owner(), transfer, transferDetails: await token.getTransaction(transfer.tx.transactionHash),
    tokenInfo: await token.getTokenInfo(), nftMint: { ...nftMint, transaction: { ...nftMint.transaction, requestedAt: 'now' } },
    nftInfo: await nft.getTokenInfo('1'), asset, assetInfo: await service.getTokenInfo(asset.tokenId),
  };
}

async function deployContract(service, name, constructorArguments) {
  const deployed = await service.deploy(contracts[name], '', constructorArguments, { requestedAt: '1' }, FUNDING);
  return service.getContract(contracts[name], deployed.tx.contractAddress);
}
