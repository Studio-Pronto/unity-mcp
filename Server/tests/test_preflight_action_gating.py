"""Static AST regression test for preflight refresh_if_dirty gating.

Read-capable MCP tools must NOT pass `refresh_if_dirty=True` as a literal kwarg
to `preflight()`. Doing so makes any read-only call trigger
`AssetDatabase.Refresh(ForceUpdate | ForceSynchronousImport)` and
`CompilationPipeline.RequestScriptCompilation()` server-side whenever the
project filesystem mtime has advanced — recompiling Unity on read-only
operations.

A behavior test would be unreliable: `preflight._in_pytest()` no-ops the entire
preflight under pytest, and existing tests stub it via
`monkeypatch.setattr(module, "preflight", noop)`. So we check the property we
care about directly: the source must compute the boolean rather than passing
literal True.
"""
from __future__ import annotations

import ast
import inspect

import pytest

import services.tools.find_gameobjects as find_gameobjects_mod
import services.tools.manage_asset as manage_asset_mod
import services.tools.manage_components as manage_components_mod
import services.tools.manage_gameobject as manage_gameobject_mod
import services.tools.manage_prefabs as manage_prefabs_mod
import services.tools.manage_scene as manage_scene_mod
import services.tools.manage_texture as manage_texture_mod
import services.tools.run_tests as run_tests_mod


# Tools that have at least one read-capable action. None of them may pass
# `refresh_if_dirty=True` as a literal — the boolean must be computed from
# the action so reads don't trigger a Unity recompile.
TOOLS_REQUIRING_GATED_REFRESH = [
    (find_gameobjects_mod, "find_gameobjects"),
    (manage_gameobject_mod, "manage_gameobject"),
    (manage_prefabs_mod, "manage_prefabs"),
    (manage_components_mod, "manage_components"),
    (manage_texture_mod, "manage_texture"),
    (manage_asset_mod, "manage_asset"),
    (manage_scene_mod, "manage_scene"),
]


def _find_preflight_calls(module) -> list[ast.Call]:
    src = inspect.getsource(module)
    tree = ast.parse(src)
    calls = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        func = node.func
        name = (
            func.attr if isinstance(func, ast.Attribute)
            else func.id if isinstance(func, ast.Name)
            else None
        )
        if name == "preflight":
            calls.append(node)
    return calls


@pytest.mark.parametrize("module,name", TOOLS_REQUIRING_GATED_REFRESH)
def test_preflight_refresh_if_dirty_is_not_literal_true(module, name):
    calls = _find_preflight_calls(module)
    assert calls, f"{name}: expected at least one preflight() call"
    for call in calls:
        for kw in call.keywords:
            if kw.arg != "refresh_if_dirty":
                continue
            if isinstance(kw.value, ast.Constant) and kw.value.value is True:
                pytest.fail(
                    f"{name}: preflight() at line {call.lineno} passes "
                    f"refresh_if_dirty=True as a literal. Compute it from "
                    f"the action and pass refresh_if_dirty=is_write instead — "
                    f"otherwise read-only calls will trigger a Unity recompile."
                )


def test_run_tests_explicitly_keeps_refresh_true():
    """run_tests deliberately keeps refresh_if_dirty=True — fresh compile is
    required for test runs. Pin that decision so the audit doesn't silently
    regress it.
    """
    calls = _find_preflight_calls(run_tests_mod)
    assert calls, "run_tests: expected at least one preflight() call"
    has_literal_true = any(
        any(
            kw.arg == "refresh_if_dirty"
            and isinstance(kw.value, ast.Constant)
            and kw.value.value is True
            for kw in call.keywords
        )
        for call in calls
    )
    assert has_literal_true, (
        "run_tests must keep refresh_if_dirty=True (test runs need a fresh "
        "compile to pick up source edits)"
    )
