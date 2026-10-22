# Changelog

## 0.2.0

- Several blockchains with the same API: the provider passed to `Web3Service` selects the implementation (`IEvmProvider`, `IAvmProvider`, `IBitcoinProvider`), described by the `IWeb3Service` interface. An operation a blockchain does not offer throws `NotImplementedError` (code `NOT_IMPLEMENTED`), `NotImplementedException` in .NET, before any request.
- Algorand through algod: `deploy` from an ARC-56 description or from programs, `mint`, ARC-4 methods called by name with `getContract`, free simulated reads, writes simulated before sending (resources and inner transaction fees filled in automatically, rejections reported with the ARC-56 error message), ARC-200 tokens and ARC-72 NFTs, Algorand Standard Assets with `createToken` and `getTokenInfo`, signing with a 25-word mnemonic, a private key or a browser wallet (`IAvmWallet`), and `AlgorandWallets`. The .NET implementation is native: canonical msgpack, Ed25519 signatures and the ARC-4 codec, with no Algorand SDK.
- Bitcoin, read-only, through an Esplora API: `check`, `getTransaction` and `getTransactionReceipt`.
- New `createToken` and `getTokenInfo` methods, `kind` property and `TOKEN_NOT_FOUND` error code. `Web3Contract` is now an abstract class with one implementation per blockchain; in .NET the constructor accepts any `IBlockchainProvider` and `Provider` is a `ResolvedProvider`. Behavior on EVM networks is unchanged.
- Code organized by blockchain in `Evm/`, `Algorand/` and `Bitcoin/`, with endpoint selection, notarization, token reads and REST requests shared in `Internal/`. Algorand and Esplora requests go through one small REST client (`fetch` in TypeScript, one pooled `HttpClient` in .NET) instead of the algosdk HTTP clients. New runtime dependency `algosdk` in TypeScript; the browser bundles are ~940 KB minified (~300 KB gzipped).
- New tests on provider selection, on a real Algorand node (AlgoKit LocalNet, enabled by `WEB3_TEST_ALGOD_URI`) and TypeScript/.NET parity on Algorand too.

## 0.1.0

- Any contract: `getContract(abi, address)` returns a `Web3Contract` that lists the ABI functions with their kind (`read`, `write`, `payable`) and their parameters as simple JSON types, and calls them by name with a JSON object (`call`, `read`, `send`, `encode`). It recognizes ERC standards (`standards`, `Web3Service.detectStandards`), reads `owner()`, mints NFTs on ERC-721 and ERC-1155 contracts (`mintNft`, returning the new token IDs) and reads token data (`getTokenInfo`). `getTransaction(hash, abi?)` reads any transaction, pending or mined, and decodes the called function. New error code `NOT_SUPPORTED`. Same API in .NET (`GetContract`, `CallAsync`, `MintNftAsync`, …).
- Universal distribution: npm package for Node.js and bundled frontends, standalone ESM module (`@certifie/web3-service/browser`) and `dist/browser/web3-service.min.js` script with the global namespace `CertifieWeb3`.
- Browser signing through an EIP-1193 wallet (`signer: { wallet: window.ethereum }`), with automatic network switching or adding and the `USER_REJECTED` and `WALLET_ERROR` codes.
- `Certifie.Web3Service` NuGet package (net8.0 and netstandard2.0): native C# client built on Nethereum with the same API, defaults and JSON output.
- Repository split into `typescript/` and `dotnet/`, each with `src` and `test`: files and methods have the same names in both languages (`Web3Service`, `Internal/Configuration`, `Internal/KeySigner`, …), so a fix can be applied to both in the same place.
- Mirrored tests with the same names in TypeScript and .NET, shared expected values in `shared/test-vectors.json`, and a parity test (`npm run test:dotnet`) that checks both versions return the same JSON for the same calls on the same chain.
- New `pollingIntervalMs` option (default 1 second), matching `PollingInterval` in .NET. `provider.chainId` is now normalized to lowercase hex, and `callContractMethod` returns an array for several outputs, as in .NET.
- GitHub Actions: on every push to `main`, tests in both languages, then npm and NuGet packages on GitHub Packages with the `package.json` version (plus a build number when it is already published), and the documentation on GitHub Pages.
- Multi-page documentation in `docs/`: reusable examples for Next.js, Vanilla, Fastify, Express and .NET, signatures and types in TypeScript and C#.
- Source split into small single-purpose modules (TypeScript) and internal classes (.NET), with methods of at most 10 lines; public names are unchanged and `NativeCurrency` is now exported as a type.

- EVM configuration at initialization with Ethereum defaults (`0x1`, Ether, ETH, 18 decimals), which can be partially overridden.
- Initialization from an `IEvmProvider` object or an HTTP(S)/WS(S) URI, with transport selection, timeouts and closing of WebSocket connections.

- Extracted `Web3Service` into a standalone library with ESM/CommonJS builds and TypeScript declarations.
- Deployment, minting, reads and receipts on EVM networks, with explicit credentials and verified RPC fallback.
- Calculations with `big.js`, numeric output as integer strings and gas cost in the native token's smallest unit.
- Removed company models, database, API configuration and fiat prices.
- Added tests on a local blockchain and checks for both import formats.
