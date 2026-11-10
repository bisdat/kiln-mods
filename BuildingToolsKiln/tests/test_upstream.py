#!/usr/bin/env python3
"""Upstream version-check tests; no network/game required."""
import importlib.util
from pathlib import Path
import unittest

p=Path(__file__).resolve().parents[1] / "propose_update.py"
spec=importlib.util.spec_from_file_location("bt_upstream",p)
mod=importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

PIN={"adapter_version":"1.0.0","upstream_version":"0.31.2",
     "upstream_sha256":"a"*64,"gameplay_verified_upstream_version":"0.31.2",
     "tested_client_sha256":"b"*64}

class UpdateTests(unittest.TestCase):
    def test_current(self):
        self.assertIsNone(mod.propose(PIN,"v0.31.2","a"*64))
    def test_bump_and_preserve_gameplay_gate(self):
        p=mod.propose(PIN,"0.31.3","c"*64)
        self.assertEqual(p["upstream_version"],"0.31.3")
        self.assertEqual(p["adapter_version"],"1.0.1")
        self.assertEqual(p["gameplay_verified_upstream_version"],"0.31.2")
        self.assertEqual(p["tested_client_sha256"],"b"*64)
        self.assertEqual(PIN["upstream_version"],"0.31.2")
    def test_minor_update(self):
        self.assertEqual(mod.propose(PIN,"0.32.0","c"*64)["adapter_version"],"1.0.1")
    def test_no_downgrade(self):
        self.assertIsNone(mod.propose(PIN,"0.30.9","c"*64))
    def test_retagged_release_is_rejected(self):
        with self.assertRaisesRegex(ValueError,"CRITICAL"):
            mod.propose(PIN,"0.31.2","c"*64)
    def test_bad_checksum(self):
        for bad in ["bad","", "z"*64]:
            with self.subTest(bad=bad):
                with self.assertRaisesRegex(ValueError,"SHA256"):
                    mod.propose(PIN,"0.31.3",bad)
    def test_prerelease_and_invalid_versions_rejected(self):
        for bad in ["main","0.31.3-rc1","1.2","1.2.3.4"]:
            with self.subTest(bad=bad):
                with self.assertRaisesRegex(ValueError,"Version"):
                    mod.propose(PIN,bad,"c"*64)

if __name__=="__main__":
    unittest.main(verbosity=2)
