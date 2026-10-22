"""The Algorand test contracts: the ARC-4 counterparts of the Solidity contracts in typescript/test/fixtures/contracts.mjs.

The TypeScript and .NET tests use the compiled ARC-56 files next to this one. After a change, rebuild them from this folder with
Algorand Python (pip install puyapy):

    puyapy test_contracts.py --output-arc56 --output-bytecode --no-output-teal --no-output-source-map --out-dir .
"""

import typing

from algopy import (
    Account, ARC4Contract, BoxMap, Bytes, Global, String, Txn, UInt64, arc4, gtxn, itxn, op, subroutine,
)

Bytes4: typing.TypeAlias = arc4.StaticArray[arc4.Byte, typing.Literal[4]]
Bytes8: typing.TypeAlias = arc4.StaticArray[arc4.Byte, typing.Literal[8]]
Bytes32: typing.TypeAlias = arc4.StaticArray[arc4.Byte, typing.Literal[32]]
Bytes256: typing.TypeAlias = arc4.StaticArray[arc4.Byte, typing.Literal[256]]


class Document(arc4.Struct):
    algorithm: arc4.String
    metadata: arc4.String


@subroutine
def padded(text: String, size: UInt64) -> Bytes:
    """The text as a fixed-size byte value, padded with zero bytes."""
    assert text.bytes.length <= size, "text too long"
    return text.bytes + op.bzero(size - text.bytes.length)


@subroutine
def pair_key(first: Account, second: Account) -> Bytes:
    """Box names are at most 64 bytes, so an account pair is stored under its hash."""
    return op.sha256(first.bytes + second.bytes)


class Notarizer(ARC4Contract):
    """Notarizes document hashes. Fund the application account at deploy time: each document is stored in a box."""

    def __init__(self) -> None:
        self.title = String()
        self.documents = BoxMap(Bytes, Document, key_prefix=b"d")

    @arc4.abimethod(create="require")
    def create(self, label: arc4.String) -> None:
        self.title = label.native

    @arc4.abimethod
    def mint(self, recipient: arc4.Address, hash: arc4.String, algorithm: arc4.String, uri: arc4.String) -> None:
        assert recipient == Global.current_application_address, "wrong recipient"
        key = op.sha256(hash.native.bytes)
        assert key not in self.documents, "already notarized"
        self.documents[key] = Document(algorithm, uri)
        arc4.emit("Notarized(string)", hash)

    @arc4.abimethod(readonly=True)
    def label(self) -> arc4.String:
        return arc4.String(self.title)

    @arc4.abimethod(readonly=True)
    def metadata(self, hash: arc4.String) -> tuple[arc4.String, arc4.String, arc4.String]:
        document = self.documents[op.sha256(hash.native.bytes)].copy()
        return hash, document.algorithm, document.metadata

    @arc4.abimethod(readonly=True)
    def numbers(self) -> tuple[arc4.UInt64, arc4.UInt8, arc4.Bool]:
        return arc4.UInt64(2**64 - 1), arc4.UInt8(255), arc4.Bool(True)


