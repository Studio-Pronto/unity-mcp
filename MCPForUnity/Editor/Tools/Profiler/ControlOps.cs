using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class ControlOps
    {
        // === profiler_enable ===

        internal static object ProfilerEnable(JObject @params)
        {
            UnityEngine.Profiling.Profiler.enabled = true;

            return new
            {
                success = true,
                message = "Profiler recording enabled.",
                data = new
                {
                    profiler_enabled = true,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === profiler_disable ===

        internal static object ProfilerDisable(JObject @params)
        {
            UnityEngine.Profiling.Profiler.enabled = false;

            return new
            {
                success = true,
                message = "Profiler recording disabled. Counter sampling (ProfilerRecorder) continues working.",
                data = new
                {
                    profiler_enabled = false,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === deep_profiling_set ===

        internal static object DeepProfilingSet(JObject @params)
        {
            var p = new ToolParams(@params);
            if (!p.Has("enabled"))
                return new ErrorResponse("'enabled' parameter is required for deep_profiling_set.");
            bool enabled = p.GetBool("enabled");

            ProfilerDriver.deepProfiling = enabled;

            string warning = enabled
                ? " Warning: deep profiling adds significant overhead (~5-10x slower). Disable when done."
                : "";

            return new
            {
                success = true,
                message = $"Deep profiling {(enabled ? "enabled" : "disabled")}.{warning}",
                data = new
                {
                    deep_profiling = enabled,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === area_set ===

        internal static object AreaSet(JObject @params)
        {
            var p = new ToolParams(@params);
            string areaName = p.Get("area");
            bool enabled = p.GetBool("enabled", true);

            if (string.IsNullOrEmpty(areaName))
                return new ErrorResponse("'area' parameter is required. Valid areas: CPU, GPU, Rendering, Memory, Audio, Video, Physics, Physics2D, NetworkMessages, NetworkOperations, UI, UIDetails, GlobalIllumination, VirtualTexturing.");

            if (!System.Enum.TryParse<UnityEngine.Profiling.ProfilerArea>(areaName, true, out var area))
                return new ErrorResponse($"Unknown profiler area: '{areaName}'. Valid areas: CPU, GPU, Rendering, Memory, Audio, Video, Physics, Physics2D, NetworkMessages, NetworkOperations, UI, UIDetails, GlobalIllumination, VirtualTexturing.");

            UnityEngine.Profiling.Profiler.SetAreaEnabled(area, enabled);

            return new
            {
                success = true,
                message = $"Profiler area '{area}' {(enabled ? "enabled" : "disabled")}.",
                data = new
                {
                    area = area.ToString(),
                    enabled,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === profiler_status ===

        internal static object ProfilerStatus(JObject @params)
        {
            var areas = new Dictionary<string, bool>();
            foreach (UnityEngine.Profiling.ProfilerArea a in
                Enum.GetValues(typeof(UnityEngine.Profiling.ProfilerArea)))
            {
                try { areas[a.ToString()] = UnityEngine.Profiling.Profiler.GetAreaEnabled(a); }
                catch { }
            }

            bool callstacksEnabled = false;
            try { callstacksEnabled = UnityEngine.Profiling.Profiler.enableAllocationCallstacks; }
            catch { } // Unity < 2021.2

            return new
            {
                success = true,
                message = "Profiler status.",
                data = new
                {
                    profiler_enabled = UnityEngine.Profiling.Profiler.enabled,
                    deep_profiling = ProfilerDriver.deepProfiling,
                    allocation_callstacks = callstacksEnabled,
                    binary_log_enabled = UnityEngine.Profiling.Profiler.enableBinaryLog,
                    log_file = UnityEngine.Profiling.Profiler.logFile ?? "",
                    max_used_memory = UnityEngine.Profiling.Profiler.maxUsedMemory,
                    areas,
                    active_sessions = CounterOps.GetActiveSessionCount(),
                    frame_count = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex,
                    first_frame = ProfilerDriver.firstFrameIndex,
                    last_frame = ProfilerDriver.lastFrameIndex,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === callstacks_set ===

        internal static object CallstacksSet(JObject @params)
        {
            var p = new ToolParams(@params);
            if (!p.Has("enabled"))
                return new ErrorResponse("'enabled' parameter is required for callstacks_set.");
            bool enabled = p.GetBool("enabled");

            try
            {
                UnityEngine.Profiling.Profiler.enableAllocationCallstacks = enabled;
            }
            catch (Exception)
            {
                return new ErrorResponse("Allocation callstacks require Unity 2021.2+.");
            }

            string warning = enabled
                ? " Warning: allocation callstacks add overhead on every managed allocation. Disable when done."
                : "";

            return new
            {
                success = true,
                message = $"Allocation callstacks {(enabled ? "enabled" : "disabled")}.{warning}",
                data = new
                {
                    allocation_callstacks = enabled,
                    profiler_enabled = UnityEngine.Profiling.Profiler.enabled,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }
    }
}
