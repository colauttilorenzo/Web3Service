import { readFile } from 'node:fs/promises';
import { Web3Service, Web3Error } from '@certifie/web3-service';

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable: ${name}`);
  return value;
}

try {
  const artifact = JSON.parse(await readFile(required('ABI_PATH'), 'utf8'));
  const service = new Web3Service({
    chainId: process.env.CHAIN_ID,
    rpcURI: required('RPC_URL'),
    rpcPublicURI: process.env.RPC_FALLBACK_URL,
    nativeCurrency: {
      name: process.env.GAS_TOKEN_NAME,
      symbol: process.env.GAS_TOKEN_SYMBOL,
      decimals: process.env.GAS_TOKEN_DECIMALS === undefined ? undefined : Number(process.env.GAS_TOKEN_DECIMALS),
    },
  }, { signer: { privateKey: required('PRIVATE_KEY') } });
  const address = required('CONTRACT_ADDRESS');
  // mint(address recipient, string hash, string algorithm, string uri): the parameter names come from the contract's ABI.
  const result = await service.getContract(artifact.abi ?? artifact, address).send('mint', {
    recipient: address,
    hash: required('DOCUMENT_HASH'),
    algorithm: process.env.HASH_ALGORITHM ?? 'sha2',
    uri: process.env.METADATA_URI ?? '',
  });
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  console.error(error instanceof Web3Error ? error.toJSON() : error.message);
  process.exitCode = 1;
}
