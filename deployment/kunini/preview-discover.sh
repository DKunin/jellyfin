#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
port="${1:-8765}"

printf 'Kunini Discover preview: http://127.0.0.1:%s/preview/index.html#/details?id=3cf2cc5c1467441a9f8bb42babc2eb7d\n' "${port}"
exec python3 -m http.server "${port}" --bind 127.0.0.1 --directory "${script_dir}/discover-plugin"
