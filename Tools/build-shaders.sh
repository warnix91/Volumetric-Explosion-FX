#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 Tools/shaders.py --target "${1:-mac}"
