import click
from cli.utils.connection import handle_unity_errors, run_command, get_config
from cli.utils.output import format_output


@click.group("profiler")
def profiler():
    """Profiler commands for performance analysis."""
    pass


# --- Counter sampling ---

@profiler.command("sample-start")
@click.option("--label", "-l", required=True, help="Session label.")
@click.option("--counters", "-c", required=True, help="Category name or counter names.")
@click.option("--capacity", type=int, default=None, help="Ring buffer capacity (frames).")
@handle_unity_errors
def sample_start(label, counters, capacity):
    """Start recording profiler counters."""
    config = get_config()
    params = {"action": "sample_start", "label": label, "counters": counters}
    if capacity is not None:
        params["capacity"] = capacity
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("sample-stop")
@click.option("--label", "-l", default=None, help="Session label (omit to stop all).")
@handle_unity_errors
def sample_stop(label):
    """Stop sampling session(s)."""
    config = get_config()
    params = {"action": "sample_stop"}
    if label is not None:
        params["label"] = label
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("sample-read")
@click.option("--label", "-l", required=True, help="Session label to read.")
@click.option("--last-n", type=int, default=None, help="Read only last N frames.")
@handle_unity_errors
def sample_read(label, last_n):
    """Read accumulated stats from a sampling session."""
    config = get_config()
    params = {"action": "sample_read", "label": label}
    if last_n is not None:
        params["last_n"] = last_n
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("sample-compare")
@click.option("--label-a", "-a", required=True, help="First session label.")
@click.option("--label-b", "-b", required=True, help="Second session label.")
@click.option("--threshold", type=float, default=None, help="Min % change to report.")
@handle_unity_errors
def sample_compare(label_a, label_b, threshold):
    """Compare two sampling sessions."""
    config = get_config()
    params = {"action": "sample_compare", "label_a": label_a, "label_b": label_b}
    if threshold is not None:
        params["threshold_pct"] = threshold
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("sample-list")
@handle_unity_errors
def sample_list():
    """List active sampling sessions."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "sample_list"}, config)
    click.echo(format_output(result, config.format))


# --- Frame time ---

@profiler.command("frame-time")
@click.option("--frames", "-f", type=int, default=None, help="Frames to collect (default 120).")
@handle_unity_errors
def frame_time(frames):
    """Get frame time breakdown with bottleneck classification."""
    config = get_config()
    params = {"action": "frame_time_get"}
    if frames is not None:
        params["frames"] = frames
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


# --- Hotspots ---

@profiler.command("hotspots")
@click.option("--top-n", "-n", type=int, default=None, help="Number of results (default 20).")
@click.option("--frames", "-f", type=int, default=None, help="Frames to analyze (default 120).")
@click.option("--thread", "-t", default=None, help="Thread: main, render, or all.")
@click.option("--min-ms", type=float, default=None, help="Min self time in ms (default 0.1).")
@handle_unity_errors
def hotspots(top_n, frames, thread, min_ms):
    """Get top CPU hotspots by self time."""
    config = get_config()
    params = {"action": "hotspots_get"}
    if top_n is not None:
        params["top_n"] = top_n
    if frames is not None:
        params["frames"] = frames
    if thread is not None:
        params["thread"] = thread
    if min_ms is not None:
        params["min_ms"] = min_ms
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


# --- Memory ---

@profiler.command("memory-snapshot")
@click.option("--label", "-l", default=None, help="Label for later comparison.")
@handle_unity_errors
def memory_snapshot(label):
    """Capture memory snapshot."""
    config = get_config()
    params = {"action": "memory_snapshot"}
    if label is not None:
        params["label"] = label
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


# --- Capture ---

@profiler.command("capture-start")
@click.option("--output", "-o", default=None, help="Output path for .raw file.")
@handle_unity_errors
def capture_start(output):
    """Start profiler capture to .raw file."""
    config = get_config()
    params = {"action": "capture_start"}
    if output is not None:
        params["output_path"] = output
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("capture-stop")
@click.option("--keep-profiler-enabled", is_flag=True, default=False, help="Keep profiler recording after stopping capture.")
@handle_unity_errors
def capture_stop(keep_profiler_enabled):
    """Stop profiler capture."""
    config = get_config()
    params = {"action": "capture_stop"}
    if keep_profiler_enabled:
        params["keep_profiler_enabled"] = True
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("capture-status")
@handle_unity_errors
def capture_status():
    """Show profiler capture and recording status."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "capture_status"}, config)
    click.echo(format_output(result, config.format))


# --- Counter discovery ---

