#!/usr/bin/env python3
"""Build-only compatibility fix for upstream Kiln Items' sym<uintptr_t>.
The released source uses reinterpret_cast<F>(a) where F is uintptr_t;
MSVC rejects an integer-to-integer reinterpret_cast. The code below
uses an equivalent static_cast only for integral symbol types.
No hooks, native gameplay, transaction or item semantics are changed.
"""
import os
from pathlib import Path
import re
import subprocess
import sys

if len(sys.argv)!=2: raise SystemExit("Usage: items-msvc-compat.py <disposable-pinned-kiln-checkout>")
root=Path(sys.argv[1]).resolve()
pin=os.environ.get("KILN_UPSTREAM_SHA","")
if not re.fullmatch(r"[0-9a-f]{40}",pin): raise SystemExit("Pinned immutable upstream SHA required")
actual=subprocess.check_output(["git","-C",str(root),"rev-parse","HEAD"],text=True).strip()
if actual!=pin:raise SystemExit("Wrong upstream commit")

file=root/"native/services/items/items.cpp"
s=file.read_text(encoding="utf8")
old="    *out = reinterpret_cast<F>(a);"
if s.count(old)!=1 or s.count("#include <cstring>")!=1:
    raise SystemExit("Unexpected original native Kiln Items symbol helper")
s=s.replace("#include <cstring>", "#include <cstring>\n#include <type_traits>",1)
s=s.replace(old,"    if constexpr (std::is_integral_v<F>) *out = static_cast<F>(a);\n"
              "    else *out = reinterpret_cast<F>(a);",1)
file.write_text(s,encoding="utf8")
print("PASS: baseline MSVC integer symbol cast in Kiln Items fixed (CI checkout only)")
