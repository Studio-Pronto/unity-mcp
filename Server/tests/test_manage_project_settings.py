"""Tests for manage_project_settings MCP tool."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_project_settings import ALL_ACTIONS, manage_project_settings


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
        "services.tools.manage_project_settings.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_project_settings.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── action validation ───────────────────────────────────────────────

def test_all_actions_count():
    assert len(ALL_ACTIONS) == 4


def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(manage_project_settings(SimpleNamespace(), action="nonexistent"))
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


# ── get action ──────────────────────────────────────────────────────

def test_get_forwards_params(mock_unity):
    asyncio.run(
        manage_project_settings(
            SimpleNamespace(), action="get", category="quality", property="shadowDistance"
        )
    )
    params = mock_unity["params"]
    assert params["action"] == "get"
    assert params["category"] == "quality"
    assert params["property"] == "shadowDistance"
    assert "value" not in params


# ── set action ──────────────────────────────────────────────────────

def test_set_forwards_params(mock_unity):
    asyncio.run(
        manage_project_settings(
            SimpleNamespace(),
            action="set",
            category="physics",
            property="gravity",
            value="[0, -20, 0]",
        )
    )
    params = mock_unity["params"]
    assert params["action"] == "set"
    assert params["category"] == "physics"
    assert params["property"] == "gravity"
    assert params["value"] == "[0, -20, 0]"


# ── list action ─────────────────────────────────────────────────────

def test_list_forwards_category(mock_unity):
    asyncio.run(
        manage_project_settings(SimpleNamespace(), action="list", category="time")
    )
    params = mock_unity["params"]
    assert params["action"] == "list"
    assert params["category"] == "time"
    assert "property" not in params


# ── list_categories action ──────────────────────────────────────────

def test_list_categories_sends_minimal_params(mock_unity):
    asyncio.run(manage_project_settings(SimpleNamespace(), action="list_categories"))
    params = mock_unity["params"]
    assert params == {"action": "list_categories"}


# ── param filtering ─────────────────────────────────────────────────

def test_none_values_omitted(mock_unity):
    asyncio.run(manage_project_settings(SimpleNamespace(), action="get"))
    params = mock_unity["params"]
    assert params == {"action": "get"}


# ── transport ───────────────────────────────────────────────────────

def test_sends_to_correct_tool_name(mock_unity):
    asyncio.run(manage_project_settings(SimpleNamespace(), action="list_categories"))
    assert mock_unity["tool_name"] == "manage_project_settings"
