#!/usr/bin/env python3
"""Check correct behaviour while the upstream author is publishing assets."""
import importlib.util
from pathlib import Path
import unittest

path = Path(__file__).resolve().parents[1] / "release_assets.py"
spec = importlib.util.spec_from_file_location("building_tools_release_assets", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def make(version="0.32.0", ready=True):
    return {
        "tag_name": "v" + version,
        "draft": False,
        "prerelease": False,
        "assets": ([
            {"name": f"EnshroudedBuildingTools-{version}.zip",
             "size": 284123, "state": "uploaded"},
            {"name": f"EnshroudedBuildingTools-{version}.zip.sha256",
             "size": 106, "state": "uploaded"},
        ] if ready else []),
    }


class ReleaseCompletenessTests(unittest.TestCase):
    def test_release_with_two_real_uploaded_assets(self):
        ver, good, missing = module.inspect_release(make())
        self.assertEqual(ver, "0.32.0")
        self.assertTrue(good)
        self.assertEqual(missing, [])

    def test_published_release_without_assets_is_pending(self):
        ver, good, missing = module.inspect_release(make(ready=False))
        self.assertEqual(ver, "0.32.0")
        self.assertFalse(good)
        self.assertEqual(len(missing), 2)

    def test_missing_checksum_blocks_update(self):
        release = make()
        del release["assets"][1]
        self.assertFalse(module.inspect_release(release)[1])
        self.assertTrue(module.inspect_release(release)[2][0].endswith(".sha256"))

    def test_upload_in_progress_blocks_update(self):
        release = make()
        release["assets"][0]["state"] = "new"
        self.assertFalse(module.inspect_release(release)[1])

    def test_zero_byte_assets_block_update(self):
        release = make()
        release["assets"][0]["size"] = 0
        self.assertFalse(module.inspect_release(release)[1])

    def test_renamed_assets_not_accepted(self):
        release = make()
        release["assets"][0]["name"] = "Source-code.zip"
        self.assertFalse(module.inspect_release(release)[1])

    def test_unstable_tag_fails_closed(self):
        for tag in ("v0.32.0-rc1", "main", "0.32.0", "v0.32"):
            with self.subTest(tag=tag):
                release = make()
                release["tag_name"] = tag
                with self.assertRaisesRegex(ValueError, "Unexpected"):
                    module.inspect_release(release)

    def test_unknown_asset_metadata_fails_closed(self):
        release = make()
        release["assets"] = None
        with self.assertRaisesRegex(ValueError, "metadata"):
            module.inspect_release(release)

    def test_draft_and_prerelease_rejected(self):
        for key in ("draft", "prerelease"):
            with self.subTest(key=key):
                release = make()
                release[key] = True
                with self.assertRaisesRegex(ValueError, "published and stable"):
                    module.inspect_release(release)


if __name__ == "__main__":
    unittest.main(verbosity=2)
