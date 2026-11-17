#!/usr/bin/env python3
"""Both upstream release checksum formats must verify without game access."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "official_checksum.py"
spec = importlib.util.spec_from_file_location("bt_official_checksum", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class OfficialChecksumTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory()
        self.addCleanup(self.work.cleanup)
        root = Path(self.work.name)
        self.archive = root / "EnshroudedBuildingTools-0.33.2.zip"
        self.archive.write_bytes(b"fixture for the official ZIP")
        self.digest = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.legacy = root / (self.archive.name + ".sha256")
        self.sums = root / "SHA256SUMS"
        self.normalized = root / "normalized.sha256"

    def test_sha256sums_selects_only_the_exact_ordinary_zip(self):
        self.sums.write_text(
            "a" * 64 + "  dinput8.dll\n" +
            self.digest + "  " + self.archive.name + "\n" +
            "b" * 64 + "  EnshroudedBuildingTools-Shroudforge-0.33.2.zip\n")
        self.assertEqual(self.digest, module.verify(self.archive, self.sums, self.normalized))
        self.assertEqual(f"{self.digest}  {self.archive.name}\n", self.normalized.read_text())

    def test_legacy_checksum_with_filename_or_bare_hash(self):
        self.legacy.write_text(f"{self.digest}  dist/{self.archive.name}\n")
        self.assertEqual(self.digest, module.verify(self.archive, self.legacy, self.normalized))
        self.legacy.write_text(self.digest + "\n")
        self.assertEqual(self.digest, module.verify(self.archive, self.legacy, self.normalized))

    def test_checked_pin_is_not_bypassed(self):
        self.sums.write_text(f"{self.digest}  {self.archive.name}\n")
        with self.assertRaisesRegex(ValueError, "pin"):
            module.verify(self.archive, self.sums, self.normalized, "0" * 64)
        self.assertFalse(self.normalized.exists())

    def test_wrong_hash_fails_without_writing_normalized_file(self):
        self.sums.write_text(f"{'0' * 64}  {self.archive.name}\n")
        with self.assertRaisesRegex(ValueError, "published SHA256"):
            module.verify(self.archive, self.sums, self.normalized)
        self.assertFalse(self.normalized.exists())

    def test_duplicate_archive_hash_is_not_accepted(self):
        self.sums.write_text(f"{self.digest}  {self.archive.name}\n{self.digest} *{self.archive.name}\n")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            module.verify(self.archive, self.sums, self.normalized)

    def test_missing_archive_hash_fails(self):
        self.sums.write_text(f"{self.digest}  EnshroudedBuildingTools-0.33.1.zip\n")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            module.verify(self.archive, self.sums, self.normalized)

    def test_invalid_checksum_asset_name_fails(self):
        wrong = self.sums.with_name("wrong-checksums")
        wrong.write_text(f"{self.digest}  {self.archive.name}\n")
        with self.assertRaisesRegex(ValueError, "asset name"):
            module.verify(self.archive, wrong, self.normalized)

    def test_malformed_extra_line_fails(self):
        self.sums.write_text(f"{self.digest}  {self.archive.name}\nnot a checksum\n")
        with self.assertRaisesRegex(ValueError, "Malformed"):
            module.verify(self.archive, self.sums, self.normalized)

    def test_wrong_archive_name_fails(self):
        with self.assertRaisesRegex(ValueError, "versioned"):
            module.official_hash(f"{self.digest}  wrong.zip\n", "SHA256SUMS", "wrong.zip")

    def test_refuses_overwriting_original_file(self):
        self.sums.write_text(f"{self.digest}  {self.archive.name}\n")
        with self.assertRaisesRegex(ValueError, "overwrite"):
            module.verify(self.archive, self.sums, self.sums)


if __name__ == "__main__":
    unittest.main()
