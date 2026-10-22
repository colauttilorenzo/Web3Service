// Local Ganache chain with the Notarizer contract; the .NET tests use the same chain through `npm run test:dotnet`.
import ganache from 'ganache';
import { abi, bytecode } from './notarizer.mjs';
import { vectors } from './TestVectors.mjs';

export class Chain {
  constructor(server) {
    this.server = server;
    const { port } = server.address();
    this.http = `http://127.0.0.1:${port}`;
    this.webSocket = `ws://127.0.0.1:${port}`;
    this.mnemonic = vectors.mnemonic;
    this.abi = abi;
    this.bytecode = bytecode;
  }

  static async start() {
    const server = ganache.server({ chain: { chainId: 1337 }, wallet: { mnemonic: vectors.mnemonic }, logging: { quiet: true } });
    await server.listen(0, '127.0.0.1');
    return new Chain(server);
  }

  provider(rpcURI = this.http) {
    return { rpcURI, chainId: '1337' };
  }

  signer() {
    return { signer: { mnemonic: this.mnemonic }, pollingIntervalMs: 100 };
  }

  async request(method, params = []) {
    const response = await fetch(this.http, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params }) });
    return (await response.json()).result;
  }

  close() {
    return this.server.close();
  }
}
