from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

SAMPLE_ACTIONS = [
    "sample_start", "sample_stop", "sample_read", "sample_compare", "sample_list",
]

COUNTER_ACTIONS = [
    "counter_read", "counter_list",
]

FRAME_TIME_ACTIONS = [
    "frame_time_get",
]

HIERARCHY_ACTIONS = [
    "hotspots_get", "hotspots_detail", "gc_track", "threads_list",
]

MEMORY_ACTIONS = [
    "memory_snapshot", "memory_compare", "memory_objects", "memory_type_summary",
    "memory_fragmentation",
]

CAPTURE_ACTIONS = [
    "capture_start", "capture_stop", "capture_status", "capture_load",
]

CONTROL_ACTIONS = [
    "profiler_enable", "profiler_disable", "deep_profiling_set", "area_set",
    "profiler_status", "callstacks_set",
]

PHYSICS_ACTIONS = [
    "physics_get",
]

ALL_ACTIONS = (
    ["ping"] + SAMPLE_ACTIONS + COUNTER_ACTIONS + FRAME_TIME_ACTIONS
    + HIERARCHY_ACTIONS + MEMORY_ACTIONS + CAPTURE_ACTIONS + CONTROL_ACTIONS
    + PHYSICS_ACTIONS
)


@mcp_for_unity_tool(
    group="core",
    description=(
        "Manage Unity Profiler: counter sampling, frame time analysis, CPU hotspots, "
        "memory profiling, and profiler capture management. "
        "Use ping to check profiler state and play mode.\n\n"
        "COUNTER SAMPLING (ProfilerRecorder, works without Profiler window):\n"
        "- sample_start: Start recording counters by category or name (label required)\n"
        "- sample_stop: Stop and dispose recorders (by label or all)\n"
        "- sample_read: Read accumulated stats (mean/p95/p99) from a session\n"
        "- sample_compare: Compare two sessions with delta/percentage\n"
        "- sample_list: List active sampling sessions\n"
        "- counter_read: One-shot read of specific counters\n"
        "- counter_list: List available profiler counters by category\n\n"
        "FRAME TIME:\n"
        "- frame_time_get: Self-contained blocking call. Returns main thread, "
        "render thread, GPU breakdown with bottleneck classification\n\n"
        "CPU HOTSPOTS (HierarchyFrameDataView, auto-enables profiler):\n"
        "- hotspots_get: Top-N expensive markers by self time\n"
        "- hotspots_detail: Drill into a specific marker's callers/callees (includes callstack when available)\n"
        "- gc_track: GC allocation tracking with per-marker attribution (includes callstacks when available)\n"
        "- threads_list: List all profiled threads (main, render, workers)\n\n"
        "MEMORY (instant, no profiler needed):\n"
        "- memory_snapshot: Labeled memory snapshot (total/mono/graphics/GC)\n"
        "- memory_compare: Compare two labeled snapshots\n"
        "- memory_objects: Per-object memory by type/name (paged)\n"
        "- memory_type_summary: Memory grouped by object type\n"
        "- memory_fragmentation: Heap fragmentation by size bucket\n\n"
        "CAPTURE (.raw files):\n"
        "- capture_start: Start recording to .raw profiler capture file\n"
        "- capture_stop: Stop recording, return file path\n"
        "- capture_status: Current profiler recording state\n"
        "- capture_load: Load .raw profiler capture for offline analysis\n\n"
        "CONTROL:\n"
        "- profiler_enable/disable: Toggle profiler recording\n"
        "- deep_profiling_set: Toggle deep profiling (significant overhead)\n"
        "- area_set: Enable/disable specific profiler areas\n"
        "- profiler_status: Full profiler configuration state (areas, callstacks, buffer)\n"
        "- callstacks_set: Toggle allocation callstack recording (significant overhead)\n\n"
        "PHYSICS:\n"
        "- physics_get: Self-contained physics counter snapshot"
    ),
    annotations=ToolAnnotations(title="Manage Profiler"),
)
async def manage_profiler(
    ctx: Context,
    action: Annotated[str, "The profiler action to perform."],
    # Counter sampling
    label: Annotated[Optional[str], "Session label for sampling or memory snapshots."] = None,
    counters: Annotated[Optional[str], "Category name (e.g. 'render', 'physics') or JSON array of counter names."] = None,
    capacity: Annotated[Optional[int], "Ring buffer capacity (frames). Default 300."] = None,
    last_n: Annotated[Optional[int], "Read only last N frames from session."] = None,
    # Comparison
    label_a: Annotated[Optional[str], "First session label for comparison."] = None,
    label_b: Annotated[Optional[str], "Second session label for comparison."] = None,
    threshold_pct: Annotated[Optional[float], "Min % change to report in comparison. Default 5.0."] = None,
    # Frame time / hotspots / physics
    frames: Annotated[Optional[int], "Number of frames to collect/analyze. Default 120."] = None,
    top_n: Annotated[Optional[int], "Number of top results to return. Default 20."] = None,
    min_ms: Annotated[Optional[float], "Minimum self time (ms) to include. Default 0.1."] = None,
    thread: Annotated[Optional[str], "Thread to analyze: 'main', 'render', or 'all'. Default 'main'."] = None,
    marker_name: Annotated[Optional[str], "Specific marker name for hotspots_detail."] = None,
    # Memory
    target: Annotated[Optional[str], "Filter by object name or instance ID."] = None,
    object_type: Annotated[Optional[str], "Filter by Unity object type name."] = None,
    min_size_kb: Annotated[Optional[float], "Minimum object size in KB."] = None,
    min_total_mb: Annotated[Optional[float], "Minimum total MB per type for memory_type_summary."] = None,
    max_objects: Annotated[Optional[int], "Safety cap for object iteration. Default 10000."] = None,
    page_size: Annotated[Optional[int], "Results per page."] = None,
    cursor: Annotated[Optional[str], "Pagination cursor."] = None,
    # Counter discovery
    category: Annotated[Optional[str], "Filter counters by category for counter_list."] = None,
    search: Annotated[Optional[str], "Filter counters by name substring for counter_list."] = None,
    # Capture
    output_path: Annotated[Optional[str], "File path for .raw capture output."] = None,
    input_path: Annotated[Optional[str], "File path of .raw capture to load."] = None,
    keep_profiler_enabled: Annotated[Optional[bool], "Keep profiler on after capture_stop."] = None,
    # Control
    enabled: Annotated[Optional[bool], "Enable/disable toggle for deep_profiling_set, callstacks_set, and area_set."] = None,
    area: Annotated[Optional[str], "Profiler area name for area_set."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_lower}

    param_map = {
        "label": label, "counters": counters, "capacity": capacity,
        "last_n": last_n, "label_a": label_a, "label_b": label_b,
        "threshold_pct": threshold_pct, "frames": frames, "top_n": top_n,
        "min_ms": min_ms, "thread": thread, "marker_name": marker_name,
        "target": target, "type": object_type, "min_size_kb": min_size_kb,
        "min_total_mb": min_total_mb, "max_objects": max_objects,
        "page_size": page_size, "cursor": cursor,
        "category": category, "search": search, "output_path": output_path,
        "input_path": input_path, "keep_profiler_enabled": keep_profiler_enabled,
        "enabled": enabled,
        "area": area,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_profiler", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
