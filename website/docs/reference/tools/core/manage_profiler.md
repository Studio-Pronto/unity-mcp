---
title: manage_profiler
sidebar_label: manage_profiler
description: "Unity Profiler: CPU hotspots, memory, counters, frame time, captures."
---

# `manage_profiler`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_profiler`

## Description

Unity Profiler: CPU hotspots, memory, counters, frame time, captures. Use ping to check state. See also: manage_graphics stats_get for rendering counters (draw calls, batches, triangles); resource mcpforunity://profiler/snapshot for instant FPS/memory/rendering overview.

GOTCHAS: hotspots_get/gc_track auto-enable profiler (no manual profiler_enable needed). Sessions & memory snapshots are cleared on domain reload (script recompilation). frames param clamped [1,1500] with 25s hard timeout. gpu_profiling_set: no Metal support (macOS).

CONTROL: profiler_enable, profiler_disable, profiler_status (full config state), deep_profiling_set, area_set, callstacks_set, gpu_profiling_set

CPU HOTSPOTS (auto-enables profiler): hotspots_get (top-N by self time), hotspots_detail (callers/callees + callstack), gc_track (GC alloc per marker + callstacks), threads_list, timeline_get, frame_get

FRAME TIME: frame_time_get (main/render/GPU breakdown + bottleneck), frame_timing_get (FrameTimingManager: VSync wait, dynamic resolution)

COUNTER SAMPLING (works without Profiler window): sample_start (label required), sample_stop, sample_read (mean/p95/p99), sample_compare (delta between sessions), sample_list, counter_read (one-shot), counter_list

MEMORY (instant, no profiler needed): memory_snapshot (labeled), memory_compare, memory_objects (per-object, paged), memory_type_summary, memory_fragmentation

CAPTURE (.raw files): capture_start, capture_stop, capture_status, capture_load, capture_save

PHYSICS: physics_get (self-contained, all physics counters, mean/p95/p99)

OBJECT MEMORY: object_memory_get (runtime memory of a single object by scene path or asset path)

MEMORY SNAPSHOTS (.snap, requires com.unity.memoryprofiler): snap_take, snap_list, snap_compare

FRAME DEBUGGER: frame_debugger_enable, frame_debugger_disable, frame_debugger_get_events (paged draw call events with shader/mesh/RT info, batch_break_cause + readable text, shader_keywords; pass include_render_state=true for per-draw blend/raster/depth/stencil state)

EVENT WINDOW (bracket a gameplay event, all-thread CPU+GC): event_begin (marks current frame) -> trigger the event -> event_end (per-marker self-time + GC across ALL threads incl. Job/Burst workers, with the worst frame flagged). Reuses label/top_n/min_ms. Pairs with deep_profiling_set for managed per-method detail. Catches transient worker-thread bursts that hotspots_get/gc_track (trailing window, thread 0) miss.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['ping', 'sample_start', 'sample_stop', 'sample_read', 'sample_compare', 'sample_list', 'counter_read', 'counter_list', 'frame_time_get', 'frame_timing_get', 'hotspots_get', 'hotspots_detail', 'gc_track', 'threads_list', 'timeline_get', 'frame_get', 'memory_snapshot', 'memory_compare', 'memory_objects', 'memory_type_summary', 'memory_fragmentation', 'capture_start', 'capture_stop', 'capture_status', 'capture_load', 'capture_save', 'profiler_enable', 'profiler_disable', 'deep_profiling_set', 'area_set', 'profiler_status', 'callstacks_set', 'gpu_profiling_set', 'physics_get', 'object_memory_get', 'snap_take', 'snap_list', 'snap_compare', 'frame_debugger_enable', 'frame_debugger_disable', 'frame_debugger_get_events', 'event_begin', 'event_end']` | yes | The profiler action to perform. |
| `label` | `str \| None` | — | Session label for sampling or memory snapshots. |
| `counters` | `list[str] \| str \| None` | — | Category name (e.g. 'render', 'physics', 'gc'), or a list of counter names (see counter_list). |
| `capacity` | `int \| None` | — | Ring buffer capacity (frames). Default 300. |
| `last_n` | `int \| None` | — | Read only last N frames from session. |
| `label_a` | `str \| None` | — | First session label for comparison. |
| `label_b` | `str \| None` | — | Second session label for comparison. |
| `threshold_pct` | `float \| None` | — | Min % change to report in comparison. Default 5.0. |
| `frames` | `int \| None` | — | Number of frames to collect/analyze. Default 120. |
| `top_n` | `int \| None` | — | Number of top results to return. Default 20. |
| `min_ms` | `float \| None` | — | Minimum self time (ms) to include. Default 0.1. |
| `thread` | `str \| None` | — | Thread to analyze: 'main', 'render', or 'all'. Default 'main'. |
| `thread_index` | `int \| None` | — | Thread index for timeline_get. Default 0 (main thread). |
| `frame` | `int \| None` | — | Specific frame index for frame_get and timeline_get. |
| `marker_name` | `str \| None` | — | Specific marker name for hotspots_detail. |
| `target` | `str \| None` | — | Filter by object name or instance ID. |
| `object_type` | `str \| None` | — | Filter by Unity object type name. |
| `min_size_kb` | `float \| None` | — | Minimum object size in KB. |
| `min_total_mb` | `float \| None` | — | Minimum total MB per type for memory_type_summary. |
| `max_objects` | `int \| None` | — | Safety cap for object iteration. Default 10000. |
| `page_size` | `int \| None` | — | Results per page. |
| `cursor` | `str \| None` | — | Pagination cursor. |
| `category` | `str \| None` | — | Filter counters by category for counter_list. |
| `search` | `str \| None` | — | Filter counters by name substring for counter_list. |
| `output_path` | `str \| None` | — | File path for .raw capture output. |
| `input_path` | `str \| None` | — | File path of .raw capture to load. |
| `keep_profiler_enabled` | `bool \| None` | — | Keep profiler on after capture_stop. |
| `enabled` | `bool \| None` | — | Enable/disable toggle for deep_profiling_set, callstacks_set, gpu_profiling_set, and area_set. |
| `area` | `str \| None` | — | Profiler area name for area_set. |
| `object_path` | `str \| None` | — | Scene hierarchy path or asset path for object_memory_get. |
| `snapshot_path` | `str \| None` | — | Output path for snap_take. |
| `search_path` | `str \| None` | — | Search directory for snap_list. |
| `snapshot_a` | `str \| None` | — | First snapshot path for snap_compare. |
| `snapshot_b` | `str \| None` | — | Second snapshot path for snap_compare. |
| `include_render_state` | `bool \| None` | — | frame_debugger_get_events: include per-draw render state (blend/raster/depth/stencil). Verbose; default off. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

