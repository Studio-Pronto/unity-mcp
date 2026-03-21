using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.Profiler;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Resources.Scene
{
    [McpForUnityResource("get_profiler_snapshot")]
    public static class ProfilerSnapshotResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                float fps = Time.smoothDeltaTime > 0 ? 1f / Time.smoothDeltaTime : 0;

                // Key rendering counters (instant single-frame reads)
                long drawCalls = 0, batches = 0, triangles = 0;
                try
                {
                    using var dcRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                    drawCalls = dcRec.CurrentValue;
                    using var bRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                    batches = bRec.CurrentValue;
                    using var tRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                    triangles = tRec.CurrentValue;
                }
                catch { }

                long gcAllocPerFrame = 0;
                try
                {
                    using var gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                    gcAllocPerFrame = gcRec.CurrentValue;
                }
                catch { }

                return new
                {
                    success = true,
                    message = "Profiler snapshot.",
                    data = new
                    {
                        play_mode = EditorApplication.isPlaying,
                        profiler_enabled = UnityEngine.Profiling.Profiler.enabled,
                        estimated_fps = Math.Round(fps, 1),
                        frame_time_ms = Time.smoothDeltaTime > 0 ? Math.Round(Time.smoothDeltaTime * 1000, 2) : 0.0,
                        memory = new
                        {
                            total_allocated_mb = Math.Round(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 2),
                            mono_used_mb = Math.Round(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 2),
                            graphics_mb = Math.Round(UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024.0 * 1024.0), 2)
                        },
                        rendering = new
                        {
                            draw_calls = drawCalls,
                            batches,
                            triangles
                        },
                        gc_alloc_per_frame_bytes = gcAllocPerFrame,
                        active_sessions = CounterOps.GetActiveSessionCount()
                    }
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ProfilerSnapshotResource] Error: {e}");
                return new ErrorResponse($"Error getting profiler snapshot: {e.Message}");
            }
        }
    }
}
