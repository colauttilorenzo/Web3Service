import { build } from 'esbuild';

// Self-contained bundles for pages without a bundler: dependencies are inlined and Node built-ins are not used.
const shared = {
  entryPoints: [new URL('../src/index.ts', import.meta.url).pathname],
  bundle: true, minify: true, platform: 'browser', target: ['es2022'], legalComments: 'linked',
};
await build({ ...shared, format: 'esm', outfile: new URL('../dist/browser/web3-service.esm.min.js', import.meta.url).pathname });
await build({ ...shared, format: 'iife', globalName: 'CertifieWeb3', outfile: new URL('../dist/browser/web3-service.min.js', import.meta.url).pathname });
