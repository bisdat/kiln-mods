# Unit tests for the upstream release resolver: no network, no game.
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

script = Path(__file__).resolve().parents[2] / "scripts" / "resolve-kiln-release.py"
spec = importlib.util.spec_from_file_location("kiln_release_resolver", script)
assert spec and spec.loader
resolver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resolver)


def release(n: int, published: str, **changes):
    version = f"0.2.0-dev.{n}"
    result = {
        "tag_name": "v" + version,
        "published_at": published,
        "target_commitish": "a" * 40,
        "draft": False,
        "prerelease": True,
        "assets": [
            {"name": f"Kiln.Sdk.{version}.nupkg"},
            {"name": f"Kiln.Api.{version}.nupkg"},
        ],
    }
    result.update(changes)
    return result


class LatestKilnReleaseTests(unittest.TestCase):
    def test_picks_latest_published_dev_and_not_main(self):
        found = resolver.select_release([
            release(21, "2026-11-09T09:00:00Z"),
            release(20, "2026-11-09T06:27:11Z"),
            release(22, "2026-11-09T07:00:00Z"),
        ])
        self.assertEqual(found["tag"], "v0.2.0-dev.21")

    def test_skip_drafts_and_stable_and_older(self):
        found = resolver.select_release([
            release(22, "2026-11-09T11:00:00Z", draft=True),
            release(23, "2026-11-09T12:00:00Z", prerelease=False),
            release(20, "2026-11-09T06:27:11Z"),
            {"tag_name": "v0.2.0", "draft": False, "prerelease": False,
             "published_at": "2026-11-09T13:00:00Z"},
        ])
        self.assertEqual(found["version"], "0.2.0-dev.20")

    def test_latest_incomplete_release_is_failure_not_older_fallback(self):
        with self.assertRaisesRegex(ValueError, "missing"):
            resolver.select_release([
                release(20, "2026-11-09T06:27:11Z"),
                release(21, "2026-11-09T09:00:00Z", assets=[]),
            ])

    def test_requested_version_must_be_published(self):
        versions = [release(20, "2026-11-09T06:27:11Z"),
                    release(19, "2026-11-08T04:51:42Z")]
        found = resolver.select_release(versions, requested_tag="v0.2.0-dev.19")
        self.assertEqual(found["version"], "0.2.0-dev.19")
        with self.assertRaisesRegex(ValueError, "No verified"):
            resolver.select_release(versions, requested_tag="v0.2.0-dev.21")
        with self.assertRaisesRegex(ValueError, "Invalid"):
            resolver.select_release(versions, requested_tag="main")

    def test_no_valid_dev_release_is_error(self):
        with self.assertRaises(ValueError):
            resolver.select_release([])

    def test_write_updates_one_central_sdk_and_metadata(self):
        with tempfile.TemporaryDirectory() as work:
            root = Path(work)
            root.joinpath("global.json").write_text(
                json.dumps({"msbuild-sdks": {"Other.Sdk": "1.2.3"}}), encoding="utf-8"
            )
            result = resolver.select_release([release(20, "2026-11-09T06:27:11Z")])
            resolver.write_files(root, result)
            record = json.loads(root.joinpath("global.json").read_text(encoding="utf-8"))
            self.assertEqual(record["msbuild-sdks"]["Kiln.Sdk"], "0.2.0-dev.20")
            self.assertEqual(record["msbuild-sdks"]["Other.Sdk"], "1.2.3")
            self.assertIn("KILN_UPSTREAM_TAG=v0.2.0-dev.20",
                          root.joinpath(".kiln-release.env").read_text(encoding="utf-8"))
            self.assertEqual(json.loads(root.joinpath(".kiln-release.json").read_text(encoding="utf-8"))["tag"],
                             "v0.2.0-dev.20")


if __name__ == "__main__":
    unittest.main()
