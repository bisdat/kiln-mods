#!/usr/bin/env bash
# Read-only, separate Base Visual Probe; not a Base Cleanup release.
set -euo pipefail
cd -- "$(dirname -- "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo 'Requires .NET 10 (dotnet).' >&2
  exit 1
fi
python3 -m unittest discover -s tests/base-visual-probe -p 'test_*.py' -v
python3 BaseCleanup/fetch-sdk.py
dotnet restore BaseVisualProbe/BaseVisualProbe.csproj --configfile NuGet.Config
dotnet build BaseVisualProbe/BaseVisualProbe.csproj -c Release --no-restore
dotnet build BaseVisualProbe/BaseVisualProbe.csproj -c Release --no-restore -t:KilnPack
test -s bin/Release/base-visual-probe-0.1.1.zip
unzip -t bin/Release/base-visual-probe-0.1.1.zip
echo 'Built bin/Release/base-visual-probe-0.1.1.zip'
