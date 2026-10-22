# Certifie.Web3Service

Native .NET client for `@certifie/web3-service`: contract deployment, document hash notarization through `mint`, contract reads, tokens and receipts with one API for EVM networks, Algorand and Bitcoin. The provider passed to `Web3Service` selects the blockchain. It does not start Node.js: EVM networks go through [Nethereum](https://nethereum.com/), Algorand through a native algod REST client (Ed25519 and SHA-512/256 from BouncyCastle, no Algorand SDK), Bitcoin through an Esplora REST API.

Same defaults, same error codes and same JSON as the JavaScript library: every numeric value is an integer string (e.g. `"21000000000000"` wei) and the fee is in the native token's smallest unit (wei, microAlgo, satoshi).

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

## Any contract, tokens and NFTs

`GetContract(abi, address)` lists the functions of any contract and calls them by name; the parameters are a `JsonObject`, a dictionary or an anonymous object with the ABI names.

```csharp
Web3Contract token = amoy.GetContract(tokenAbiJson, tokenAddress);
IReadOnlyList<string> standards = token.Standards;   // ERC-20, ERC-173
IReadOnlyList<ContractMethod> methods = token.Methods; // Name, Kind (read, write, payable), Parameters (name → JSON type)
string? owner = await token.OwnerAsync();
JsonNode? balance = await token.CallAsync("balanceOf", new { account = holder });                // read: the value
JsonNode? transfer = await token.CallAsync("transfer", new { to = recipient, amount = "1000" }); // write: the transaction as JSON
TransactionResult deposit = await token.SendAsync("deposit", new { note = "tip" }, new TransactionOptions { Value = 1000 });
TokenInfo coin = await token.GetTokenInfoAsync();                                              // Name, Symbol, Decimals, TotalSupply

Web3Contract nft = amoy.GetContract(nftAbiJson, nftAddress);
NftMint minted = await nft.MintNftAsync(new { to = holder, uri = "ipfs://…" }); // TokenIds, Transaction
TokenInfo art = await nft.GetTokenInfoAsync(minted.TokenIds[0]);                 // Owner, TokenURI

TransactionDetails details = await amoy.GetTransactionAsync(hash, tokenAbiJson); // Method: Name, Signature, Parameters
List<string> detected = Web3Service.DetectStandards(nftAbiJson);                // ERC-721, ERC-721 Metadata, ERC-165
```

The members match the TypeScript `Web3Contract` (`CallAsync`, `ReadAsync`, `SendAsync`, `Encode`, `OwnerAsync`, `MintNftAsync`, `GetTokenInfoAsync`, `GetTransactionAsync`) and serialize to the same JSON.

## Algorand and Bitcoin

The class of the provider selects the implementation: `EvmProvider` (or a URI) for EVM networks, `AvmProvider` for Algorand, `BitcoinProvider` for Bitcoin. Your own classes that implement `IEvmProvider`, `IAvmProvider` or `IBitcoinProvider` work too. The methods and the JSON are the same; an operation the blockchain does not have throws `System.NotImplementedException` before any request.

```csharp
var algorand = new Web3Service(new AvmProvider
{
    RpcURI = "https://testnet-api.algonode.cloud",     // algod
    IndexerURI = "https://testnet-idx.algonode.cloud",  // optional, for old transactions
    ChainId = "testnet-v1.0",                           // genesis ID, checked before every operation
}, new Web3ServiceOptions { Mnemonic = configuration["Algorand:Mnemonic"] }); // 25 words, or PrivateKey

var notarizer = await File.ReadAllTextAsync("Notarizer.arc56.json"); // ARC-56, ARC-32 or ARC-4 description
var deployed = await algorand.DeployAsync(notarizer, "", new object[] { "CERTIFIE" }, options: new TransactionOptions { Value = 200_000 });
var appId = deployed.Tx!.ContractAddress!; // the application ID
await algorand.MintAsync(notarizer, appId, document);
var label = await algorand.GetContract(notarizer, appId).CallAsync("label"); // ARC-4 methods by name, reads are free simulations
var coin = await algorand.CreateTokenAsync(new TokenDefinition { Name = "Certifie Coin", Symbol = "CERT", TotalSupply = 1_000_000, Decimals = 2 });
TokenInfo asset = await algorand.GetTokenInfoAsync(coin.TokenId); // Standard "ASA"

var bitcoin = new Web3Service(new BitcoinProvider { RpcURI = "https://mempool.space/api" }); // an Esplora API
CheckResult tip = await bitcoin.CheckAsync();                          // height and fee in sat/vB
TransactionDetails details = await bitcoin.GetTransactionAsync(txid);  // amounts and fee in satoshi
await bitcoin.DeployAsync(abi, bytecode);                              // NotImplementedException: Bitcoin has no smart contracts
```

| Method | EVM | Algorand | Bitcoin |
| --- | --- | --- | --- |
| `CheckAsync`, `GetTransactionAsync`, `GetTransactionReceiptAsync` | Yes | Yes | Yes |
| `GetContract`, `CallContractMethodAsync`, `MintAsync` | Yes | Yes, ARC-4 applications | `NotImplementedException` |
| `DeployAsync` | Yes, from bytecode | Yes, from an ARC-56 description or `AvmPrograms` | `NotImplementedException` |
| `CreateTokenAsync`, `GetTokenInfoAsync` | `NotImplementedException`: EVM tokens are contracts | Yes, Algorand Standard Assets | `NotImplementedException` |

On Algorand every write is simulated before signing: the library finds the accounts, applications, assets and boxes the application reads, adds the fee of inner transactions, and throws `TRANSACTION_REVERTED` without paying when the application would reject the call. `GetContract` recognizes ARC-200, ARC-72 and ARC-73 tokens. Fees are in microAlgo; `GasUsed` and `Nonce` are `"0"`.

## Wallets

```csharp
string phrase = Web3Service.CreateMnemonic();
WalletInfo wallet = Web3Service.GetWallet(phrase, index: 1); // m/44'/60'/0'/0/1
string privateKey = Web3Service.GetPrivateKey(phrase);

// Algorand accounts are not derived from a path
string algorandPhrase = Certifie.Web3.Algorand.AlgorandWallets.CreateMnemonic();      // 25 words
WalletInfo account = Certifie.Web3.Algorand.AlgorandWallets.GetWallet(algorandPhrase); // Address, PrivateKey (32-byte seed as 0x hex)
```

## Errors

Every error is a `Web3Exception` with a `Code` (`INVALID_ARGUMENT`, `SIGNER_REQUIRED`, `CHAIN_ID_MISMATCH`, `PROVIDER_UNAVAILABLE`, `RPC_ERROR`, `METHOD_NOT_FOUND`, `NOT_SUPPORTED`, `TRANSACTION_SUBMISSION_FAILED`, `TRANSACTION_REVERTED`, `TRANSACTION_NOT_FOUND`, `TOKEN_NOT_FOUND`, `RECEIPT_DETAILS_FAILED`) and, when available, a `TransactionHash`. `ToInfo()` returns the serializable form without RPC details.

An operation the blockchain does not offer, such as `DeployAsync` on Bitcoin, throws the standard `System.NotImplementedException` instead (code `NOT_IMPLEMENTED` in TypeScript).

## ASP.NET Core

```csharp
builder.Services.AddSingleton<IWeb3Service>(_ => new Web3Service(
    builder.Configuration["Web3:RpcURI"]!,
    new Web3ServiceOptions { PrivateKey = builder.Configuration["Web3:PrivateKey"] }));

app.MapPost("/notarize", async (IWeb3Service web3, NotarizeRequest body) =>
    Results.Ok(await web3.MintAsync(abiJson, body.Contract, body.Document)));
```

Register it as a singleton: writes on the same instance are serialized, so nonces stay distinct. Depending on `IWeb3Service` keeps the rest of the application independent of the blockchain, which is chosen by the provider in the registration, and lets tests replace the service.

## Development

`src/Certifie.Web3Service` has the same files and method names as `typescript/src` (see the [root README](../README.md)): when you fix or change a method here, apply the same change to the TypeScript file with the same name.

The tests in `test/Certifie.Web3Service.Tests` mirror the TypeScript tests (`UnitTests`, `ChainTests`, `MultiChainTests`, `AlgorandTests`) and read the expected values from `../shared/test-vectors.json`. `ParityTests` and `AlgorandParityTests` compare the JSON returned by this package with the TypeScript output for the same calls. They need a local chain, which the npm script starts:

```bash
cd typescript
npm ci
npm run test:dotnet
```

The Algorand tests need an algod node, such as AlgoKit LocalNet, and are skipped without `WEB3_TEST_ALGOD_URI`:

```bash
algokit localnet start
WEB3_TEST_ALGOD_URI=http://localhost:4001 npm run test:dotnet
```

`WEB3_TEST_ALGOD_TOKEN` and `WEB3_TEST_ALGOD_MNEMONIC` default to the LocalNet token and dispenser.
