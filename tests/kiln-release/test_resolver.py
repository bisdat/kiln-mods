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

    def test_published_dev_patch_sdk_release(self):
        selected = resolver.select_release(
            [release(29, "2026-11-16T00:04:22Z"),
             release("29.14", "2026-11-17T06:13:19Z")],
            "v0.2.0-dev.29.14",
        )
        self.assertEqual(selected["version"], "0.2.0-dev.29.14")
        self.assertEqual(selected["tag"], "v0.2.0-dev.29.14")

    def test_compatibility_packager_accepts_patch_numbered_sdk(self):
        path = Path(__file__).resolve().parents[2] / "scripts/build-compat.py"
        spec = importlib.util.spec_from_file_location("kiln_build_compat", path)
        assert spec and spec.loader
        builder = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(builder)
        for tag in ("v0.2.0-dev.29", "v0.2.0-dev.29.15"):
            with self.subTest(tag=tag):
                self.assertIsNotNone(builder.TAG.fullmatch(tag))


    def test_patch_numbered_dev_release_is_selected_and_can_be_pinned(self):
        patched = release(
            29,
            "2026-11-17T16:36:47Z",
            tag_name="v0.2.0-dev.29.15",
            html_url="https://github.com/frostdev-ops/kiln/releases/tag/v0.2.0-dev.29.15",
            assets=[
                {"name": "Kiln.Sdk.0.2.0-dev.29.15.nupkg"},
                {"name": "Kiln.Api.0.2.0-dev.29.15.nupkg"},
                {"name": "SHA256SUMS"},
            ],
        )
        old = release(29, "2026-11-17T08:00:00Z")
        self.assertEqual(resolver.select_release([old, patched])["tag"], "v0.2.0-dev.29.15")
        self.assertEqual(resolver.select_release([old, patched], "v0.2.0-dev.29.15")["version"],
                         "0.2.0-dev.29.15")

    def test_drafts_and_unpublished_are_ignored(self):
        found = resolver.select_release([
            release(22, "2026-11-11T06:00:00Z", draft=True),
            release(23, "2026-11-12T06:00:00Z", published_at=None),
            release(21),
        ])
        self.assertEqual(found["version"], "0.2.0-dev.21")

    def test_launcher_only_release_is_skipped_without_failure(self):
        # Upstream dev.29.23 was launcher-only (save editor), with no
        # Kiln.Api/Kiln.Sdk NuGet. The game-side SDK stayed at dev.29.22.
        sdk = release("29.22", "2026-11-20T17:32:29Z")
        launcher = release("29.23", "2026-11-20T20:18:53Z",
                           assets=[{"name": "SHA256SUMS"},
                                   {"name": "kiln-launcher_0.2.0-dev.29.23_amd64.deb"}])
        found = resolver.select_release([sdk, launcher])
        self.assertEqual(found["tag"], "v0.2.0-dev.29.22")

    def test_newer_complete_sdk_is_selected_over_launcher_only(self):
        sdk_old = release("29.22", "2026-11-20T17:32:29Z")
        launcher = release("29.23", "2026-11-20T20:18:53Z",
                           assets=[{"name": "SHA256SUMS"}])
        sdk_new = release("30.1", "2026-11-21T02:37:33Z")
        self.assertEqual(resolver.select_release([sdk_old, launcher, sdk_new])["tag"],
                         "v0.2.0-dev.30.1")

    def test_partial_sdk_release_still_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "missing assets"):
            resolver.select_release([
                release(20, "2026-11-09T06:27:11Z"),
                release(21, assets=[{"name": "SHA256SUMS"},
                                    {"name": "Kiln.Api.0.2.0-dev.21.nupkg"}]),
            ])

    def test_explicit_request_for_launcher_only_patch_fails(self):
        launcher = release("29.23", "2026-11-20T20:18:53Z",
                           assets=[{"name": "SHA256SUMS"}])
        with self.assertRaisesRegex(ValueError, "missing assets"):
            resolver.select_release([launcher], "v0.2.0-dev.29.23")

    def test_no_sdk_release_available_fails(self):
        launcher = release("29.23", assets=[{"name": "SHA256SUMS"}])
        with self.assertRaisesRegex(ValueError, "No published Kiln dev SDK"):
            resolver.select_release([launcher])

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
