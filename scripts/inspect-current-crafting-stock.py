#!/usr/bin/env python3
"""Static, local-only Enshrouded server stock-collector call-map.

The game EXE must come from the official Steam anonymous dedicated-server
depot, acquired separately by CI. Never commit, log, upload or redistribute it.
Produces only bounded machine-code location/branch metadata, not game assets.
No game code executes, no in-memory hooks and no inventory mutation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import capstone
import pefile

# Current published Kiln v0.2.0-dev.20 signatures; no RVAs are trusted.
SIGNATURES = {
    "original_recipe_apply": "48 89 5C 24 20 55 56 41 56 48 81 EC 90 00 00 00 4C 8B F2 49 8B F0 4C 8B 84 24 D0 00 00 00 48 8B E9 48 8B 94 24 D8 00 00 00 49 8B CE 49 8B D9 E8",
    "original_remove_items": "48 89 5C 24 08 48 89 74 24 18 48 89 7C 24 20 48 89 54 24 10 55 41 54 41 55 41 56 41 57 48 8D AC 24 20 FF FF",
    "original_commit": "40 55 53 41 55 48 8D AC 24 30 FC FF",
    "original_rollback": "4C 8B DC 55 56 41 55 41 56 41 57 49",
    # 26430253: base-stocking resolver that returns an ordinary item-owner
    # pointer/count and separately a fuel owner pointer/count (four qwords).
    "original_base_stock_selector": "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 20 57 48 83 EC 40 49 8B F9 49 8B E8 48 8B F2 48 8B D9 4D 85 C0",
    # Historical EMF snippets, for hints only. Zero matches on a new build
    # are normal; these may not identify the same function now.
    "historical_remove_recipe_inputs": "48 89 5C 24 10 44 89 4C 24 20 55 56 57 41 54 41 55 41 56 41 57 48 81 EC B0 00 00 00 45 33 ED 45",
    "historical_recipe_core": "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 55 41 54 41 55 41 56 41 57 48 8D AC 24 A0 8B FF FF",
}
STOCK_LABELS = [
    "cache_crafting_stock",
    "cache_building_stock",
    "InventoryCraftingStock",
    "DynamicInventoryCraftingStock",
    "PlayerBasesSystemServer",
    "PlayerBasesSystemClient",
    "handle_crafting_events",
    "crafting_stock",
]


def match(data: bytes, pat: str) -> list[int]:
    tokens = [None if token == "??" else int(token, 16) for token in pat.split()]
    if not tokens or all(token is None for token in tokens):
        return []
    first = next(i for i, value in enumerate(tokens) if value is not None)
    result = []
    token = bytes([tokens[first]])
    start = 0
    while True:
        found = data.find(token, start)
        if found < 0:
            break
        pos = found - first
        if pos >= 0 and pos + len(tokens) <= len(data) and all(
            value is None or data[pos + i] == value for i, value in enumerate(tokens)
        ):
            result.append(pos)
            if len(result) >= 128:
                break
        start = found + 1
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("exe", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    raw = args.exe.read_bytes()
    pe = pefile.PE(data=raw, fast_load=True)
    imagebase = pe.OPTIONAL_HEADER.ImageBase
    sections = [
        (s.Name.rstrip(b"\0").decode("ascii", "replace"), imagebase + s.VirtualAddress,
         s.get_data(), int(s.Characteristics))
        for s in pe.sections
    ]
    code = [(n, va, data) for n, va, data, flags in sections if flags & 0x20000000]
    text_sections = [(n, va, data) for n, va, data in code if n == ".text"]
    if len(text_sections) != 1:
        raise SystemExit("Refusing unknown executable: expected one .text PE section")
    _, text_base, text = text_sections[0]
    cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    cs.detail = True

    loc = {}
    for name, pattern in SIGNATURES.items():
        found = match(text, pattern)
        loc[name] = [hex(text_base + pos - imagebase) for pos in found]

    def parse_rva(s):
        return int(s, 16)
    known = {imagebase + parse_rva(addr): name
             for name, addresses in loc.items() for addr in addresses if len(addresses) == 1}
    functions = {}
    for name, values in loc.items():
        if len(values) != 1:
            continue
        ptr = imagebase + parse_rva(values[0])
        offset = ptr - text_base
        if offset < 0 or offset >= len(text):
            continue
        sample = text[offset:offset + (1800 if name == "original_recipe_apply" else 1200)]
        calls = []
        instructions = []
        for ins in cs.disasm(sample, ptr):
            if len(instructions) >= 700:
                break
            instructions.append({"rva": hex(ins.address - imagebase), "ins": f"{ins.mnemonic} {ins.op_str}".strip()})
            if ins.mnemonic in ("call", "jmp") and ins.operands and ins.operands[0].type == capstone.x86.X86_OP_IMM:
                target = ins.operands[0].imm
                if text_base <= target < text_base + len(text):
                    tp = target - text_base
                    calls.append({"kind": ins.mnemonic, "at": hex(ins.address - imagebase),
                                  "to": hex(target - imagebase),
                                  "known": known.get(target),
                                  "bytes32": text[tp:tp+32].hex()})
        functions[name] = {"rva":hex(ptr-imagebase),"calls":calls[:80],
                           "instructions":instructions[:540]}

    # Extra static function map for the observed (exact-current-build)
    # original payer call edges. A new EXE digest must be reverified before
    # any of these research-only addresses are interpreted.
    digest = hashlib.sha256(raw).hexdigest()
    research_points = {}
    if digest == "f0d824be7fcd48ebfe2458ff5c28da26060c2074e3ec1fcac587e5a1d508aeb2":
        research_points = {
            "payer_stock_helper": 0x2081c0,
            "payer_remove_auto": 0x1b0d60,
            "alternate_recipe_payer": 0x19a9b0,
            "payer_stock_inventory_helper": 0x1b0bf0,
            "payer_inventory_read_helper": 0x1b09e0,
            "base_search": 0x208c60,
        }
    for name, rva in research_points.items():
        ptr = imagebase + rva
        pos = ptr - text_base
        if not 0 <= pos < len(text):
            continue
        insns = []
        calls = []
        for ins in cs.disasm(text[pos:pos + 3200], ptr):
            if len(insns) >= 460:
                break
            insns.append({"rva":hex(ins.address - imagebase),
                          "ins":f"{ins.mnemonic} {ins.op_str}".strip()})
            if ins.mnemonic in ("call", "jmp") and ins.operands and ins.operands[0].type == capstone.x86.X86_OP_IMM:
                target = ins.operands[0].imm
                if text_base <= target < text_base + len(text):
                    calls.append({"kind":ins.mnemonic, "at":hex(ins.address-imagebase),
                                  "to":hex(target-imagebase),
                                  "known":known.get(target)})
        functions[name] = {"rva":hex(rva),"calls":calls[:100],
                           "instructions":insns[:460]}

    label_matches = {}
    for label in STOCK_LABELS:
        hits = []
        for secname, start, data, flags in sections:
            if flags & 0x20000000:
                continue
            needle = label.encode("ascii")
            cursor = 0
            while len(hits) < 12:
                cursor = data.find(needle, cursor)
                if cursor < 0:
                    break
                hits.append({"section":secname,"rva":hex(start+cursor-imagebase)})
                cursor += len(needle)
        label_matches[label] = hits

    # Search for executable RIP-relative xrefs to stock-related string or
    # data addresses, to locate the game's original collector registration.
    targets = {imagebase + int(p["rva"],16): label
               for label, hits in label_matches.items() for p in hits}
    xrefs = []
    if targets:
        for ins in cs.disasm(text, text_base):
            if ins.mnemonic not in ("lea", "mov"):
                continue
            for operand in ins.operands:
                if operand.type != capstone.x86.X86_OP_MEM or operand.mem.base != capstone.x86.X86_REG_RIP:
                    continue
                to = ins.address + ins.size + operand.mem.disp
                if to in targets and len(xrefs) < 120:
                    offset = ins.address - text_base
                    xrefs.append({"target":targets[to],"at":hex(ins.address-imagebase),
                                  "ins":f"{ins.mnemonic} {ins.op_str}",
                                  "around":text[max(0,offset-48):offset+64].hex()})
    report = {
        "notice":"Static source-target reconnaissance only; no entrypoint/ABI or payment permission proven",
        "exe_sha256":hashlib.sha256(raw).hexdigest(),
        "exe_size":len(raw),
        "machine":hex(pe.FILE_HEADER.Machine),
        "image_base":hex(imagebase),
        "timestamp":hex(pe.FILE_HEADER.TimeDateStamp),
        "text_size":len(text),
        "signatures":loc,
        "known_functions":functions,
        "stock_labels":label_matches,
        "stock_string_xrefs":xrefs,
    }
    args.output.write_text(json.dumps(report,indent=2)+"\n",encoding="utf8")
    print(f"PE SHA256: {report['exe_sha256']}; .text size: {len(text)}")
    for key,hits in loc.items():
        print(f"{key}: {len(hits)} match(es): {', '.join(hits[:8])}")
    for label, hits in label_matches.items():
        print(f"string {label}: {len(hits)}")
    for name, fn in functions.items():
        print(f"original branch map: {name} @ {fn['rva']}:")
        for call in fn["calls"][:28]:
            print(f"  {call['kind']} {call['at']} => {call['to']} {call['known'] or ''}")
    for name in ("historical_remove_recipe_inputs","payer_stock_helper","payer_stock_inventory_helper","payer_inventory_read_helper"):
        fn = functions.get(name)
        if not fn:
            continue
        instructions = fn["instructions"]
        print(f"FOCUSED {name} @ {fn['rva']}: {len(instructions)} instructions")
        if name == "historical_remove_recipe_inputs":
            focus = {"0x199d8e","0x199fa2","0x19a01c"}
            for i, ins in enumerate(instructions):
                if ins["rva"] in focus:
                    for near in instructions[max(0,i-16):i+6]:
                        print(f"   {near['rva']}: {near['ins']}")
            for ins in instructions:
                if 0x199fa7 <= int(ins["rva"],16) <= 0x19a0da:
                    print(f"   PAY-SOURCE {ins['rva']}: {ins['ins']}")
        else:
            for ins in instructions[:125]:
                print(f"   {ins['rva']}: {ins['ins']}")
            for call in fn["calls"][:35]:
                print(f"   {call['kind']} {call['at']} -> {call['to']}")
    for x in xrefs[:25]:
        print(f"xref {x['target']}: {x['at']} {x['ins']}")
    print("Bounded non-copyright callmap report:",args.output)

if __name__ == "__main__":
    main()
