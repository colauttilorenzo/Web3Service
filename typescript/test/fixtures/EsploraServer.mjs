// A Bitcoin Esplora API serving the responses of the shared vectors; the .NET tests have the same fixture (EsploraServer.cs).
import { createServer } from 'node:http';
import { once } from 'node:events';
import { vectors } from './TestVectors.mjs';

const { esplora } = vectors.bitcoin;
const ROUTES = {
  '/block-height/0': esplora.genesis,
  '/blocks/tip/height': esplora.tipHeight,
  '/fee-estimates': esplora.feeEstimates,
  [`/tx/${esplora.transaction.txid}`]: esplora.transaction,
  [`/block/${esplora.transaction.status.block_hash}/txids`]: esplora.blockTxids,
};

export class EsploraServer {
  constructor() {
    this.server = createServer((request, response) => {
      const body = ROUTES[request.url];
      response.writeHead(body === undefined ? 404 : 200, { 'content-type': typeof body === 'string' ? 'text/plain' : 'application/json' });
      response.end(body === undefined ? 'Not found' : typeof body === 'string' ? body : JSON.stringify(body));
    });
  }

  static async start() {
    const server = new EsploraServer();
    server.server.listen(0, '127.0.0.1');
    await once(server.server, 'listening');
    server.uri = `http://127.0.0.1:${server.server.address().port}`;
    return server;
  }

  provider(chainId = esplora.chainId) {
    return { kind: 'bitcoin', rpcURI: this.uri, chainId };
  }

  close() {
    this.server.close();
  }
}
