// TypeScript only: browser wallets (EIP-1193) exist only in JavaScript, so .NET has no WalletSigner.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import ganache from 'ganache';
import { Web3 } from 'web3';
import { Web3Service } from '@certifie/web3-service';
import { contracts } from './fixtures/contracts.mjs';
import { vectors } from './fixtures/TestVectors.mjs';

const { abi, bytecode } = contracts.Notarizer;

const document = { hash: { data: 'd'.repeat(64), algorithm: 'sha2' }, metadata: 'ipfs://wallet' };

/** An EIP-1193 wallet like MetaMask: it holds the key, signs and broadcasts; the page never sees the key. */
function browserWallet(chain, { chainId = '0x539', knownChains = ['0x539'], reject = false } = {}) {
  const calls = [];
  let current = chainId;
  const wallet = {
    calls,
    async request({ method, params }) {
      calls.push(method);
      switch (method) {
        case 'eth_requestAccounts': return (await chain.request({ method: 'eth_accounts' })).slice(0, 1);
        case 'eth_chainId': return current;
        case 'wallet_switchEthereumChain':
          if (!knownChains.includes(params[0].chainId)) throw Object.assign(new Error('Unrecognized chain'), { code: 4902 });
          current = params[0].chainId;
          return null;
        case 'wallet_addEthereumChain':
          wallet.added = params[0];
          knownChains.push(params[0].chainId);
          current = params[0].chainId;
          return null;
        case 'eth_sendTransaction':
          if (reject) throw Object.assign(new Error('User denied transaction signature'), { code: 4001 });
          wallet.sent = params[0];
          return chain.request({ method, params });
        default: throw new Error(`Unexpected wallet method ${method}`);
      }
    },
  };
  return wallet;
}

function localChain(t) {
  const chain = ganache.provider({ chain: { chainId: 1337 }, wallet: { mnemonic: vectors.mnemonic }, logging: { quiet: true } });
  t.after(async () => { await chain.disconnect(); });
  return chain;
}

test('a browser wallet signs deploy and mint while reads and receipts use the RPC node', async t => {
  const chain = localChain(t);
  const wallet = browserWallet(chain);
  const service = new Web3Service({ rpcURI: 'http://node.test', chainId: 1337 }, { signer: { wallet }, web3Factory: () => new Web3(chain) });
  const deployed = await service.deploy(abi, bytecode, ['Wallet']);
  assert.equal(deployed.tx.status, '1');
  assert.match(deployed.nativeFee.amount, /^\d+$/);
  assert.equal(wallet.sent.gasPrice, undefined, 'fees are left to the wallet');
  assert.match(wallet.sent.gas, /^0x[0-9a-f]+$/);
  const address = deployed.tx.contractAddress;
  const minted = await service.mint(abi, address, document);
  assert.equal(minted.name, 'mint');
  assert.equal(minted.tx.from.toLowerCase(), wallet.sent.from.toLowerCase());
  const metadata = await service.callContractMethod('metadata', abi, address, [document.hash.data]);
  assert.equal(metadata[2], 'ipfs://wallet');
  assert.equal(wallet.calls.filter(method => method === 'eth_sendTransaction').length, 2);
  assert.equal(wallet.calls.includes('wallet_switchEthereumChain'), false);
});

test('the wallet is switched to the configured chain, adding it when unknown', async t => {
  const chain = localChain(t);
  const wallet = browserWallet(chain, { chainId: '0x1', knownChains: ['0x1'] });
  const service = new Web3Service({
    rpcURI: 'http://node.test', rpcPublicURI: 'https://public.node.test', chainId: 1337, chainName: 'Local EVM',
    nativeCurrency: { symbol: 'GAS', decimals: 18 }, blockExplorerURI: 'https://explorer.test',
  }, { signer: { wallet }, web3Factory: () => new Web3(chain) });
  assert.equal((await service.deploy(abi, bytecode, ['Switched'])).tx.status, '1');
  assert.deepEqual(wallet.added, {
    chainId: '0x539', chainName: 'Local EVM', nativeCurrency: { name: 'Ether', symbol: 'GAS', decimals: 18 },
    rpcUrls: ['https://public.node.test', 'http://node.test'], blockExplorerUrls: ['https://explorer.test'],
  });

  const fixed = browserWallet(chain, { chainId: '0x1', knownChains: ['0x1', '0x539'] });
  const noSwitch = new Web3Service({ rpcURI: 'http://node.test', chainId: 1337 }, { signer: { wallet: fixed, switchChain: false }, web3Factory: () => new Web3(chain) });
  await assert.rejects(noSwitch.deploy(abi, bytecode, ['No switch']), error => error.code === 'CHAIN_ID_MISMATCH');
  assert.equal(fixed.calls.includes('eth_sendTransaction'), false);
});

test('a wallet cannot be combined with a private key, and must be an EIP-1193 provider', async t => {
  const chain = localChain(t);
  const wallet = browserWallet(chain);
  const signer = { wallet, privateKey: vectors.wallets[0].privateKey };
  const service = new Web3Service({ rpcURI: 'http://node.test', chainId: 1337 }, { signer, web3Factory: () => new Web3(chain) });
  await assert.rejects(service.deploy(abi, bytecode, ['Both']), error => error.code === 'INVALID_ARGUMENT');
  assert.deepEqual(wallet.calls, []);
  const algorandWallet = { address: 'ADDRESS', signTransactions: async () => [] };
  const mismatched = new Web3Service({ rpcURI: 'http://node.test', chainId: 1337 }, { signer: { wallet: algorandWallet }, web3Factory: () => new Web3(chain) });
  await assert.rejects(mismatched.deploy(abi, bytecode, ['Algorand wallet']), error => error.code === 'INVALID_ARGUMENT');
});

test('a transaction rejected in the wallet reports USER_REJECTED and broadcasts nothing', async t => {
  const chain = localChain(t);
  const wallet = browserWallet(chain, { reject: true });
  const service = new Web3Service({ rpcURI: 'http://node.test', chainId: 1337 }, { signer: { wallet }, web3Factory: () => new Web3(chain) });
  const before = await chain.request({ method: 'eth_blockNumber' });
  await assert.rejects(service.deploy(abi, bytecode, ['Rejected']), error => error.code === 'USER_REJECTED' && !('cause' in error.toJSON()));
  assert.equal(await chain.request({ method: 'eth_blockNumber' }), before);
});
