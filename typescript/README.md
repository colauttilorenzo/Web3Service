# @certifie/web3-service

TypeScript library for notarizing document hashes on EVM networks and Algorand, extracted from CERTIFIE's `Web3Service`. It covers contract deployment, minting, transaction receipts and wallet derivation, and works with any contract: it lists the functions of an ABI (or of an ARC-4/ARC-56 application on Algorand), calls them by name with JSON parameters, recognizes ERC and ARC standards, mints NFTs and reads tokens and transactions. The provider passed to `Web3Service` selects the blockchain: EVM networks, Algorand, or Bitcoin for reads, with the same methods and the same JSON. It runs both on the backend (Node.js 20+) and in the browser (ES2022), signing with a private key or mnemonic, or with the user's own wallet (MetaMask or any EIP-1193 wallet).

Runtime dependencies: `web3`, `ethers`, `big.js` and `algosdk`.

The full documentation (installation, types, API and examples for React, Next.js, Angular, Fastify, Express, vanilla JavaScript and .NET) is in [`docs/`](../docs/index.html) at the repository root, written in Italian: open `docs/index.html` in a browser. The repository layout is described in the [root README](../README.md).

## Distributions

| Distribution | Files | Use |
| --- | --- | --- |
| npm package (ESM + CommonJS + types) | `dist/esm`, `dist/cjs` | Node.js and bundled frontends (Vite, Next.js, Angular, webpack): `import { Web3Service } from '@certifie/web3-service'` |
| Standalone ESM module | `dist/browser/web3-service.esm.min.js` | `<script type="module">`, import maps or a CDN, no bundler: `@certifie/web3-service/browser` |
| Script tag build | `dist/browser/web3-service.min.js` | `<script src="…">`, exposes the global namespace `CertifieWeb3` |
| NuGet package `Certifie.Web3Service` | `../artifacts/nuget/*.nupkg` | .NET 6+/.NET Core and .NET Standard 2.0, native C# client: see [`dotnet/README.md`](../dotnet/README.md) |

The two files in `dist/browser` bundle every dependency (~940 KB minified, ~300 KB gzipped). Once the package is published to npm they are also available from a CDN, for example `https://cdn.jsdelivr.net/npm/@certifie/web3-service@0.2.0/dist/browser/web3-service.min.js` (the `unpkg` and `jsdelivr` fields are already set).

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
npm install ~/Desktop/workspace/web3-service/typescript/certifie-web3-service-0.2.0.tgz
```

The npm name is `@certifie/web3-service`. `npm pack` builds the package without publishing it to the registry.

NuGet package:

```sh
npm run pack:nuget     # creates ../artifacts/nuget/Certifie.Web3Service.0.2.0.nupkg
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
      const notarizer = service.getContract(abi, contractAddress);
      const result = await notarizer.send('mint', { recipient: contractAddress, hash: documentHash, algorithm: 'sha2', metadataURI: '' });
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
  import { Web3Service } from 'https://cdn.jsdelivr.net/npm/@certifie/web3-service@0.2.0/dist/browser/web3-service.esm.min.js';
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

// Notarization is a call to the contract's mint method, with the parameter names of its ABI.
const contractAddress = process.env.CONTRACT_ADDRESS!;
const result = await service.getContract(abi, contractAddress).send('mint', {
  recipient: contractAddress,
  hash: 'hash-computed-by-the-application',
  algorithm: 'sha2',
  metadataURI: 'ipfs://document-metadata',
});

console.log(result.tx?.transactionHash);
console.log(result.nativeFee);
// { amount: '21000000000000', symbol: 'ETH', decimals: '18' }
```

There is no dedicated notarization method: any contract works, and `send` waits for the block and returns the transaction with its receipt and gas cost, named after the method (`'mint'`). In this example the recipient is the contract's own address, as in the original class. Computing the document hash is up to the application; `algorithm` is passed to the contract unchanged. Without a `signer`, only read operations are available.

A runnable example that loads the ABI from a file is in [`examples/notarize.mjs`](examples/notarize.mjs).

## Algorand and Bitcoin

The same `Web3Service` works on Algorand and Bitcoin: the `kind` field of the provider selects the implementation, and a provider without `kind` is EVM.

```ts
import { AlgorandWallets, NotImplementedError, Web3Service, type IAvmProvider } from '@certifie/web3-service';
import notarizer from './Notarizer.arc56.json' with { type: 'json' };

