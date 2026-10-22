// Mirrors dotnet/test/Certifie.Web3Service.Tests/AlgorandTests.cs: same test names, same node, same assertions.
// The tests need an Algorand node such as AlgoKit LocalNet; see fixtures/AlgorandNode.mjs.
import { before, test } from 'node:test';
import assert from 'node:assert/strict';
import { AlgorandWallets, Web3Service } from '@certifie/web3-service';
import { AlgorandNode, contracts, skip } from './fixtures/AlgorandNode.mjs';
import { vectors } from './fixtures/TestVectors.mjs';

/** Funds each application account for the minimum balance of its boxes. */
const FUNDING = { value: '1000000' };
const NOT_A_TRANSACTION = 'A'.repeat(52);
const { Notarizer } = contracts;
const recipient = vectors.algorand.wallets[1].address;
// Notarization is a call to mint(address,string,string,string) on the test application, whose own account is the recipient.
const notarization = (address, letter, uri = '') =>
  ({ recipient: AlgorandWallets.getApplicationAddress(address), hash: letter.repeat(64), algorithm: 'sha2', uri });

let node;
before(async () => { if (!skip) node = await AlgorandNode.start(); });

async function deployContract(service, name, constructorArguments = []) {
  const deployed = await service.deploy(contracts[name], '', constructorArguments, undefined, FUNDING);
  return service.getContract(contracts[name], deployed.tx.contractAddress);
}

test('AlgorandDeploysNotarizesReadsAndReturnsJsonSafeReceipts', { skip }, async () => {
  const service = new Web3Service(node.provider(), node.signer());
  const deployed = await service.deploy(Notarizer, '', [vectors.contract.label], { requestedAt: '123', rejectedAt: '999' }, FUNDING);
  assert.deepEqual([deployed.name, deployed.tx.status, deployed.tx.type, deployed.requestedAt, deployed.rejectedAt], ['deploy', '1', 'appl', '123', undefined]);
  assert.match(deployed.completedAt, /^\d+$/);
  const address = deployed.tx.contractAddress;
  assert.match(address, /^\d+$/);
  assert.equal(await service.callContractMethod('label', Notarizer, address), vectors.contract.label);

  const notarized = notarization(address, 'a', 'ipfs://example');
  const minted = await service.getContract(Notarizer, address).send('mint', notarized);
  assert.deepEqual([minted.name, minted.tx.status], ['mint', '1']);
  assert.deepEqual([minted.tx.logs.length, minted.tx.logs[0].logIndex, minted.tx.gasUsed], [1, '0', '0']);
  assert.deepEqual(minted.nativeFee, { amount: '1000', symbol: 'ALGO', decimals: '6' });

  const metadata = await service.callContractMethod('metadata', Notarizer, address, [notarized.hash]);
  assert.deepEqual(metadata, [notarized.hash, 'sha2', 'ipfs://example']);
  assert.deepEqual(await service.callContractMethod('numbers', Notarizer, address), vectors.algorand.numbers);

  const confirmed = await service.getTransactionReceipt(minted.tx.transactionHash, { rejectedAt: '1' });
  assert.deepEqual([confirmed.nativeFee.amount, confirmed.rejectedAt, confirmed.tx.blockNumber], ['1000', undefined, minted.tx.blockNumber]);

  await assert.rejects(service.getTransactionReceipt(NOT_A_TRANSACTION), error => error.code === 'TRANSACTION_NOT_FOUND');
  await assert.rejects(service.callContractMethod('missing', Notarizer, address), error => error.code === 'METHOD_NOT_FOUND');
  await assert.rejects(new Web3Service(node.provider()).getContract(Notarizer, address).send('mint', notarized), error => error.code === 'SIGNER_REQUIRED');
});

