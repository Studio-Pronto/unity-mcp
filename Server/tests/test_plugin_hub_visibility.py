"""Regression tests for PluginHub._sync_server_tool_visibility.

An all-instances-disconnected window (every Unity domain reload drops the
plugin WebSocket) must NOT hide tools from MCP clients: the HTTP path's
registry union going empty means "no Unity connected right now" — absence of
information — not a "disable everything" signal. Before the guard, the sync
disabled every group (including ``core``) during that window, surfacing as
transient ``Unknown tool: 'read_console'`` errors for every connected MCP
session and bypassing the reload-retry path in ``send_command_for_instance``.

The explicit-list path (stdio's ``sync_tool_visibility_from_unity``) is
affirmative data from a live Unity and must keep its disable semantics even
for an empty list — pinned here so the guard stays scoped to the registry
branch.
"""

import asyncio

import pytest

# Importing a tool module runs its @mcp_for_unity_tool decorator, populating
# the python-side _tool_registry so get_group_tool_names() maps manage_scene
# to group "core" (package import alone does not import tool modules —
# discovery happens at server startup).
import services.tools.manage_scene  # noqa: F401
from models.models import ToolDefinitionModel
from transport.plugin_hub import PluginHub
from transport.plugin_registry import PluginRegistry


class FakeMCP:
    """Minimal FastMCP stand-in recording enable/disable transform calls."""

    def __init__(self):
        self._transforms = ["startup-sentinel"]
        self.enabled_tags: list[set] = []
        self.disabled_tags: list[set] = []

    def enable(self, tags=None, components=None):
        self._transforms.append(("enable", frozenset(tags or ())))
        self.enabled_tags.append(set(tags or ()))

    def disable(self, tags=None, components=None):
        self._transforms.append(("disable", frozenset(tags or ())))
        self.disabled_tags.append(set(tags or ()))


@pytest.fixture
def hub_state():
    """Snapshot/restore PluginHub class state.

    Assigns _mcp/_registry directly instead of PluginHub.configure() to avoid
    the _install_session_tracking() side effect (it monkeypatches fastmcp's
    MiddlewareServerSession process-wide). Existing fixtures elsewhere do not
    reset _mcp or _unity_transform_start, so this fixture restores both.
    """
    saved = (
        PluginHub._registry,
        PluginHub._mcp,
        PluginHub._unity_transform_start,
        PluginHub._lock,
    )
    registry = PluginRegistry()
    mcp = FakeMCP()
    PluginHub._registry = registry
    PluginHub._mcp = mcp
    PluginHub._unity_transform_start = None
    PluginHub._lock = asyncio.Lock()
    yield registry, mcp
    (
        PluginHub._registry,
        PluginHub._mcp,
        PluginHub._unity_transform_start,
        PluginHub._lock,
    ) = saved


@pytest.mark.asyncio
async def test_empty_registry_union_freezes_visibility_and_returns_false(hub_state):
    """Empty HTTP registry union (Unity mid-domain-reload) must not touch
    transforms — the regression was all groups (incl. core) getting disabled,
    causing 'Unknown tool' for every MCP session in the reload window."""
    _registry, mcp = hub_state

    changed = await PluginHub._sync_server_tool_visibility()

    assert changed is False
    assert mcp._transforms == ["startup-sentinel"]
    assert mcp.enabled_tags == []
    assert mcp.disabled_tags == []
    assert PluginHub._unity_transform_start is None


@pytest.mark.asyncio
async def test_nonempty_registry_union_rewrites_and_returns_true(hub_state):
    """A connected Unity session with registered tools re-enables its groups
    (core here) and reports the rewrite so callers know to notify clients."""
    registry, mcp = hub_state
    await registry.register("sess-1", "Proj", "hash-1", "6000.0")
    await registry.register_tools_for_session(
        "sess-1", [ToolDefinitionModel(name="manage_scene")]
    )

    changed = await PluginHub._sync_server_tool_visibility()

    assert changed is True
    enabled = set().union(*mcp.enabled_tags) if mcp.enabled_tags else set()
    assert "group:core" in enabled
    # First real sync records where Unity's overrides start.
    assert PluginHub._unity_transform_start == 1  # after the startup sentinel


@pytest.mark.asyncio
async def test_empty_explicit_list_still_disables_groups(hub_state):
    """The stdio explicit-list path is affirmative Unity data: an empty list
    must keep disabling groups (guard applies only to the registry branch)."""
    _registry, mcp = hub_state

    changed = await PluginHub._sync_server_tool_visibility(registered_tools=[])

    assert changed is True
    assert mcp.enabled_tags == []
    assert len(mcp.disabled_tags) > 0
