# @certifie/web3-service

TypeScript library for notarizing document hashes on EVM networks, extracted from CERTIFIE's `Web3Service`. It covers contract deployment, minting, transaction receipts and wallet derivation, and works with any contract: it lists the functions of an ABI, calls them by name with JSON parameters, recognizes ERC standards, mints NFTs and reads tokens and transactions. It runs both on the backend (Node.js 20+) and in the browser (ES2022), signing with a private key or mnemonic, or with the user's own wallet (MetaMask or any EIP-1193 wallet).

Runtime dependencies: `web3`, `ethers` and `big.js`.

The full documentation (installation, types, API and examples for React, Next.js, Angular, Fastify, Express, vanilla JavaScript and .NET) is in [`docs/`](../docs/index.html) at the repository root, written in Italian: open `docs/index.html` in a browser. The repository layout is described in the [root README](../README.md).

## Distributions

| Distribution | Files | Use |
| --- | --- | --- |
| npm package (ESM + CommonJS + types) | `dist/esm`, `dist/cjs` | Node.js and bundled frontends (Vite, Next.js, Angular, webpack): `import { Web3Service } from '@certifie/web3-service'` |
| Standalone ESM module | `dist/browser/web3-service.esm.min.js` | `<script type="module">`, import maps or a CDN, no bundler: `@certifie/web3-service/browser` |
| Script tag build | `dist/browser/web3-service.min.js` | `<script src="…">`, exposes the global namespace `CertifieWeb3` |
| NuGet package `Certifie.Web3Service` | `../artifacts/nuget/*.nupkg` | .NET 6+/.NET Core and .NET Standard 2.0, native C# client: see [`dotnet/README.md`](../dotnet/README.md) |

The two files in `dist/browser` bundle every dependency (~610 KB minified, ~200 KB gzipped). Once the package is published to npm they are also available from a CDN, for example `https://cdn.jsdelivr.net/npm/@certifie/web3-service@0.1.0/dist/browser/web3-service.min.js` (the `unpkg` and `jsdelivr` fields are already set).

## Local installation

In the `typescript` folder:

```sh
cd ~/Desktop/workspace/web3-service/typescript
npm ci
npm test
npm pack
```

In the project that uses it:

```sh
npm install ~/Desktop/workspace/web3-service/typescript/certifie-web3-service-0.1.0.tgz
```

The npm name is `@certifie/web3-service`. `npm pack` builds the package without publishing it to the registry.

NuGet package:

```sh
npm run pack:nuget     # creates ../artifacts/nuget/Certifie.Web3Service.0.1.0.nupkg
dotnet add package Certifie.Web3Service --source ~/Desktop/workspace/web3-service/artifacts/nuget
```

## Using it in the browser

On the frontend the private key must never reach the page: pass the user's wallet as the signer. The wallet signs and broadcasts the transaction, while reads, waiting for the block and receipts go through the `rpcURI` node, so the result has the same shape as on the backend.

```html
<script src="/vendor/web3-service.min.js"></script>
<script>
  const { Web3Service, Web3Error } = CertifieWeb3;
  const service = new Web3Service(
    { rpcURI: 'https://rpc-amoy.polygon.technology', chainId: 80002, chainName: 'Polygon Amoy',
      nativeCurrency: { name: 'POL', symbol: 'POL', decimals: 18 }, blockExplorerURI: 'https://amoy.polygonscan.com' },
    { signer: { wallet: window.ethereum } },
  );
  document.querySelector('#notarize').onclick = async () => {
    try {
      const result = await service.mint(abi, contractAddress, { hash: { data: documentHash, algorithm: 'sha2' } });
      console.log(result.tx.transactionHash, result.nativeFee);
    } catch (error) {
      if (error instanceof Web3Error && error.code === 'USER_REJECTED') console.log('Signature cancelled');
      else throw error;
    }
  };
</script>
```

With a bundler (React, a Next.js client component, Angular) the same code works with `import { Web3Service } from '@certifie/web3-service'`. Without a bundler:

```html
<script type="module">
  import { Web3Service } from 'https://cdn.jsdelivr.net/npm/@certifie/web3-service@0.1.0/dist/browser/web3-service.esm.min.js';
</script>
```

With `signer: { wallet }` the library:

- asks for the account with `eth_requestAccounts`;
- if the wallet is on another network, requests `wallet_switchEthereumChain` and, when the network is unknown to the wallet, adds it with `wallet_addEthereumChain` using the configured `chainName`, `nativeCurrency`, `rpcPublicURI`/`rpcURI` and `blockExplorerURI` (turn this off with `switchChain: false`);
- estimates gas through the node and lets the wallet choose the fees, unless `gasPrice` is set explicitly;
- returns `USER_REJECTED` when the user cancels, without broadcasting anything.

