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

                    // --- Memory (Profiler.GetTotal*, sync) ---
                    case "memory_snapshot":
                        return MemoryOps.Snapshot(@params);
                    case "memory_compare":
                        return MemoryOps.Compare(@params);
                    case "memory_objects":
                        return MemoryOps.Objects(@params);
                    case "memory_type_summary":
                        return MemoryOps.TypeSummary(@params);

                    // --- Capture (.raw files, sync) ---
                    case "capture_start":
                        return CaptureOps.Start(@params);
                    case "capture_stop":
                        return CaptureOps.Stop(@params);
                    case "capture_status":
                        return CaptureOps.Status(@params);

                    // --- Profiler control (sync) ---
                    case "profiler_enable":
                        return ControlOps.ProfilerEnable(@params);
                    case "profiler_disable":
                        return ControlOps.ProfilerDisable(@params);
                    case "deep_profiling_set":
                        return ControlOps.DeepProfilingSet(@params);
                    case "area_set":
                        return ControlOps.AreaSet(@params);

                    // --- Physics (async blocking) ---
                    case "physics_get":
                        return await CounterOps.PhysicsGet(@params);

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: ping, "
                            + "sample_start, sample_stop, sample_read, sample_compare, sample_list, "
                            + "counter_read, counter_list, "
                            + "frame_time_get, "
                            + "hotspots_get, hotspots_detail, gc_track, "
                            + "memory_snapshot, memory_compare, memory_objects, memory_type_summary, "
                            + "capture_start, capture_stop, capture_status, "
                            + "profiler_enable, profiler_disable, deep_profiling_set, area_set, "
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
