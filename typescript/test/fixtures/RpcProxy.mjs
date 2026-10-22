// An HTTP JSON-RPC proxy in front of the chain that simulates faulty nodes; the .NET tests have the same fixture (RpcProxy.cs).
import { createServer } from 'node:http';
import { once } from 'node:events';
import { vectors } from './TestVectors.mjs';

/** stall: never answers. loseBroadcast: forwards eth_sendRawTransaction, then answers HTTP 500. largeFees: rewrites receipt fees. */
export class RpcProxy {
  broadcasts = 0;

  constructor(target, mode) {
    this.target = target;
    this.mode = mode;
    this.server = createServer((request, response) => this.#handle(request, response));
  }

  static async start(target, mode) {
    const proxy = new RpcProxy(target, mode);
    proxy.server.listen(0, '127.0.0.1');
    await once(proxy.server, 'listening');
    proxy.uri = `http://127.0.0.1:${proxy.server.address().port}`;
    return proxy;
  }

  async #handle(request, response) {
    let body = '';
    for await (const chunk of request) body += chunk;
    if (this.mode === 'stall') return;
    const payload = JSON.parse(body);
    const forwarded = await fetch(this.target, { method: 'POST', headers: { 'content-type': 'application/json' }, body });
    const lost = this.mode === 'loseBroadcast' && payload.method === 'eth_sendRawTransaction';
    response.writeHead(lost ? 500 : 200, { 'content-type': 'application/json' });
    response.end(JSON.stringify(this.#rewrite(payload, await forwarded.json())));
  }

  #rewrite(payload, result) {
    if (this.mode === 'loseBroadcast' && payload.method === 'eth_sendRawTransaction') {
      this.broadcasts += 1;
      return { jsonrpc: '2.0', id: payload.id, error: { code: -32603, message: 'Lost response after submission' } };
    }
    if (this.mode === 'largeFees' && payload.method === 'eth_getTransactionReceipt' && result.result) {
      const { gasUsed, effectiveGasPrice } = vectors.largeFee;
      result.result = { ...result.result, gasUsed: toHex(gasUsed), effectiveGasPrice: toHex(effectiveGasPrice) };
    }
    return result;
  }

  close() {
    this.server.closeAllConnections();
    return new Promise(resolve => this.server.close(resolve));
  }
}

function toHex(decimal) {
  return `0x${BigInt(decimal).toString(16)}`;
}
