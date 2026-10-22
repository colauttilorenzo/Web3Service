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

const document = letter => ({ hash: { data: letter.repeat(64), algorithm: 'sha2' } });

function assertIntegerStrings(value) {
  assert.notEqual(typeof value, 'number');
  assert.notEqual(typeof value, 'bigint');
  if (value && typeof value === 'object') Object.values(value).forEach(assertIntegerStrings);
}

test('DeploysNotarizesReadsAndReturnsJsonSafeReceipts', async () => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const deployed = await service.deploy(chain.abi, chain.bytecode, [vectors.contract.label]);
  assert.deepEqual([deployed.name, deployed.tx.status, deployed.tx.type], ['deploy', '1', '0']);
  assert.match(deployed.completedAt, /^\d+$/);
  const address = deployed.tx.contractAddress;
  assert.equal(await service.callContractMethod('label', chain.abi, address), vectors.contract.label);

  const notarized = { ...document('a'), metadata: 'ipfs://example' };
  const minted = await service.mint(chain.abi, address, notarized, { requestedAt: '123', rejectedAt: '999' });
  assert.deepEqual([minted.name, minted.tx.status, minted.requestedAt], ['mint', '1', '123']);
  assert.equal(minted.rejectedAt, undefined);
  assert.equal(minted.tx.logs[0].logIndex, '0');
  assert.equal(minted.nativeFee.amount, new Big(minted.tx.gasUsed).times(minted.tx.effectiveGasPrice).toFixed(0));
  assert.deepEqual([minted.nativeFee.symbol, minted.nativeFee.decimals], ['ETH', '18']);

  const metadata = await service.callContractMethod('metadata', chain.abi, address, [notarized.hash.data]);
  assert.deepEqual(metadata, [notarized.hash.data, 'sha2', 'ipfs://example']);
  assert.deepEqual(await service.callContractMethod('numbers', chain.abi, address), vectors.contract.numbers);

  const confirmed = await service.getTransactionReceipt(minted.tx.transactionHash, { rejectedAt: '1' });
  assert.equal(confirmed.nativeFee.amount, minted.nativeFee.amount);
  assert.equal(confirmed.rejectedAt, undefined);
  assertIntegerStrings(confirmed);

  await assert.rejects(service.getTransactionReceipt(`0x${'0'.repeat(64)}`), error => error.code === 'TRANSACTION_NOT_FOUND');
  await assert.rejects(service.callContractMethod('missing', chain.abi, address), error => error.code === 'METHOD_NOT_FOUND');
  await assert.rejects(new Web3Service(chain.provider()).mint(chain.abi, address, notarized), error => error.code === 'SIGNER_REQUIRED');
});

test('ConcurrentWritesUseDistinctNoncesAndRevertsAreReported', async () => {
  const service = new Web3Service(chain.provider(), chain.signer());
  const address = (await service.deploy(chain.abi, chain.bytecode, ['Nonces'])).tx.contractAddress;
  const results = await Promise.all(['b', 'c'].map(letter => service.mint(chain.abi, address, document(letter))));
  for (const result of results) assert.equal(result.tx.status, '1');

  let failure;
  await assert.rejects(service.mint(chain.abi, address, document('b'), undefined, { gas: 300000 }), error => (failure = error).code === 'TRANSACTION_REVERTED');
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
