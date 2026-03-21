using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class ControlOps
    {
        // === profiler_enable ===

        internal static object ProfilerEnable(JObject @params)
        {
            var p = new ToolParams(@params);
            int? maxHistory = p.GetInt("max_history_frames");

            Profiler.enabled = true;

            if (maxHistory.HasValue && maxHistory.Value > 0)
                ProfilerDriver.SetMaxFrameHistoryLength(maxHistory.Value);

            return new
            {
                success = true,
                message = "Profiler recording enabled.",
                data = new
                {
                    profiler_enabled = true,
                    max_history_frames = maxHistory,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === profiler_disable ===

        internal static object ProfilerDisable(JObject @params)
        {
            Profiler.enabled = false;

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

            if (!System.Enum.TryParse<ProfilerArea>(areaName, true, out var area))
                return new ErrorResponse($"Unknown profiler area: '{areaName}'. Valid areas: CPU, GPU, Rendering, Memory, Audio, Video, Physics, Physics2D, NetworkMessages, NetworkOperations, UI, UIDetails, GlobalIllumination, VirtualTexturing.");

            Profiler.SetAreaEnabled(area, enabled);

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
    }
}
