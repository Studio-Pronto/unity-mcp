from typing import Annotated, Any, Literal, get_args

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

AnimationAction = Literal[
    # Animator (runtime)
    "animator_get_info", "animator_get_parameter",
    "animator_play", "animator_crossfade",
    "animator_set_parameter", "animator_set_speed", "animator_set_enabled",
    # Controller (asset)
    "controller_create", "controller_add_state", "controller_add_transition",
    "controller_add_parameter", "controller_get_info", "controller_assign",
    "controller_set_state_motion", "controller_remove_state",
    "controller_remove_transition", "controller_remove_parameter",
    "controller_modify_state", "controller_modify_transition",
    "controller_add_sub_state_machine", "controller_remove_sub_state_machine",
    "controller_modify_sub_state_machine",
    "controller_add_entry_transition", "controller_remove_entry_transition",
    "controller_add_layer", "controller_remove_layer", "controller_set_layer_weight",
    "controller_create_blend_tree_1d", "controller_create_blend_tree_2d",
    "controller_add_blend_tree_child", "controller_add_blend_tree_child_tree",
    # Clip (asset)
    "clip_create", "clip_get_info",
    "clip_add_curve", "clip_set_curve", "clip_set_vector_curve",
    "clip_create_preset", "clip_assign",
    "clip_add_event", "clip_remove_event",
]

ALL_ACTIONS: list[str] = list(get_args(AnimationAction))

ANIMATOR_ACTIONS = [a for a in ALL_ACTIONS if a.startswith("animator_")]
CONTROLLER_ACTIONS = [a for a in ALL_ACTIONS if a.startswith("controller_")]
CLIP_ACTIONS = [a for a in ALL_ACTIONS if a.startswith("clip_")]


@mcp_for_unity_tool(
    group="animation",
    description=(
        "Manage Unity animation: Animator control, AnimatorController editing, and AnimationClip creation.\n\n"
        "ANIMATOR (runtime): animator_get_info, animator_get_parameter, animator_play, "
        "animator_crossfade, animator_set_parameter, animator_set_speed, animator_set_enabled\n"
        "CONTROLLER (asset): controller_create, controller_get_info, controller_assign, "
        "controller_add_state, controller_remove_state, controller_modify_state, "
        "controller_set_state_motion, controller_add_transition, controller_remove_transition, "
        "controller_modify_transition, controller_add_parameter, controller_remove_parameter, "
        "controller_add_sub_state_machine, controller_remove_sub_state_machine, "
        "controller_modify_sub_state_machine, "
        "controller_add_entry_transition, controller_remove_entry_transition, "
        "controller_add_layer, controller_remove_layer, controller_set_layer_weight, "
        "controller_create_blend_tree_1d, controller_create_blend_tree_2d, "
        "controller_add_blend_tree_child, controller_add_blend_tree_child_tree\n"
        "CLIP (asset): clip_create, clip_get_info, clip_add_curve, clip_set_curve, "
        "clip_set_vector_curve, clip_create_preset, clip_assign, clip_add_event, clip_remove_event\n\n"
        "States in sub-state machines use path notation: 'SubMachine/StateName'.\n"
        "Action-specific parameters go in `properties` (keys match ManageAnimation.cs)."
    ),
    annotations=ToolAnnotations(
        title="Manage Animation",
        destructiveHint=True,
    ),
)
async def manage_animation(
    ctx: Context,
    action: Annotated[AnimationAction, "Action to perform (prefix: animator_, controller_, clip_)."],
    target: Annotated[str | None, "Target GameObject (name/path/id)."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path", "by_tag", "by_layer"] | None,
        "How to find the target GameObject.",
    ] = None,
    clip_path: Annotated[str | None, "Asset path for AnimationClip (e.g. 'Assets/Animations/Walk.anim')."] = None,
    clip_name: Annotated[str | None, "Name of a specific clip within a multi-clip asset (e.g. FBX). Required when clip_path points to an FBX with multiple animations."] = None,
    controller_path: Annotated[str | None, "Asset path for AnimatorController (e.g. 'Assets/Animators/Player.controller')."] = None,
    properties: Annotated[
        dict[str, Any] | str | None,
        "Action-specific parameters (dict or JSON string).",
    ] = None,
) -> dict[str, Any]:
    """Unified animation management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        prefix = action_normalized.split("_")[0] + "_" if "_" in action_normalized else ""
        available_by_prefix = {
            "animator_": ANIMATOR_ACTIONS,
            "controller_": CONTROLLER_ACTIONS,
            "clip_": CLIP_ACTIONS,
        }
        suggestions = available_by_prefix.get(prefix, [])
        if suggestions:
            return {
                "success": False,
                "message": f"Unknown action '{action}'. Available {prefix}* actions: {', '.join(suggestions)}",
            }
        else:
            return {
                "success": False,
                "message": (
                    f"Unknown action '{action}'. Use prefixes: "
                    "animator_* (Animator control), controller_* (AnimatorController CRUD), "
                    "clip_* (AnimationClip operations)."
                ),
            }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}
    if properties is not None:
        params_dict["properties"] = properties
    if target is not None:
        params_dict["target"] = target
    if search_method is not None:
        params_dict["searchMethod"] = search_method
    if clip_path is not None:
        params_dict["clipPath"] = clip_path
    if clip_name is not None:
        params_dict["clipName"] = clip_name
    if controller_path is not None:
        params_dict["controllerPath"] = controller_path

    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_animation",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
