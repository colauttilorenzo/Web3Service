# CERTIFIE Web3 Service

[![Test and publish](https://github.com/colauttilorenzo/Web3Service/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/colauttilorenzo/Web3Service/actions/workflows/release.yml)
[![Documentation](https://github.com/colauttilorenzo/Web3Service/actions/workflows/docs.yml/badge.svg?branch=main)](https://github.com/colauttilorenzo/Web3Service/actions/workflows/docs.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-0a7bbb?logo=githubpages&logoColor=white)](https://colauttilorenzo.github.io/Web3Service/)
[![npm on GitHub Packages](https://img.shields.io/badge/npm-%40colauttilorenzo%2Fweb3--service-cb3837?logo=npm&logoColor=white)](https://github.com/colauttilorenzo/Web3Service/pkgs/npm/web3-service)
[![NuGet on GitHub Packages](https://img.shields.io/badge/NuGet-Certifie.Web3Service-004880?logo=nuget&logoColor=white)](https://github.com/colauttilorenzo/Web3Service/pkgs/nuget/Certifie.Web3Service)
[![Node.js](https://img.shields.io/badge/Node.js-%E2%89%A520-5fa04e?logo=nodedotjs&logoColor=white)](typescript/package.json)
[![TypeScript](https://img.shields.io/badge/TypeScript-strict-3178c6?logo=typescript&logoColor=white)](typescript/)
[![.NET](https://img.shields.io/badge/.NET-net8.0%20%7C%20netstandard2.0-512bd4?logo=dotnet&logoColor=white)](dotnet/)
[![Changelog](https://img.shields.io/badge/changelog-CHANGELOG.md-blue)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-proprietary-lightgrey)](typescript/package.json)

Notarization of document hashes on EVM networks, in two implementations that behave the same way:

| Folder | Package | Readme |
| --- | --- | --- |
| [`typescript/`](typescript/) | npm `@certifie/web3-service` (Node.js, bundled frontends, browser script) | [`typescript/README.md`](typescript/README.md) |
| [`dotnet/`](dotnet/) | NuGet `Certifie.Web3Service` (net8.0, netstandard2.0) | [`dotnet/README.md`](dotnet/README.md) |
| [`docs/`](docs/index.html) | Documentation in Italian, open `docs/index.html` in a browser | |
| [`shared/`](shared/) | Expected values used by the tests of both implementations | |

Release notes are in [`CHANGELOG.md`](CHANGELOG.md).

## Same structure in both languages

Each implementation has a `src` and a `test` folder, and every source file has a counterpart with the same name:

| TypeScript (`typescript/src`) | C# (`dotnet/src/Certifie.Web3Service`) | Role |
| --- | --- | --- |
| `Web3Service.ts` | `Web3Service.cs` | Public API |
| `Models.ts` | `Models.cs` | Public types |
| `Web3Exception.ts` (`Web3Error`) | `Web3Exception.cs` | Errors and their JSON |
| `Internal/Configuration.ts` | `Internal/Configuration.cs` | Defaults and validation |
| `Internal/ContractCalls.ts` | `Internal/ContractCalls.cs` | ABI lookups, JSON-safe outputs |
| `Internal/Errors.ts` | `Internal/Errors.cs` | Shared error factories |
| `Internal/HdWallets.ts` | `Internal/HdWallets.cs` | BIP-39 / BIP-44 wallets |
| `Internal/Integers.ts` | `Internal/Integers.cs` | Integer parsing and formatting |
| `Internal/KeySigner.ts` | `Internal/KeySigner.cs` | Local signing with a private key or mnemonic |
| `Internal/Receipts.ts` | `Internal/Receipts.cs` | Receipt lookups and polling |
| `Internal/RpcConnector.ts` | `Internal/RpcConnector.cs` | Endpoint selection and chain check |
| `Internal/RpcSession.ts` | `Internal/RpcSession.cs` | One connection with timeouts |
| `Internal/TransactionRequest.ts` | `Internal/TransactionRequest.cs` | Encoded call, gas limit |
| `Internal/TransactionResults.ts` | `Internal/TransactionResults.cs` | Receipt to `ITransaction` / `TransactionResult` |
| `Internal/WriteQueue.ts` | `Internal/WriteQueue.cs` | One write at a time per service |

Method names match too: the TypeScript name is the C# name with a lowercase first letter and without `Async` (`MintAsync` → `mint`, `Receipts.WaitAsync` → `Receipts.wait`). C# static classes are TypeScript modules imported as namespaces (`import * as Receipts from './Receipts.js'`), so the call sites read the same. A fix or change goes in the file with the same name on both sides.

Only three TypeScript files have no C# counterpart, because they cover what exists only in JavaScript: `Internal/WalletSigner.ts` (browser wallets such as MetaMask), `Internal/TimeoutHttpProvider.ts` and `Internal/TimeoutWebSocketProvider.ts` (web3.js transports; .NET uses `HttpClient` and `WebSocketClient`).

## Tests

The tests have the same names in both languages and read their expected values from [`shared/test-vectors.json`](shared/test-vectors.json):

| TypeScript (`typescript/test`) | C# (`dotnet/test/Certifie.Web3Service.Tests`) |
| --- | --- |
| `UnitTests.test.mjs` | `UnitTests.cs` |
| `ChainTests.test.mjs` | `ChainTests.cs` |
| `fixtures/Chain.mjs`, `fixtures/RpcProxy.mjs`, `fixtures/ParityScenario.mjs` | `Fixtures/Chain.cs`, `Fixtures/RpcProxy.cs`, `Fixtures/ParityScenario.cs` |
| | `ParityTests.cs` |

`ParityTests` checks that the two implementations are consistent: the TypeScript package runs the parity scenario (deploy, mint, receipt and contract reads) on a local chain, then .NET runs the same calls and compares the JSON field by field, ignoring only hashes, addresses, block numbers and times.

```sh
cd typescript
npm ci
npm test              # TypeScript tests
npm run test:dotnet   # .NET tests and the TypeScript/.NET parity test (requires the .NET 8 SDK)
```

## Continuous integration

Two GitHub Actions workflows run on every push to `main`:

- [`release.yml`](.github/workflows/release.yml) runs the TypeScript tests, the .NET tests and the parity test, then publishes both packages to GitHub Packages. The version is the one in `typescript/package.json`; if that version is already published, it gets a build number (`0.1.0-build.1`, `0.1.0-build.2`, …), computed by [`.github/scripts/resolve-version.mjs`](.github/scripts/resolve-version.mjs). To publish a clean version, raise `version` in `typescript/package.json`.
- [`docs.yml`](.github/workflows/docs.yml) publishes `docs/` to GitHub Pages when it changes. Enable it once in **Settings → Pages → Source: GitHub Actions**.

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
