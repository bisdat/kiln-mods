#!/usr/bin/env python3
"""Reject accidental gameplay changes in experimental native craft trace."""
import pathlib
import subprocess
import sys

root = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else None
if root is None or not (root / ".git").exists():
    raise SystemExit("Usage: verify.py <patched-disposable-kiln-checkout>")

def git(*args):
    return subprocess.check_output(["git", "-C", str(root), *args], text=True)

wanted = {
    "native/core/abi/services/kiln_rules.h",
    "native/services/rules/creative.h",
    "native/services/rules/creative.cpp",
    "native/services/rules/rules.cpp",
    "managed/services/Kiln.Rules/Native.cs",
    "managed/services/Kiln.Rules/Rules.cs",
}
changed = set(git("diff", "--name-only").splitlines())
assert changed == wanted, f"Unexpected modified files: {changed ^ wanted}"
diff = git("diff", "--unified=0")
added = "\n".join(
    line[1:] for line in diff.splitlines()
    if line.startswith("+") and not line.startswith("+++")
)
deleted = "\n".join(
    line[1:] for line in diff.splitlines()
    if line.startswith("-") and not line.startswith("---")
)
assert not any(keyword in added for keyword in (
    "Items.Take(", "Items.Give(", "return 0;  // free", "create_inline(",
    "write_other(", "WriteOther(", "InventoryCommit(", "InventoryRemoveItems(",
)), "Unexpected direct inventory manipulation or new native detour"
assert "h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra)" in added
assert "h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra)" in added
assert "h_craftable.call<uint32_t>(ui, recipe, cap, selections)" in added
assert "return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);" in deleted
assert "return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);" in deleted
assert "KilnRulesCraftTrace" in added
assert "Services.Get<CraftTraceTable>" in added
assert "trace_primary_payment" in added
assert "CT.alternative.fetch_add" in added
assert "CT.ui.fetch_add" in added
assert "KILN_WORLD_STARTED" in (root / "native/services/rules/rules.cpp").read_text()
assert "return result;" in added
print("PASS: 6 permitted source files; existing original paths preserved; no new detours or item transactions.")
