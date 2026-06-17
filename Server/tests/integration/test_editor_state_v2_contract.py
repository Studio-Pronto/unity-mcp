import time

import pytest

from services.registry import get_registered_resources

from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_editor_state_v2_is_registered_and_has_contract_fields(monkeypatch):
    """
    Canonical editor state resource should be `mcpforunity://editor/state` and conform to v2 contract fields.
    """
    # Import module to ensure it registers its decorator without disturbing global registry state.
    import services.resources.editor_state  # noqa: F401

    resources = get_registered_resources()

    state_res = next(
        (r for r in resources if r.get("uri") == "mcpforunity://editor/state"),
        None,
    )
    assert state_res is not None, (
        "Expected canonical editor state resource `mcpforunity://editor/state` to be registered. "
        "This is required so clients can poll readiness/staleness and avoid tool loops."
    )

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        # Minimal stub payload for v2 resource tests. The server layer should enrich with staleness/advice.
        assert command_type == "get_editor_state"
        return {
            "success": True,
            "data": {
                "schema_version": "unity-mcp/editor_state@2",
                "observed_at_unix_ms": 1730000000000,
                "sequence": 1,
                "compilation": {"is_compiling": False, "is_domain_reload_pending": False},
                "tests": {"is_running": False},
            },
        }

    # Patch transport so the resource can be invoked without Unity running.
    import transport.unity_transport as unity_transport
    monkeypatch.setattr(unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    result = await state_res["func"](DummyContext())
    payload = result.model_dump() if hasattr(result, "model_dump") else result
    assert isinstance(payload, dict)

    # Contract assertions (top-level)
    assert payload.get("success") is True
    data = payload.get("data")
    assert isinstance(data, dict)
    assert data.get("schema_version") == "unity-mcp/editor_state@2"
    assert "observed_at_unix_ms" in data
    assert "sequence" in data
    assert "advice" in data
    assert "staleness" in data


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "play_mode, activity_phase",
    [
        ({"is_playing": True, "is_paused": False, "is_changing": False}, "playing"),
        ({"is_playing": False, "is_paused": False, "is_changing": True}, "playmode_transition"),
    ],
    ids=["settled_play", "play_transition"],
)
async def test_editor_state_play_mode_never_gates_tools(monkeypatch, play_mode, activity_phase):
    """
    Play-mode state must never gate tools. A settled Play session ("playing", is_changing=False) and an
    active enter/exit transition ("playmode_transition", is_changing=True) both leave
    advice.ready_for_tools == True with no play-related blocking reason. The advice logic deliberately
    ignores play state, so the snapshot can no longer contradict itself (is_changing is true only during
    the brief transition window). The play_mode/activity fields round-trip through the pydantic model.
    """
    import services.resources.editor_state  # noqa: F401

    resources = get_registered_resources()
    state_res = next(
        (r for r in resources if r.get("uri") == "mcpforunity://editor/state"),
        None,
    )
    assert state_res is not None

    # Fresh timestamp so the snapshot is not flagged stale (which would itself gate tools).
    observed = int(time.time() * 1000)

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        assert command_type == "get_editor_state"
        return {
            "success": True,
            "data": {
                "schema_version": "unity-mcp/editor_state@2",
                "observed_at_unix_ms": observed,
                "sequence": 1,
                "editor": {"play_mode": play_mode},
                "activity": {"phase": activity_phase, "since_unix_ms": observed},
                "compilation": {"is_compiling": False, "is_domain_reload_pending": False},
                "tests": {"is_running": False},
            },
        }

    import transport.unity_transport as unity_transport
    monkeypatch.setattr(unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    result = await state_res["func"](DummyContext())
    payload = result.model_dump() if hasattr(result, "model_dump") else result
    data = payload["data"]

    # Fields survive the pydantic round-trip unchanged.
    assert data["editor"]["play_mode"]["is_changing"] is play_mode["is_changing"]
    assert data["activity"]["phase"] == activity_phase

    advice = data["advice"]
    assert advice["ready_for_tools"] is True, (
        f"play state '{activity_phase}' must not gate tools; blocking={advice['blocking_reasons']}"
    )
    assert "playmode_transition" not in advice["blocking_reasons"]
    assert "playing" not in advice["blocking_reasons"]


