#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Read-only inventory of precisely the top-level files EML fingerprints.

No file content reads, no mod/game launch and no Kiln profile changes.
"""
import os
from pathlib import Path

game = Path(os.environ.get(
    "ENSHROUDED_GAME_DIR",
    Path.home() / ".local/share/Steam/steamapps/common/Enshrouded",
))
wanted = {"dat", "kfc", "kfc_resources", "exe", "bak", "integrity"}
rows = []
errors = []
try:
    entries = list(game.iterdir())
except OSError as error:
    raise SystemExit(f"Could not list {game}: {error}")
for path in entries:
    name = path.name
    ext = name.rsplit(".", 1)[-1] if "." in name else ""
    if ext not in wanted and name != "eml.json":
        continue
    try:
        # Preserve the original FileStateCache::track_game_files inclusion rule:
        # filter by extension first; report metadata separately, no open/hash.
        st = path.stat()
        rows.append((st.st_size, name, "regular" if path.is_file() else "not-regular"))
    except OSError as error:
        errors.append(f"{name}: {error}")
rows.sort(reverse=True)
print(f"EML top-level fingerprint candidates in: {game}")
print("File size in GiB (not estimated read time):")
for size, name, typ in rows:
    print(f"{size / (1024**3):10.3f} GiB   {size:>14} bytes   {typ:>11}   {name}")
print(f"TOTAL {sum(x[0] for x in rows) / (1024**3):.3f} GiB across {len(rows)} entries")
for error in errors:
    print(f"METADATA ERROR {error}")
