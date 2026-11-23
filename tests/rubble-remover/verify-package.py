#!/usr/bin/env python3
"""Verify that the installable Kiln ZIP contains the baked, boot-time tool shapes."""
import json
import sys
from pathlib import Path
from zipfile import ZipFile

EXPECTED_RADII = {"small": 0.5, "medium": 1.5, "large": 2.5}

def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: verify-package.py <rubble-remover-*.zip>")
    path = Path(sys.argv[1])
    with ZipFile(path) as z:
        files = z.namelist()
        def exactly_one(suffix):
            matches = [f for f in files if f.endswith(suffix)]
            if len(matches) != 1:
                raise AssertionError(f"Expected exactly one {suffix} in ZIP, found {matches}")
            return matches[0]
        root = exactly_one("/RubbleRemover.dll").rsplit("/", 1)[0]
        tables_path = root + "/blocks.tables.json"
        if tables_path not in files:
            raise AssertionError(
                "MISSING blocks.tables.json: BuildingToolBlock stays pending; "
                "rubble-remover output item not found at runtime"
            )
        tables = json.loads(z.read(tables_path))
        # Kiln's Baker.WorldTablesNative deliberately strips the mod name from
        # blocks.tables.json: the owning mod comes from the containing package.
        if tables.get("format") != 1 or "comment" not in tables:
            raise AssertionError("Not a Kiln-generated world-table document")
        shapes = tables.get("toolBrushShapes")
        if not isinstance(shapes, dict) or set(shapes) != set(EXPECTED_RADII):
            raise AssertionError(f"Expected three baked brush shapes; found {shapes!r}")
        for name, radius in EXPECTED_RADII.items():
            item = shapes[name]
            if item.get("shape") != "sphere" or float(item.get("radius", -1)) != radius:
                raise AssertionError(f"{name} brush is wrong: {item!r}")
        print(f"PASS: {path.name}: baked world tables contain three tool brushes")
        print("       " + tables_path)
        print("       This verifies packaging, NOT runtime publication or in-game functionality")
    return 0

if __name__ == "__main__":
    sys.exit(main())
