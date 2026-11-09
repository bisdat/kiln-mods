#!/usr/bin/env python3
"""CI-only fix for pre-existing Kiln main invalid integer-to-integer reinterpret_cast.

Some native kiln.rules source files instantiate sym<F> with F=uintptr_t.
MSVC rightly rejects reinterpret_cast<uintptr_t>(uintptr_t). This function
changes only the symbol-lookup cast, to preserve numeric vs pointer behavior.
Run AFTER verify.py (which tests the actual feature-only source diff).
"""
from pathlib import Path
import os
import subprocess
import sys

# Historical dev snapshot remains the local default; CI always sets the newest
# PUBLISHED release commit and then checks it against the checkout.\nPIN = os.environ.get("KILN_UPSTREAM_SHA", "77e525888ac0d5f4f6b16103e945ed7ac049b50e")
if len(sys.argv) != 2:
    raise SystemExit("Usage: msvc-compat.py <disposable-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != PIN:
    raise SystemExit(f"Refusing upstream commit {actual}; expected {PIN}")

for name in ("rules.cpp", "creative.cpp", "flight.cpp"):
    file = root / "native/services/rules" / name
    source = file.read_text(encoding="utf8")
    old = "    *out = reinterpret_cast<F>(a);"
    if source.count(old) != 1 or source.count("#include <cstring>") != 1:
        raise RuntimeError(f"Unexpected upstream symbol helper in {name}")
    source = source.replace("#include <cstring>", "#include <cstring>\n#include <type_traits>", 1)
    source = source.replace(
        old,
        "    if constexpr (std::is_integral_v<F>) *out = static_cast<F>(a);\n"
        "    else *out = reinterpret_cast<F>(a);",
        1,
    )
    file.write_text(source, encoding="utf8")
    print("MSVC baseline compatibility:", name)
print("PASS: only the existing Kiln source symbol-cast helper was altered; no hook changes.")
