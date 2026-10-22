// The test contracts, compiled with solc: the Notarizer, an ERC-20 token, an ERC-721 NFT and an ERC-1155 multi-token.
// The .NET tests receive the same compiled contracts through `npm run test:dotnet`.
import assert from 'node:assert/strict';
import solc from 'solc';

const notarizer = `
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

const token = `
contract TestToken {
  string public name; string public symbol; uint8 public decimals = 18; uint256 public totalSupply; address public owner;
  mapping(address => uint256) public balanceOf;
  mapping(address => mapping(address => uint256)) public allowance;
  uint256 public deposits;
  event Transfer(address indexed from, address indexed to, uint256 value);
  event Approval(address indexed owner, address indexed spender, uint256 value);
  constructor(string memory tokenName, string memory tokenSymbol, uint256 supply) {
    name = tokenName; symbol = tokenSymbol; owner = msg.sender; totalSupply = supply; balanceOf[msg.sender] = supply;
    emit Transfer(address(0), msg.sender, supply);
  }
  function transfer(address to, uint256 amount) public returns (bool) { return move(msg.sender, to, amount); }
  function approve(address spender, uint256 amount) public returns (bool) {
    allowance[msg.sender][spender] = amount; emit Approval(msg.sender, spender, amount); return true;
  }
  function transferFrom(address from, address to, uint256 amount) public returns (bool) {
    allowance[from][msg.sender] -= amount; return move(from, to, amount);
  }
  function transferOwnership(address newOwner) public { require(msg.sender == owner, "not owner"); owner = newOwner; }
  function deposit(string memory note) public payable { require(bytes(note).length > 0, "note"); deposits += msg.value; }
  function move(address from, address to, uint256 amount) private returns (bool) {
    balanceOf[from] -= amount; balanceOf[to] += amount; emit Transfer(from, to, amount); return true;
  }
}`;

const nft = `
contract TestNft {
  string public name; string public symbol; uint256 public totalSupply; address public owner;
  mapping(uint256 => address) private owners; mapping(address => uint256) private balances;
  mapping(uint256 => address) private approvals; mapping(address => mapping(address => bool)) private operators;
  mapping(uint256 => string) private uris;
  event Transfer(address indexed from, address indexed to, uint256 indexed tokenId);
  event Approval(address indexed owner, address indexed approved, uint256 indexed tokenId);
  event ApprovalForAll(address indexed owner, address indexed operator, bool approved);
  constructor(string memory collection, string memory collectionSymbol) { name = collection; symbol = collectionSymbol; owner = msg.sender; }
  function supportsInterface(bytes4 id) public pure returns (bool) { return id == 0x80ac58cd || id == 0x5b5e139f || id == 0x01ffc9a7; }
  function balanceOf(address holder) public view returns (uint256) { return balances[holder]; }
  function ownerOf(uint256 tokenId) public view returns (address) { require(owners[tokenId] != address(0), "no token"); return owners[tokenId]; }
  function tokenURI(uint256 tokenId) public view returns (string memory) { ownerOf(tokenId); return uris[tokenId]; }
  function approve(address to, uint256 tokenId) public { approvals[tokenId] = to; emit Approval(ownerOf(tokenId), to, tokenId); }
  function getApproved(uint256 tokenId) public view returns (address) { return approvals[tokenId]; }
  function setApprovalForAll(address operator, bool approved) public { operators[msg.sender][operator] = approved; emit ApprovalForAll(msg.sender, operator, approved); }
  function isApprovedForAll(address holder, address operator) public view returns (bool) { return operators[holder][operator]; }
  function transferFrom(address from, address to, uint256 tokenId) public {
    require(ownerOf(tokenId) == from && (msg.sender == from || approvals[tokenId] == msg.sender || operators[from][msg.sender]), "not allowed");
    balances[from] -= 1; balances[to] += 1; owners[tokenId] = to; approvals[tokenId] = address(0); emit Transfer(from, to, tokenId);
  }
  function safeTransferFrom(address from, address to, uint256 tokenId) public { transferFrom(from, to, tokenId); }
  function safeTransferFrom(address from, address to, uint256 tokenId, bytes memory) public { transferFrom(from, to, tokenId); }
  function safeMint(address to, string memory uri) public returns (uint256) {
    require(msg.sender == owner, "not owner");
    uint256 tokenId = ++totalSupply; owners[tokenId] = to; balances[to] += 1; uris[tokenId] = uri;
    emit Transfer(address(0), to, tokenId);
    return tokenId;
  }
}`;

const multiToken = `
contract TestMultiToken {
  mapping(uint256 => mapping(address => uint256)) private balances; mapping(address => mapping(address => bool)) private operators;
  string private baseUri;
  event TransferSingle(address indexed operator, address indexed from, address indexed to, uint256 id, uint256 value);
  event TransferBatch(address indexed operator, address indexed from, address indexed to, uint256[] ids, uint256[] values);
  event ApprovalForAll(address indexed account, address indexed operator, bool approved);
  constructor(string memory uriTemplate) { baseUri = uriTemplate; }
  function uri(uint256) public view returns (string memory) { return baseUri; }
  function balanceOf(address account, uint256 id) public view returns (uint256) { return balances[id][account]; }
  function balanceOfBatch(address[] memory accounts, uint256[] memory ids) public view returns (uint256[] memory result) {
    result = new uint256[](accounts.length);
    for (uint256 i = 0; i < accounts.length; i++) result[i] = balances[ids[i]][accounts[i]];
  }
  function setApprovalForAll(address operator, bool approved) public { operators[msg.sender][operator] = approved; emit ApprovalForAll(msg.sender, operator, approved); }
  function isApprovedForAll(address account, address operator) public view returns (bool) { return operators[account][operator]; }
  function safeTransferFrom(address from, address to, uint256 id, uint256 amount, bytes memory) public {
    balances[id][from] -= amount; balances[id][to] += amount; emit TransferSingle(msg.sender, from, to, id, amount);
  }
  function safeBatchTransferFrom(address from, address to, uint256[] memory ids, uint256[] memory amounts, bytes memory) public {
    for (uint256 i = 0; i < ids.length; i++) { balances[ids[i]][from] -= amounts[i]; balances[ids[i]][to] += amounts[i]; }
    emit TransferBatch(msg.sender, from, to, ids, amounts);
  }
  function mint(address to, uint256 id, uint256 amount, bytes memory) public {
    balances[id][to] += amount; emit TransferSingle(msg.sender, address(0), to, id, amount);
  }
  function mint(address to, uint256[] memory ids, uint256[] memory amounts) public {
    for (uint256 i = 0; i < ids.length; i++) balances[ids[i]][to] += amounts[i];
    emit TransferBatch(msg.sender, address(0), to, ids, amounts);
  }
}`;

const sources = { Notarizer: notarizer, TestToken: token, TestNft: nft, TestMultiToken: multiToken };
const header = '// SPDX-License-Identifier: UNLICENSED\npragma solidity ^0.8.0;\n';
const compiled = JSON.parse(solc.compile(JSON.stringify({
  language: 'Solidity',
  sources: Object.fromEntries(Object.entries(sources).map(([name, source]) => [`${name}.sol`, { content: header + source }])),
  settings: { evmVersion: 'shanghai', outputSelection: { '*': { '*': ['abi', 'evm.bytecode'] } } },
})));
assert.deepEqual(compiled.errors?.filter(error => error.severity === 'error') ?? [], []);

/** Name → { abi, bytecode }. */
export const contracts = Object.fromEntries(Object.keys(sources).map(name => {
  const artifact = compiled.contracts[`${name}.sol`][name];
  return [name, { abi: artifact.abi, bytecode: `0x${artifact.evm.bytecode.object}` }];
}));