test('AlgorandQueuesConcurrentWritesAndRejectsFailingCalls', { skip }, async () => {
  const service = new Web3Service(node.provider(), node.signer());
  const address = (await service.deploy(Notarizer, '', ['Queue'], undefined, FUNDING)).tx.contractAddress;
  const contract = service.getContract(Notarizer, address);
  const results = await Promise.all(['b', 'c'].map(letter => contract.send('mint', notarization(address, letter))));
  assert.deepEqual(results.map(result => result.tx.status), ['1', '1']);

  let failure;
  await assert.rejects(contract.send('mint', notarization(address, 'b')), error => (failure = error).code === 'TRANSACTION_REVERTED');
  assert.match(failure.transactionHash, /^[A-Z2-7]{52}$/);
  assert.match(failure.message, /already notarized/);
  await assert.rejects(service.getTransactionReceipt(failure.transactionHash), error => error.code === 'TRANSACTION_NOT_FOUND');
});

test('AlgorandFallsBackToTheSecondEndpointAndValidatesTheNetwork', { skip }, async () => {
  const fallback = new Web3Service({ ...node.provider('http://127.0.0.1:1'), rpcPublicURI: node.uri });
  const check = await fallback.check();
  assert.match(check.blockNumber, /^\d+$/);
  assert.equal(check.gasPrice, '1000');
  await assert.rejects(new Web3Service({ ...node.provider(), chainId: 'mainnet-v1.0' }).check(), error => error.code === 'CHAIN_ID_MISMATCH');
  await assert.rejects(new Web3Service(node.provider('http://127.0.0.1:1')).check(), error => error.code === 'PROVIDER_UNAVAILABLE');
});

test('AlgorandSignsWithAPrivateKeyOrAWallet', { skip }, async () => {
  const { privateKey } = AlgorandWallets.getWallet(node.mnemonic);
  const keyService = new Web3Service(node.provider(), { signer: { privateKey }, pollingIntervalMs: 100 });
  const address = (await keyService.deploy(Notarizer, '', ['Key'])).tx.contractAddress;
  assert.equal(await keyService.callContractMethod('label', Notarizer, address), 'Key');

  const signed = [];
  const wallet = { address: node.address, signTransactions: async transactions => node.signAll(transactions, signed) };
  const walletService = new Web3Service(node.provider(), { signer: { wallet }, pollingIntervalMs: 100 });
  assert.equal((await walletService.deploy(Notarizer, '', ['Wallet'])).tx.status, '1');
  assert.equal(signed.length, 1);
  const refusing = { address: node.address, signTransactions: async transactions => transactions.map(() => null) };
  await assert.rejects(new Web3Service(node.provider(), { signer: { wallet: refusing } }).deploy(Notarizer, '', ['No']),
    error => error.code === 'USER_REJECTED');
});

test('AlgorandCallsContractMethodsWithNamedParameters', { skip }, async () => {
  const service = new Web3Service(node.provider(), node.signer());
  const token = await deployContract(service, 'TestToken', ['Coin', 'CN', '1000000']);
  await token.send('mintInitialSupply');
  assert.deepEqual(token.standards, ['ARC-200']);
  assert.equal(token.methods.find(method => method.name === 'deposit').kind, 'payable');
  assert.equal(await token.owner(), node.address);
  assert.equal(await token.call('arc200_balanceOf', { owner: node.address }), '1000000');

  const transfer = await token.call('arc200_transfer', { to: recipient, value: 250 });
  assert.deepEqual([transfer.name, transfer.tx.status], ['arc200_transfer', '1']);
  assert.equal(await token.read('arc200_balanceOf', { owner: recipient }), '250');
  assert.equal(await token.read('arc200_approve', { spender: recipient, value: '1' }), true);
  await token.send('deposit', { note: 'tip' }, { value: '12345' });
  assert.equal(await token.read('deposits'), '12345');
  const info = { standard: 'ARC-200', address: token.address, name: 'Coin', symbol: 'CN', decimals: '6', totalSupply: '1000000' };
  assert.deepEqual(await token.getTokenInfo(), info);

  const details = await token.getTransaction(transfer.tx.transactionHash);
  const method = { name: 'arc200_transfer', signature: 'arc200_transfer(address,uint256)bool', parameters: { to: recipient, value: '250' } };
  assert.deepEqual(details.method, method);
  assert.deepEqual([details.pending, details.value, details.from, details.receipt.status], [false, '0', node.address, '1']);
  assert.equal(details.nativeFee.amount, transfer.nativeFee.amount);
  assert.match(details.timestamp, /^\d+$/);
  assert.equal((await service.getTransaction(transfer.tx.transactionHash)).method, undefined);
  assert.equal(await service.getContract(Notarizer, token.address).owner(), undefined);

  await assert.rejects(service.getTransaction(NOT_A_TRANSACTION), error => error.code === 'TRANSACTION_NOT_FOUND');
  await assert.rejects(token.call('arc200_transfer', { to: recipient }), error => error.code === 'INVALID_ARGUMENT');
  await assert.rejects(service.getContract(Notarizer, token.address).getTokenInfo(), error => error.code === 'NOT_SUPPORTED');
});

