#!/usr/bin/env python3
"""Dependency-free packaging regression tests. No game executable required."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[1] / "package.py"
spec = importlib.util.spec_from_file_location("buildingtools_adapter", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def binary():
    b = bytearray(1024)
    b[:2] = b"MZ"
    b[0x3c:0x40] = (0x80).to_bytes(4, "little")
    b[0x80:0x84] = b"PE\0\0"
    b[0x84:0x86] = b"\x64\x86"
    b[0x96:0x98] = b"\x22\x20"  # x64 DLL characteristics
    return bytes(b)


def fixture(path, extra=None, omit=None):
    files = {
        "dinput8.dll": binary(),
        "BuildingTools-LICENSE.txt": b"MIT License\n\nCopyright 2026 Aleksander Wojtal",
        "BuildingTools-README.txt": b"Enshrouded Building Tools test fixture",
        "BuildingTools-Languages/en.txt": b"Language=en\n",
    }
    for key in (omit or []):
        files.pop(key)
    files.update(extra or {})
    with zipfile.ZipFile(path, "w") as z:
        for k, v in files.items():
            z.writestr(k, v)
    sha = hashlib.sha256(path.read_bytes()).hexdigest()
    return sha, files


class NativeAdapterTest(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory()
        self.addCleanup(self.work.cleanup)
        base = Path(self.work.name)
        self.src = base / "EnshroudedBuildingTools-0.31.2.zip"
        self.checksum = base / "official.sha256"
        self.out = base / "building-tools-kiln-1.0.0.zip"
        self.digest, self.files = fixture(self.src)
        self.release = {"adapter_version": "1.0.0", "upstream_version": "0.31.2",
                        "upstream_sha256": self.digest,
                        "tested_client_sha256": "5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77"}
        self.checksum.write_text(self.digest + "  EnshroudedBuildingTools-0.31.2.zip\n")

    def run_pack(self):
        return mod.pack(self.src, self.checksum, self.out, self.release)

    def test_exact_binary_and_license(self):
        info = self.run_pack()
        with zipfile.ZipFile(self.out) as z:
            names = z.namelist()
            root = "kiln/native/building-tools/"
            self.assertNotIn("dinput8.dll", names)
            self.assertNotIn(root + "dinput8.dll", names)
            self.assertEqual(z.read(root + "BuildingTools.dll"), self.files["dinput8.dll"])
            self.assertEqual(z.read(root + "BuildingTools-LICENSE.txt"),
                             self.files["BuildingTools-LICENSE.txt"])
            manifest = json.loads(z.read(root + "native.json"))
            self.assertEqual(manifest["version"], "1.0.0")
            self.assertEqual(manifest["dlls"], ["BuildingTools.dll"])
            self.assertEqual(manifest["phase"], "boot")
            self.assertEqual(manifest["side"], "client")
            self.assertEqual(manifest["builds"], [self.release["tested_client_sha256"]])
            self.assertIn("BuildingTools-Languages/en.txt", names)
        self.assertEqual(info["upstream_archive_sha256"], self.digest)

    def test_deterministic_bytes(self):
        self.run_pack()
        other = self.out.with_name("test-second.zip")
        mod.pack(self.src, self.checksum, other, self.release)
        self.assertEqual(self.out.read_bytes(), other.read_bytes())

    def test_bad_checksum(self):
        self.checksum.write_text("0" * 64 + "\n")
        with self.assertRaisesRegex(ValueError, "checksum"):
            self.run_pack()

    def test_unpinned_version_or_hash(self):
        self.release["upstream_sha256"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "pinned"):
            self.run_pack()
        self.release["upstream_sha256"] = self.digest
        self.release["upstream_version"] = "0.31.1"
        with self.assertRaisesRegex(ValueError, "pinned"):
            self.run_pack()

    def test_missing_license(self):
        self.digest, _ = fixture(self.src, omit=["BuildingTools-LICENSE.txt"])
        self.release["upstream_sha256"] = self.digest
        self.checksum.write_text(self.digest + "\n")
        with self.assertRaisesRegex(ValueError, "missing"):
            self.run_pack()

    def test_zip_traversal(self):
        self.digest, _ = fixture(self.src, extra={"../dinput8.dll": b"poison"})
        self.release["upstream_sha256"] = self.digest
        self.checksum.write_text(self.digest + "\n")
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.run_pack()

    def test_unexpected_file(self):
        self.digest, _ = fixture(self.src, extra={"other.exe": b"exe"})
        self.release["upstream_sha256"] = self.digest
        self.checksum.write_text(self.digest + "\n")
        with self.assertRaisesRegex(ValueError, "Unexpected"):
            self.run_pack()

    def test_overwrite_rejected(self):
        self.run_pack()
        with self.assertRaisesRegex(ValueError, "exists"):
            self.run_pack()


if __name__ == "__main__":
    unittest.main(verbosity=2)