class TestToken(ARC4Contract):
    """An ARC-200 token with an owner and a payable deposit method. Fund the application account: balances are boxes."""

    def __init__(self) -> None:
        self.name = Bytes()
        self.symbol = Bytes()
        self.supply = arc4.UInt256()
        self.owner_account = Account()
        self.deposit_total = UInt64()
        self.balances = BoxMap(Account, arc4.UInt256, key_prefix=b"b")
        self.allowances = BoxMap(Bytes, arc4.UInt256, key_prefix=b"a")

    @arc4.abimethod(create="require")
    def createApplication(self, name: arc4.String, symbol: arc4.String, supply: arc4.UInt256) -> None:
        self.name = padded(name.native, UInt64(32))
        self.symbol = padded(symbol.native, UInt64(8))
        self.supply = supply
        self.owner_account = Txn.sender

    @arc4.abimethod
    def mintInitialSupply(self) -> None:
        """Boxes cannot be written while the application is created, so the owner receives the supply in a second call."""
        assert Txn.sender == self.owner_account, "not owner"
        assert Txn.sender not in self.balances, "already minted"
        supply = self.supply
        self.balances[Txn.sender] = supply
        arc4.emit("arc200_Transfer(address,address,uint256)", arc4.Address(Global.zero_address), arc4.Address(Txn.sender), supply)

    @arc4.abimethod(readonly=True)
    def arc200_name(self) -> Bytes32:
        return Bytes32.from_bytes(self.name)

    @arc4.abimethod(readonly=True)
    def arc200_symbol(self) -> Bytes8:
        return Bytes8.from_bytes(self.symbol)

    @arc4.abimethod(readonly=True)
    def arc200_decimals(self) -> arc4.UInt8:
        return arc4.UInt8(6)

    @arc4.abimethod(readonly=True)
    def arc200_totalSupply(self) -> arc4.UInt256:
        return self.supply

    @arc4.abimethod(readonly=True)
    def arc200_balanceOf(self, owner: arc4.Address) -> arc4.UInt256:
        return self.balances.get(owner.native, default=arc4.UInt256(0))

    @arc4.abimethod(readonly=True)
    def arc200_allowance(self, owner: arc4.Address, spender: arc4.Address) -> arc4.UInt256:
        return self.allowances.get(pair_key(owner.native, spender.native), default=arc4.UInt256(0))

    @arc4.abimethod
    def arc200_transfer(self, to: arc4.Address, value: arc4.UInt256) -> arc4.Bool:
        self._move(Txn.sender, to.native, value)
        return arc4.Bool(True)

    @arc4.abimethod
    def arc200_transferFrom(self, from_: arc4.Address, to: arc4.Address, value: arc4.UInt256) -> arc4.Bool:
        key = pair_key(from_.native, Txn.sender)
        allowed = self.allowances.get(key, default=arc4.UInt256(0))
        assert allowed.native >= value.native, "allowance"
        self.allowances[key] = arc4.UInt256(allowed.native - value.native)
        self._move(from_.native, to.native, value)
        return arc4.Bool(True)

    @arc4.abimethod
    def arc200_approve(self, spender: arc4.Address, value: arc4.UInt256) -> arc4.Bool:
        self.allowances[pair_key(Txn.sender, spender.native)] = value
        arc4.emit("arc200_Approval(address,address,uint256)", arc4.Address(Txn.sender), spender, value)
        return arc4.Bool(True)

    @arc4.abimethod(readonly=True)
    def owner(self) -> arc4.Address:
        return arc4.Address(self.owner_account)

    @arc4.abimethod
    def transferOwnership(self, newOwner: arc4.Address) -> None:
        assert Txn.sender == self.owner_account, "not owner"
        self.owner_account = newOwner.native

    @arc4.abimethod
    def deposit(self, payment: gtxn.PaymentTransaction, note: arc4.String) -> None:
        assert payment.receiver == Global.current_application_address, "wrong receiver"
        assert note.native != "", "note"
        self.deposit_total += payment.amount

    @arc4.abimethod(readonly=True)
    def deposits(self) -> arc4.UInt64:
        return arc4.UInt64(self.deposit_total)

    @subroutine
    def _move(self, sender: Account, receiver: Account, value: arc4.UInt256) -> None:
        balance = self.balances.get(sender, default=arc4.UInt256(0))
        assert balance.native >= value.native, "balance"
        self.balances[sender] = arc4.UInt256(balance.native - value.native)
        received = self.balances.get(receiver, default=arc4.UInt256(0))
        self.balances[receiver] = arc4.UInt256(received.native + value.native)
        arc4.emit("arc200_Transfer(address,address,uint256)", arc4.Address(sender), arc4.Address(receiver), value)


