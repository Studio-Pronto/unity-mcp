from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, coerce_int


@mcp_for_unity_tool(
    description="Performs prefab operations (open_stage, close_stage, save_open_stage, create_from_gameobject, get_hierarchy).",
    annotations=ToolAnnotations(
        title="Manage Prefabs",
        destructiveHint=True,
    ),
)
async def manage_prefabs(
    ctx: Context,
    action: Annotated[Literal["open_stage", "close_stage", "save_open_stage", "create_from_gameobject", "get_hierarchy"], "Perform prefab operations."],
    prefab_path: Annotated[str,
                           "Prefab asset path relative to Assets e.g. Assets/Prefabs/favorite.prefab"] | None = None,
    mode: Annotated[str,
                    "Optional prefab stage mode (only 'InIsolation' is currently supported)"] | None = None,
    save_before_close: Annotated[bool,
                                 "When true, `close_stage` will save the prefab before exiting the stage."] | None = None,
    target: Annotated[str,
                      "Scene GameObject name required for create_from_gameobject"] | None = None,
    allow_overwrite: Annotated[bool,
                               "Allow replacing an existing prefab at the same path"] | None = None,
    search_inactive: Annotated[bool,
                               "Include inactive objects when resolving the target name"] | None = None,
    parent: Annotated[str,
                      "Parent GameObject name/path/instanceID for get_hierarchy. Omit to get prefab root."] | None = None,
    page_size: Annotated[int | float | str,
                         "Number of items per page for get_hierarchy (default: 50, max: 500)"] | None = None,
    cursor: Annotated[int | str,
                      "Pagination cursor for get_hierarchy (start index)"] | None = None,
    include_transform: Annotated[bool | str,
                                 "Include transform data in get_hierarchy results"] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {"action": action}

        if prefab_path:
            params["prefabPath"] = prefab_path
        if mode:
            params["mode"] = mode
        save_before_close_val = coerce_bool(save_before_close)
        if save_before_close_val is not None:
            params["saveBeforeClose"] = save_before_close_val
        if target:
            params["target"] = target
        allow_overwrite_val = coerce_bool(allow_overwrite)
        if allow_overwrite_val is not None:
            params["allowOverwrite"] = allow_overwrite_val
        search_inactive_val = coerce_bool(search_inactive)
        if search_inactive_val is not None:
            params["searchInactive"] = search_inactive_val
        if parent:
            params["parent"] = parent
        coerced_page_size = coerce_int(page_size, default=None)
        if coerced_page_size is not None:
            params["pageSize"] = coerced_page_size
        coerced_cursor = coerce_int(cursor, default=None)
        if coerced_cursor is not None:
            params["cursor"] = coerced_cursor
        include_transform_val = coerce_bool(include_transform)
        if include_transform_val is not None:
            params["includeTransform"] = include_transform_val
        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_prefabs", params)

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Prefab operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as exc:
        return {"success": False, "message": f"Python error managing prefabs: {exc}"}
