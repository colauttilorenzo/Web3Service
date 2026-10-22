# Certifie.Web3Service

Native .NET client for `@certifie/web3-service`: contract deployment, document hash notarization through `mint`, contract reads and receipts on EVM networks. It does not start Node.js: it uses [Nethereum](https://nethereum.com/).

Same defaults, same error codes and same JSON as the JavaScript library: every numeric value is an integer string (e.g. `"21000000000000"` wei) and the gas cost is in the native token's smallest unit.

Targets: `net8.0` and `netstandard2.0` (so .NET 6/7/8/9+, ASP.NET Core, Azure Functions and so on).

## Installation

```bash
# from the NuGet feed, once published
dotnet add package Certifie.Web3Service

# or from the local package
dotnet add package Certifie.Web3Service --source ./artifacts/nuget
```

## Initialization

```csharp
using Certifie.Web3;

// URI only: Ethereum defaults (0x1, Ether, ETH, 18 decimals). HTTP(S) or WS(S).
var ethereum = new Web3Service("https://node.example");

// Full configuration; omitted fields use the defaults.
var amoy = new Web3Service(new EvmProvider
{
    RpcURI = "wss://polygon-amoy.example",
    RpcPublicURI = "https://rpc-amoy.polygon.technology", // fallback
    ChainId = "80002",
    ChainName = "Polygon Amoy",
    NativeCurrency = new NativeCurrency { Name = "POL", Symbol = "POL", Decimals = 18 },
}, new Web3ServiceOptions
{
    PrivateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY"), // or Mnemonic + WalletIndex (default 1)
    Timeout = TimeSpan.FromSeconds(30),
    TransactionTimeout = TimeSpan.FromMinutes(2),
});
```

## Notarization and deployment

```csharp
var deployed = await amoy.DeployAsync(abiJson, bytecode, new object[] { "CERTIFIE" });
var contract = deployed.Tx!.ContractAddress!;

var minted = await amoy.MintAsync(abiJson, contract, new NotarizationDocument
{
    Hash = new NotarizationHash { Data = sha256Hex, Algorithm = "sha2" },
    Metadata = "ipfs://...",
});

Console.WriteLine(minted.Tx!.TransactionHash);
Console.WriteLine($"{minted.NativeFee!.Amount} (smallest unit of {minted.NativeFee.Symbol})");

// Same JSON as the JavaScript library
var json = System.Text.Json.JsonSerializer.Serialize(minted);
```

## Reads and receipts

```csharp
JsonNode? label = await amoy.CallContractMethodAsync("label", abiJson, contract);
JsonNode? metadata = await amoy.CallContractMethodAsync("metadata", abiJson, contract, new object[] { sha256Hex }); // several outputs → JsonArray
TransactionResult receipt = await amoy.GetTransactionReceiptAsync(transactionHash);
CheckResult status = await amoy.CheckAsync();
```

## Wallets

```csharp
string phrase = Web3Service.CreateMnemonic();
WalletInfo wallet = Web3Service.GetWallet(phrase, index: 1); // m/44'/60'/0'/0/1
string privateKey = Web3Service.GetPrivateKey(phrase);
```

## Errors

Every error is a `Web3Exception` with a `Code` (`INVALID_ARGUMENT`, `SIGNER_REQUIRED`, `CHAIN_ID_MISMATCH`, `PROVIDER_UNAVAILABLE`, `RPC_ERROR`, `METHOD_NOT_FOUND`, `TRANSACTION_SUBMISSION_FAILED`, `TRANSACTION_REVERTED`, `TRANSACTION_NOT_FOUND`, `RECEIPT_DETAILS_FAILED`) and, when available, a `TransactionHash`. `ToInfo()` returns the serializable form without RPC details.

## ASP.NET Core

```csharp
builder.Services.AddSingleton(_ => new Web3Service(
    builder.Configuration["Web3:RpcURI"]!,
    new Web3ServiceOptions { PrivateKey = builder.Configuration["Web3:PrivateKey"] }));

app.MapPost("/notarize", async (Web3Service web3, NotarizeRequest body) =>
    Results.Ok(await web3.MintAsync(abiJson, body.Contract, body.Document)));
```

Register it as a singleton: writes on the same instance are serialized, so nonces stay distinct.

## Development

`src/Certifie.Web3Service` has the same files and method names as `typescript/src` (see the [root README](../README.md)): when you fix or change a method here, apply the same change to the TypeScript file with the same name.

The tests in `test/Certifie.Web3Service.Tests` mirror the TypeScript tests (`UnitTests`, `ChainTests`) and read the expected values from `../shared/test-vectors.json`. `ParityTests` compares the JSON returned by this package with the TypeScript output for the same calls. They need a local chain, which the npm script starts:

```bash
cd typescript
npm ci
npm run test:dotnet
```
