// The same calls as dotnet/test/Certifie.Web3Service.Tests/Fixtures/ParityScenario.cs; the .NET ParityTests compare the two outputs.
import { Web3Service } from '@certifie/web3-service';

export async function run(chain) {
  const service = new Web3Service(chain.provider(), chain.signer());
  const deployed = await service.deploy(chain.abi, chain.bytecode, ['Parity'], { name: 'parity', requestedAt: '123' });
  const address = deployed.tx.contractAddress;
  const document = { hash: { data: 'f'.repeat(64), algorithm: 'sha2' }, metadata: 'ipfs://parity' };
  const minted = await service.mint(chain.abi, address, document, { requestedAt: '456' });
  const receipt = await service.getTransactionReceipt(minted.tx.transactionHash, { name: 'lookup', requestedAt: '789' });
  return {
    deployed, minted, receipt,
    label: await service.callContractMethod('label', chain.abi, address),
    metadata: await service.callContractMethod('metadata', chain.abi, address, [document.hash.data]),
    numbers: await service.callContractMethod('numbers', chain.abi, address),
  };
}
