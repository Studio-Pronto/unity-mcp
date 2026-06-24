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
    /// <summary>
    /// Event-scoped, all-thread CPU + GC attribution. `event_begin` marks the current profiler
    /// frame; the user triggers a gameplay event; `event_end` analyzes ONLY the bracketed frames,
    /// summing per-marker self-time + GC across EVERY thread (main + Job/Burst workers + named
    /// threads) and flags the worst frame by non-main-thread self-time.
    ///
    /// Unlike `hotspots_get`/`gc_track` (trailing N-frame window, thread 0 only), this brackets in
    /// profiler-frame space so a transient parallel-worker burst isn't diluted or invisible. Both
    /// handlers are synchronous: the user controls the gap between the two calls, and frames
    /// accumulate in the ProfilerDriver ring during gameplay.
    /// </summary>
    internal static class EventCaptureOps
    {
        private const int MaxFramesAnalyzed = 1500;

        private sealed class EventSession
        {
            public string Label;
            public int BeginFrameIndex;
            public bool ForcedProfilerOn;
            public DateTime StartTime;
        }

        // Keyed by label (default "default"). Wiped on domain reload, like CounterOps sessions.
        private static readonly Dictionary<string, EventSession> _windows = new();

        [InitializeOnLoadMethod]
        private static void CleanupOnDomainReload() => _windows.Clear();

        // === event_begin ===
        internal static object EventBegin(JObject @params)
        {
            var p = new ToolParams(@params);
            string label = p.Get("label", "default");

            if (_windows.ContainsKey(label))
                return new ErrorResponse(
                    $"Event window '{label}' is already open. Call event_end first or use a different label.");

            bool wasEnabled = UnityEngine.Profiling.Profiler.enabled;
            bool forced = false;
            if (!wasEnabled)
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                forced = true;
            }

            int beginFrame = ProfilerDriver.lastFrameIndex;
            _windows[label] = new EventSession
            {
                Label = label,
                BeginFrameIndex = beginFrame,
                ForcedProfilerOn = forced,
                StartTime = DateTime.UtcNow
            };

            bool playing = EditorApplication.isPlaying;
            string msg = $"Event window '{label}' armed at frame {beginFrame}.";
            if (!playing)
                msg += " NOTE: profiler frames only advance in Play mode — enter Play mode before triggering the event.";

            return new
            {
                success = true,
                message = msg,
                data = new
                {
                    label,
                    begin_frame = beginFrame,
                    profiler_was_enabled = wasEnabled,
                    play_mode = playing
                }
            };
        }

        // === event_end ===
        internal static object EventEnd(JObject @params)
        {
            var p = new ToolParams(@params);
            string label = p.Get("label", "default");
            int topN = p.GetInt("top_n") ?? 20;
            float minMs = p.GetFloat("min_ms") ?? 0.1f;

            if (!_windows.TryGetValue(label, out var session))
                return new ErrorResponse(
                    $"Event window '{label}' not found. It may have been cleared by script recompilation.");

            try
            {
                int last = ProfilerDriver.lastFrameIndex;
                int first = ProfilerDriver.firstFrameIndex;
                int from = Math.Max(session.BeginFrameIndex, first);
                int dropped = Math.Max(0, first - session.BeginFrameIndex);

                bool truncated = false;
                if (last - from > MaxFramesAnalyzed)
                {
                    from = last - MaxFramesAnalyzed;
                    truncated = true;
                }

                if (last <= from)
                {
                    return new
                    {
                        success = true,
                        message = $"Event window '{label}': no profiler frames elapsed (are you in Play mode?).",
                        data = new
                        {
                            label,
                            frames_analyzed = 0,
                            frames_dropped = dropped,
                            play_mode = EditorApplication.isPlaying
                        }
                    };
                }

                var markerAccum = new Dictionary<(string thread, string marker), MarkerAccum>();
                var threadTotals = new Dictionary<string, ThreadTotal>();
                int worstFrame = -1;
                double worstNonMainSelf = -1;
                double worstTotalSelf = 0;
                long worstGc = 0;
                double totalSelfMs = 0;
                long totalGcBytes = 0;
                int framesAnalyzed = 0;

                for (int fi = from; fi < last; fi++)
                {
                    double frameNonMainSelf = 0;
                    double frameTotalSelf = 0;
                    long frameGc = 0;
                    bool anyThread = false;

                    // Threads are contiguous 0..N-1 within a frame; break at the first invalid index.
                    for (int ti = 0; ti < 64; ti++)
                    {
                        using var fd = ProfilerDriver.GetHierarchyFrameDataView(
                            fi, ti,
                            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                            HierarchyFrameDataView.columnSelfTime, false);
                        if (fd == null || !fd.valid) break;
                        anyThread = true;

                        string threadName = fd.threadName ?? "";
                        string threadGroup = fd.threadGroupName ?? "";
                        double threadSelf = 0;
                        long threadGc = 0;
                        WalkThread(fd, fd.GetRootItemID(), threadName, threadGroup, markerAccum, ref threadSelf, ref threadGc);

                        if (!threadTotals.TryGetValue(threadName, out var tt))
                        {
                            tt = new ThreadTotal { Name = threadName, Group = threadGroup };
                            threadTotals[threadName] = tt;
                        }
                        tt.SelfMs += threadSelf;
                        tt.GcBytes += threadGc;

                        frameTotalSelf += threadSelf;
                        frameGc += threadGc;
                        if (ti != 0) frameNonMainSelf += threadSelf;  // ti 0 == main thread
                    }

                    if (!anyThread) continue;
                    framesAnalyzed++;
                    totalSelfMs += frameTotalSelf;
                    totalGcBytes += frameGc;

                    if (frameNonMainSelf > worstNonMainSelf)
                    {
                        worstNonMainSelf = frameNonMainSelf;
                        worstFrame = fi;
                        worstTotalSelf = frameTotalSelf;
                        worstGc = frameGc;
                    }
                }

                var topBySelf = markerAccum.Values
                    .Where(m => m.SelfMs >= minMs)
                    .OrderByDescending(m => m.SelfMs)
                    .Take(topN)
                    .Select(m => new
                    {
                        marker = m.Marker,
                        thread = m.Thread,
                        group = m.Group,
                        self_ms = Math.Round(m.SelfMs, 2),
                        total_ms = Math.Round(m.TotalMs, 2),
                        calls = m.Calls,
                        gc_alloc_bytes = m.GcBytes,
                        object_name = m.ObjectName
                    })
                    .ToList();

                var topByGc = markerAccum.Values
                    .Where(m => m.GcBytes > 0)
                    .OrderByDescending(m => m.GcBytes)
                    .Take(topN)
                    .Select(m => new
                    {
                        marker = m.Marker,
                        thread = m.Thread,
                        group = m.Group,
                        gc_alloc_bytes = m.GcBytes,
                        self_ms = Math.Round(m.SelfMs, 2),
                        calls = m.Calls
                    })
                    .ToList();

                var threads = threadTotals.Values
                    .OrderByDescending(t => t.SelfMs)
                    .Select(t => new
                    {
                        name = t.Name,
                        group = t.Group,
                        self_ms = Math.Round(t.SelfMs, 2),
                        gc_alloc_bytes = t.GcBytes
                    })
                    .ToList();

                object worstFrameObj = null;
                if (worstFrame >= 0)
                {
                    var wfMarkers = new Dictionary<(string thread, string marker), MarkerAccum>();
                    for (int ti = 0; ti < 64; ti++)
                    {
                        using var fd = ProfilerDriver.GetHierarchyFrameDataView(
                            worstFrame, ti,
                            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                            HierarchyFrameDataView.columnSelfTime, false);
                        if (fd == null || !fd.valid) break;
                        double s = 0; long g = 0;
                        WalkThread(fd, fd.GetRootItemID(), fd.threadName ?? "", fd.threadGroupName ?? "", wfMarkers, ref s, ref g);
                    }

                    var wfTop = wfMarkers.Values
                        .Where(m => m.SelfMs >= minMs)
                        .OrderByDescending(m => m.SelfMs)
                        .Take(topN)
                        .Select(m => new
                        {
                            marker = m.Marker,
                            thread = m.Thread,
                            self_ms = Math.Round(m.SelfMs, 2),
                            gc_alloc_bytes = m.GcBytes
                        })
                        .ToList();

                    worstFrameObj = new
                    {
                        frame = worstFrame,
                        nonmain_self_ms = Math.Round(worstNonMainSelf, 2),
                        total_self_ms = Math.Round(worstTotalSelf, 2),
                        gc_alloc_bytes = worstGc,
                        top_markers = wfTop
                    };
                }

                string summary = topBySelf.Count > 0
                    ? $"Event '{label}': {framesAnalyzed} frame(s). Hottest: {topBySelf[0].marker} ({topBySelf[0].thread}) {topBySelf[0].self_ms}ms self."
                    : $"Event '{label}': {framesAnalyzed} frame(s), no markers above {minMs}ms.";

                return new
                {
                    success = true,
                    message = summary,
                    data = new
                    {
                        label,
                        frames_analyzed = framesAnalyzed,
                        frames_dropped = dropped,
                        truncated,
                        play_mode = EditorApplication.isPlaying,
                        profiler_was_enabled = !session.ForcedProfilerOn,
                        total_self_ms = Math.Round(totalSelfMs, 2),
                        total_gc_bytes = totalGcBytes,
                        threads,
                        top_by_self = topBySelf,
                        top_by_gc = topByGc,
                        worst_frame = worstFrameObj
                    }
                };
            }
            finally
            {
                if (session.ForcedProfilerOn)
                    UnityEngine.Profiling.Profiler.enabled = false;
                _windows.Remove(label);
            }
        }

        // Private DFS walker (per the codebase's per-file-private-walker convention; cf. HierarchyOps).
        // Accumulates per (thread, marker) and returns this thread's total self-time + GC for the frame.
        private static void WalkThread(HierarchyFrameDataView fd, int itemId, string threadName, string threadGroup,
            Dictionary<(string thread, string marker), MarkerAccum> accum, ref double threadSelf, ref long threadGc)
        {
            var children = new List<int>();
            fd.GetItemChildren(itemId, children);

            foreach (int childId in children)
            {
                string name = fd.GetItemName(childId);
                float selfTime = fd.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnSelfTime);
                float totalTime = fd.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnTotalTime);
                int calls = (int)fd.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnCalls);
                long gc = (long)fd.GetItemColumnDataAsSingle(childId, HierarchyFrameDataView.columnGcMemory);
                string objectName = fd.GetItemColumnData(childId, HierarchyFrameDataView.columnObjectName);

                var key = (threadName, name);
                if (!accum.TryGetValue(key, out var m))
                {
                    m = new MarkerAccum { Marker = name, Thread = threadName, Group = threadGroup, ObjectName = objectName ?? "" };
                    accum[key] = m;
                }
                m.SelfMs += selfTime;
                m.TotalMs += totalTime;
                m.Calls += calls;
                m.GcBytes += gc;

                threadSelf += selfTime;
                threadGc += gc;

                WalkThread(fd, childId, threadName, threadGroup, accum, ref threadSelf, ref threadGc);
            }
        }

        private sealed class MarkerAccum
        {
            public string Marker;
            public string Thread;
            public string Group;
            public string ObjectName;
            public double SelfMs;
            public double TotalMs;
            public int Calls;
            public long GcBytes;
        }

        private sealed class ThreadTotal
        {
            public string Name;
            public string Group;
            public double SelfMs;
            public long GcBytes;
        }
    }
}
