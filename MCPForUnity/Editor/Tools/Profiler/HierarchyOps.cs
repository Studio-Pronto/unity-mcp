using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class HierarchyOps
    {
        // === hotspots_get (async -- may need to wait for profiler frames) ===

        internal static async Task<object> HotspotsGet(JObject @params)
        {
            var p = new ToolParams(@params);
            int frames = p.GetInt("frames") ?? 120;
            int topN = p.GetInt("top_n") ?? 20;
            float minMs = p.GetFloat("min_ms") ?? 0.1f;
            string threadParam = p.Get("thread", "main");

            if (frames < 1) frames = 1;
            if (frames > 1500) frames = 1500;

            bool wasEnabled = UnityEngine.Profiling.Profiler.enabled;
            bool needsWait = false;

            // Ensure profiler is recording
            if (!wasEnabled)
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                needsWait = true;
            }
            else
            {
                // Check if enough frames are in the buffer
                int available = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex;
                if (available < frames)
                    needsWait = true;
            }

            if (needsWait)
            {
                await ProfilerHelpers.WaitForFrames(frames);
            }

            try
            {
                return ReadHotspots(frames, topN, minMs, threadParam);
            }
            finally
            {
                // Restore previous profiler state if we enabled it
                if (!wasEnabled)
                    UnityEngine.Profiling.Profiler.enabled = false;
            }
        }

        private static object ReadHotspots(int frames, int topN, float minMs, string threadParam)
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            int firstFrame = ProfilerDriver.firstFrameIndex;
            int available = lastFrame - firstFrame;
            int framesToRead = Math.Min(frames, available);

            if (framesToRead <= 0)
            {
                return new
                {
                    success = true,
                    message = "No profiler frames available.",
                    data = new
                    {
                        frames_analyzed = 0,
                        hotspots = new object[0],
                        play_mode = EditorApplication.isPlaying
                    }
                };
            }

            // Determine thread indices to read
            var threadIndices = new List<int>();
            string tp = threadParam?.ToLowerInvariant();
            if (tp == "render")
                threadIndices.Add(1);
            else if (tp == "all")
            {
                threadIndices.Add(0);
                threadIndices.Add(1);
            }
            else if (int.TryParse(tp, out int customIndex))
                threadIndices.Add(customIndex);
            else // "main" or default
                threadIndices.Add(0);

            // Accumulate marker data across frames
            var markerAccum = new Dictionary<string, MarkerData>();
            int startFrame = lastFrame - framesToRead;
            double totalFrameTimeMs = 0;
            double totalGpuTimeMs = 0;

            for (int fi = startFrame; fi < lastFrame; fi++)
            {
                foreach (int threadIndex in threadIndices)
                {
                    using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                        fi, threadIndex,
                        HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                        HierarchyFrameDataView.columnSelfTime,
                        false);

                    if (frameData == null || !frameData.valid) continue;

                    if (threadIndex == 0)
                    {
                        totalFrameTimeMs += frameData.frameTimeMs;
                        totalGpuTimeMs += frameData.frameGpuTimeMs;
                    }

                    WalkHierarchy(frameData, frameData.GetRootItemID(), markerAccum);
                }
            }

            // Sort by self time descending, filter, take top N
            var hotspots = markerAccum.Values
                .Where(m => m.SelfTimeMs >= minMs)
                .OrderByDescending(m => m.SelfTimeMs)
                .Take(topN)
                .Select(m => new
                {
                    marker = m.Name,
                    total_ms = Math.Round(m.TotalTimeMs, 2),
                    self_ms = Math.Round(m.SelfTimeMs, 2),
                    calls = m.Calls,
                    gc_alloc_bytes = m.GcAllocBytes,
                    avg_self_ms = m.Calls > 0 ? Math.Round(m.SelfTimeMs / m.Calls, 3) : 0.0
                })
                .ToList();

            // Build summary
            var summaryParts = hotspots.Take(3).Select(h =>
                $"{h.marker} ({h.self_ms}ms self, {h.calls} calls)");
            string summary = hotspots.Count > 0
                ? "Top hotspots: " + string.Join(", ", summaryParts)
                : "No significant hotspots found.";

            return new
            {
                success = true,
                message = summary,
                data = new
                {
                    frames_analyzed = framesToRead,
                    thread = threadParam ?? "main",
                    avg_frame_time_ms = framesToRead > 0 ? Math.Round(totalFrameTimeMs / framesToRead, 2) : 0.0,
                    avg_gpu_time_ms = framesToRead > 0 ? Math.Round(totalGpuTimeMs / framesToRead, 2) : 0.0,
                    hotspots,
                    summary,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === hotspots_detail ===

        internal static async Task<object> HotspotsDetail(JObject @params)
        {
            var p = new ToolParams(@params);
            string markerName = p.Get("marker_name", "markerName");
            int frames = p.GetInt("frames") ?? 120;

            if (string.IsNullOrEmpty(markerName))
                return new ErrorResponse("'marker_name' parameter is required.");

            if (frames < 1) frames = 1;
            if (frames > 1500) frames = 1500;

            bool wasEnabled = UnityEngine.Profiling.Profiler.enabled;
            if (!wasEnabled)
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                await ProfilerHelpers.WaitForFrames(frames);
            }
            else
            {
                int available = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex;
                if (available < frames)
                    await ProfilerHelpers.WaitForFrames(frames);
            }

            try
            {
                int lastFrame = ProfilerDriver.lastFrameIndex;
                int firstFrame = ProfilerDriver.firstFrameIndex;
                int framesToRead = Math.Min(frames, lastFrame - firstFrame);
                int startFrame = lastFrame - framesToRead;

                var callers = new Dictionary<string, MarkerData>();
                var callees = new Dictionary<string, MarkerData>();
                var perFrameSelfMs = new List<double>();
                double totalSelfMs = 0;
                int totalCalls = 0;
                long totalGcBytes = 0;
                int lastFoundFrame = -1;
                int lastFoundItemId = -1;

                for (int fi = startFrame; fi < lastFrame; fi++)
                {
                    using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                        fi, 0,
                        HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                        HierarchyFrameDataView.columnSelfTime, false);

                    if (frameData == null || !frameData.valid) continue;

                    double frameSelfMs = 0;
                    int foundId = -1;
                    FindMarkerDetail(frameData, frameData.GetRootItemID(), markerName,
                        callers, callees, ref frameSelfMs, ref totalCalls, ref totalGcBytes, null, ref foundId);
                    if (foundId >= 0)
                    {
                        lastFoundFrame = fi;
                        lastFoundItemId = foundId;
                    }
                    perFrameSelfMs.Add(frameSelfMs);
                    totalSelfMs += frameSelfMs;
                }

                if (totalCalls == 0)
                {
                    return new
                    {
                        success = true,
                        message = $"Marker '{markerName}' not found in {framesToRead} frames.",
                        data = new { marker_name = markerName, frames_analyzed = framesToRead, found = false, play_mode = EditorApplication.isPlaying }
                    };
                }

                var topCallers = callers.Values.OrderByDescending(m => m.SelfTimeMs).Take(10)
                    .Select(m => new { marker = m.Name, total_ms = Math.Round(m.TotalTimeMs, 2), calls = m.Calls }).ToList();
                var topCallees = callees.Values.OrderByDescending(m => m.SelfTimeMs).Take(10)
                    .Select(m => new { marker = m.Name, self_ms = Math.Round(m.SelfTimeMs, 2), calls = m.Calls, gc_alloc_bytes = m.GcAllocBytes }).ToList();

                // Resolve callstack for the marker if available
                string callstack = "";
                if (lastFoundFrame >= 0 && lastFoundItemId >= 0)
                {
                    using var csFrame = ProfilerDriver.GetHierarchyFrameDataView(
                        lastFoundFrame, 0,
                        HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                        HierarchyFrameDataView.columnSelfTime, false);
                    if (csFrame != null && csFrame.valid)
                    {
                        try { callstack = csFrame.ResolveItemCallstack(lastFoundItemId); }
                        catch { }
                    }
                }

                return new
                {
                    success = true,
                    message = $"Detail for '{markerName}': {Math.Round(totalSelfMs, 2)}ms self over {totalCalls} calls.",
                    data = new
                    {
                        marker_name = markerName,
                        frames_analyzed = framesToRead,
                        found = true,
                        total_self_ms = Math.Round(totalSelfMs, 2),
                        total_calls = totalCalls,
                        total_gc_alloc_bytes = totalGcBytes,
                        avg_self_ms = totalCalls > 0 ? Math.Round(totalSelfMs / totalCalls, 3) : 0.0,
                        callers = topCallers,
                        callees = topCallees,
                        callstack = callstack ?? "",
                        callstacks_available = !string.IsNullOrEmpty(callstack),
                        play_mode = EditorApplication.isPlaying
                    }
                };
            }
            finally
            {
                if (!wasEnabled)
                    UnityEngine.Profiling.Profiler.enabled = false;
            }
        }

        private static void FindMarkerDetail(HierarchyFrameDataView frameData, int itemId, string targetName,
            Dictionary<string, MarkerData> callers, Dictionary<string, MarkerData> callees,
            ref double frameSelfMs, ref int totalCalls, ref long totalGcBytes, string parentName, ref int foundItemId)
        {
            var children = new List<int>();
            frameData.GetItemChildren(itemId, children);

            foreach (int childId in children)
            {
                string name = frameData.GetItemName(childId);
                float selfTime = frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnSelfTime);
                float totalTime = frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnTotalTime);
                int calls = (int)frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnCalls);
                float gcAlloc = frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnGcMemory);

                if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    frameSelfMs += selfTime;
                    totalCalls += calls;
                    totalGcBytes += (long)gcAlloc;
                    foundItemId = childId;

                    // Record caller (parent)
                    if (!string.IsNullOrEmpty(parentName))
                    {
                        if (!callers.TryGetValue(parentName, out var callerData))
                        {
                            callerData = new MarkerData { Name = parentName };
                            callers[parentName] = callerData;
                        }
                        callerData.TotalTimeMs += totalTime;
                        callerData.Calls += calls;
                    }

                    // Record callees (children of this marker)
                    var subChildren = new List<int>();
                    frameData.GetItemChildren(childId, subChildren);
                    foreach (int subId in subChildren)
                    {
                        string subName = frameData.GetItemName(subId);
                        float subSelf = frameData.GetItemColumnDataAsSingle(subId, HierarchyFrameDataView.columnSelfTime);
                        int subCalls = (int)frameData.GetItemColumnDataAsSingle(subId, HierarchyFrameDataView.columnCalls);
                        float subGc = frameData.GetItemColumnDataAsSingle(subId, HierarchyFrameDataView.columnGcMemory);

                        if (!callees.TryGetValue(subName, out var calleeData))
                        {
                            calleeData = new MarkerData { Name = subName };
                            callees[subName] = calleeData;
                        }
                        calleeData.SelfTimeMs += subSelf;
                        calleeData.Calls += subCalls;
                        calleeData.GcAllocBytes += (long)subGc;
                    }
                }

                // Continue walking to find deeper instances
                FindMarkerDetail(frameData, childId, targetName, callers, callees,
                    ref frameSelfMs, ref totalCalls, ref totalGcBytes, name, ref foundItemId);
            }
        }

        // === gc_track ===

        internal static async Task<object> GcTrack(JObject @params)
        {
            var p = new ToolParams(@params);
            int frames = p.GetInt("frames") ?? 180;
            int topN = p.GetInt("top_n") ?? 15;

            if (frames < 1) frames = 1;
            if (frames > 1500) frames = 1500;

            bool wasEnabled = UnityEngine.Profiling.Profiler.enabled;
            if (!wasEnabled)
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                await ProfilerHelpers.WaitForFrames(frames);
            }
            else
            {
                int available = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex;
                if (available < frames)
                    await ProfilerHelpers.WaitForFrames(frames);
            }

            try
            {
                int lastFrame = ProfilerDriver.lastFrameIndex;
                int firstFrame = ProfilerDriver.firstFrameIndex;
                int framesToRead = Math.Min(frames, lastFrame - firstFrame);
                int startFrame = lastFrame - framesToRead;

                var markerGc = new Dictionary<string, long>();
                var perFrameGc = new List<(int frame, long gcBytes)>();
                long totalGcBytes = 0;

                for (int fi = startFrame; fi < lastFrame; fi++)
                {
                    using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                        fi, 0,
                        HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                        HierarchyFrameDataView.columnGcMemory, false);

                    if (frameData == null || !frameData.valid) continue;

                    long frameGcTotal = 0;
                    WalkGcHierarchy(frameData, frameData.GetRootItemID(), markerGc, ref frameGcTotal);
                    perFrameGc.Add((fi, frameGcTotal));
                    totalGcBytes += frameGcTotal;
                }

                int gcCollections = GC.CollectionCount(0);

                var topMarkerNames = markerGc
                    .Where(kv => kv.Value > 0)
                    .OrderByDescending(kv => kv.Value)
                    .Take(topN)
                    .Select(kv => kv.Key)
                    .ToList();

                // Resolve callstacks for top 3 allocators
                var markerCallstacks = new Dictionary<string, string>();
                bool anyCallstacks = false;
                foreach (string targetMarker in topMarkerNames.Take(3))
                {
                    for (int fi = lastFrame - 1; fi >= startFrame; fi--)
                    {
                        using var csFrame = ProfilerDriver.GetHierarchyFrameDataView(
                            fi, 0,
                            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                            HierarchyFrameDataView.columnGcMemory, false);
                        if (csFrame == null || !csFrame.valid) continue;

                        int itemId = FindMarkerItemId(csFrame, csFrame.GetRootItemID(), targetMarker);
                        if (itemId >= 0)
                        {
                            try
                            {
                                string cs = csFrame.ResolveItemCallstack(itemId);
                                if (!string.IsNullOrEmpty(cs))
                                {
                                    markerCallstacks[targetMarker] = cs;
                                    anyCallstacks = true;
                                }
                            }
                            catch { }
                            break;
                        }
                    }
                }

                var topAllocators = topMarkerNames
                    .Select(name => new
                    {
                        marker = name,
                        total_bytes = markerGc[name],
                        total_kb = Math.Round(markerGc[name] / 1024.0, 1),
                        pct = totalGcBytes > 0 ? Math.Round((double)markerGc[name] / totalGcBytes * 100, 1) : 0.0,
                        callstack = markerCallstacks.TryGetValue(name, out var cs) ? cs : ""
                    })
                    .ToList();

                // Worst frames by GC
                var worstFrames = perFrameGc
                    .OrderByDescending(f => f.gcBytes)
                    .Take(5)
                    .Select(f => new { frame = f.frame, gc_bytes = f.gcBytes })
                    .ToList();

                double avgGcPerFrame = framesToRead > 0 ? totalGcBytes / (double)framesToRead : 0;

                return new
                {
                    success = true,
                    message = $"GC tracked over {framesToRead} frames. Total: {Math.Round(totalGcBytes / 1024.0, 1)}KB, Avg: {Math.Round(avgGcPerFrame / 1024.0, 1)}KB/frame.",
                    data = new
                    {
                        frames_analyzed = framesToRead,
                        total_gc_bytes = totalGcBytes,
                        per_frame_avg_bytes = (long)avgGcPerFrame,
                        gc_collections = gcCollections,
                        top_allocators = topAllocators,
                        worst_frames = worstFrames,
                        callstacks_available = anyCallstacks,
                        play_mode = EditorApplication.isPlaying
                    }
                };
            }
            finally
            {
                if (!wasEnabled)
                    UnityEngine.Profiling.Profiler.enabled = false;
            }
        }

        private static int FindMarkerItemId(HierarchyFrameDataView frameData, int itemId, string targetName)
        {
            var children = new List<int>();
            frameData.GetItemChildren(itemId, children);
            foreach (int childId in children)
            {
                if (string.Equals(frameData.GetItemName(childId), targetName, StringComparison.OrdinalIgnoreCase))
                    return childId;
                int found = FindMarkerItemId(frameData, childId, targetName);
                if (found >= 0) return found;
            }
            return -1;
        }

        private static void WalkGcHierarchy(HierarchyFrameDataView frameData, int itemId,
            Dictionary<string, long> markerGc, ref long frameGcTotal)
        {
            var children = new List<int>();
            frameData.GetItemChildren(itemId, children);

            foreach (int childId in children)
            {
                string name = frameData.GetItemName(childId);
                long gcAlloc = (long)frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnGcMemory);

                if (gcAlloc > 0)
                {
                    frameGcTotal += gcAlloc;
                    if (!markerGc.ContainsKey(name))
                        markerGc[name] = 0;
                    markerGc[name] += gcAlloc;
                }

                WalkGcHierarchy(frameData, childId, markerGc, ref frameGcTotal);
            }
        }

        // --- Shared hierarchy walker ---

        private static void WalkHierarchy(HierarchyFrameDataView frameData, int itemId, Dictionary<string, MarkerData> accum)
        {
            var children = new List<int>();
            frameData.GetItemChildren(itemId, children);

            foreach (int childId in children)
            {
                string name = frameData.GetItemName(childId);
                float totalTime = frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnTotalTime);
                float selfTime = frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnSelfTime);
                int calls = (int)frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnCalls);
                float gcAlloc = frameData.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnGcMemory);

                if (!accum.TryGetValue(name, out var data))
                {
                    data = new MarkerData { Name = name };
                    accum[name] = data;
                }

                data.TotalTimeMs += totalTime;
                data.SelfTimeMs += selfTime;
                data.Calls += calls;
                data.GcAllocBytes += (long)gcAlloc;

                // Recurse into children
                WalkHierarchy(frameData, childId, accum);
            }
        }

        // === threads_list ===

        internal static object ThreadsList(JObject @params)
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (lastFrame <= 0)
            {
                return new
                {
                    success = true,
                    message = "No profiler frames available. Enable the profiler and collect some frames first.",
                    data = new { threads = new object[0], play_mode = EditorApplication.isPlaying }
                };
            }

            var threads = new List<object>();
            for (int threadIndex = 0; threadIndex < 64; threadIndex++)
            {
                using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                    lastFrame, threadIndex,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnSelfTime, false);

                if (frameData == null || !frameData.valid) break;

                threads.Add(new
                {
                    index = threadIndex,
                    name = frameData.threadName ?? "",
                    group = frameData.threadGroupName ?? ""
                });
            }

            return new
            {
                success = true,
                message = $"{threads.Count} profiled thread(s) found.",
                data = new
                {
                    threads,
                    frame = lastFrame,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        private sealed class MarkerData
        {
            public string Name;
            public double TotalTimeMs;
            public double SelfTimeMs;
            public int Calls;
            public long GcAllocBytes;
        }
    }
}
