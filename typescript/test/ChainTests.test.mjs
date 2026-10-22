// Mirrors dotnet/test/Certifie.Web3Service.Tests/ChainTests.cs: same test names, same chain, same assertions.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import Big from 'big.js';
import { Web3Service } from '@certifie/web3-service';
import { Chain } from './fixtures/Chain.mjs';
import { RpcProxy } from './fixtures/RpcProxy.mjs';
import { vectors } from './fixtures/TestVectors.mjs';

let chain;
before(async () => { chain = await Chain.start(); });
after(() => chain.close());

// Notarization is a call to mint(address,string,string,string) on the test contract, whose own address is the recipient.
const notarization = (address, letter, uri = '') => ({ recipient: address, hash: letter.repeat(64), algorithm: 'sha2', uri });

function assertIntegerStrings(value) {
  assert.notEqual(typeof value, 'number');
  assert.notEqual(typeof value, 'bigint');
  if (value && typeof value === 'object') Object.values(value).forEach(assertIntegerStrings);
}

test('DeploysNotarizesReadsAndReturnsJsonSafeReceipts', async () => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const deployed = await service.deploy(chain.abi, chain.bytecode, [vectors.contract.label], { requestedAt: '123', rejectedAt: '999' });
  assert.deepEqual([deployed.name, deployed.tx.status, deployed.tx.type, deployed.requestedAt], ['deploy', '1', '0', '123']);
  assert.equal(deployed.rejectedAt, undefined);
  assert.match(deployed.completedAt, /^\d+$/);
  const address = deployed.tx.contractAddress;
  assert.equal(await service.callContractMethod('label', chain.abi, address), vectors.contract.label);

  const notarized = notarization(address, 'a', 'ipfs://example');
  const minted = await service.getContract(chain.abi, address).send('mint', notarized);
  assert.deepEqual([minted.name, minted.tx.status], ['mint', '1']);
  assert.equal(minted.tx.logs[0].logIndex, '0');
  assert.equal(minted.nativeFee.amount, new Big(minted.tx.gasUsed).times(minted.tx.effectiveGasPrice).toFixed(0));
  assert.deepEqual([minted.nativeFee.symbol, minted.nativeFee.decimals], ['ETH', '18']);

  const metadata = await service.callContractMethod('metadata', chain.abi, address, [notarized.hash]);
  assert.deepEqual(metadata, [notarized.hash, 'sha2', 'ipfs://example']);
  assert.deepEqual(await service.callContractMethod('numbers', chain.abi, address), vectors.contract.numbers);

  const confirmed = await service.getTransactionReceipt(minted.tx.transactionHash, { rejectedAt: '1' });
  assert.equal(confirmed.nativeFee.amount, minted.nativeFee.amount);
  assert.equal(confirmed.rejectedAt, undefined);
  assertIntegerStrings(confirmed);

  await assert.rejects(service.getTransactionReceipt(`0x${'0'.repeat(64)}`), error => error.code === 'TRANSACTION_NOT_FOUND');
  await assert.rejects(service.callContractMethod('missing', chain.abi, address), error => error.code === 'METHOD_NOT_FOUND');
  await assert.rejects(new Web3Service(chain.provider()).getContract(chain.abi, address).send('mint', notarized), error => error.code === 'SIGNER_REQUIRED');
});

test('ConcurrentWritesUseDistinctNoncesAndRevertsAreReported', async () => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const address = (await service.deploy(chain.abi, chain.bytecode, ['Nonces'])).tx.contractAddress;
  const contract = service.getContract(chain.abi, address);
  const results = await Promise.all(['b', 'c'].map(letter => contract.send('mint', notarization(address, letter))));
  for (const result of results) assert.equal(result.tx.status, '1');

  let failure;
  await assert.rejects(contract.send('mint', notarization(address, 'b'), { gas: 300000 }), error => (failure = error).code === 'TRANSACTION_REVERTED');
  assert.match(failure.transactionHash, /^0x[0-9a-f]{64}$/);
  const reverted = await service.getTransactionReceipt(failure.transactionHash, { completedAt: '1' });
  assert.equal(reverted.tx.status, '0');
  assert.equal(reverted.completedAt, undefined);
  assert.match(reverted.rejectedAt, /^\d+$/);
  assert.equal(reverted.error.code, 'TRANSACTION_REVERTED');
  assert.ok(reverted.nativeFee);
});

test('PrivateKeySignerAndWebSocketUriWork', async () => {
  const privateKey = await Web3Service.getPrivateKey(await Web3Service.getMnemonic(chain.mnemonic));
  const service = new Web3Service(chain.provider(chain.webSocket), { signer: { privateKey }, pollingIntervalMs: 100 });
  const deployed = await service.deploy(chain.abi, chain.bytecode, ['WebSocket']);
  assert.equal(deployed.tx.status, '1');
  assert.equal(await service.callContractMethod('label', chain.abi, deployed.tx.contractAddress), 'WebSocket');
  assert.match((await service.check()).gasPrice, /^\d+$/);
});

test('FallsBackToTheSecondEndpointAndValidatesTheChain', async () => {
  const fallback = new Web3Service({ rpcURI: 'http://127.0.0.1:1', rpcPublicURI: chain.http, chainId: '1337' });
  assert.match((await fallback.check()).blockNumber, /^\d+$/);
  await assert.rejects(new Web3Service(chain.http).check(), error => error.code === 'CHAIN_ID_MISMATCH');
  await assert.rejects(new Web3Service('http://127.0.0.1:1').check(), error => error.code === 'PROVIDER_UNAVAILABLE');
});

