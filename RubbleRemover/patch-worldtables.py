#!/usr/bin/env python3
"""Work around Kiln dev.30.6 native worldtables.read_mod skipping tool-only files.

The loader's entry filter checks for blueprints, but omits toolBrushShapes
and toolMaterialPools. Its downstream read_blocks recognises toolBrushShapes.
An EMPTY blueprints section opens the gate without defining a blueprint.
Delete this shim when upstream updates the reader's entry gate.
"""
import json
import os
import sys
import tempfile
from pathlib import Path

EXPECTED_RADII = {"small": 0.5, "medium": 1.5, "large": 2.5}


def patch(path: Path) -> None:
    doc = json.loads(path.read_text(encoding="utf-8"))
    shapes = doc.get("toolBrushShapes")
    if not isinstance(shapes, dict) or set(shapes) != set(EXPECTED_RADII):
        raise RuntimeError(f"Expected all 3 baked brush shapes, got {shapes!r}")
    for name, radius in EXPECTED_RADII.items():
        brush = shapes[name]
        if brush.get("shape") != "sphere" or brush.get("radius") != radius:
            raise RuntimeError(f"Unexpected baked {name} shape: {brush!r}")
    if "blueprints" in doc and doc["blueprints"] != {}:
        raise RuntimeError("Refusing to modify a real blueprint section")
    doc["blueprints"] = {}
    fd, tmp = tempfile.mkstemp(prefix=".tables-", suffix=".json", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as out:
            json.dump(doc, out, ensure_ascii=False, separators=(",", ":"))
            out.write("\n")
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)
    print(f"RubbleRemover: patched {path} with an empty blueprints read gate")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: patch-worldtables.py <blocks.tables.json>")
    patch(Path(sys.argv[1]))