class TestNft(ARC4Contract):
    """An ARC-72 NFT collection with enumeration, management and ARC-73. Fund the application account: tokens are boxes."""

    def __init__(self) -> None:
        self.minted = UInt64()
        self.owner_account = Account()
        self.owners = BoxMap(arc4.UInt256, arc4.Address, key_prefix=b"o")
        self.uris = BoxMap(arc4.UInt256, Bytes256, key_prefix=b"u")
        self.balances = BoxMap(Account, arc4.UInt256, key_prefix=b"b")
        self.approvals = BoxMap(arc4.UInt256, arc4.Address, key_prefix=b"a")
        self.operators = BoxMap(Bytes, arc4.Bool, key_prefix=b"p")

    @arc4.abimethod(create="require")
    def createApplication(self) -> None:
        self.owner_account = Txn.sender

    @arc4.abimethod
    def arc72_mint(self, to: arc4.Address, uri: arc4.String) -> arc4.UInt256:
        assert Txn.sender == self.owner_account, "not owner"
        self.minted += 1
        tokenId = arc4.UInt256(self.minted)
        self.uris[tokenId] = Bytes256.from_bytes(padded(uri.native, UInt64(256)))
        self._receive(to.native, tokenId)
        arc4.emit("arc72_Transfer(address,address,uint256)", arc4.Address(Global.zero_address), to, tokenId)
        return tokenId

    @arc4.abimethod(readonly=True)
    def supportsInterface(self, interfaceId: Bytes4) -> arc4.Bool:
        """ARC-73 itself, ARC-72 core, ARC-72 enumeration and ARC-72 management."""
        selector = interfaceId.bytes
        return arc4.Bool(
            selector == Bytes.from_hex("4e913796") or selector == Bytes.from_hex("53f02a40")
            or selector == Bytes.from_hex("a57d4fdb") or selector == Bytes.from_hex("b9c6f696")
        )

    @arc4.abimethod(readonly=True)
    def arc72_ownerOf(self, tokenId: arc4.UInt256) -> arc4.Address:
        return self.owners[tokenId]

    @arc4.abimethod(readonly=True)
    def arc72_tokenURI(self, tokenId: arc4.UInt256) -> Bytes256:
        return self.uris[tokenId]

    @arc4.abimethod(readonly=True)
    def arc72_totalSupply(self) -> arc4.UInt256:
        return arc4.UInt256(self.minted)

    @arc4.abimethod(readonly=True)
    def arc72_tokenByIndex(self, index: arc4.UInt256) -> arc4.UInt256:
        assert index.native < self.minted, "index"
        return arc4.UInt256(index.native + 1)

    @arc4.abimethod(readonly=True)
    def arc72_balanceOf(self, owner: arc4.Address) -> arc4.UInt256:
        return self.balances.get(owner.native, default=arc4.UInt256(0))

    @arc4.abimethod(readonly=True)
    def arc72_getApproved(self, tokenId: arc4.UInt256) -> arc4.Address:
        return self.approvals.get(tokenId, default=arc4.Address())

    @arc4.abimethod(readonly=True)
    def arc72_isApprovedForAll(self, owner: arc4.Address, operator: arc4.Address) -> arc4.Bool:
        return self.operators.get(pair_key(owner.native, operator.native), default=arc4.Bool(False))

    @arc4.abimethod
    def arc72_approve(self, approved: arc4.Address, tokenId: arc4.UInt256) -> None:
        owner = self.owners[tokenId]
        assert Txn.sender == owner.native, "not owner"
        self.approvals[tokenId] = approved
        arc4.emit("arc72_Approval(address,address,uint256)", owner, approved, tokenId)

    @arc4.abimethod
    def arc72_setApprovalForAll(self, operator: arc4.Address, approved: arc4.Bool) -> None:
        self.operators[pair_key(Txn.sender, operator.native)] = approved
        arc4.emit("arc72_ApprovalForAll(address,address,bool)", arc4.Address(Txn.sender), operator, approved)

    @arc4.abimethod
    def arc72_transferFrom(self, from_: arc4.Address, to: arc4.Address, tokenId: arc4.UInt256) -> None:
        assert self.owners[tokenId] == from_, "not owner"
        assert self._may_transfer(from_.native, tokenId), "not allowed"
        balance = self.balances[from_.native]
        self.balances[from_.native] = arc4.UInt256(balance.native - 1)
        self.approvals[tokenId] = arc4.Address()
        self._receive(to.native, tokenId)
        arc4.emit("arc72_Transfer(address,address,uint256)", from_, to, tokenId)

    @subroutine
    def _may_transfer(self, owner: Account, tokenId: arc4.UInt256) -> bool:
        if Txn.sender == owner:
            return True
        if self.approvals.get(tokenId, default=arc4.Address()).native == Txn.sender:
            return True
        return self.operators.get(pair_key(owner, Txn.sender), default=arc4.Bool(False)).native

    @subroutine
    def _receive(self, owner: Account, tokenId: arc4.UInt256) -> None:
        self.owners[tokenId] = arc4.Address(owner)
        balance = self.balances.get(owner, default=arc4.UInt256(0))
        self.balances[owner] = arc4.UInt256(balance.native + 1)


class TestAssetMinter(ARC4Contract):
    """Mints each NFT as an Algorand Standard Asset created by an inner transaction, held by the application."""

    @arc4.abimethod
    def mint(self, uri: arc4.String) -> arc4.UInt64:
        created = itxn.AssetConfig(
            asset_name="Minted", unit_name="MNT", total=1, decimals=0, url=uri.native, manager=Global.current_application_address,
        ).submit()
        return arc4.UInt64(created.created_asset.id)
