"""
MCP Resources for Unity animation tooling.

- mcpforunity://animation-api — schema/conventions doc for the manage_animation tool
- mcpforunity://animation/controller/{encoded_path} — controller graph (states, transitions, parameters, layers)
"""
from urllib.parse import unquote

from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _normalize_response(response: dict | object) -> MCPResponse:
    if isinstance(response, dict):
        return MCPResponse(**response)
    return response


@mcp_for_unity_resource(
    uri="mcpforunity://animation-api",
    name="animation_api",
    description=(
        "Schema and conventions for the manage_animation tool. Read this BEFORE editing "
        "AnimatorControllers — it lists per-action `properties` keys, condition-mode rules, "
        "AnyState aliases, path notation, and identity-preservation guidance.\n\n"
        "URI: mcpforunity://animation-api"
    ),
)
async def get_animation_api_docs(_ctx: Context) -> MCPResponse:
    docs = {
        "overview": (
            "manage_animation is a single tool with action prefixes: animator_* (runtime), "
            "controller_* (asset CRUD), clip_* (AnimationClip). Action-specific keys go in `properties` "
            "(snake_case and camelCase both accepted)."
        ),
        "workflow": [
            "1. Read the controller graph: mcpforunity://animation/controller/{encoded_path}",
            "2. Edit via controller_* actions (prefer modify_state/modify_parameter/rename_layer over remove+add)",
            "3. Verify by re-reading the controller graph",
        ],
        "path_encoding": {
            "note": "Controller paths must be URL-encoded when used in resource URIs",
            "example": "Assets/Animators/Player.controller -> Assets%2FAnimators%2FPlayer.controller",
        },
        "actions": {
            "controller_add_state": {
                "required": ["controller_path", "properties.stateName"],
                "optional": {
                    "layerIndex": "default 0",
                    "clipPath": "Assets/.../X.anim or .fbx",
                    "clipName": "sub-clip name when clipPath is an FBX",
                    "speed": "float, default 1",
                    "tag": "string",
                    "isDefault": "bool — set as the layer's defaultState",
                    "writeDefaultValues": "bool",
                    "iKOnFeet": "bool",
                    "mirror": "bool",
                    "cycleOffset": "float",
                    "speedParameter": "name of an existing Float param to drive speed",
                    "cycleOffsetParameter": "Float param",
                    "mirrorParameter": "Bool param",
                    "timeParameter": "Float param",
                },
                "notes": "stateName supports paths: 'Sub/Inner' creates 'Inner' inside sub-state machine 'Sub'. Use '/' as separator.",
                "example": {
                    "action": "controller_add_state",
                    "controller_path": "Assets/Animators/Player.controller",
                    "properties": {"stateName": "Locomotion/Walk", "clipPath": "Assets/Anims/Walk.anim", "isDefault": True},
                },
            },
            "controller_modify_state": {
                "required": ["controller_path", "properties.stateName"],
                "optional": {
                    "newName": "rename in place — preserves AnimatorState fileID; transitions survive",
                    "tag": "string",
                    "speed": "float",
                    "writeDefaultValues": "bool",
                    "iKOnFeet": "bool",
                    "mirror": "bool",
                    "cycleOffset": "float",
                    "speedParameter": "string",
                    "cycleOffsetParameter": "string",
                    "mirrorParameter": "string",
                    "timeParameter": "string",
                },
                "notes": "Prefer modify_state with newName over remove+add for renames — keeps external refs (Timeline, animation events) intact.",
            },
            "controller_remove_state": {
                "required": ["controller_path", "properties.stateName"],
                "behavior": (
                    "Cleans inbound transitions automatically (counts surfaced in data.removedTransitions). "
                    "Reassigns parent state machine's defaultState if it matched the removed state."
                ),
            },
            "controller_add_transition": {
                "required": ["controller_path", "properties.fromState", "properties.toState"],
                "optional": {
                    "layerIndex": "default 0",
                    "hasExitTime": "bool, default true",
                    "exitTime": "float, default 0.75",
                    "duration": "float, default 0.25",
                    "offset": "float",
                    "hasFixedDuration": "bool",
                    "interruptionSource": "none | source | destination | sourceThenDestination | destinationThenSource",
                    "orderedInterruption": "bool",
                    "canTransitionToSelf": "bool",
                    "conditions": "[{parameter, mode, threshold}] — see condition_modes",
                },
                "notes": (
                    "fromState may be 'AnyState' (also 'Any', 'Any State'). toState may be a state OR a sub-state machine name. "
                    "If a name resolves as both, the call errors and you must disambiguate with a path."
                ),
            },
            "controller_modify_transition": {
                "required": ["controller_path", "properties.fromState", "properties.toState"],
                "optional": {"transitionIndex": "default 0 — disambiguate when multiple transitions match"},
                "notes": "All transition properties accepted. `conditions` REPLACES the entire array.",
            },
            "controller_add_parameter": {
                "required": ["controller_path", "properties.parameterName", "properties.parameterType"],
                "parameterType": ["float", "int", "bool", "trigger"],
                "optional": {"defaultValue": "matches type"},
            },
            "controller_modify_parameter": {
                "required": ["controller_path", "properties.parameterName"],
                "optional": {
                    "newName": "rename — also rewrites all condition references AND state-level bindings (speed/cycleOffset/mirror/time)",
                    "parameterType": "change type",
                    "defaultValue": "change default",
                },
                "notes": "Prefer over remove+add: rename is reference-preserving.",
            },
            "controller_remove_parameter": {
                "required": ["controller_path", "properties.parameterName"],
                "optional": {"force": "bool — if false (default), errors when conditions/states reference it. If true, strips refs and surfaces count in data.warnings."},
            },
            "controller_add_layer": {
                "required": ["controller_path", "properties.layerName"],
                "optional": {"weight": "float, default 1", "blendingMode": "override (default) | additive"},
            },
            "controller_rename_layer": {
                "required": ["controller_path", "properties.newName", "properties.layerIndex OR properties.layerName"],
            },
            "controller_set_layer_weight": {
                "required": ["controller_path", "properties.weight", "properties.layerIndex OR properties.layerName"],
            },
            "controller_add_sub_state_machine": {
                "required": ["controller_path", "properties.name"],
                "optional": {"parentPath": "place inside this sub-state machine (default root)", "position": "[x,y,z]"},
            },
            "controller_modify_sub_state_machine": {
                "required": ["controller_path", "properties.name"],
                "optional": {"newName": "rename", "defaultState": "set defaultState of this SSM", "position": "[x,y,z]"},
            },
            "controller_add_entry_transition": {
                "required": ["controller_path", "properties.toState"],
                "optional": {"stateMachinePath": "default root", "conditions": "[{parameter, mode, threshold}]"},
            },
        },
        "condition_modes": {
            "by_parameter_type": {
                "Bool": ["if", "ifNot"],
                "Trigger": ["if", "ifNot"],
                "Int": ["equals", "notEqual", "greater", "less"],
                "Float": ["greater", "less"],
            },
            "aliases": {
                "if": ["true"],
                "ifNot": ["if_not", "ifnot", "false"],
                "notEqual": ["not_equal", "notequal"],
            },
            "validation": "Mode/parameter-type mismatch returns an error at validation time.",
        },
        "any_state": {
            "fromState_aliases": ["AnyState", "Any", "Any State"],
            "case_sensitive": False,
            "notes": "AnyState transitions attach to the layer's root state machine only.",
        },
        "paths": {
            "canonical_separator": "/",
            "dot_alternative": "'.' is accepted on lookups (translated to '/' on miss). For state CREATION always use '/'.",
            "examples": {
                "top_level_state": "Walk",
                "nested_state": "Locomotion/Walk",
                "sub_state_machine": "Combat/Melee",
            },
        },
        "identity_preservation": {
            "rule": "Prefer modify_state / modify_parameter / rename_layer over remove+add.",
            "why": (
                "Remove+add destroys the AnimatorState/Parameter sub-asset and creates a new one with a fresh fileID. "
                "External references (Timeline tracks, animation events, AnimatorOverrideController bindings) silently break."
            ),
        },
        "related_resources": {
            "mcpforunity://animation/controller/{encoded_path}": "Read the controller graph (states, transitions, parameters, layers) before editing.",
        },
    }
    return MCPResponse(success=True, data=docs)


@mcp_for_unity_resource(
    uri="mcpforunity://animation/controller/{encoded_path}",
    name="animation_controller",
    description=(
        "Get the AnimatorController graph (states, transitions, parameters, layers) for a "
        "URL-encoded controller asset path. Use this BEFORE making edits to understand the "
        "current shape and avoid name collisions.\n\n"
        "URI: mcpforunity://animation/controller/{encoded_path}"
    ),
)
async def get_animation_controller(ctx: Context, encoded_path: str) -> MCPResponse:
    unity_instance = await get_unity_instance_from_context(ctx)
    controller_path = unquote(encoded_path)

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_animation",
        {
            "action": "controller_get_info",
            "controllerPath": controller_path,
        },
    )

    return _normalize_response(response)
