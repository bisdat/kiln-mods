#!/usr/bin/env python3
"""One-time READ-ONLY static proof of Enshrouded's client-hosted crafting stock.

Designed for the user's own installed enshrouded.exe (Steam/Proton).
Reads the EXE on disk, does not launch it, access saves or connect to Steam,
and emits only bounded function/callsite addresses and provenance. Never
uploads/distributes game code, and never installs or modifies a DLL.

Install local Python dependencies (venv preferred): pefile==2024.8.26,
capstone==5.0.6. Input is rejected unless its SHA256 is an exact Kiln
runtime-verified game build. The server option exists solely to validate the
analysis against the officially available dedicated-server EXE in CI.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import struct
from pathlib import Path

import capstone
import pefile

EXE_PINS = {
    "client": {
        "5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77": 26430625
    },
    "server": {
        "f0d824be7fcd48ebfe2458ff5c28da26060c2074e3ec1fcac587e5a1d508aeb2": 26430253
    },
}
# The first two are from the observed exact-current 26430253 dedicated-server
# stock path. Unknown match count on a client is evidence, NOT permission to
# invoke an address from the server or replace a payer.
PATTERNS = {
    "stock_selector": "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 20 57 48 83 EC 40 49 8B F9 49 8B E8 48 8B F2 48 8B D9 4D 85 C0",
    "original_inventory_payer": "48 89 5C 24 10 44 89 4C 24 20 55 56 57 41 54 41 55 41 56 41 57 48 81 EC B0 00 00 00 45 33 ED 45",
    "recipe_apply": "48 89 5C 24 20 55 56 41 56 48 81 EC 90 00 00 00 4C 8B F2 49 8B F0 4C 8B 84 24 D0 00 00 00 48 8B E9 48 8B 94 24 D8 00 00 00 49 8B CE 49 8B D9 E8",
    "remove_items": "48 89 5C 24 08 48 89 74 24 18 48 89 7C 24 20 48 89 54 24 10 55 41 54 41 55 41 56 41 57 48 8D AC 24 20 FF FF",
}
LABELS = (
    "cache_crafting_stock", "cache_building_stock", "PlayerBasesSystemClient",
    "PlayerBasesSystemServer", "InventoryCraftingStock",
    "DynamicInventoryCraftingStock", "crafting_stock",
)
MAX_LABEL_HITS = 8
MAX_REFS = 40
MAX_PAYER_INSTRUCTIONS = 700


def byte_matches(data: bytes, hex_pattern: str) -> list[int]:
    pat = bytes.fromhex(hex_pattern)
    if not pat:
        return []
    result = []
    offset = 0
    while len(result) < 16:
        found = data.find(pat, offset)
        if found < 0:
            break
        result.append(found)
        offset = found + 1
    return result


def direct_call_target(ins) -> int | None:
    """Only Capstone-decoded direct x64 calls; never treat bytes in data as code."""
    if ins.mnemonic != "call" or len(ins.operands) != 1:
        return None
    op = ins.operands[0]
    return op.imm if op.type == capstone.x86.X86_OP_IMM else None


def inspect(exe: Path, side: str) -> dict:
    if not exe.is_file() or exe.stat().st_size > 400 * 1024 * 1024:
        raise ValueError("Expected one local Enshrouded EXE no larger than 400 MiB")
    raw = exe.read_bytes()  # local, never sent over the network
    digest = hashlib.sha256(raw).hexdigest()
    if digest not in EXE_PINS[side]:
        raise ValueError(
            f"Unverified {side} executable SHA256={digest}; "
            "do not reuse addresses from a different game build."
        )
    image = pefile.PE(data=raw, fast_load=True)
    if image.FILE_HEADER.Machine != 0x8664:
        raise ValueError("Not an x64 Windows executable")
    base = image.OPTIONAL_HEADER.ImageBase
    code_sections = [s for s in image.sections if s.Characteristics & 0x20000000]
    text = [s for s in code_sections if s.Name.rstrip(b"\0") == b".text"]
    if len(text) != 1:
        raise ValueError("Unknown PE section structure; expected one .text")
    txt = text[0].get_data()
    text_va = base + text[0].VirtualAddress
    hits = {
        name: [hex(text[0].VirtualAddress + i) for i in byte_matches(txt, pat)]
        for name, pat in PATTERNS.items()
    }
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    decoder.detail = True
    calls = []
    verified_selector = len(hits["stock_selector"]) == 1
    verified_payer = len(hits["original_inventory_payer"]) == 1
    if verified_payer:
        payer_va = base + int(hits["original_inventory_payer"][0], 16)
        start = payer_va - text_va
        expected = base + int(hits["stock_selector"][0], 16) if verified_selector else None
        for ins in decoder.disasm(txt[start:start+3600], payer_va):
            if len(calls) >= MAX_PAYER_INSTRUCTIONS:
                break
            target = direct_call_target(ins)
            if target is not None:
                calls.append({
                    "at": hex(ins.address - base),
                    "target": hex(target - base),
                    "targets_selector": expected is not None and target == expected,
                })
    labels = {}
    pointers: dict[int, str] = {}
    for label in LABELS:
        results = []
        for section in image.sections:
            if section.Characteristics & 0x20000000:
                continue
            blob = section.get_data()
            offset = 0
            while len(results) < MAX_LABEL_HITS:
                i = blob.find(label.encode("ascii"), offset)
                if i == -1:
                    break
                addr = base + section.VirtualAddress + i
                results.append(hex(addr - base))
                pointers[addr] = label
                offset = i + len(label)
        labels[label] = results
    refs = []
    # Only exact, disassembled RIP-relative references to existing string
    # labels; no arbitrary binary snippets or proprietary assets leave disk.
    if pointers:
        for ins in decoder.disasm(txt, text_va):
            if len(refs) >= MAX_REFS:
                break
            if ins.mnemonic not in ("lea", "mov"):
                continue
            for op in ins.operands:
                if op.type != capstone.x86.X86_OP_MEM or op.mem.base != capstone.x86.X86_REG_RIP:
                    continue
                target = ins.address + ins.size + op.mem.disp
                if target in pointers:
                    refs.append({"label": pointers[target], "at": hex(ins.address-base),
                                 "target": hex(target-base)})
    chosen = [c for c in calls if c["targets_selector"]]
    # Evidence level, not a proof of runtime game eligibility or payment.
    result = {
        "purpose": "Original game client-host stock collector reconnaissance; READ ONLY",
        "side": side,
        "game_build": EXE_PINS[side][digest],
        "exe_sha256": digest,
        "signatures": hits,
        "payer_direct_calls_to_selector": chosen[:8],
        "payer_calls_total": len(calls),
        "string_label_locations": labels,
        "string_code_references": refs,
        "selector_unique": verified_selector,
        "payer_unique": verified_payer,
        "two_verified_payer_calls": len(chosen) == 2,
        "client_stock_cache_entry_verified": False,  # never invent this ABI
        "authorised_remote_sources_verified": False,
        "safe_to_enable_hook": False,
        "note": "No user game data, save, inventory, or executable bytes included.",
    }
    return result


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("exe", type=Path, help="Absolute path to your installed enshrouded.exe")
    p.add_argument("--side", choices=("client", "server"), default="client")
    p.add_argument("--output", type=Path, default=Path("enshrouded-client-stock-map.json"))
    args = p.parse_args()
    if args.output.resolve() == args.exe.resolve():
        raise SystemExit("Refusing to overwrite input EXE")
    try:
        result = inspect(args.exe,args.side)
    except (OSError,ValueError,pefile.PEFormatError) as exc:
        raise SystemExit(f"Read-only inspection refused: {exc}")
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Enshrouded {args.side} build {result['game_build']}: "
          f"sha256 {result['exe_sha256']}")
    for key, addresses in result["signatures"].items():
        print(f"{key}: {len(addresses)} signature(s): {', '.join(addresses)}")
    print(f"Direct original-payer calls to selector: "
          f"{len(result['payer_direct_calls_to_selector'])}")
    for edge in result["payer_direct_calls_to_selector"]:
        print(f"  {edge['at']} -> {edge['target']}")
    for label, positions in result["string_label_locations"].items():
        if positions:
            print(f"{label}: {len(positions)} literal(s)")
    print(f"Stock-registration string xrefs: "
          f"{len(result['string_code_references'])}")
    print("NO hook enabled or game modified. Metadata:", args.output)


if __name__ == "__main__":
    main()
