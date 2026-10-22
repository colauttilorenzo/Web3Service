// An Algorand node for the Algorand tests, such as AlgoKit LocalNet (`algokit localnet start`). The .NET tests use the same variables:
//   WEB3_TEST_ALGOD_URI      algod URL, e.g. http://localhost:4001; without it the Algorand tests are skipped
//   WEB3_TEST_ALGOD_TOKEN    algod API token; default: the LocalNet token
//   WEB3_TEST_ALGOD_MNEMONIC 25-word mnemonic of a funded account; default: the LocalNet dispenser, read from KMD
//   WEB3_TEST_KMD_URI        KMD URL for the dispenser; default: port 4002 of the algod host
import { readFileSync } from 'node:fs';
import algosdk from 'algosdk';

const LOCALNET_TOKEN = 'a'.repeat(64);
const LOCALNET_WALLET = 'unencrypted-default-wallet';
const CONTRACTS = ['Notarizer', 'TestToken', 'TestNft', 'TestAssetMinter'];
const env = process.env;

/** The reason to skip the Algorand tests, or false when a node is configured. */
export const skip = env.WEB3_TEST_ALGOD_URI ? false : 'Set WEB3_TEST_ALGOD_URI to run the Algorand tests, e.g. against AlgoKit LocalNet.';

/** Name → ARC-56 contract, compiled from shared/algorand/test_contracts.py. */
export const contracts = Object.fromEntries(CONTRACTS.map(name => [name, JSON.parse(readFileSync(contractFile(name), 'utf8'))]));

export class AlgorandNode {
  constructor(uri, token, genesisId, mnemonic) {
    Object.assign(this, { uri, token, genesisId, mnemonic, address: algosdk.mnemonicToSecretKey(mnemonic).addr.toString() });
  }

  static async start() {
    const uri = env.WEB3_TEST_ALGOD_URI;
    const token = env.WEB3_TEST_ALGOD_TOKEN ?? LOCALNET_TOKEN;
    const { genesisID } = await client(algosdk.Algodv2, uri, token).getTransactionParams().do();
    return new AlgorandNode(uri, token, genesisID, env.WEB3_TEST_ALGOD_MNEMONIC ?? await dispenser(uri, token));
  }

  provider(rpcURI = this.uri) {
    return { kind: 'avm', rpcURI, token: this.token, chainId: this.genesisId };
  }

  signer() {
    return { signer: { mnemonic: this.mnemonic }, pollingIntervalMs: 100 };
  }

  /** A stand-in for a wallet such as Pera: signs the encoded transactions with the funded account and records them. */
  signAll(transactions, signed) {
    const { sk } = algosdk.mnemonicToSecretKey(this.mnemonic);
    signed.push(...transactions);
    return transactions.map(bytes => algosdk.decodeUnsignedTransaction(bytes).signTxn(sk));
  }
}

/** The richest account of the LocalNet KMD wallet. */
async function dispenser(algodUri, token) {
  const url = new URL(algodUri);
  url.port = '4002';
  const kmd = client(algosdk.Kmd, env.WEB3_TEST_KMD_URI ?? url.toString(), token);
  const { wallets } = await kmd.listWallets();
  const { wallet_handle_token: handle } = await kmd.initWalletHandle(wallets.find(wallet => wallet.name === LOCALNET_WALLET).id, '');
  const { addresses } = await kmd.listKeys(handle);
  const algod = client(algosdk.Algodv2, algodUri, token);
  const balances = await Promise.all(addresses.map(async address => (await algod.accountInformation(address).do()).amount));
  const richest = addresses[balances.indexOf(balances.reduce((a, b) => (a > b ? a : b)))];
  return algosdk.secretKeyToMnemonic((await kmd.exportKey(handle, '', richest)).private_key);
}

/** algosdk takes the port apart from the URL. */
function client(Client, uri, token) {
  const url = new URL(uri);
  const { port } = url;
  url.port = '';
  return new Client(token, url.toString().replace(/\/$/, ''), port);
}

function contractFile(name) {
  return new URL(`../../../shared/algorand/${name}.arc56.json`, import.meta.url);
}
