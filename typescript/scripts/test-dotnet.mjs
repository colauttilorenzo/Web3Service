// Runs the .NET tests against a local Ganache chain with the same Notarizer contract as the TypeScript tests.
// The TypeScript package first runs the parity scenario on that chain; the .NET ParityTests compare their output with it.
import { spawn } from 'node:child_process';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Chain } from '../test/fixtures/Chain.mjs';
import * as ParityScenario from '../test/fixtures/ParityScenario.mjs';

const chain = await Chain.start();
const directory = await mkdtemp(join(tmpdir(), 'web3-service-dotnet-'));
const artifact = join(directory, 'Notarizer.json');
const parity = join(directory, 'parity.json');
await writeFile(artifact, JSON.stringify({ abi: chain.abi, bytecode: chain.bytecode }));
await writeFile(parity, JSON.stringify(await ParityScenario.run(chain)));
const solution = new URL('../../dotnet/Certifie.Web3Service.sln', import.meta.url).pathname;
const code = await new Promise(resolve => {
  const env = { ...process.env, WEB3_TEST_RPC_PORT: String(chain.server.address().port), WEB3_TEST_ARTIFACT: artifact, WEB3_TEST_PARITY: parity };
  spawn('dotnet', ['test', solution, ...process.argv.slice(2)], { stdio: 'inherit', env }).on('exit', resolve);
});
await chain.close();
await rm(directory, { recursive: true, force: true });
process.exitCode = code ?? 1;
