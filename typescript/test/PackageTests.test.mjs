// TypeScript only: the npm entry points and the browser bundles.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { runInNewContext } from 'node:vm';
import ganache from 'ganache';
import { Web3Service } from '@certifie/web3-service';
import { contracts } from './fixtures/contracts.mjs';
import { vectors } from './fixtures/TestVectors.mjs';

const { abi, bytecode } = contracts.Notarizer;

const browserDir = new URL('../dist/browser/', import.meta.url);

test('ESM and CommonJS package entry points expose the public API', () => {
  const cjs = createRequire(import.meta.url)('@certifie/web3-service');
  assert.equal(typeof cjs.Web3Service, 'function');
  assert.equal(typeof cjs.Web3Error, 'function');
  assert.equal(typeof cjs.Web3Contract, 'function');
});

test('a service never serializes its credentials', () => {
  const { privateKey } = vectors.wallets[0];
  const service = new Web3Service('http://localhost', { signer: { privateKey } });
  assert.equal(JSON.stringify(service).includes(privateKey), false);
});

test('the <script> bundle defines the CertifieWeb3 global without Node globals', async () => {
  const code = await readFile(new URL('web3-service.min.js', browserDir), 'utf8');
  assert.doesNotMatch(code, /require\(["']node:/);
  const window = { TextEncoder, TextDecoder, crypto, URL, AbortSignal, fetch, setTimeout, clearTimeout };
  window.window = window;
  window.globalThis = window;
  runInNewContext(code, window);
  assert.equal(typeof window.CertifieWeb3.Web3Service, 'function');
  assert.equal(typeof window.CertifieWeb3.Web3Error, 'function');
  assert.equal(window.CertifieWeb3.Web3Service.normalizeChainId(137), '0x89');
  assert.equal(new window.CertifieWeb3.Web3Service('https://node.example').provider.nativeCurrency.symbol, 'ETH');
  assert.equal(window.CertifieWeb3.Web3Service.detectStandards(vectors.contractAbi.abi).length, 0);
});

test('the self-contained ESM bundle deploys and notarizes over HTTP with an EIP-1193 wallet', async t => {
  const server = ganache.server({ chain: { chainId: 1337 }, wallet: { mnemonic: vectors.mnemonic }, logging: { quiet: true } });
  await server.listen(0, '127.0.0.1');
  t.after(async () => { await server.close(); });
  const rpcURI = `http://127.0.0.1:${server.address().port}`;
  let id = 0;
  const rpc = async (method, params = []) => {
    const response = await fetch(rpcURI, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', id: ++id, method, params }) });
    const body = await response.json();
    if (body.error) throw Object.assign(new Error(body.error.message), { code: body.error.code });
    return body.result;
  };
  // Stand-in for window.ethereum: the node's unlocked account signs, as MetaMask would.
  const wallet = { request: ({ method, params }) => rpc(method === 'eth_requestAccounts' ? 'eth_accounts' : method, params) };
  const bundle = await import(new URL('web3-service.esm.min.js', browserDir));
  const service = new bundle.Web3Service({ rpcURI, chainId: 1337 }, { signer: { wallet } });
  const deployed = await service.deploy(abi, bytecode, ['Browser bundle']);
  assert.equal(deployed.tx.status, '1');
  const notarization = { recipient: deployed.tx.contractAddress, hash: 'e'.repeat(64), algorithm: 'sha2', uri: '' };
  const minted = await service.getContract(abi, deployed.tx.contractAddress).send('mint', notarization);
  assert.equal(minted.tx.status, '1');
  assert.equal(await service.callContractMethod('label', abi, deployed.tx.contractAddress), 'Browser bundle');
});
