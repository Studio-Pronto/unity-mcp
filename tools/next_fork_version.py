#!/usr/bin/env python3
"""Print the fork version for a just-merged upstream sync.

The fork version is upstream's base version plus -fork.N: a sync of upstream beta
10.3.1-beta.10 becomes 10.3.1-fork.N. Upstream's prerelease label is dropped because the
Editor's update check (PackageUpdateService) only parses versions shaped like 10.3.1 or
10.3.1-fork.4, and reports a version it cannot parse as never out of date.

Several beta syncs can share one base, so N continues from the fork's version on
origin/main while the base is unchanged, and starts again at 1 when it moves.

Usage (after the merge, with upstream's version in MCPForUnity/package.json):
    python3 tools/next_fork_version.py
"""

import json
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGE_JSON = "MCPForUnity/package.json"


def next_fork_version(upstream_version: str, fork_version: str) -> str:
    """The version for a sync of upstream_version into a fork currently at fork_version."""
    base = upstream_version.split("-")[0]
    fork_base, _, fork_n = fork_version.partition("-fork.")
    n = int(fork_n) + 1 if fork_base == base and fork_n.isdigit() else 1
    return f"{base}-fork.{n}"


def main() -> int:
    upstream_version = json.loads((REPO_ROOT / PACKAGE_JSON).read_text(encoding="utf-8"))["version"]
    fork_package = subprocess.check_output(
        ["git", "-C", str(REPO_ROOT), "show", f"origin/main:{PACKAGE_JSON}"], text=True
    )
    print(next_fork_version(upstream_version, json.loads(fork_package)["version"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