@profiler.command("counter-read")
@click.option("--counters", "-c", required=True, help="Category name or counter names.")
@handle_unity_errors
def counter_read(counters):
    """One-shot read of specific profiler counters."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "counter_read", "counters": counters}, config)
    click.echo(format_output(result, config.format))


@profiler.command("counter-list")
@click.option("--category", "-c", default=None, help="Filter by category (e.g. render, physics).")
@click.option("--search", "-s", default=None, help="Filter by name substring.")
@click.option("--page-size", type=int, default=None, help="Results per page (default 50).")
@click.option("--cursor", default=None, help="Pagination cursor.")
@handle_unity_errors
def counter_list(category, search, page_size, cursor):
    """List available profiler counters."""
    config = get_config()
    params = {"action": "counter_list"}
    if category is not None:
        params["category"] = category
    if search is not None:
        params["search"] = search
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


# --- Hierarchy detail ---

@profiler.command("hotspots-detail")
@click.option("--marker", "-m", required=True, help="Marker name to inspect.")
@click.option("--frames", "-f", type=int, default=None, help="Frames to analyze (default 120).")
@handle_unity_errors
def hotspots_detail(marker, frames):
    """Drill into a specific marker's callers, callees, and GC alloc."""
    config = get_config()
    params = {"action": "hotspots_detail", "marker_name": marker}
    if frames is not None:
        params["frames"] = frames
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("gc-track")
@click.option("--frames", "-f", type=int, default=None, help="Frames to analyze (default 180).")
@click.option("--top-n", "-n", type=int, default=None, help="Top allocators (default 15).")
@handle_unity_errors
def gc_track(frames, top_n):
    """Track GC allocations with per-marker attribution."""
    config = get_config()
    params = {"action": "gc_track"}
    if frames is not None:
        params["frames"] = frames
    if top_n is not None:
        params["top_n"] = top_n
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


# --- Memory extended ---

@profiler.command("memory-compare")
@click.option("--label-a", "-a", required=True, help="First snapshot label.")
@click.option("--label-b", "-b", required=True, help="Second snapshot label.")
@handle_unity_errors
def memory_compare(label_a, label_b):
    """Compare two labeled memory snapshots."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "memory_compare", "label_a": label_a, "label_b": label_b}, config)
    click.echo(format_output(result, config.format))


@profiler.command("memory-objects")
@click.option("--type", "object_type", default=None, help="Filter by type (e.g. Texture2D, Mesh).")
@click.option("--target", default=None, help="Filter by name substring.")
@click.option("--min-size-kb", type=float, default=None, help="Min object size in KB.")
@click.option("--max-objects", type=int, default=None, help="Safety cap on objects scanned (default 10000).")
@click.option("--page-size", type=int, default=None, help="Results per page (default 20).")
@click.option("--cursor", default=None, help="Pagination cursor.")
@handle_unity_errors
def memory_objects(object_type, target, min_size_kb, max_objects, page_size, cursor):
    """List Unity objects by memory size (paged)."""
    config = get_config()
    params = {"action": "memory_objects"}
    if object_type is not None:
        params["type"] = object_type
    if target is not None:
        params["target"] = target
    if min_size_kb is not None:
        params["min_size_kb"] = min_size_kb
    if max_objects is not None:
        params["max_objects"] = max_objects
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("memory-type-summary")
@click.option("--min-total-mb", type=float, default=None, help="Min total MB per type (default 1.0).")
@click.option("--max-objects", type=int, default=None, help="Safety cap on objects scanned.")
@handle_unity_errors
def memory_type_summary(min_total_mb, max_objects):
    """Memory grouped by object type."""
    config = get_config()
    params = {"action": "memory_type_summary"}
    if min_total_mb is not None:
        params["min_total_mb"] = min_total_mb
    if max_objects is not None:
        params["max_objects"] = max_objects
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


# --- Profiler control ---

@profiler.command("profiler-enable")
@handle_unity_errors
def profiler_enable():
    """Enable profiler recording."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "profiler_enable"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("profiler-disable")
@handle_unity_errors
def profiler_disable():
    """Disable profiler recording."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "profiler_disable"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("deep-profiling")
@click.option("--enabled/--disabled", required=True, help="Enable or disable deep profiling.")
@handle_unity_errors
def deep_profiling(enabled):
    """Toggle deep profiling (significant overhead)."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "deep_profiling_set", "enabled": enabled}, config)
    click.echo(format_output(result, config.format))


@profiler.command("area-set")
@click.option("--area", "-a", required=True, help="Profiler area (CPU, GPU, Rendering, Memory, Audio, Physics, etc.).")
@click.option("--enabled/--disabled", required=True, help="Enable or disable the area.")
@handle_unity_errors
def area_set(area, enabled):
    """Enable/disable a specific profiler area."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "area_set", "area": area, "enabled": enabled}, config)
    click.echo(format_output(result, config.format))


# --- Physics ---

@profiler.command("physics")
@click.option("--frames", "-f", type=int, default=None, help="Frames to collect (default 120).")
@handle_unity_errors
def physics(frames):
    """Get physics profiler counter snapshot."""
    config = get_config()
    params = {"action": "physics_get"}
    if frames is not None:
        params["frames"] = frames
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))