The `rpcURI` node used from the browser must accept CORS requests; public nodes usually do. WebSockets in the browser use the native `WebSocket`.

## Configuration and notarization

The EVM configuration is passed to the constructor either as an `IEvmProvider` object or as the node URI string. `https://` and `wss://` are supported, plus `http://` and `ws://` for local nodes. With a plain string, the EVM metadata uses these defaults:

```ts
{
  chainId: '0x1',
  chainName: 'Ethereum',
  nativeCurrency: { name: 'Ether', symbol: 'ETH', decimals: 18 },
}
```

```ts
import { Web3Service, type IEvmProvider } from '@certifie/web3-service';

const http = new Web3Service('https://your-ethereum-rpc.example');
const websocket = new Web3Service('wss://your-ethereum-rpc.example');

const provider: IEvmProvider = {
  rpcURI: 'wss://your-node.example',
  chainId: '0x539',
  chainName: 'Local EVM',
  nativeCurrency: { name: 'Gas token', symbol: 'GAS', decimals: 18 },
};
const custom = new Web3Service(provider);
```

Only `rpcURI` is required in the object. `IEvmProvider` also lets you override individual `nativeCurrency` fields; omitted fields keep their default. `EthereumProviderConfig` remains available as a compatibility alias. The RPC endpoint must match the configured `chainId`.

```ts
import { Web3Service, type Abi } from '@certifie/web3-service';

// ABI and address of your notarization contract.
const abi: Abi = [
  {
    type: 'function', name: 'mint', stateMutability: 'nonpayable',
    inputs: [
      { name: 'recipient', type: 'address' },
      { name: 'hash', type: 'string' },
      { name: 'algorithm', type: 'string' },
      { name: 'metadataURI', type: 'string' },
    ],
    outputs: [],
  },
];

const service = new Web3Service(
  {
    chainId: '1337',
    rpcURI: 'http://localhost:8545',
    nativeCurrency: { symbol: 'ETH', decimals: 18 },
    // rpcPublicURI: 'https://fallback.example',
  },
  { signer: { privateKey: process.env.PRIVATE_KEY! } },
);

const result = await service.mint(abi, process.env.CONTRACT_ADDRESS!, {
  hash: { data: 'hash-computed-by-the-application', algorithm: 'sha2' },
  metadata: 'ipfs://document-metadata',
});

console.log(result.tx?.transactionHash);
console.log(result.nativeFee);
// { amount: '21000000000000', symbol: 'ETH', decimals: '18' }
```

The contract must expose `mint(address,string,string,string)`. As in the original class, the first `mint` argument is the contract's own address. Computing the document hash is up to the application; `algorithm` is passed to the contract unchanged. Without a `signer`, only read operations are available.

A runnable example that loads the ABI from a file is in [`examples/notarize.mjs`](examples/notarize.mjs).

## Numbers and gas cost

Calculations use `big.js`. Every numeric value returned is an **integer string**, including millisecond timestamps, wallet indexes, RPC quantities and numbers returned by contract calls. Booleans stay booleans. Normalized network identifiers keep their hex encoding, for example `"0x539"`.

`nativeFee.amount` is the gas token spent, in its smallest unit:

```text
amount = gasUsed × effectiveGasPrice
```

`nativeFee.symbol` identifies the token and `nativeFee.decimals` gives its decimals. No decimal amounts, prices or fiat conversions are returned. The amount is the EVM execution fee; extra rollup fees, such as the L1 data cost, need a dedicated integration.