test('AlgorandMintsNftsAndReadsTokenData', { skip }, async () => {
  const service = new Web3Service(node.provider(), node.signer());
  const nft = await deployContract(service, 'TestNft');
  assert.deepEqual(nft.standards, ['ARC-72', 'ARC-72 Enumeration', 'ARC-72 Management', 'ARC-73']);
  const minted = await nft.mintNft({ to: node.address, uri: 'ipfs://art/1' });
  assert.deepEqual([minted.standard, minted.tokenIds, minted.transaction.name, minted.transaction.tx.status], ['ARC-72', ['1'], 'arc72_mint', '1']);
  const info = { standard: 'ARC-72', address: nft.address, tokenId: '1', totalSupply: '1', owner: node.address, tokenURI: 'ipfs://art/1' };
  assert.deepEqual(await nft.getTokenInfo(1), info);
  await nft.call('arc72_transferFrom', { from_: node.address, to: recipient, tokenId: '1' });
  assert.equal(await nft.read('arc72_ownerOf', { tokenId: 1 }), recipient);
  assert.equal(await nft.read('supportsInterface', { interfaceId: '0x53f02a40' }), true);

  const minter = await deployContract(service, 'TestAssetMinter');
  const asset = await minter.mintNft({ uri: 'ipfs://asset/1' });
  assert.deepEqual([asset.standard, asset.transaction.tx.status], ['ASA', '1']);
  const assetInfo = await service.getTokenInfo(asset.tokenIds[0]);
  assert.deepEqual([assetInfo.name, assetInfo.symbol, assetInfo.totalSupply, assetInfo.tokenURI], ['Minted', 'MNT', '1', 'ipfs://asset/1']);

  await assert.rejects(nft.mintNft({ to: node.address }), error => error.code === 'INVALID_ARGUMENT');
  await assert.rejects(service.getContract(contracts.TestToken, nft.address).mintNft({}), error => error.code === 'NOT_SUPPORTED');
});

test('AlgorandCreatesStandardAssets', { skip }, async () => {
  const service = new Web3Service(node.provider(), node.signer());
  const token = { name: 'Certifie Coin', symbol: 'CRT', totalSupply: '1000000', decimals: 2, uri: 'https://certifie.example' };
  const created = await service.createToken(token, { requestedAt: '1' });
  assert.deepEqual([created.standard, created.transaction.name, created.transaction.tx.status, created.transaction.tx.type], ['ASA', 'createToken', '1', 'acfg']);
  const info = { standard: 'ASA', address: created.tokenId, name: token.name, symbol: token.symbol, decimals: '2', totalSupply: '1000000', tokenURI: token.uri, creator: node.address };
  assert.deepEqual(await service.getTokenInfo(created.tokenId), info);

  await assert.rejects(service.getTokenInfo('999999999'), error => error.code === 'TOKEN_NOT_FOUND');
  await assert.rejects(service.createToken({ ...token, symbol: 'TOO-LONG-SYMBOL' }), error => error.code === 'INVALID_ARGUMENT');
});
