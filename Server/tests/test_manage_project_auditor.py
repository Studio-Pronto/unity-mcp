from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_project_auditor import (
    manage_project_auditor,
    ALL_ACTIONS,
    AUDIT_ACTIONS,
    QUERY_ACTIONS,
    RULE_ACTIONS,
    STATUS_ACTIONS,
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
        "services.tools.manage_project_auditor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_project_auditor.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_all_actions_is_union_of_sub_lists():
    expected = set(AUDIT_ACTIONS + QUERY_ACTIONS + RULE_ACTIONS + STATUS_ACTIONS)
    assert set(ALL_ACTIONS) == expected


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_all_actions_count():
    assert len(ALL_ACTIONS) == 11


def test_audit_actions_count():
    assert len(AUDIT_ACTIONS) == 2


def test_query_actions_count():
    assert len(QUERY_ACTIONS) == 5


def test_rule_actions_count():
    assert len(RULE_ACTIONS) == 3


def test_status_actions_count():
    assert len(STATUS_ACTIONS) == 1


# ---------------------------------------------------------------------------
# Invalid actions
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="")
    )
    assert result["success"] is False


# ---------------------------------------------------------------------------
# Status
# ---------------------------------------------------------------------------

def test_status_forwards(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="status")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "status"


# ---------------------------------------------------------------------------
# Audit param forwarding
# ---------------------------------------------------------------------------

def test_audit_forwards_params(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="audit",
            categories="Code,Shader", assemblies="Assembly-CSharp",
            platform="StandaloneWindows64",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "audit"
    assert mock_unity["params"]["categories"] == "Code,Shader"
    assert mock_unity["params"]["assemblies"] == "Assembly-CSharp"
    assert mock_unity["params"]["platform"] == "StandaloneWindows64"


def test_audit_omits_none_params(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="audit")
    )
    assert result["success"] is True
    assert "categories" not in mock_unity["params"]
    assert "assemblies" not in mock_unity["params"]
    assert "platform" not in mock_unity["params"]


# ---------------------------------------------------------------------------
# Load report
# ---------------------------------------------------------------------------

def test_load_report_forwards_path(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="load_report",
            report_path="Library/my-report.projectauditor",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["report_path"] == "Library/my-report.projectauditor"


def test_load_report_no_path_sends_minimal(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="load_report")
    )
    assert result["success"] is True
    assert "report_path" not in mock_unity["params"]
    assert mock_unity["params"]["action"] == "load_report"


# ---------------------------------------------------------------------------
# Get summary
# ---------------------------------------------------------------------------

def test_get_summary_forwards(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="get_summary")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_summary"


# ---------------------------------------------------------------------------
# List issues with filters
# ---------------------------------------------------------------------------

def test_list_issues_forwards_filters(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="list_issues",
            category="Code", severity="Major", area="CPU",
            path_filter="Assets/Scripts/", search="allocation",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["category"] == "Code"
    assert mock_unity["params"]["severity"] == "Major"
    assert mock_unity["params"]["area"] == "CPU"
    assert mock_unity["params"]["path_filter"] == "Assets/Scripts/"
    assert mock_unity["params"]["search"] == "allocation"


def test_list_issues_pagination_params(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="list_issues",
            page_size=25, cursor="50",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["page_size"] == 25
    assert mock_unity["params"]["cursor"] == "50"


# ---------------------------------------------------------------------------
# Get issue detail
# ---------------------------------------------------------------------------

def test_get_issue_detail_forwards_descriptor_id(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="get_issue_detail",
            descriptor_id="PAC2000",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["descriptor_id"] == "PAC2000"


# ---------------------------------------------------------------------------
# List categories / areas
# ---------------------------------------------------------------------------

def test_list_categories_forwards(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="list_categories")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "list_categories"


def test_list_areas_forwards(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="list_areas")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "list_areas"


# ---------------------------------------------------------------------------
# Rule management
# ---------------------------------------------------------------------------

def test_add_rule_forwards_params(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="add_rule",
            descriptor_id="PAC2000", rule_severity="None",
            rule_filter="Assets/ThirdParty/",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["descriptor_id"] == "PAC2000"
    assert mock_unity["params"]["rule_severity"] == "None"
    assert mock_unity["params"]["rule_filter"] == "Assets/ThirdParty/"


def test_add_rule_global_suppress(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="add_rule",
            descriptor_id="PAC2000", rule_severity="None",
        )
    )
    assert result["success"] is True
    assert "rule_filter" not in mock_unity["params"]


def test_remove_rule_forwards_params(mock_unity):
    result = asyncio.run(
        manage_project_auditor(
            SimpleNamespace(), action="remove_rule",
            descriptor_id="PAC2000", rule_filter="Assets/ThirdParty/",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["descriptor_id"] == "PAC2000"
    assert mock_unity["params"]["rule_filter"] == "Assets/ThirdParty/"


def test_list_rules_forwards(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="list_rules")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "list_rules"


# ---------------------------------------------------------------------------
# None params omitted
# ---------------------------------------------------------------------------

def test_none_params_omitted(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="list_issues", category="Code")
    )
    assert result["success"] is True
    assert "severity" not in mock_unity["params"]
    assert "area" not in mock_unity["params"]
    assert "path_filter" not in mock_unity["params"]
    assert "page_size" not in mock_unity["params"]


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="Get_Summary")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_summary"


def test_action_uppercase(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="LIST_ISSUES")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "list_issues"


# ---------------------------------------------------------------------------
# Parametrized: every action forwards to Unity
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("action_name", ALL_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    """Every valid action should be forwarded to Unity without error."""
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action=action_name)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_project_auditor"
    assert mock_unity["params"]["action"] == action_name


# ---------------------------------------------------------------------------
# Non-dict response wrapping
# ---------------------------------------------------------------------------

def test_non_dict_response_wrapped(monkeypatch):
    monkeypatch.setattr(
        "services.tools.manage_project_auditor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_project_auditor.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="status")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]


# ---------------------------------------------------------------------------
# Tool registration
# ---------------------------------------------------------------------------

def test_tool_registered_with_auditor_group():
    from services.registry.tool_registry import _tool_registry

    auditor_tools = [
        t for t in _tool_registry if t.get("name") == "manage_project_auditor"
    ]
    assert len(auditor_tools) == 1
    assert auditor_tools[0]["group"] == "auditor"


# ---------------------------------------------------------------------------
# Sends to correct tool name
# ---------------------------------------------------------------------------

def test_sends_to_correct_tool_name(mock_unity):
    result = asyncio.run(
        manage_project_auditor(SimpleNamespace(), action="status")
    )
    assert mock_unity["tool_name"] == "manage_project_auditor"
