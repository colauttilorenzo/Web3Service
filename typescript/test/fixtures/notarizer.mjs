import assert from 'node:assert/strict';
import solc from 'solc';

const source = `
// SPDX-License-Identifier: UNLICENSED
pragma solidity ^0.8.0;
contract Notarizer {
  struct Document { string hash; string algorithm; string metadataURI; }
  mapping(string => Document) private documents;
  string public label;
  event Notarized(string hash);
  constructor(string memory name) { label = name; }
  function mint(address recipient, string memory hash, string memory algorithm, string memory uri) public {
    require(recipient == address(this), "wrong recipient");
    require(bytes(documents[hash].hash).length == 0, "already notarized");
    documents[hash] = Document(hash, algorithm, uri);
    emit Notarized(hash);
  }
  function metadata(string memory hash) public view returns (string memory, string memory, string memory) {
    Document storage doc = documents[hash];
    return (doc.hash, doc.algorithm, doc.metadataURI);
  }
  function numbers() public pure returns (uint256, int256, bool) { return (9007199254740993, -42, true); }
}`;
const compiled = JSON.parse(solc.compile(JSON.stringify({
  language: 'Solidity', sources: { 'Notarizer.sol': { content: source } },
  settings: { evmVersion: 'shanghai', outputSelection: { '*': { '*': ['abi', 'evm.bytecode'] } } },
})));
assert.equal(compiled.errors?.some(error => error.severity === 'error') ?? false, false);
const artifact = compiled.contracts['Notarizer.sol'].Notarizer;
export const abi = artifact.abi;
export const bytecode = `0x${artifact.evm.bytecode.object}`;
