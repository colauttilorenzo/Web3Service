// Mirrors dotnet/test/Certifie.Web3Service.Tests/UnitTests.cs: same test names, same shared vectors.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Web3Error, Web3Service } from '@certifie/web3-service';
import { vectors } from './fixtures/TestVectors.mjs';

test('AppliesEthereumDefaultsAndPartialOverrides', () => {
  const { defaults, customProvider } = vectors;
  const service = new Web3Service('https://node.example');
  assert.equal(service.chainId, defaults.chainId);
  assert.equal(service.provider.chainName, defaults.chainName);
  assert.deepEqual(service.provider.nativeCurrency, { name: defaults.currencyName, symbol: defaults.symbol, decimals: defaults.decimals });
  const custom = new Web3Service({ rpcURI: customProvider.rpcURI, chainId: customProvider.chainId, nativeCurrency: { symbol: customProvider.symbol, decimals: customProvider.decimals } });
  assert.equal(custom.chainId, customProvider.normalizedChainId);
  assert.deepEqual(custom.provider.nativeCurrency, { name: defaults.currencyName, symbol: customProvider.symbol, decimals: customProvider.decimals });
  assert.equal(new Web3Service({ rpcURI: 'http://localhost', chainId: '0' }).chainId, '0x0');
});

test('NormalizesChainIdsAndValidatesInput', () => {
  for (const { input, hex } of vectors.chainIds) assert.equal(Web3Service.normalizeChainId(input), hex);
  assert.equal(Web3Service.normalizeChainId(137), '0x89');
  assert.equal(Web3Service.normalizeChainId(), undefined);
  for (const id of vectors.invalidChainIds) assert.throws(() => Web3Service.normalizeChainId(id), error => error.code === 'INVALID_ARGUMENT');
  for (const uri of vectors.validUris) assert.equal(new Web3Service(uri).provider.rpcURI, uri);
  for (const uri of vectors.invalidUris) assert.throws(() => new Web3Service(uri), error => error.code === 'INVALID_ARGUMENT');
  assert.throws(() => new Web3Service('http://localhost', { timeoutMs: 0 }), error => error.code === 'INVALID_ARGUMENT');
  assert.equal(Web3Service.shrinkAddress(vectors.shrinkAddress.input), vectors.shrinkAddress.output);
});

test('DerivesTheSharedTestVectorWallets', async () => {
  for (const expected of vectors.wallets) {
    const wallet = await Web3Service.getWallet(await Web3Service.getMnemonic(vectors.mnemonic, expected.password), expected.index);
    assert.deepEqual(wallet, { address: expected.address, publicKey: expected.publicKey, privateKey: expected.privateKey, path: expected.path, index: String(expected.index) });
  }
  const mnemonic = await Web3Service.getMnemonic(vectors.mnemonic);
  assert.equal(await Web3Service.getPrivateKey(mnemonic), vectors.wallets[0].privateKey);
  await assert.rejects(Web3Service.getWallet(mnemonic, -1), error => error.code === 'INVALID_ARGUMENT');
  assert.equal((await Web3Service.createMnemonic()).phrase.split(' ').length, 12);
});

test('SerializedErrorsUseTheSharedShape', () => {
  const { code, message, secret, json } = vectors.error;
  const error = new Web3Error(code, message, { cause: new Error(secret) });
  assert.deepEqual(JSON.parse(JSON.stringify(error)), json);
});
