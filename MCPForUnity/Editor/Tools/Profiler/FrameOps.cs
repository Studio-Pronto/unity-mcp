using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameOps
    {
        // === frame_get ===

        internal static object FrameGet(JObject @params)
        {
            var p = new ToolParams(@params);
            int? frameParam = p.GetInt("frame");

            int lastFrame = ProfilerDriver.lastFrameIndex;
            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (lastFrame <= 0)
                return new ErrorResponse("No profiler frames available.");

            int frame = frameParam ?? lastFrame;
            if (frame < firstFrame || frame > lastFrame)
                return new ErrorResponse($"Frame {frame} out of range [{firstFrame}, {lastFrame}].");

            // Get overview text for key areas
            var overviews = new Dictionary<string, string>();
            var areas = new[] { "CPU", "GPU", "Rendering", "Memory" };
            foreach (string areaName in areas)
            {
                if (Enum.TryParse<UnityEngine.Profiling.ProfilerArea>(areaName, true, out var area))
                {
                    try
                    {
                        string text = ProfilerDriver.GetOverviewText(area, frame);
                        if (!string.IsNullOrEmpty(text))
                            overviews[areaName] = text;
                    }
                    catch { }
                }
            }

            // Get frame timing from hierarchy view
            double frameTimeMs = 0;
            double gpuTimeMs = 0;
            using (var hierData = ProfilerDriver.GetHierarchyFrameDataView(
                frame, 0,
                HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnSelfTime, false))
            {
                if (hierData != null && hierData.valid)
                {
                    frameTimeMs = hierData.frameTimeMs;
                    gpuTimeMs = hierData.frameGpuTimeMs;
                }
            }

            double fps = frameTimeMs > 0 ? 1000.0 / frameTimeMs : 0;

            return new
            {
                success = true,
                message = $"Frame {frame}: {Math.Round(frameTimeMs, 2)}ms ({Math.Round(fps, 1)} FPS).",
                data = new
                {
                    frame,
                    frame_range = new { first = firstFrame, last = lastFrame },
                    frame_time_ms = Math.Round(frameTimeMs, 2),
                    gpu_time_ms = Math.Round(gpuTimeMs, 2),
                    fps = Math.Round(fps, 1),
                    overviews,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }
    }
}