const testnet: IAvmProvider = {
  kind: 'avm',
  rpcURI: 'https://testnet-api.algonode.cloud',     // algod
  indexerURI: 'https://testnet-idx.algonode.cloud', // optional, for old transactions
  chainId: 'testnet-v1.0',                          // genesis ID, checked before every operation
};
const algorand = new Web3Service(testnet, { signer: { mnemonic: process.env.ALGORAND_MNEMONIC! } });

const deployed = await algorand.deploy(notarizer, '', ['CERTIFIE'], undefined, { value: '200000' });
const appId = deployed.tx!.contractAddress!; // the application ID
const app = algorand.getContract(notarizer, appId);      // ARC-4 methods by name
await app.send('mint', { recipient: AlgorandWallets.getApplicationAddress(appId), hash: documentHash, algorithm: 'sha2', uri: 'ipfs://…' });
await app.call('label');                                  // reads are free simulations
const { tokenId } = await algorand.createToken({ name: 'Certifie Coin', symbol: 'CERT', totalSupply: '1000000', decimals: 2 });

const bitcoin = new Web3Service({ kind: 'bitcoin', rpcURI: 'https://mempool.space/api' }); // an Esplora API
await bitcoin.check();                // { blockNumber, gasPrice }: height and fee in sat/vB
await bitcoin.getTransaction(txid);   // the same ITransactionDetails as on EVM networks
await bitcoin.deploy(abi, bytecode);  // throws NotImplementedError: Bitcoin has no smart contracts
```

| Method | EVM | Algorand | Bitcoin |
| --- | --- | --- | --- |
| `check`, `getTransaction`, `getTransactionReceipt` | Yes | Yes | Yes |
| `getContract`, `callContractMethod` | Yes | Yes, ARC-4 applications | `NotImplementedError` |
| `deploy` | Yes, from bytecode | Yes, from an ARC-56 description or `{ approval, clear }` programs | `NotImplementedError` |
| `createToken`, `getTokenInfo` | `NotImplementedError`: EVM tokens are contracts | Yes, Algorand Standard Assets | `NotImplementedError` |

On Algorand the library talks to algod over REST and simulates every write before signing it: it finds the accounts, applications, assets and boxes the application reads, adds the fee of inner transactions, and returns `TRANSACTION_REVERTED` without paying when the application would reject the call. `getContract` recognizes ARC-200, ARC-72 and ARC-73 tokens. The signer is a 25-word mnemonic, a private key, or in the browser an `IAvmWallet` (`{ address, signTransactions }`, for example from `@txnlab/use-wallet`). Fees are in microAlgo; `gasUsed` and `nonce` are `'0'`. Bitcoin is read-only and returns amounts and fees in satoshi.

`NotImplementedError` extends `Web3Error` with the code `NOT_IMPLEMENTED`, and is thrown before any request. Code that should work with any blockchain can depend on the `IWeb3Service` interface. The details are in the documentation pages *Più blockchain*, *Algorand* and *Bitcoin*.

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
| `deploy(abi, bytecode, constructorArguments?, event?, options?)` | Confirmed transaction with the address in `tx.contractAddress` (the application ID on Algorand) |
| `createToken(token, event?, options?)` | `{ standard: 'ASA', tokenId, transaction }`: a token without a contract (Algorand Standard Asset) |
| `getTokenInfo(tokenId)` | Data of a token created with `createToken` |
| `getTransaction(hash, abi?)` | Any transaction, pending or confirmed, with the called function decoded |
| `getTransactionReceipt(hash, event?)` | Receipt enriched with timestamp and gas cost |

`getContract(abi, address)` returns a [`Web3Contract`](#any-contract-tokens-and-nfts) to call any method, `mint` included. Write methods wait for inclusion in a block, without waiting for further confirmations. `bytecode` must be a non-empty hex string prefixed with `0x`.

```ts
const deployment = await service.deploy(abi, bytecode, ['constructor argument']);
const receipt = await service.getTransactionReceipt(transactionHash);
const metadata = await service.callContractMethod(
  'metadata', abi, contractAddress, [documentHash],
);
```

`event` keeps `name` and `requestedAt` (a millisecond string). The object you pass is not modified. A failed receipt has `error` and `rejectedAt`; a successful one has `completedAt`. Stale error fields are removed when a successful receipt is retrieved.

The last argument of `deploy` and `createToken`, and of `Web3Contract` writes, accepts `gas`, `gasPrice`, `nonce` and `value`, all integers. With a key or mnemonic, the library signs `gasPrice` (legacy type) transactions. By default it estimates gas, adds a buffer of `10000` and uses the `pending` nonce. Writes on the same instance are serialized; applications that share a wallet across processes or instances must coordinate nonces.

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

Algorand accounts are not derived from a path. `AlgorandWallets` creates and reads their 25-word mnemonics, and gives the account of an application:

```ts
import { AlgorandWallets } from '@certifie/web3-service';

