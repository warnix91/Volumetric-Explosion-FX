#!/usr/bin/env bash
set -euo pipefail
TASK_TOOLS_DIR="$(cd "$(dirname "$0")" && pwd)"
exec python3 "$TASK_TOOLS_DIR/project.py" test "$@"
