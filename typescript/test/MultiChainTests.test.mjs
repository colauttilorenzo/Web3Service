// Mirrors dotnet/test/Certifie.Web3Service.Tests/MultiChainTests.cs: the provider selects the blockchain, and what each one offers
// without a node: Algorand accounts and ARC-4 contracts, Bitcoin through a local Esplora API, and NotImplementedError.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { AlgorandWallets, NotImplementedError, Web3Error, Web3Service } from '@certifie/web3-service';
import { EsploraServer } from './fixtures/EsploraServer.mjs';
import { vectors } from './fixtures/TestVectors.mjs';

const { algorand, bitcoin } = vectors;
const algorandService = () => new Web3Service({ kind: 'avm', rpcURI: 'http://localhost' });
const bitcoinService = () => new Web3Service({ kind: 'bitcoin', rpcURI: 'http://localhost' });
const currencyOf = defaults => ({ name: defaults.currencyName, symbol: defaults.symbol, decimals: defaults.decimals });
const isNotImplemented = error => error instanceof NotImplementedError && error instanceof Web3Error && error.code === 'NOT_IMPLEMENTED';

test('SelectsTheBlockchainFromTheProvider', () => {
  assert.equal(new Web3Service('http://localhost').kind, 'evm');
  assert.equal(new Web3Service({ kind: 'evm', rpcURI: 'http://localhost' }).chainId, vectors.defaults.chainId);
  for (const [service, defaults, kind] of [[algorandService(), algorand.defaults, 'avm'], [bitcoinService(), bitcoin.defaults, 'bitcoin']]) {
    assert.deepEqual([service.kind, service.chainId, service.provider.chainName], [kind, defaults.chainId, defaults.chainName]);
    assert.deepEqual(service.provider.nativeCurrency, currencyOf(defaults));
  }
  assert.equal(new Web3Service({ kind: 'avm', rpcURI: 'http://localhost', chainId: 'testnet-v1.0' }).chainId, 'testnet-v1.0');
  assert.throws(() => new Web3Service({ kind: 'solana', rpcURI: 'http://localhost' }), error => error.code === 'INVALID_ARGUMENT');
  assert.throws(() => new Web3Service({ kind: 'avm', rpcURI: 'ws://localhost' }), error => error.code === 'INVALID_ARGUMENT');
});

test('UnavailableOperationsThrowNotImplemented', async () => {
  const evm = new Web3Service('http://localhost');
  await assert.rejects(evm.createToken({ name: 'Coin', symbol: 'CN', totalSupply: '1' }), isNotImplemented);
  await assert.rejects(evm.getTokenInfo('1'), isNotImplemented);
  const service = bitcoinService();
  for (const operation of bitcoin.notImplemented) {
    await assert.rejects(async () => service[operation](), isNotImplemented, operation);
  }
});

test('DerivesTheSharedAlgorandWallets', () => {
  for (const { mnemonic, address, publicKey, privateKey } of algorand.wallets) {
    assert.deepEqual(AlgorandWallets.getWallet(mnemonic), { address, publicKey, privateKey, path: null, index: '0' });
  }
  assert.equal(AlgorandWallets.createMnemonic().split(' ').length, 25);
  assert.throws(() => AlgorandWallets.getWallet(vectors.mnemonic), error => error.code === 'INVALID_ARGUMENT');
});

test('DescribesArc4MethodsWithJsonParameterTypes', () => {
  const { abi, address, methods } = algorand.contract;
  const contract = algorandService().getContract(abi, address);
  assert.deepEqual(contract.methods, methods);
  assert.deepEqual([contract.address, contract.standards], [address, []]);
  for (const invalid of ['0', '-1', '1.5', algorand.wallets[0].address]) {
    assert.throws(() => algorandService().getContract(abi, invalid), error => error.code === 'INVALID_ARGUMENT', invalid);
  }
  assert.throws(() => algorandService().getContract(vectors.contractAbi.abi, address), error => error.code === 'INVALID_ARGUMENT');
  assert.throws(() => new Web3Service('http://localhost').getContract(abi, vectors.contractAbi.address), error => error.code === 'INVALID_ARGUMENT');
});

test('DetectsArcStandardsFromTheMethods', () => {
  for (const { methods, expected } of algorand.standards) {
    const abi = { methods: methods.map(name => ({ name, args: [], returns: { type: 'void' } })) };
    assert.deepEqual(Web3Service.detectStandards(abi), expected);
    assert.deepEqual(algorandService().getContract(abi, '1').standards, expected);
  }
});

test('EncodesNamedParametersAsApplicationArguments', () => {
  const { abi, address, encoded, invalid } = algorand.contract;
  const contract = algorandService().getContract(abi, address);
  for (const { method, parameters, data } of encoded) assert.equal(contract.encode(method, parameters), data, method);
  for (const { method, parameters } of invalid) {
    assert.throws(() => contract.encode(method, parameters), error => error.code === 'INVALID_ARGUMENT', JSON.stringify(parameters));
  }
  assert.throws(() => contract.encode('missing'), error => error.code === 'METHOD_NOT_FOUND');
});

test('ReadsBitcoinThroughEsplora', async t => {
  const esplora = await EsploraServer.start();
  t.after(() => esplora.close());
  const service = new Web3Service(esplora.provider());
  assert.deepEqual(await service.check(), bitcoin.check);
  const { txid } = bitcoin.esplora.transaction;
  assert.deepEqual(await service.getTransaction(txid), bitcoin.details);
  const receipt = await service.getTransactionReceipt(txid, { name: 'lookup', requestedAt: '1', rejectedAt: '2' });
  assert.deepEqual(JSON.parse(JSON.stringify(receipt)), bitcoin.receipt);
  await assert.rejects(service.getTransaction('00'.repeat(32)), error => error.code === 'TRANSACTION_NOT_FOUND');
  await assert.rejects(new Web3Service(esplora.provider('testnet')).check(), error => error.code === 'CHAIN_ID_MISMATCH');
});
