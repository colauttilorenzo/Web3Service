import { Web3Service, Web3Error, type IWallet } from '@certifie/web3-service';
async function derive(): Promise<IWallet> {
  const mnemonic = await Web3Service.createMnemonic();
  return Web3Service.getWallet(mnemonic, 1);
}
void derive;
const serialized: string = new Web3Error('TEST', 'Example').toJSON().code;
void serialized;
