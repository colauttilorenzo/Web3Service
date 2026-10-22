# CERTIFIE Web3 Service

| | npm (TypeScript) | NuGet (.NET) |
| --- | --- | --- |
| **Pipeline** | [![npm package](https://github.com/colauttilorenzo/Web3Service/actions/workflows/release-npm.yml/badge.svg?branch=main)](https://github.com/colauttilorenzo/Web3Service/actions/workflows/release-npm.yml) | [![NuGet package](https://github.com/colauttilorenzo/Web3Service/actions/workflows/release-nuget.yml/badge.svg?branch=main)](https://github.com/colauttilorenzo/Web3Service/actions/workflows/release-nuget.yml) |
| **Package** | [![npm on GitHub Packages](https://img.shields.io/badge/npm-%40colauttilorenzo%2Fweb3--service-cb3837?logo=npm&logoColor=white)](https://github.com/colauttilorenzo/Web3Service/pkgs/npm/web3-service) | [![NuGet on GitHub Packages](https://img.shields.io/badge/NuGet-Certifie.Web3Service-004880?logo=nuget&logoColor=white)](https://github.com/colauttilorenzo/Web3Service/pkgs/nuget/Certifie.Web3Service) |
| **Releases** | [![npm releases](https://img.shields.io/badge/releases-npm-cb3837?logo=github)](https://github.com/colauttilorenzo/Web3Service/releases?q=npm-v&expanded=true) | [![NuGet releases](https://img.shields.io/badge/releases-NuGet-004880?logo=github)](https://github.com/colauttilorenzo/Web3Service/releases?q=nuget-v&expanded=true) |
| **Platform** | [![Node.js](https://img.shields.io/badge/Node.js-%E2%89%A520-5fa04e?logo=nodedotjs&logoColor=white)](typescript/package.json) [![TypeScript](https://img.shields.io/badge/TypeScript-strict-3178c6?logo=typescript&logoColor=white)](typescript/) | [![.NET](https://img.shields.io/badge/.NET-net8.0%20%7C%20netstandard2.0-512bd4?logo=dotnet&logoColor=white)](dotnet/) |

[![Documentation](https://github.com/colauttilorenzo/Web3Service/actions/workflows/docs.yml/badge.svg?branch=main)](https://github.com/colauttilorenzo/Web3Service/actions/workflows/docs.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-0a7bbb?logo=githubpages&logoColor=white)](https://colauttilorenzo.github.io/Web3Service/)
[![Changelog](https://img.shields.io/badge/changelog-CHANGELOG.md-blue)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-proprietary-lightgrey)](typescript/package.json)

Notarization of document hashes and use of any smart contract (ERC and ARC standards, NFT minting, token and transaction reads) on EVM networks and Algorand, plus Bitcoin reads, behind one API. The provider passed to `Web3Service` selects the blockchain: `IEvmProvider` (or a node URL) for EVM networks, `IAvmProvider` for Algorand, `IBitcoinProvider` for Bitcoin. The methods and the JSON they return are the same on every blockchain; an operation a blockchain does not have, such as `deploy` on Bitcoin, throws `NotImplementedError` (`NotImplementedException` in .NET).

There are two implementations that behave the same way:

| Folder | Package | Readme |
| --- | --- | --- |
| [`typescript/`](typescript/) | npm `@certifie/web3-service` (Node.js, bundled frontends, browser script) | [`typescript/README.md`](typescript/README.md) |
| [`dotnet/`](dotnet/) | NuGet `Certifie.Web3Service` (net8.0, netstandard2.0) | [`dotnet/README.md`](dotnet/README.md) |
| [`docs/`](docs/index.html) | Documentation in Italian, open `docs/index.html` in a browser | |
| [`shared/`](shared/) | Expected values used by the tests of both implementations, and the Algorand test contracts in `shared/algorand` | |

Release notes are in [`CHANGELOG.md`](CHANGELOG.md).

## Architecture

`Web3Service` is a facade over the `IWeb3Service` interface: `Internal/Blockchains` creates the implementation that matches the provider type, and each blockchain has its own folder. Code that is the same on every blockchain (endpoint fallback, configuration, errors, notarization arguments, token reads, results, the write queue) lives in `Internal/`.

| Folder | Implementation | Talks to |
| --- | --- | --- |
| `Evm/` | `EvmChain`, `EvmContract` | A JSON-RPC node (web3.js and ethers in TypeScript, Nethereum in .NET) |
| `Algorand/` | `AlgorandChain`, `AlgorandContract` | algod, and optionally the indexer, over REST |
| `Bitcoin/` | `BitcoinChain` | An Esplora REST API |

`Web3Contract` is abstract, with one implementation per blockchain, so `getContract(abi, address)` returns the same API everywhere. Adding a blockchain (Solana, for example) means adding a folder with a class that implements `IWeb3Service`, a provider interface in `Models`, and one line in `Internal/Blockchains`.

## Same structure in both languages

Each implementation has a `src` and a `test` folder, and every source file has a counterpart with the same name:

| TypeScript (`typescript/src`) | C# (`dotnet/src/Certifie.Web3Service`) | Role |
| --- | --- | --- |
| `Web3Service.ts` | `Web3Service.cs` | Public API: a facade over the implementation the provider selects |
| `IWeb3Service.ts` | `IWeb3Service.cs` | What every blockchain implements |
| `Web3Contract.ts` | `Web3Contract.cs` | Any contract: methods, calls by name, tokens and NFTs (abstract, one implementation per blockchain) |
| `Models.ts` | `Models.cs` | Public types, including the providers |
| `Web3Exception.ts` (`Web3Error`, `NotImplementedError`) | `Web3Exception.cs` | Errors and their JSON |
| `Internal/Blockchains.ts` | `Internal/Blockchains.cs` | Provider type → implementation |
| `Internal/Configuration.ts` | `Internal/Configuration.cs` | Defaults and validation |
| `Internal/ContractMethods.ts` | `Internal/ContractMethods.cs` | Method lookup by name or signature, JSON parameters |
| `Internal/Endpoints.ts` | `Internal/Endpoints.cs` | Endpoint fallback and network check, for every blockchain |
| `Internal/Errors.ts` | `Internal/Errors.cs` | Shared error factories |
| `Internal/Integers.ts` | `Internal/Integers.cs` | Integer parsing and formatting |
| `Internal/Notarization.ts` | `Internal/Notarization.cs` | Arguments of `mint` |
| `Internal/RestClient.ts` | `Internal/RestClient.cs` | JSON REST client for algod, the indexer and Esplora |
| `Internal/SharedResults.ts` | `Internal/SharedResults.cs` | Request time, block times and native fee of results |
| `Internal/Standards.ts` | `Internal/Standards.cs` | Token standards recognized from method names |
| `Internal/TokenMethods.ts` | `Internal/TokenMethods.cs` | NFT mint function and token reads |
| `Internal/WriteQueue.ts` | `Internal/WriteQueue.cs` | One write at a time per service |
| `Evm/EvmChain.ts` | `Evm/EvmChain.cs` | EVM implementation of `IWeb3Service` |
| `Evm/EvmContract.ts` | `Evm/EvmContract.cs` | EVM `Web3Contract` |
| `Evm/AbiMethods.ts` | `Evm/AbiMethods.cs` | ABI functions, JSON parameters to ordered arguments |
| `Evm/ContractCalls.ts` | `Evm/ContractCalls.cs` | ABI lookups, JSON-safe outputs |
| `Evm/ErcStandards.ts` | `Evm/ErcStandards.cs` | ERC standards recognized from the ABI |
| `Evm/HdWallets.ts` | `Evm/HdWallets.cs` | BIP-39 / BIP-44 wallets |
| `Evm/KeySigner.ts` | `Evm/KeySigner.cs` | Local signing with a private key or mnemonic |
| `Evm/Receipts.ts` | `Evm/Receipts.cs` | Receipt lookups and polling |
| `Evm/RpcConnector.ts` | `Evm/RpcConnector.cs` | JSON-RPC endpoints and chain ID check |
| `Evm/RpcSession.ts` | `Evm/RpcSession.cs` | One connection with timeouts |
| `Evm/Tokens.ts` | `Evm/Tokens.cs` | Minted token IDs, token reads |
| `Evm/TransactionRequest.ts` | `Evm/TransactionRequest.cs` | Encoded call, gas limit |
| `Evm/TransactionResults.ts` | `Evm/TransactionResults.cs` | Receipt to `ITransaction` / `TransactionResult` |
| `Evm/Transactions.ts` | `Evm/Transactions.cs` | Transactions by hash, decoded calls |
| `Algorand/AlgorandChain.ts` | `Algorand/AlgorandChain.cs` | Algorand implementation of `IWeb3Service` |
| `Algorand/AlgorandContract.ts` | `Algorand/AlgorandContract.cs` | ARC-4 application as a `Web3Contract`; its address is the application ID |
| `Algorand/AlgodConnector.ts` | `Algorand/AlgodConnector.cs` | algod endpoints and genesis ID check |
| `Algorand/AlgodSession.ts` | `Algorand/AlgodSession.cs` | One algod (and indexer) session with its suggested parameters |
| `Algorand/AlgorandSigner.ts` | `Algorand/AlgorandSigner.cs` | Signing with a 25-word mnemonic, a private key or a wallet |
| `Algorand/AlgorandWallets.ts` | `Algorand/AlgorandWallets.cs` | Algorand accounts and mnemonics (public) |
| `Algorand/AppCalls.ts` | `Algorand/AppCalls.cs` | Application calls and creations as transaction groups |
| `Algorand/AppPrograms.ts` | `Algorand/AppPrograms.cs` | Programs, state schema and create method for `deploy` |
| `Algorand/Arc4Methods.ts` | `Algorand/Arc4Methods.cs` | ARC-4 methods: selectors, arguments, return values, decoded calls |
| `Algorand/Arc4Values.ts` | `Algorand/Arc4Values.cs` | JSON values to ARC-4 values and back |
| `Algorand/ArcStandards.ts` | `Algorand/ArcStandards.cs` | ARC-200, ARC-72 and ARC-73 recognized from method names |
| `Algorand/Assets.ts` | `Algorand/Assets.cs` | Algorand Standard Assets |
| `Algorand/AvmRequest.ts` | `Algorand/AvmRequest.cs` | A transaction group to build, simulate and sign |
| `Algorand/ProgramErrors.ts` | `Algorand/ProgramErrors.cs` | ARC-56 error messages for rejected calls |
| `Algorand/Results.ts` | `Algorand/Results.cs` | Receipts, details and fees in the shared JSON shape |
| `Algorand/Simulation.ts` | `Algorand/Simulation.cs` | Simulation before signing: resources, inner transaction fees, rejections |
| `Algorand/Submission.ts` | `Algorand/Submission.cs` | Sending a group and waiting for confirmation |
| `Algorand/Tokens.ts` | `Algorand/Tokens.cs` | ARC-72 minted token IDs, token reads |
| `Algorand/Transactions.ts` | `Algorand/Transactions.cs` | Transactions from algod or the indexer |
| `Bitcoin/BitcoinChain.ts` | `Bitcoin/BitcoinChain.cs` | Bitcoin implementation: reads; contracts and tokens throw `NotImplementedError` |
| `Bitcoin/EsploraSession.ts` | `Bitcoin/EsploraSession.cs` | Esplora endpoints and genesis block check |
| `Bitcoin/Transactions.ts` | `Bitcoin/Transactions.cs` | Transactions and receipts in the shared JSON shape |

Method names match too: the TypeScript name is the C# name with a lowercase first letter and without `Async` (`MintAsync` → `mint`, `Receipts.WaitAsync` → `Receipts.wait`). C# static classes are TypeScript modules imported as namespaces (`import * as Receipts from './Receipts.js'`), so the call sites read the same. A fix or change goes in the file with the same name on both sides.

A few files exist on one side only, because the other language gets the same thing from a library:

- TypeScript only: `Evm/WalletSigner.ts` (browser wallets such as MetaMask), `Evm/TimeoutHttpProvider.ts` and `Evm/TimeoutWebSocketProvider.ts` (web3.js transports; .NET uses `HttpClient` and `WebSocketClient`), and `index.ts` (the package exports).
- C# only: the Algorand primitives that TypeScript takes from `algosdk`, so the NuGet package needs no Algorand SDK: `Algorand/AvmCrypto` (SHA-512/256 and Ed25519 through BouncyCastle), `Algorand/Base32`, `Algorand/AlgorandAddress`, `Algorand/Mnemonics`, `Algorand/Msgpack` (canonical encoding), `Algorand/UnsignedTransaction`, `Algorand/TransactionFields`, `Algorand/SuggestedParams`, `Algorand/Arc4Types` and `Algorand/Arc4Tuple` (the ARC-4 codec), plus `Internal/JsonValues` (typed reads from `JsonNode`).

## Tests

The tests have the same names in both languages and read their expected values from [`shared/test-vectors.json`](shared/test-vectors.json):

| TypeScript (`typescript/test`) | C# (`dotnet/test/Certifie.Web3Service.Tests`) |
| --- | --- |
| `UnitTests.test.mjs` | `UnitTests.cs` |
| `ChainTests.test.mjs` | `ChainTests.cs` |
| `MultiChainTests.test.mjs` (provider selection, `NotImplementedError`, Algorand accounts and encoding, Bitcoin against a local Esplora stub) | `MultiChainTests.cs` |
| `AlgorandTests.test.mjs` (deploy, mint, ARC-4 calls, ARC-200, ARC-72, assets, receipts on a real Algorand node) | `AlgorandTests.cs` |
| `TransportTests.test.mjs`, `WalletSignerTests.test.mjs`, `PackageTests.test.mjs` (JavaScript transports, browser wallets, package formats) | |
| `fixtures/Chain.mjs`, `fixtures/RpcProxy.mjs`, `fixtures/ParityScenario.mjs` | `Fixtures/Chain.cs`, `Fixtures/RpcProxy.cs`, `Fixtures/ParityScenario.cs` |
| `fixtures/AlgorandNode.mjs`, `fixtures/AlgorandParityScenario.mjs`, `fixtures/EsploraServer.mjs` | `Fixtures/AlgorandNode.cs`, `Fixtures/AlgorandParityScenario.cs`, `Fixtures/EsploraServer.cs` |
| | `Fixtures/AlgorandTransactionVectors.cs` (transactions signed by algosdk, to check the native encoding byte by byte) |
| `fixtures/contracts.mjs` (Notarizer, ERC-20, ERC-721, ERC-1155 test contracts, compiled with solc) | received from `npm run test:dotnet` |
| `shared/algorand/*.arc56.json` (Notarizer, ARC-200, ARC-72 and asset-minting test applications, built from `shared/algorand/test_contracts.py` with Algorand Python) | the same files, copied to the test output |
| | `ParityTests.cs`, `AlgorandParityTests.cs` |

`ParityTests` checks that the two implementations are consistent: the TypeScript package runs the parity scenario (deploy, mint, receipt, contract reads, `Web3Contract` methods and calls, NFT mints, token data and decoded transactions) on a local chain, then .NET runs the same calls and compares the JSON field by field, ignoring only hashes, addresses, block numbers, nonces and times. `AlgorandParityTests` does the same on Algorand.

```sh
cd typescript
npm ci
npm test              # TypeScript tests
npm run test:dotnet   # .NET tests and the TypeScript/.NET parity tests (requires the .NET 8 SDK)
```

The Algorand tests need an algod node, such as AlgoKit LocalNet (`algokit localnet start`); without `WEB3_TEST_ALGOD_URI` they are skipped. Both test suites read the same variables, and `npm run test:dotnet` passes them on to .NET together with the Algorand parity scenario:

| Variable | Value |
| --- | --- |
| `WEB3_TEST_ALGOD_URI` | algod URL, e.g. `http://localhost:4001` |
| `WEB3_TEST_ALGOD_TOKEN` | algod API token; default: the LocalNet token |
| `WEB3_TEST_ALGOD_MNEMONIC` | 25-word mnemonic of a funded account; default: the LocalNet dispenser, read from KMD |
| `WEB3_TEST_KMD_URI` | KMD URL for the dispenser; default: port 4002 of the algod host |

```sh
algokit localnet start
WEB3_TEST_ALGOD_URI=http://localhost:4001 npm test
WEB3_TEST_ALGOD_URI=http://localhost:4001 npm run test:dotnet
```

To run them in CI, the workflow has to start LocalNet and set `WEB3_TEST_ALGOD_URI`.

## Continuous integration

Three GitHub Actions workflows run on pushes to `main`, each only when its own files change (and on demand from the Actions tab):

- [`release-npm.yml`](.github/workflows/release-npm.yml) (changes in `typescript/` or `shared/`) runs the TypeScript typecheck and tests, publishes the npm package to GitHub Packages and creates the GitHub Release `npm-v<version>` with the `.tgz` and the two browser bundles. The version is `version` in `typescript/package.json`.
- [`release-nuget.yml`](.github/workflows/release-nuget.yml) (changes in `dotnet/` or `shared/`) runs the .NET tests and the TypeScript/.NET parity test, publishes the NuGet package to GitHub Packages and creates the GitHub Release `nuget-v<version>` with the `.nupkg` and `.snupkg`. The version is `<Version>` in `dotnet/src/Certifie.Web3Service/Certifie.Web3Service.csproj`.
- [`docs.yml`](.github/workflows/docs.yml) publishes `docs/` to GitHub Pages when it changes. Enable it once in **Settings → Pages → Source: GitHub Actions**.

In the two package workflows, a version that is already published gets a build number (`0.1.0-build.1`, `0.1.0-build.2`, …) computed by [`.github/scripts/resolve-version.mjs`](.github/scripts/resolve-version.mjs), and is released as a prerelease; to publish a clean version, raise the version in the project file. The release notes are the `CHANGELOG.md` section for that version plus the commits since the previous release of the same package.

GitHub Packages requires the npm scope to match the repository owner, so the npm package is published as `@<owner>/web3-service` (for example `@certifie/web3-service` when the repository belongs to the `certifie` organization). The packages have the same visibility as the repository, and GitHub Packages always asks for a token to install them, even when they are public: use a GitHub token with the `read:packages` scope.

```sh
# npm: .npmrc in the consuming project
@<owner>:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}
```

```sh
# NuGet (add --prerelease to dotnet add package for build versions)
dotnet nuget add source https://nuget.pkg.github.com/<owner>/index.json --name github --username <user> --password <token> --store-password-in-clear-text
dotnet add package Certifie.Web3Service --source github
```
