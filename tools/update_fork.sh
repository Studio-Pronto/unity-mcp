#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# update_fork.sh — Merge upstream into fork and apply fork-specific fixups
#
# Usage:
#   ./tools/update_fork.sh [upstream-ref]
#
# Examples:
#   ./tools/update_fork.sh                    # merges upstream/main
#   ./tools/update_fork.sh v9.6.0             # merges a specific tag
#   ./tools/update_fork.sh upstream/beta      # merges a specific branch
# =============================================================================

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATOR_DIR="$REPO_ROOT/MCPForUnity/Editor/Clients/Configurators"
KEEP_CONFIGURATOR="ClaudeCodeConfigurator"

UPSTREAM_REF="${1:-upstream/main}"

# --- Pre-flight checks -------------------------------------------------------

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
    echo "Error: working tree is not clean. Commit or stash changes first."
    exit 1
fi

CURRENT_BRANCH="$(git -C "$REPO_ROOT" branch --show-current)"
if [[ "$CURRENT_BRANCH" != "main" ]]; then
    echo "Error: expected to be on 'main', currently on '$CURRENT_BRANCH'."
    exit 1
fi

# --- Fetch & merge upstream ---------------------------------------------------

echo "Fetching upstream..."
git -C "$REPO_ROOT" fetch upstream

echo "Merging $UPSTREAM_REF..."
if ! git -C "$REPO_ROOT" merge "$UPSTREAM_REF" -m "Merge $UPSTREAM_REF into fork"; then
    echo ""
    echo "Merge conflicts detected. Resolve them, then run:"
    echo "  git commit"
    echo "  ./tools/update_fork.sh --fixup-only"
    exit 1
fi

# --- Fork fixups (also reachable via --fixup-only) ----------------------------

apply_fixups() {
    # 1. Remove non-ClaudeCode configurators
    local removed=0
    if [[ -d "$CONFIGURATOR_DIR" ]]; then
        for f in "$CONFIGURATOR_DIR"/*; do
            base="$(basename "$f")"
            if [[ "$base" != "$KEEP_CONFIGURATOR"* ]]; then
                git -C "$REPO_ROOT" rm -f "$f" 2>/dev/null && ((removed++)) || true
            fi
        done
    fi
    if (( removed > 0 )); then
        echo "Removed $removed non-ClaudeCode configurator file(s)."
    else
        echo "No extra configurators to remove."
    fi

    # 2. Detect upstream version and apply fork suffix
    local upstream_version
    upstream_version="$(python3 -c "
import json, pathlib
p = pathlib.Path('$REPO_ROOT/MCPForUnity/package.json')
v = json.loads(p.read_text())['version']
# Strip any existing fork suffix
print(v.split('-fork')[0])
")"

    local fork_version="${upstream_version}-fork.1"
    echo "Setting fork version: $fork_version"
    python3 "$REPO_ROOT/tools/update_versions.py" --version "$fork_version"

    # 3. Regenerate uv.lock
    echo "Regenerating uv.lock..."
    (cd "$REPO_ROOT/Server" && uv lock 2>/dev/null) || echo "Warning: uv lock failed (uv may not be installed)"

    # 4. Stage and commit fixups
    git -C "$REPO_ROOT" add -A
    if git -C "$REPO_ROOT" diff --cached --quiet; then
        echo "No fixups needed."
    else
        git -C "$REPO_ROOT" commit -m "Apply fork fixups: remove non-ClaudeCode configurators, set version $fork_version"
        echo "Fork fixups committed."
    fi
}

if [[ "${1:-}" == "--fixup-only" ]]; then
    apply_fixups
    exit 0
fi

apply_fixups

echo ""
echo "Done. Review with: git log --oneline -5"
