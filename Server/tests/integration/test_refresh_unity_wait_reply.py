import pytest

from .test_helpers import DummyContext

INSTANCE = "UnityMCPTests@cc8756d4cce0805a"
POLL_HINT = (
    "If Unity enters compilation/domain reload, poll the mcpforunity://editor/state resource "
    "until data.advice.ready_for_tools is true."
)

# What the plugin answers on Unity 6 for compile="request": it cannot wait across the
# domain reload, so it replies once the compile has started.
UNITY6_COMPILE_REPLY = {
    "success": True,
    "message": "Refresh requested.",
    "data": {
        "refresh_triggered": True,
        "compile_requested": True,
        "compile_started": True,
        "resulting_state": "compiling",
        "hint": POLL_HINT,
    },
}


@pytest.fixture
def refresh(monkeypatch):
    import services.tools.refresh_unity as refresh_mod

    waits = []

    async def fake_send_with_unity_instance(unity_instance, command_type, params, **kwargs):
        assert command_type == "refresh_unity"
        return UNITY6_COMPILE_REPLY

    async def fake_wait_for_editor_ready(ctx, timeout_s=30.0):
        waits.append(timeout_s)
        return True, 0.4

    monkeypatch.setattr(refresh_mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    monkeypatch.setattr(refresh_mod, "wait_for_editor_ready", fake_wait_for_editor_ready)

    async def call(**kwargs):
        ctx = DummyContext()
        await ctx.set_state("unity_instance", INSTANCE)
        resp = await refresh_mod.refresh_unity(ctx, compile="request", **kwargs)
        return resp.model_dump() if hasattr(resp, "model_dump") else resp

    call.waits = waits
    return call


@pytest.mark.asyncio
async def test_compile_reply_reports_ready_once_the_server_wait_confirms_it(refresh):
    """#45: the plugin's mid-compile state and poll hint must not outlive the server's own wait."""
    payload = await refresh(wait_for_ready=True)

    assert refresh.waits, "the server-side readiness wait should have run"
    assert payload["success"] is True
    data = payload["data"]
    assert data["resulting_state"] == "idle"
    assert data["hint"] == "Unity refresh completed; editor should be ready."
    assert data["compile_started"] is True
    assert data["refresh_triggered"] is True


@pytest.mark.asyncio
async def test_no_wait_reply_keeps_the_plugin_state_and_poll_hint(refresh):
    payload = await refresh(wait_for_ready=False)

    assert not refresh.waits
    assert payload["data"]["resulting_state"] == "compiling"
    assert payload["data"]["hint"] == POLL_HINT
