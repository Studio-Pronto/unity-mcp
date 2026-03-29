"""Project settings management — quality, physics, time, editor settings."""

from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "get",
    "set",
    "list",
    "list_categories",
]


async def _send_command(
    ctx: Context,
    params_dict: dict[str, Any],
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_project_settings", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}


@mcp_for_unity_tool(
    group="core",
    description=(
        "Read, write, and discover Unity project settings across categories: "
        "quality (QualitySettings), physics, physics2d, time, editor (EditorSettings). "
        "Supports any static property via reflection, including snake_case names. "
        "For PlayerSettings, use manage_build(action='settings') instead. "
        "Actions: get, set, list, list_categories."
    ),
    annotations=ToolAnnotations(
        title="Manage Project Settings",
        destructiveHint=True,
        readOnlyHint=False,
    ),
)
async def manage_project_settings(
    ctx: Context,
    action: Annotated[str, "Action: get, set, list, list_categories"],
    category: Annotated[Optional[str], "Settings category: quality, physics, physics2d, time, editor"] = None,
    property: Annotated[Optional[str], "Property name (snake_case or camelCase, e.g. shadow_distance or shadowDistance)"] = None,
    value: Annotated[Optional[str], "Value to set. Scalars as strings, vectors as JSON arrays (e.g. '[0, -9.81, 0]')"] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    params_dict: dict[str, Any] = {"action": action_lower}

    param_map: dict[str, Any] = {
        "category": category,
        "property": property,
        "value": value,
    }

    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    return await _send_command(ctx, params_dict)
