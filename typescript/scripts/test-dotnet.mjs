// Runs the .NET tests against a local Ganache chain with the same test contracts as the TypeScript tests.
// The TypeScript package first runs the parity scenario on that chain; the .NET ParityTests compare their output with it.
// With WEB3_TEST_ALGOD_URI set (e.g. AlgoKit LocalNet), it does the same on the Algorand node for AlgorandTests and AlgorandParityTests.
import { spawn } from 'node:child_process';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { AlgorandNode, skip as skipAlgorand } from '../test/fixtures/AlgorandNode.mjs';
import * as AlgorandParityScenario from '../test/fixtures/AlgorandParityScenario.mjs';
import { Chain } from '../test/fixtures/Chain.mjs';
import * as ParityScenario from '../test/fixtures/ParityScenario.mjs';

const chain = await Chain.start();
const directory = await mkdtemp(join(tmpdir(), 'web3-service-dotnet-'));
const artifact = join(directory, 'contracts.json');
const parity = join(directory, 'parity.json');
await writeFile(artifact, JSON.stringify({ abi: chain.abi, bytecode: chain.bytecode, contracts: chain.contracts }));
await writeFile(parity, JSON.stringify(await ParityScenario.run(chain)));
const solution = new URL('../../dotnet/Certifie.Web3Service.sln', import.meta.url).pathname;
const code = await new Promise(async resolve => {
  const env = { ...process.env, WEB3_TEST_RPC_PORT: String(chain.server.address().port), WEB3_TEST_ARTIFACT: artifact, WEB3_TEST_PARITY: parity };
  Object.assign(env, await algorandEnvironment());
  spawn('dotnet', ['test', solution, ...process.argv.slice(2)], { stdio: 'inherit', env }).on('exit', resolve);
});
await chain.close();
await rm(directory, { recursive: true, force: true });
process.exitCode = code ?? 1;

/** The node settings and the TypeScript output of the Algorand parity scenario; none when no Algorand node is configured. */
async function algorandEnvironment() {
  if (skipAlgorand) return {};
  const node = await AlgorandNode.start();
  const output = join(directory, 'algorand-parity.json');
  await writeFile(output, JSON.stringify(await AlgorandParityScenario.run(node)));
  return { WEB3_TEST_ALGOD_TOKEN: node.token, WEB3_TEST_ALGOD_MNEMONIC: node.mnemonic, WEB3_TEST_ALGORAND_PARITY: output };
}
