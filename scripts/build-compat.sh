#!/usr/bin/env bash
# Keep the CI entrypoint stable; Python handles safe project rollback.
set -euo pipefail
exec python3 "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/build-compat.py" "$@"
