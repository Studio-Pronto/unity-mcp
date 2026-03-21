from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_profiler import (
    manage_profiler,
    ALL_ACTIONS,
    SAMPLE_ACTIONS,
    COUNTER_ACTIONS,
    FRAME_TIME_ACTIONS,
    HIERARCHY_ACTIONS,
    MEMORY_ACTIONS,
    CAPTURE_ACTIONS,
    CONTROL_ACTIONS,
    PHYSICS_ACTIONS,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_all_actions_is_union_of_sub_lists():
    expected = set(
        ["ping"] + SAMPLE_ACTIONS + COUNTER_ACTIONS + FRAME_TIME_ACTIONS
        + HIERARCHY_ACTIONS + MEMORY_ACTIONS + CAPTURE_ACTIONS + CONTROL_ACTIONS
        + PHYSICS_ACTIONS
    )
    assert set(ALL_ACTIONS) == expected


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_all_actions_count():
    assert len(ALL_ACTIONS) == 24


def test_sample_actions_count():
    assert len(SAMPLE_ACTIONS) == 5


def test_counter_actions_count():
    assert len(COUNTER_ACTIONS) == 2


def test_frame_time_actions_count():
    assert len(FRAME_TIME_ACTIONS) == 1


def test_hierarchy_actions_count():
    assert len(HIERARCHY_ACTIONS) == 3


def test_memory_actions_count():
    assert len(MEMORY_ACTIONS) == 4


def test_capture_actions_count():
    assert len(CAPTURE_ACTIONS) == 3


def test_control_actions_count():
    assert len(CONTROL_ACTIONS) == 4


def test_physics_actions_count():
    assert len(PHYSICS_ACTIONS) == 1


# ---------------------------------------------------------------------------
# Invalid actions
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="")
    )
    assert result["success"] is False


# ---------------------------------------------------------------------------
# Ping
# ---------------------------------------------------------------------------

def test_ping_forwards(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="ping")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "ping"


# ---------------------------------------------------------------------------
# Counter sampling param forwarding
# ---------------------------------------------------------------------------

def test_sample_start_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="sample_start",
            label="baseline", counters="render", capacity=600,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "sample_start"
    assert mock_unity["params"]["label"] == "baseline"
    assert mock_unity["params"]["counters"] == "render"
    assert mock_unity["params"]["capacity"] == 600


def test_sample_stop_forwards_label(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="sample_stop", label="baseline")
    )
    assert result["success"] is True
    assert mock_unity["params"]["label"] == "baseline"


def test_sample_read_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="sample_read", label="test", last_n=60)
    )
    assert result["success"] is True
    assert mock_unity["params"]["last_n"] == 60


def test_sample_compare_forwards_labels(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="sample_compare",
            label_a="before", label_b="after", threshold_pct=3.0,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["label_a"] == "before"
    assert mock_unity["params"]["label_b"] == "after"
    assert mock_unity["params"]["threshold_pct"] == 3.0


def test_sample_list_sends_action(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="sample_list")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "sample_list"


# ---------------------------------------------------------------------------
# Counter discovery
# ---------------------------------------------------------------------------

def test_counter_read_forwards_counters(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="counter_read", counters="physics")
    )
    assert result["success"] is True
    assert mock_unity["params"]["counters"] == "physics"


def test_counter_list_forwards_category_and_search(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="counter_list",
            category="render", search="Draw", page_size=25,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["category"] == "render"
    assert mock_unity["params"]["search"] == "Draw"
    assert mock_unity["params"]["page_size"] == 25


# ---------------------------------------------------------------------------
# Frame time
# ---------------------------------------------------------------------------

def test_frame_time_get_forwards_frames(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="frame_time_get", frames=240)
    )
    assert result["success"] is True
    assert mock_unity["params"]["frames"] == 240


# ---------------------------------------------------------------------------
# Hierarchy / hotspots
# ---------------------------------------------------------------------------

def test_hotspots_get_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="hotspots_get",
            top_n=10, frames=60, min_ms=0.5, thread="render",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["top_n"] == 10
    assert mock_unity["params"]["thread"] == "render"
    assert mock_unity["params"]["min_ms"] == 0.5
    assert mock_unity["params"]["frames"] == 60