test('AStalledEndpointTimesOutAndFallsBack', async t => {
  const stalled = await RpcProxy.start(chain.http, 'stall');
  t.after(() => stalled.close());
  const service = new Web3Service({ rpcURI: stalled.uri, rpcPublicURI: chain.http, chainId: '1337' }, { timeoutMs: 300 });
  assert.match((await service.check()).blockNumber, /^\d+$/);
});

test('RecoversAMinedTransactionAfterALostBroadcastResponse', async t => {
  const proxy = await RpcProxy.start(chain.http, 'loseBroadcast');
  t.after(() => proxy.close());
  const service = new Web3Service(chain.provider(proxy.uri), chain.signer());
  const deployed = await service.deploy(chain.abi, chain.bytecode, ['Recovered']);
  assert.equal(deployed.tx.status, '1');
  assert.equal(proxy.broadcasts, 1);
});

test('ComputesExactFeesBeyondSafeIntegers', async t => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const deployed = await service.deploy(chain.abi, chain.bytecode, ['Fees']);
  const proxy = await RpcProxy.start(chain.http, 'largeFees');
  t.after(() => proxy.close());
  const receipt = await new Web3Service(chain.provider(proxy.uri)).getTransactionReceipt(deployed.tx.transactionHash);
  assert.equal(receipt.nativeFee.amount, vectors.largeFee.amount);
});

const [holder, recipient] = [vectors.wallets[0].address, vectors.wallets[1].address];

async function deployContract(service, name, constructorArguments) {
  const { abi, bytecode } = chain.contracts[name];
  return service.getContract(abi, (await service.deploy(abi, bytecode, constructorArguments)).tx.contractAddress);
}

test('CallsContractFunctionsWithNamedParameters', async () => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const token = await deployContract(service, 'TestToken', ['Coin', 'CN', '1000000']);
  assert.deepEqual(token.standards, ['ERC-20', 'ERC-173']);
  assert.deepEqual(token.methods.find(method => method.name === 'deposit').kind, 'payable');
  assert.equal(await token.owner(), holder);
  assert.equal(await token.call('balanceOf', { arg0: holder }), '1000000');

  const transfer = await token.call('transfer', { to: recipient, amount: 250 });
  assert.deepEqual([transfer.name, transfer.tx.status], ['transfer', '1']);
  assert.equal(await token.read('balanceOf', { arg0: recipient }), '250');
  assert.equal(await token.read('approve', { spender: recipient, amount: '1' }), true);
  await token.send('deposit', { note: 'tip' }, { value: '12345' });
  assert.equal(await token.read('deposits'), '12345');
  assert.deepEqual(await token.getTokenInfo(), { standard: 'ERC-20', address: token.address, name: 'Coin', symbol: 'CN', decimals: '18', totalSupply: '1000000' });

  const details = await token.getTransaction(transfer.tx.transactionHash);
  assert.deepEqual(details.method, { name: 'transfer', signature: 'transfer(address,uint256)', parameters: { to: recipient, amount: '250' } });
  assert.deepEqual([details.pending, details.value, details.from.toLowerCase(), details.receipt.status], [false, '0', holder.toLowerCase(), '1']);
  assert.equal(details.nativeFee.amount, transfer.nativeFee.amount);
  assert.match(details.timestamp, /^\d+$/);
  assert.equal((await service.getTransaction(transfer.tx.transactionHash)).method, undefined);
  assert.equal(await service.getContract(chain.abi, token.address).owner(), undefined);

  await assert.rejects(service.getTransaction(`0x${'0'.repeat(64)}`), error => error.code === 'TRANSACTION_NOT_FOUND');
  await assert.rejects(token.call('transfer', { to: recipient }), error => error.code === 'INVALID_ARGUMENT');
  await assert.rejects(service.getContract(chain.abi, token.address).getTokenInfo(), error => error.code === 'NOT_SUPPORTED');
});

test('MintsNftsAndReadsTokenData', async () => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const nft = await deployContract(service, 'TestNft', ['Art', 'ART']);
  assert.deepEqual(nft.standards, ['ERC-721', 'ERC-721 Metadata', 'ERC-165']);
  const minted = await nft.mintNft({ to: holder, uri: 'ipfs://art/1' });
  assert.deepEqual([minted.standard, minted.tokenIds, minted.transaction.name, minted.transaction.tx.status], ['ERC-721', ['1'], 'safeMint', '1']);
  const info = { standard: 'ERC-721', address: nft.address, tokenId: '1', name: 'Art', symbol: 'ART', totalSupply: '1', owner: holder, tokenURI: 'ipfs://art/1' };
  assert.deepEqual(await nft.getTokenInfo(1), info);
  await nft.call('safeTransferFrom', { from: holder, to: recipient, tokenId: '1' });
  assert.equal(await nft.read('ownerOf', { tokenId: 1 }), recipient);

  const multi = await deployContract(service, 'TestMultiToken', ['ipfs://multi/{id}.json']);
  assert.deepEqual(multi.standards, ['ERC-1155', 'ERC-1155 Metadata URI']);
  assert.deepEqual((await multi.mintNft({ to: holder, id: 7, amount: 3, arg3: '0x' })).tokenIds, ['7']);
  assert.deepEqual((await multi.mintNft({ to: holder, ids: [8, 9], amounts: [1, 2] })).tokenIds, ['8', '9']);
  assert.deepEqual(await multi.read('balanceOfBatch', { accounts: [holder, holder], ids: [7, 9] }), ['3', '2']);
  assert.equal((await multi.getTokenInfo('7')).tokenURI, `ipfs://multi/${'7'.padStart(64, '0')}.json`);

  await assert.rejects(nft.mintNft({ to: holder }), error => error.code === 'INVALID_ARGUMENT');
  await assert.rejects(service.getContract(chain.abi, nft.address).mintNft({}), error => error.code === 'NOT_SUPPORTED');
});
