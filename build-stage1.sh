#!/usr/bin/env bash
# Stage 2 scanner plus backup-first removal candidate. Run from bisdat/kiln-mods root.
set -euo pipefail
cd -- "$(dirname -- "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  printf '%s\n' 'Requires .NET 10 SDK (dotnet) on PATH.' >&2
  exit 1
fi
python3 -m unittest discover -s tests/base-cleanup -p 'test_*.py' -v
python3 BaseCleanup/fetch-sdk.py
dotnet restore BaseCleanup/BaseCleanup.csproj --configfile NuGet.Config
dotnet build BaseCleanup/BaseCleanup.csproj -c Release --no-restore
dotnet build BaseCleanup/BaseCleanup.csproj -c Release --no-restore -t:KilnPack
test -s bin/Release/base-cleanup-0.2.0.zip
printf 'Built bin/Release/base-cleanup-0.2.0.zip\n'