def test_hotspots_detail_forwards_marker(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="hotspots_detail",
            marker_name="Physics.Processing",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["marker_name"] == "Physics.Processing"


def test_gc_track_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="gc_track", frames=180, top_n=15)
    )
    assert result["success"] is True
    assert mock_unity["params"]["frames"] == 180


# ---------------------------------------------------------------------------
# Memory
# ---------------------------------------------------------------------------

def test_memory_snapshot_forwards_label(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="memory_snapshot", label="before_opt")
    )
    assert result["success"] is True
    assert mock_unity["params"]["label"] == "before_opt"


def test_memory_compare_forwards_labels(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="memory_compare",
            label_a="before", label_b="after",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["label_a"] == "before"


def test_memory_objects_forwards_filters(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="memory_objects",
            object_type="Texture2D", min_size_kb=100, page_size=20,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["type"] == "Texture2D"
    assert mock_unity["params"]["min_size_kb"] == 100
    assert mock_unity["params"]["page_size"] == 20


def test_memory_objects_forwards_cursor_and_max(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="memory_objects",
            cursor="40", max_objects=5000,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["cursor"] == "40"
    assert mock_unity["params"]["max_objects"] == 5000


def test_memory_type_summary_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="memory_type_summary",
            min_total_mb=5.0, max_objects=5000,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["min_total_mb"] == 5.0


# ---------------------------------------------------------------------------
# Capture
# ---------------------------------------------------------------------------

def test_capture_start_forwards_path(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="capture_start",
            output_path="Profiler/test.raw",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["output_path"] == "Profiler/test.raw"


def test_capture_stop_sends_action(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="capture_stop")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "capture_stop"


def test_capture_stop_forwards_keep_profiler_enabled(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="capture_stop", keep_profiler_enabled=True)
    )
    assert result["success"] is True
    assert mock_unity["params"]["keep_profiler_enabled"] is True


def test_capture_status_sends_action(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="capture_status")
    )
    assert result["success"] is True


# ---------------------------------------------------------------------------
# Control
# ---------------------------------------------------------------------------

def test_profiler_disable_sends_action(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_disable")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "profiler_disable"


def test_profiler_enable_sends_action(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_enable")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "profiler_enable"


def test_deep_profiling_set_forwards_enabled(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="deep_profiling_set", enabled=True)
    )
    assert result["success"] is True
    assert mock_unity["params"]["enabled"] is True


def test_area_set_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="area_set", area="GPU", enabled=False)
    )
    assert result["success"] is True
    assert mock_unity["params"]["area"] == "GPU"
    assert mock_unity["params"]["enabled"] is False


# ---------------------------------------------------------------------------
# Physics
# ---------------------------------------------------------------------------

def test_physics_get_forwards_frames(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="physics_get", frames=60)
    )
    assert result["success"] is True
    assert mock_unity["params"]["frames"] == 60


# ---------------------------------------------------------------------------
# None params omitted
# ---------------------------------------------------------------------------

def test_none_params_omitted(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="sample_start", label="test", counters="render")
    )
    assert result["success"] is True
    assert "frames" not in mock_unity["params"]
    assert "top_n" not in mock_unity["params"]
    assert "output_path" not in mock_unity["params"]


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="Frame_Time_Get")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "frame_time_get"


def test_action_uppercase(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="HOTSPOTS_GET")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "hotspots_get"


# ---------------------------------------------------------------------------
# Parametrized: every action forwards to Unity
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("action_name", ALL_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    """Every valid action should be forwarded to Unity without error."""
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action=action_name)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_profiler"
    assert mock_unity["params"]["action"] == action_name


# ---------------------------------------------------------------------------
# Non-dict response wrapping
# ---------------------------------------------------------------------------

def test_non_dict_response_wrapped(monkeypatch):
    monkeypatch.setattr(
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="ping")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]


# ---------------------------------------------------------------------------
# Tool registration
# ---------------------------------------------------------------------------

def test_tool_registered_with_core_group():
    from services.registry.tool_registry import _tool_registry

    profiler_tools = [
        t for t in _tool_registry if t.get("name") == "manage_profiler"
    ]
    assert len(profiler_tools) == 1
    assert profiler_tools[0]["group"] == "core"
