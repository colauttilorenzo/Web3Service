// The same calls as dotnet/test/Certifie.Web3Service.Tests/Fixtures/ParityScenario.cs; the .NET ParityTests compare the two outputs.
import { Web3Service } from '@certifie/web3-service';
import { vectors } from './TestVectors.mjs';

const recipient = vectors.wallets[1].address;

export async function run(chain) {
  const service = new Web3Service(chain.provider(), chain.signer());
  return { ...await notarization(service, chain), ...await contracts(service, chain) };
}

async function notarization(service, chain) {
  const deployed = await service.deploy(chain.abi, chain.bytecode, ['Parity'], { name: 'parity', requestedAt: '123' });
  const address = deployed.tx.contractAddress;
  const document = { recipient: address, hash: 'f'.repeat(64), algorithm: 'sha2', uri: 'ipfs://parity' };
  const minted = { ...await service.getContract(chain.abi, address).send('mint', document), requestedAt: 'now' };
  const receipt = await service.getTransactionReceipt(minted.tx.transactionHash, { name: 'lookup', requestedAt: '789' });
  return {
    deployed, minted, receipt,
    label: await service.callContractMethod('label', chain.abi, address),
    metadata: await service.callContractMethod('metadata', chain.abi, address, [document.hash]),
    numbers: await service.callContractMethod('numbers', chain.abi, address),
  };
}

/** Transactions sent through Web3Contract are requested now, so their requestedAt is replaced before comparing. */
async function contracts(service, chain) {
  const token = await deployContract(service, chain, 'TestToken', ['Parity', 'PAR', '1000']);
  const transfer = { ...await token.call('transfer', { to: recipient, amount: '10' }), requestedAt: 'now' };
  const nft = await deployContract(service, chain, 'TestNft', ['Parity Art', 'PART']);
  const nftMint = await nft.mintNft({ to: recipient, uri: 'ipfs://parity/1' });
  const multi = await deployContract(service, chain, 'TestMultiToken', ['ipfs://parity/{id}.json']);
  const multiMint = await multi.mintNft({ to: recipient, ids: [4, 5], amounts: [1, 2] });
  return {
    notarizerStandards: Web3Service.detectStandards(chain.abi), tokenMethods: token.methods, tokenStandards: token.standards,
    tokenOwner: await token.owner(), transfer, transferDetails: await token.getTransaction(transfer.tx.transactionHash),
    tokenInfo: await token.getTokenInfo(), nftMint: { ...nftMint, transaction: { ...nftMint.transaction, requestedAt: 'now' } },
    nftInfo: await nft.getTokenInfo('1'), multiMint: multiMint.tokenIds, multiInfo: await multi.getTokenInfo('5'),
  };
}

async function deployContract(service, chain, name, constructorArguments) {
  const { abi, bytecode } = chain.contracts[name];
  return service.getContract(abi, (await service.deploy(abi, bytecode, constructorArguments, { requestedAt: '1' })).tx.contractAddress);
}
