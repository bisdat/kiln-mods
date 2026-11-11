#!/usr/bin/env python3
"""Package a standalone, data-only Kiln mod; no SDK or .NET needed."""
from pathlib import Path
import json
import sys
import zipfile

ROOT = Path(__file__).resolve().parent
FILES = ("kiln.mod.json", "content/rod.json", "README.md")


def build(output_dir: Path) -> Path:
    manifest = json.loads((ROOT / "kiln.mod.json").read_text(encoding="utf-8"))
    if manifest["id"] != "enhanced-fishing-rod" or "entryAssembly" in manifest:
        raise ValueError("Expected the enhanced-fishing-rod data-only manifest")
    pack = json.loads((ROOT / "content/rod.json").read_text(encoding="utf-8"))
    if pack.get("format") != 1 or "Tool_T6_FishingRod_epic" not in pack.get("items", {}):
        raise ValueError("Invalid fishing-rod data pack")
    output_dir.mkdir(parents=True, exist_ok=True)
    destination = output_dir / f'{manifest["id"]}-{manifest["version"]}.zip'
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for file in FILES:
            archive.write(ROOT / file, arcname=f'{manifest["id"]}/{file}')
    return destination


if __name__ == "__main__":
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "dist"
    print(build(output.resolve()))
