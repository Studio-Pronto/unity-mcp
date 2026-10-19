"""Tests for tools/next_fork_version.py."""
import sys
from pathlib import Path

import pytest

_TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(_TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIR))

from next_fork_version import next_fork_version  # noqa: E402


@pytest.mark.parametrize(
    "upstream, fork, expected",
    [
        # A beta sync drops upstream's prerelease label: the Editor's update check cannot
        # parse 10.3.1-beta.10-fork.1, and would never report an update past it.
        ("10.3.1-beta.10", "10.3.0-fork.5", "10.3.1-fork.1"),
        # A later beta sync on the same base continues the count instead of going back to fork.1.
        ("10.3.1-beta.15", "10.3.1-fork.3", "10.3.1-fork.4"),
        # A release sync moves the base and starts a new count.
        ("10.4.0", "10.3.1-fork.4", "10.4.0-fork.1"),
        # Re-running the fixups after they stamped the version leaves it unchanged.
        ("10.3.1-fork.4", "10.3.1-fork.3", "10.3.1-fork.4"),
    ],
)
def test_next_fork_version(upstream, fork, expected):
    assert next_fork_version(upstream, fork) == expected