The native token's decimals are network metadata: the standard Ethereum JSON-RPC has no method to read them. They are set in `nativeCurrency.decimals`, default `18`, as part of the [EIP-3085](https://eips.ethereum.org/EIPS/eip-3085) configuration. For an ERC-20 token you can instead read the contract's optional [`decimals()`](https://eips.ethereum.org/EIPS/eip-20#decimals) method; that value does not describe the native token used for gas.

Numeric inputs accept `string`, `bigint` or safe-integer `number`. Use strings for large quantities. `BigInt` is only used to convert between decimal and hex encodings, never for arithmetic.

## Methods

| Method | Result |
| --- | --- |
| `check()` | `{ blockNumber, gasPrice }`, both integer strings; gas price in the smallest unit |
| `callContractMethod<T>(method, abi, address, parameters?)` | The call result, with numbers recursively converted to integer strings |
| `mint(abi, address, document, event?, options?)` | Confirmed transaction with receipt and gas cost |
| `deploy(abi, bytecode, constructorArguments?, event?, options?)` | Confirmed transaction with the address in `tx.contractAddress` |
| `getTransactionReceipt(hash, event?)` | Receipt enriched with timestamp and gas cost |

Write methods wait for inclusion in a block, without waiting for further confirmations. `bytecode` must be a non-empty hex string prefixed with `0x`.

```ts
const deployment = await service.deploy(abi, bytecode, ['constructor argument']);
const receipt = await service.getTransactionReceipt(transactionHash);
const metadata = await service.callContractMethod(
  'metadata', abi, contractAddress, [documentHash],
);
```

`event` keeps `name` and `requestedAt` (a millisecond string). The object you pass is not modified. A failed receipt has `error` and `rejectedAt`; a successful one has `completedAt`. Stale error fields are removed when a successful receipt is retrieved.

The fifth argument of `mint` and `deploy` accepts `gas`, `gasPrice`, `nonce` and `value`, all integers. With a key or mnemonic, the library signs `gasPrice` (legacy type) transactions. By default it estimates gas, adds a buffer of `10000` and uses the `pending` nonce. Writes on the same instance are serialized; applications that share a wallet across processes or instances must coordinate nonces.

## Any contract, tokens and NFTs

`getContract(abi, address)` turns any contract into an object that lists its functions and calls them by name with a JSON object of named parameters, so no ABI encoding or Solidity types are needed.

```ts
const token = service.getContract(tokenAbi, tokenAddress);

token.standards; // ['ERC-20', 'ERC-173']
token.methods;   // [{ name: 'transfer', kind: 'write', parameters: { to: 'string', amount: 'number' }, … }, …]
await token.owner();                                            // owner() when the ABI has it, otherwise undefined
await token.call('balanceOf', { account: holder });             // read: returns the value, free
await token.call('transfer', { to: recipient, amount: '1000' }); // write: returns the mined ITransaction
await token.call('deposit', { note: 'tip' }, { value: '1000' }); // payable: coins in options.value
await token.getTokenInfo();                                     // { standard: 'ERC-20', name, symbol, decimals, totalSupply }

const nft = service.getContract(nftAbi, nftAddress);
const { tokenIds } = await nft.mintNft({ to: holder, uri: 'ipfs://…' }); // safeMint, mint or mintTo, chosen by the parameter names
await nft.getTokenInfo(tokenIds[0]);                                     // name, symbol, owner, tokenURI

await service.getTransaction(hash, tokenAbi); // any transaction, with the called function and its arguments decoded
Web3Service.detectStandards(nftAbi);           // ['ERC-721', 'ERC-721 Metadata', 'ERC-165']
```

| Member | Description |
| --- | --- |
| `methods` | Every function: `name`, `signature`, `kind` (`read`, `write`, `payable`), `stateMutability`, `parameters` (name → JSON type: `string`, `number`, `boolean`, `array`, `object`), `inputs` and `outputs` (name → Solidity type). Unnamed parameters are `arg0`, `arg1`, … |
| `standards` | ERC-20, ERC-721 (Metadata, Enumerable), ERC-1155 (Metadata URI), ERC-165, ERC-173 and ERC-2981, recognized from the ABI |
| `call(method, parameters?, options?)` | Reads `read` functions, sends a transaction for the others. `method` is a name or, for overloaded names, a signature |
| `read(method, parameters?)` / `send(…)` | Force a read (also a free simulation of a write, sent from the zero address) or a transaction |
| `encode(method, parameters?)` | The call data, to have the call signed elsewhere |
| `owner()` | `owner()` of Ownable / ERC-173 contracts |
| `mintNft(parameters, options?)` | `{ standard, tokenIds, transaction }`, with the IDs read from the Transfer, TransferSingle and TransferBatch events |
| `getTokenInfo(tokenId?)` | Token data from the functions present in the ABI; `{id}` in ERC-1155 URIs is filled in |
| `getTransaction(hash)` | `service.getTransaction(hash, abi)` with this contract's ABI |

Integers can be passed as numbers or decimal strings (strings beyond 2^53), addresses and bytes as `0x` strings. Missing, unknown or mistyped parameters fail with `INVALID_ARGUMENT` before any request; `mintNft` and `getTokenInfo` on a contract that is not a token fail with `NOT_SUPPORTED`.

## Wallets

```ts
const mnemonic = await Web3Service.createMnemonic();
const restored = await Web3Service.getMnemonic(mnemonic.phrase);
const wallet = await Web3Service.getWallet(restored, 1);
const privateKey = await Web3Service.getPrivateKey(restored, 1);
```

The default path is `m/44'/60'/0'/0/1`, as in the original class. Index `0` can be requested explicitly. `wallet.index` is an integer string.

Instead of a private key, the signer accepts:

```ts
{ signer: { mnemonic: process.env.MNEMONIC!, walletIndex: 1 } }
```

`mnemonic` can also be `{ phrase, password }` or an ethers `Mnemonic` object. Decrypting and storing credentials is up to the application.

## Providers and errors

Every operation checks `eth_chainId` before proceeding. If the primary provider is unreachable or on another network, `rpcPublicURI` is tried, and it may use a different protocol. Fallback happens during the initial selection: an operation already in progress is not retried automatically.

Constructor options include `timeoutMs` (default `30000` per HTTP or WebSocket request), `transactionTimeoutMs` (default `120000` to wait for the receipt), `gasBuffer` (default `10000`) and `web3Factory(rpcURI)` for custom providers. With `web3Factory`, timeouts and closing connections are up to the application that supplies the provider.

The library picks the transport from the URI protocol. WebSocket connections it creates are closed when the operation ends, even on error, so they do not keep the Node process alive. Receipts are checked by polling on both transports. WSS connections keep normal TLS certificate verification.

Errors are `Web3Error` instances with `code`, `message`, an optional `transactionHash` and `toJSON()`. The original details are in `cause`; `toJSON()` leaves them out so RPC credentials or signed transactions are never serialized.

| Code | Meaning |
| --- | --- |
| `INVALID_ARGUMENT` | Invalid configuration or parameter |
| `SIGNER_REQUIRED` | Missing credentials for a write |
| `PROVIDER_UNAVAILABLE` | No endpoint available |
| `CHAIN_ID_MISMATCH` | The reachable endpoints (or the browser wallet) are on a different network |
| `METHOD_NOT_FOUND` | Method missing from the ABI |
| `RPC_ERROR` | RPC call, estimation or signing failed; the cause is in `cause` |
| `TRANSACTION_NOT_FOUND` | Unknown transaction, or not yet included in a block |
| `TRANSACTION_REVERTED` | Included in a block with `status: "0"` |
| `TRANSACTION_SUBMISSION_FAILED` | Submission failed or confirmation not received; check `transactionHash` before retrying |
| `RECEIPT_DETAILS_FAILED` | Transaction confirmed but receipt enrichment failed; retrieve it by hash |
| `UNSAFE_INTEGER` | A custom provider returned an unsafe JavaScript number |
| `USER_REJECTED` | The user rejected the request in the browser wallet |
| `WALLET_ERROR` | The browser wallet returned an error; details in `cause` |

`mint` and `deploy` reject the promise on error. `getTransactionReceipt` also returns reverted receipts, with the error state and the gas actually spent. After a submission error the library tries to recover a receipt that is already available, without signing or broadcasting a second transaction.

## Migrating from the class in the CERTIFIE API

Imports come from the package entry point. The constructor's second argument is now `Web3ServiceOptions`: the `Company` model is not part of the library. The application must pass an already decrypted private key or mnemonic. Database models, API configuration and market prices are not dependencies of the package.

`IDocument` is replaced by the minimal `NotarizationDocument` type; an existing document that has `hash` and `metadata` still works. Events and receipts must use integer strings for numeric fields. `nativeFee` contains only the cost in the native token.

## Development

```sh
npm run typecheck
npm test              # build, then the TypeScript tests
npm run test:dotnet   # .NET tests and the TypeScript/.NET parity test on a local Ganache chain (requires the .NET 8 SDK)
npm run pack:nuget
npm pack --dry-run
```

The .NET version lives in `../dotnet/` (`Certifie.Web3Service.sln`) and has the same structure: `src/Web3Service.ts` mirrors `Web3Service.cs`, `src/Internal/KeySigner.ts` mirrors `Internal/KeySigner.cs`, and so on, with the same method names (without the `Async` suffix and with a lowercase first letter). When you fix or change a method, apply the same change to the file with the same name on the other side.

Tests run against a local Ganache chain and compile a test Solidity contract:

- `test/UnitTests.test.mjs` and `test/ChainTests.test.mjs` mirror `UnitTests.cs` and `ChainTests.cs`: same test names, same expected values from `../shared/test-vectors.json`.
- `npm run test:dotnet` runs `test/fixtures/ParityScenario.mjs` on the chain, then the .NET tests; `ParityTests.cs` runs the same calls and checks that both versions return the same JSON.
- `WalletSignerTests`, `TransportTests` and `PackageTests` cover what exists only in TypeScript: browser wallets, the web3.js transports, the ESM/CommonJS entry points and the browser bundles. `test/types` checks the TypeScript declarations.

Transport tests open HTTP, WS and WSS servers on `127.0.0.1` only. The certificate and key in `test/fixtures/` are only for the local WSS test and are not included in the npm package.

The package stays `UNLICENSED`; the license and access to the npm scope must be decided before distributing it publicly.

API references: [Web3.js providers](https://docs.web3js.org/guides/web3_providers_guide/), [ethers wallet](https://docs.ethers.org/v6/api/wallet/), [big.js](https://mikemcl.github.io/big.js/), [Nethereum](https://docs.nethereum.com/).