const phrase = AlgorandWallets.createMnemonic();
const account = AlgorandWallets.getWallet(phrase); // { address, publicKey, privateKey (32-byte seed as 0x hex), path: null, index: '0' }
AlgorandWallets.getApplicationAddress('1002');     // the account of application 1002
```

## Providers and errors

Every operation checks the network before proceeding: `eth_chainId` on EVM networks, the genesis ID on Algorand, the genesis block hash on Bitcoin. If the primary provider is unreachable or on another network, `rpcPublicURI` is tried, and it may use a different protocol. Fallback happens during the initial selection: an operation already in progress is not retried automatically.

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
| `NOT_SUPPORTED` | The contract does not offer the requested function, e.g. `mintNft` on a contract that is not an NFT |
| `NOT_IMPLEMENTED` | The blockchain of the provider does not offer the operation, e.g. `deploy` on Bitcoin (`NotImplementedError`) |
| `RPC_ERROR` | RPC call, estimation or signing failed; the cause is in `cause` |
| `TRANSACTION_NOT_FOUND` | Unknown transaction, or not yet included in a block |
| `TOKEN_NOT_FOUND` | The token passed to `getTokenInfo` does not exist |
| `TRANSACTION_REVERTED` | Included in a block with `status: "0"`, or on Algorand rejected by the simulation before sending |
| `TRANSACTION_SUBMISSION_FAILED` | Submission failed or confirmation not received; check `transactionHash` before retrying |
| `RECEIPT_DETAILS_FAILED` | Transaction confirmed but receipt enrichment failed; retrieve it by hash |
| `UNSAFE_INTEGER` | A custom provider returned an unsafe JavaScript number |
| `USER_REJECTED` | The user rejected the request in the browser wallet |
| `WALLET_ERROR` | The browser wallet returned an error; details in `cause` |

Writes reject the promise on error. `getTransactionReceipt` also returns reverted receipts, with the error state and the gas actually spent. After a submission error the library tries to recover a receipt that is already available, without signing or broadcasting a second transaction.

## Migrating from the class in the CERTIFIE API

Imports come from the package entry point. The constructor's second argument is now `Web3ServiceOptions`: the `Company` model is not part of the library. The application must pass an already decrypted private key or mnemonic. Database models, API configuration and market prices are not dependencies of the package.

`mint(abi, address, document)` is replaced by a call to the contract: `getContract(abi, address).send('mint', { recipient: address, hash: document.hash.data, algorithm: document.hash.algorithm, metadataURI: document.metadata })`. Events and receipts must use integer strings for numeric fields. `nativeFee` contains only the cost in the native token.

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
- `test/MultiChainTests.test.mjs` mirrors `MultiChainTests.cs`: provider selection, `NotImplementedError`, Algorand accounts and encoding, and Bitcoin against a local Esplora stub.
- `test/AlgorandTests.test.mjs` mirrors `AlgorandTests.cs` and runs on a real Algorand node, such as AlgoKit LocalNet: set `WEB3_TEST_ALGOD_URI` (for example `http://localhost:4001`), otherwise these tests are skipped. `WEB3_TEST_ALGOD_TOKEN` and `WEB3_TEST_ALGOD_MNEMONIC` default to the LocalNet token and dispenser. With the variable set, `npm run test:dotnet` also runs the Algorand parity test. The test applications are in `../shared/algorand`.
- `WalletSignerTests`, `TransportTests` and `PackageTests` cover what exists only in TypeScript: browser wallets, the web3.js transports, the ESM/CommonJS entry points and the browser bundles. `test/types` checks the TypeScript declarations.

Transport tests open HTTP, WS and WSS servers on `127.0.0.1` only. The certificate and key in `test/fixtures/` are only for the local WSS test and are not included in the npm package.

The package stays `UNLICENSED`; the license and access to the npm scope must be decided before distributing it publicly.

API references: [Web3.js providers](https://docs.web3js.org/guides/web3_providers_guide/), [ethers wallet](https://docs.ethers.org/v6/api/wallet/), [big.js](https://mikemcl.github.io/big.js/), [Nethereum](https://docs.nethereum.com/), [algosdk](https://www.npmjs.com/package/algosdk), [ARC-4](https://github.com/algorandfoundation/ARCs/blob/main/ARCs/arc-0004.md), [ARC-56](https://github.com/algorandfoundation/ARCs/blob/main/ARCs/arc-0056.md), [Esplora API](https://github.com/Blockstream/esplora/blob/master/API.md).
