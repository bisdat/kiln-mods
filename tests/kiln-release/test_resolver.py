"""Release selection regression tests; no network and no game access."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

target = Path(__file__).resolve().parents[2] / "scripts/resolve-kiln-release.py"
spec = importlib.util.spec_from_file_location("kiln_release_resolver", target)
assert spec and spec.loader
resolver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resolver)


def release(number=21, published="2026-11-10T03:49:21Z", **change):
    v = f"0.2.0-dev.{number}"
    result = {
        "tag_name": "v" + v,
        "published_at": published,
        "html_url": f"https://github.com/frostdev-ops/kiln/releases/tag/v{v}",
        "target_commitish": "a" * 40,
        "prerelease": True,
        "draft": False,
        "assets": [
            {"name": f"Kiln.Sdk.{v}.nupkg"},
            {"name": f"Kiln.Api.{v}.nupkg"},
            {"name": "SHA256SUMS"},
        ],
    }
    result.update(change)
    return result


class KilnDevReleaseTests(unittest.TestCase):
    def test_newest_published_release_is_selected(self):
        found = resolver.select_release([
            release(20, "2026-11-09T06:27:11Z"),
            release(21),
            release(19, "2026-11-09T05:00:00Z"),
        ])
        self.assertEqual(found["tag"], "v0.2.0-dev.21")
        self.assertEqual(found["release_target"], "a" * 40)

    def test_drafts_and_unpublished_are_ignored(self):
        found = resolver.select_release([
            release(22, "2026-11-11T06:00:00Z", draft=True),
            release(23, "2026-11-12T06:00:00Z", published_at=None),
            release(21),
        ])
        self.assertEqual(found["version"], "0.2.0-dev.21")

    def test_newest_release_missing_required_sdk_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "missing assets"):
            resolver.select_release([
                release(20, "2026-11-09T06:27:11Z"),
                release(21, assets=[{"name": "SHA256SUMS"}]),
            ])

    def test_wrong_target_sha_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "immutable"):
            resolver.select_release([release(21, target_commitish="main")])

    def test_requested_tag_must_exist_and_be_published(self):
        self.assertEqual(
            resolver.select_release([release(20), release(21)], "v0.2.0-dev.20")["version"],
            "0.2.0-dev.20",
        )
        with self.assertRaisesRegex(ValueError, "No verified"):
            resolver.select_release([release(20)], "v0.2.0-dev.21")
        with self.assertRaisesRegex(ValueError, "Invalid"):
            resolver.select_release([release(21)], "main")

    def test_no_release_fails(self):
        with self.assertRaises(ValueError):
            resolver.select_release([])

    def test_metadata_saved_without_modifying_project_sdk(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            project = path / "Test.csproj"
            project.write_text('<Project Sdk="Kiln.Sdk/old" />')
            with patch.dict(os.environ, {}, clear=True):
                resolver.save(path, resolver.select_release([release(21)]))
            stored = json.loads((path / ".kiln-release.json").read_text())
            self.assertEqual(stored["version"], "0.2.0-dev.21")
            self.assertEqual(project.read_text(), '<Project Sdk="Kiln.Sdk/old" />')

    def test_github_output_has_safe_release_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            out = path / "output"
            with patch.dict(os.environ, {"GITHUB_OUTPUT": str(out)}):
                resolver.save(path, resolver.select_release([release(21)]))
            self.assertEqual(out.read_text(), "tag=v0.2.0-dev.21\nversion=0.2.0-dev.21\n")


if __name__ == "__main__":
    unittest.main()
