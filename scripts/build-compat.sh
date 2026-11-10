#!/usr/bin/env bash
# Build active Kiln mods against the exact OFFICIAL released NuGet packages.
# This path does not replace scripts/build.sh used for stable mod tags.
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mod="${1:-]"
case "$mod" in
  SurvivalQoL) mod_id="survival-qol" ;;
  EndlessChests) mod_id="endless-chests-kiln" ;;
  *) echo "Unsupported active mod: $mod" >&2; exit 2 ;;
esac
project="$repo/$mod/$mod.csproj"
[[ -s "$project" ]] || { echo "Missing mod project $project" >&2; exit 2; }
for cmd in git dotnet python3 curl sha256sum; do
  command -v "$cmd" >/dev/null || { echo "Missing command: $cmd" >&2; exit 2; }
done

# Validate the chosen release again inside each independent matrix job.
python3 "$repo/scripts/resolve-kiln-release.py" --root "$repo"
release="$repo/.kiln-release.json"
version="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["version"])' "$release")"
tag="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["tag"])' "$release")"
expected_sha="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["release_target"])' "$release")"
if [[ -n "${KILN_SDK_VERSION:-}" && "$KILN_SDK_VERSION" != "$version" ]]; then
  echo "Expected Kiln SDK version $KILN_SDK_VERSION but resolver returned $version" >&2
  exit 2
fi
[[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+-dev\.[0-9]+$ ]] || exit 2

# Exact tagged source is used only for source/catalogue tests and provenance.
upstream="$repo/.kiln-upstream"
if [[ ! -d "$upstream/.git" ]]; then
  mkdir -p "$upstream"
  git -C "$upstream" init -q
  git -C "$upstream" remote add origin https://github.com/frostdev-ops/kiln.git
fi
git -C "$upstream" fetch --depth=1 origin "refs/tags/$tag"
actual_sha="$(git -C "$upstream" rev-parse 'FETCH_HEAD^{commit}')"
if [[ "$actual_sha" != "$expected_sha" ]]; then
  echo "Release SHA mismatch: tag=$actual_sha published=$expected_sha" >&2
  exit 2
fi
git -C "$upstream" -c advice.detachedHead=false checkout -q --detach "$actual_sha"

# Use the OFFICIAL released SDK/API assets, not a locally repacked worktree.
feed="$repo/.kiln-mdk/nuget"
mkdir -p "$feed"
url="https://github.com/frostdev-ops/kiln/releases/download/$tag"
sums="$feed/kiln-$tag-SHA256SUMS"
curl --fail --location --silent --show-error --retry 3 "$url/SHA256SUMS" -o "$sums"
for asset in "Kiln.Sdk.$version.nupkg" "Kiln.Api.$version.nupkg"; do
  curl --fail --location --silent --show-error --retry 3 "$url/$asset" -o "$feed/$asset"
  expected="$(awk -v name="$asset" '($2==name || $2=="*"name) {print $1; exit}' "$sums")"
  [[ "$expected" =~ ^[[:xdigit:]]{64}$ ]] || { echo "Missing official checksum for $asset" >&2; exit 2; }
  actual="$(sha256sum "$feed/$asset" | cut -d' ' -f1)"
  if [[ "${actual,,}" != "${expected,,}" ]]; then
    echo "Checksum mismatch for $asset" >&2
    exit 2
  fi
done

# Both active mods deliberately pin older SDKs for stable releases.
# Temporarily rewrite only the SDK reference, restoring exact source on exit.
backup="$(mktemp)"
cp -- "$project" "$backup"
cleanup() { cp -- "$backup" "$project"; rm -f -- "$backup"; }
trap cleanup EXIT
python3 - "$project" "$version" <<'PY'
from pathlib import Path
import re
import sys
project, version = Path(sys.argv[1]), sys.argv[2]
text = project.read_text(encoding="utf-8")
new, count = re.subn(
    r'<Project Sdk="Kiln\.Sdk/[0-9A-Za-z.+-]+">',
    f'<Project Sdk="Kiln.Sdk/{version}">',
    text, count=1,
)
if count != 1:
    raise SystemExit(f"Expected exactly one Kiln.Sdk project declaration: {project}")
project.write_text(new, encoding="utf-8")
PY

cd "$repo"
dotnet restore "$project" --configfile "$repo/NuGet.Config"
dotnet build "$project" -c Release --no-restore
dotnet build "$project" -c Release --no-restore -t:KilnPack
mod_version="$(awk -F'[<>]' '/<Version>/ {print $3; exit}' "$project")"
package="$repo/bin/Release/$mod_id-$mod_version.zip"
[[ -s "$package" ]] || { echo "Missing installable ZIP $package" >&2; exit 2; }

# Provenance tied to official assets, immutable tag, and exact mod source.
source_sha="$(git -C "$repo" rev-parse HEAD)"
python3 - "$release" "$actual_sha" "$source_sha" "$mod" "$feed" <<'PY'
import hashlib, json, sys
from pathlib import Path
info_path, upstream_sha, source_sha, mod, feed = sys.argv[1:]
info = json.loads(Path(info_path).read_text(encoding="utf-8"))
info["checked_out_sha"] = upstream_sha
info["mod_source_sha"] = source_sha
info["mod"] = mod
info["official_assets_sha256"] = {
    filename: hashlib.sha256((Path(feed) / filename).read_bytes()).hexdigest()
    for filename in (
        f'Kiln.Sdk.{info["version"]}.nupkg',
        f'Kiln.Api.{info["version"]}.nupkg',
    )
}
(Path(info_path).parent / "bin/Release/kiln-sdk-provenance.json").write_text(
    json.dumps(info, indent=2) + "\n", encoding="utf-8"
)
PY
echo "Verified compatibility build: mod=$mod package=$package upstream=$tag@$actual_sha"
