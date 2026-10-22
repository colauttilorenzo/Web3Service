// TypeScript only: the HTTP and WebSocket transports of web3.js, which .NET replaces with HttpClient and WebSocketClient.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer as createHttpServer } from 'node:http';
import { createServer as createHttpsServer } from 'node:https';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { WebSocketServer } from 'ws';
import { Web3Service } from '@certifie/web3-service';

const fixtures = new URL('./fixtures/', import.meta.url);
const results = { eth_chainId: '0x1', eth_accounts: [], eth_blockNumber: '0x2', eth_gasPrice: '0x3b9aca00' };

async function rpcServer(t, { secure = false, stalled = false, chainId = '0x1', http = false } = {}) {
  const calls = [];
  const closed = [];
  const server = secure ? createHttpsServer({
    key: await readFile(new URL('localhost-key.pem', fixtures)),
    cert: await readFile(new URL('localhost-cert.pem', fixtures)),
  }) : createHttpServer(async (req, res) => {
    let body = '';
    for await (const chunk of req) body += chunk;
    const { id, method } = JSON.parse(body);
    calls.push(method);
    res.setHeader('content-type', 'application/json');
    res.end(JSON.stringify({ jsonrpc: '2.0', id, result: { ...results, eth_chainId: chainId }[method] }));
  });
  const sockets = new WebSocketServer({ server });
  sockets.on('connection', socket => {
    closed.push(once(socket, 'close'));
    socket.on('message', data => {
      const { id, method } = JSON.parse(data.toString());
      calls.push(method);
      if (!stalled) socket.send(JSON.stringify({ jsonrpc: '2.0', id, result: { ...results, eth_chainId: chainId }[method] }));
    });
  });
  t.after(async () => {
    for (const socket of sockets.clients) socket.terminate();
    await new Promise(resolve => sockets.close(resolve));
    server.closeAllConnections();
    if (server.listening) await new Promise(resolve => server.close(resolve));
  });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  return { uri: `${secure ? 'wss' : http ? 'http' : 'ws'}://127.0.0.1:${server.address().port}`, calls, closed, sockets };
}

test('WebSocket URI and IEvmProvider inputs close their sockets after each operation', async t => {
  const rpc = await rpcServer(t);
  const fromUri = new Web3Service(rpc.uri);
  const fromObject = new Web3Service({ rpcURI: rpc.uri, chainName: 'Custom Ethereum' });
  for (const service of [fromUri, fromObject]) {
    assert.deepEqual(await service.check(), { blockNumber: '2', gasPrice: '1000000000' });
  }
  await Promise.all(rpc.closed);
  assert.equal(rpc.closed.length, 2);
  assert.equal(rpc.sockets.clients.size, 0);
});

test('WSS connects with verified TLS and does not keep the consumer process alive', async t => {
  const rpc = await rpcServer(t, { secure: true });
  const script = `
    import assert from 'node:assert/strict';
    import { Web3Service } from '@certifie/web3-service';
    const service = new Web3Service(process.env.TEST_RPC_URI, { timeoutMs: 2000 });
    assert.deepEqual(await service.check(), { blockNumber: '2', gasPrice: '1000000000' });
    console.log('WSS OK');
  `;
  const { stdout } = await promisify(execFile)(process.execPath, ['--input-type=module', '-e', script], {
    cwd: fileURLToPath(new URL('../', import.meta.url)),
    timeout: 10000,
    env: { ...process.env, TEST_RPC_URI: rpc.uri, NODE_EXTRA_CA_CERTS: fileURLToPath(new URL('localhost-cert.pem', fixtures)) },
  });
  assert.match(stdout, /WSS OK/);
  await Promise.all(rpc.closed);
  assert.equal(rpc.sockets.clients.size, 0);
});

test('a stalled WebSocket endpoint times out and falls back to HTTP', async t => {
  const primary = await rpcServer(t, { stalled: true });
  const fallback = await rpcServer(t, { http: true });
  const service = new Web3Service({ rpcURI: primary.uri, rpcPublicURI: fallback.uri }, { timeoutMs: 150 });
  assert.deepEqual(await service.check(), { blockNumber: '2', gasPrice: '1000000000' });
  assert.ok(fallback.calls.includes('eth_chainId'));
  await Promise.all([...primary.closed, ...fallback.closed]);
  assert.equal(primary.sockets.clients.size, 0);
  assert.equal(fallback.sockets.clients.size, 0);
});

test('a WebSocket endpoint on the wrong chain is closed and rejected', async t => {
  const rpc = await rpcServer(t, { chainId: '0x2' });
  await assert.rejects(new Web3Service(rpc.uri).check(), error => error.code === 'CHAIN_ID_MISMATCH');
  await Promise.all(rpc.closed);
  assert.equal(rpc.sockets.clients.size, 0);
});

test('HTTP requests get fresh timeouts and a stalled primary falls back', async t => {
  const stalled = createHttpServer(() => {});
  const calls = [];
  const healthy = createHttpServer(async (req, res) => {
    let body = '';
    for await (const chunk of req) body += chunk;
    const { id, method } = JSON.parse(body);
    calls.push(method);
    res.setHeader('content-type', 'application/json');
    res.end(JSON.stringify({ jsonrpc: '2.0', id, result: { eth_chainId: '0x539', eth_blockNumber: '0x2', eth_gasPrice: '0x3b9aca00' }[method] }));
  });
  t.after(async () => {
    for (const server of [stalled, healthy]) {
      server.closeAllConnections();
      if (server.listening) await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
  for (const server of [stalled, healthy]) {
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
  }
  const provider = {
    chainId: 1337, rpcURI: `http://127.0.0.1:${stalled.address().port}`,
    rpcPublicURI: `http://127.0.0.1:${healthy.address().port}`,
    nativeCurrency: { symbol: 'ETH', decimals: 18 },
  };
  const service = new Web3Service(provider, { timeoutMs: 150 });
  assert.deepEqual(await service.check(), { blockNumber: '2', gasPrice: '1000000000' });
  assert.ok(calls.includes('eth_chainId'));
  await new Promise(resolve => setTimeout(resolve, 180));
  assert.deepEqual(await service.check(), { blockNumber: '2', gasPrice: '1000000000' });
});
