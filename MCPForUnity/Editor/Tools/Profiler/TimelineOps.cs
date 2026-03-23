using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class TimelineOps
    {
        // === timeline_get ===

        internal static object TimelineGet(JObject @params)
        {
            var p = new ToolParams(@params);
            int? frameParam = p.GetInt("frame");
            int threadIndex = p.GetInt("thread_index") ?? 0;
            int topN = p.GetInt("top_n") ?? 30;
            float minMs = p.GetFloat("min_ms") ?? 0.01f;

            int lastFrame = ProfilerDriver.lastFrameIndex;
            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (lastFrame <= 0)
                return new ErrorResponse("No profiler frames available. Enable the profiler and collect some frames first.");

            int frame = frameParam ?? lastFrame;
            if (frame < firstFrame || frame > lastFrame)
                return new ErrorResponse($"Frame {frame} out of range [{firstFrame}, {lastFrame}].");

            using var rawData = ProfilerDriver.GetRawFrameDataView(frame, threadIndex);
            if (rawData == null || !rawData.valid)
                return new ErrorResponse($"No data for frame {frame}, thread {threadIndex}.");

            // Filter by duration first, then resolve names (avoids string alloc for short samples)
            int sampleCount = rawData.sampleCount;
            var candidates = new List<(int index, double durationMs)>();

            for (int i = 0; i < sampleCount; i++)
            {
                double duration = rawData.GetSampleTimeMs(i);
                if (duration >= minMs)
                    candidates.Add((i, duration));
            }

            // Sort by duration descending, take top N, then resolve names
            var topSamples = candidates
                .OrderByDescending(c => c.durationMs)
                .Take(topN)
                .Select(c => new
                {
                    index = c.index,
                    name = rawData.GetSampleName(c.index),
                    start_ms = Math.Round(rawData.GetSampleStartTimeMs(c.index), 3),
                    duration_ms = Math.Round(c.durationMs, 3)
                })
                .ToList();

            // Collect flow events
            var flowEvents = new List<RawFrameDataView.FlowEvent>();
            rawData.GetFlowEvents(flowEvents);
            var flows = flowEvents.Select(f => new
            {
                flow_id = f.FlowId,
                type = f.FlowEventType.ToString(),
                parent_sample_index = f.ParentSampleIndex,
                parent_sample_name = f.ParentSampleIndex >= 0 && f.ParentSampleIndex < sampleCount
                    ? rawData.GetSampleName(f.ParentSampleIndex) : ""
            }).ToList();

            return new
            {
                success = true,
                message = $"Timeline for frame {frame}, thread {threadIndex}: {sampleCount} samples, {flows.Count} flow events.",
                data = new
                {
                    frame,
                    thread_index = threadIndex,
                    thread_name = rawData.threadName ?? "",
                    thread_group = rawData.threadGroupName ?? "",
                    total_samples = sampleCount,
                    frame_time_ms = Math.Round(rawData.frameTimeMs, 2),
                    top_samples = topSamples,
                    flow_events = flows,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }
    }
}
