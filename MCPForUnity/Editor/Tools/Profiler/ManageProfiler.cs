using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Profiler
{
    [McpForUnityTool("manage_profiler", AutoRegister = false, Group = "core")]
    public static class ManageProfiler
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                switch (action)
                {
                    // --- Health check ---
                    case "ping":
                        return new
                        {
                            success = true,
                            message = "Profiler tool ready.",
                            data = new
                            {
                                tool = "manage_profiler",
                                play_mode = EditorApplication.isPlaying,
                                profiler_enabled = UnityEngine.Profiling.Profiler.enabled,
                                active_sessions = CounterOps.GetActiveSessionCount()
                            }
                        };

                    // --- Counter sampling (ProfilerRecorder, sync) ---
                    case "sample_start":
                        return CounterOps.SampleStart(@params);
                    case "sample_stop":
                        return CounterOps.SampleStop(@params);
                    case "sample_read":
                        return CounterOps.SampleRead(@params);
                    case "sample_compare":
                        return CounterOps.SampleCompare(@params);
                    case "sample_list":
                        return CounterOps.SampleList();

                    // --- Frame time (ProfilerRecorder, async blocking) ---
                    case "frame_time_get":
                        return await CounterOps.FrameTimeGet(@params);
                    case "frame_timing_get":
                        return await CounterOps.FrameTimingGet(@params);

                    // --- Counter one-shot + discovery ---
                    case "counter_read":
                        return await CounterOps.CounterRead(@params);
                    case "counter_list":
                        return CounterOps.CounterList(@params);

                    // --- Hierarchy (HierarchyFrameDataView, async blocking) ---
                    case "hotspots_get":
                        return await HierarchyOps.HotspotsGet(@params);
                    case "hotspots_detail":
                        return await HierarchyOps.HotspotsDetail(@params);
                    case "gc_track":
                        return await HierarchyOps.GcTrack(@params);
                    case "threads_list":
                        return HierarchyOps.ThreadsList(@params);
                    case "timeline_get":
                        return TimelineOps.TimelineGet(@params);
                    case "frame_get":
                        return FrameOps.FrameGet(@params);

                    // --- Memory (Profiler.GetTotal*, sync) ---
                    case "memory_snapshot":
                        return MemoryOps.Snapshot(@params);
                    case "memory_compare":
                        return MemoryOps.Compare(@params);
                    case "memory_objects":
                        return MemoryOps.Objects(@params);
                    case "memory_type_summary":
                        return MemoryOps.TypeSummary(@params);
                    case "memory_fragmentation":
                        return MemoryOps.Fragmentation(@params);

                    // --- Capture (.raw files, sync) ---
                    case "capture_start":
                        return CaptureOps.Start(@params);
                    case "capture_stop":
                        return CaptureOps.Stop(@params);
                    case "capture_status":
                        return CaptureOps.Status(@params);
                    case "capture_load":
                        return CaptureOps.Load(@params);
                    case "capture_save":
                        return CaptureOps.Save(@params);

                    // --- Profiler control (sync) ---
                    case "profiler_enable":
                        return ControlOps.ProfilerEnable(@params);
                    case "profiler_disable":
                        return ControlOps.ProfilerDisable(@params);
                    case "deep_profiling_set":
                        return ControlOps.DeepProfilingSet(@params);
                    case "area_set":
                        return ControlOps.AreaSet(@params);
                    case "profiler_status":
                        return ControlOps.ProfilerStatus(@params);
                    case "callstacks_set":
                        return ControlOps.CallstacksSet(@params);
                    case "gpu_profiling_set":
                        return ControlOps.GpuProfilingSet(@params);

                    // --- Physics (async blocking) ---
                    case "physics_get":
                        return await CounterOps.PhysicsGet(@params);

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: ping, "
                            + "sample_start, sample_stop, sample_read, sample_compare, sample_list, "
                            + "counter_read, counter_list, "
                            + "frame_time_get, frame_timing_get, "
                            + "hotspots_get, hotspots_detail, gc_track, threads_list, timeline_get, frame_get, "
                            + "memory_snapshot, memory_compare, memory_objects, memory_type_summary, memory_fragmentation, "
                            + "capture_start, capture_stop, capture_status, capture_load, capture_save, "
                            + "profiler_enable, profiler_disable, deep_profiling_set, area_set, profiler_status, callstacks_set, gpu_profiling_set, "
                            + "physics_get");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageProfiler] Error in action '{action}': {ex}");
                return new ErrorResponse($"Error in '{action}': {ex.Message}");
            }
        }
    }
}
