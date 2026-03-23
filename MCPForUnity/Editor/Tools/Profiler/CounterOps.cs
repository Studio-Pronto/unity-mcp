using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class CounterOps
    {
        // --- Static state for sampling sessions ---

        private sealed class SamplingSession
        {
            public string Label;
            public List<(string name, string category, ProfilerRecorder recorder)> Recorders = new();
            public int Capacity;
            public DateTime StartTime;
        }

        private static readonly Dictionary<string, SamplingSession> _sessions = new();

        [UnityEditor.InitializeOnLoadMethod]
        private static void CleanupOnDomainReload()
        {
            foreach (var session in _sessions.Values)
                DisposeSession(session);
            _sessions.Clear();
        }

        private static void DisposeSession(SamplingSession session)
        {
            foreach (var (_, _, recorder) in session.Recorders)
            {
                if (recorder.Valid)
                    recorder.Dispose();
            }
        }

        internal static int GetActiveSessionCount() => _sessions.Count;

        // === sample_start ===

        internal static object SampleStart(JObject @params)
        {
            var p = new ToolParams(@params);
            string label = p.Get("label");
            if (string.IsNullOrEmpty(label))
                return new ErrorResponse("'label' parameter is required for sample_start.");

            if (_sessions.ContainsKey(label))
                return new ErrorResponse($"Session '{label}' already exists. Stop it first or use a different label.");

            int capacity = p.GetInt("capacity") ?? 300;
            string countersParam = p.Get("counters");

            if (string.IsNullOrEmpty(countersParam))
                return new ErrorResponse("'counters' parameter is required. Pass a category name (e.g. 'render', 'physics') or a JSON array of counter names.");

            var counterSpecs = ResolveCounters(countersParam, @params);
            if (counterSpecs.Count == 0)
                return new ErrorResponse($"No counters found for '{countersParam}'. Valid categories: render, scripts, memory, physics, animation, audio, lighting, network, gui, ai, video, loading, input, vr, particles, internal.");

            var session = new SamplingSession
            {
                Label = label,
                Capacity = capacity,
                StartTime = DateTime.UtcNow
            };

            int failed = 0;
            foreach (var (name, category) in counterSpecs)
            {
                try
                {
                    var recorder = ProfilerRecorder.StartNew(
                        TryResolveCategory(category),
                        name,
                        capacity);
                    if (recorder.Valid)
                        session.Recorders.Add((name, category, recorder));
                    else
                    {
                        recorder.Dispose();
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }

            if (session.Recorders.Count == 0)
            {
                return new ErrorResponse($"Failed to start any recorders for '{countersParam}'.");
            }

            _sessions[label] = session;

            return new
            {
                success = true,
                message = $"Started recording {session.Recorders.Count} counters (label: '{label}').",
                data = new
                {
                    label,
                    counters_started = session.Recorders.Count,
                    counters_failed = failed,
                    capacity,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === sample_stop ===

        internal static object SampleStop(JObject @params)
        {
            var p = new ToolParams(@params);
            string label = p.Get("label");

            if (string.IsNullOrEmpty(label))
            {
                int count = _sessions.Count;
                foreach (var session in _sessions.Values)
                    DisposeSession(session);
                _sessions.Clear();
                return new
                {
                    success = true,
                    message = $"Stopped all {count} sampling session(s).",
                    data = new { sessions_stopped = count }
                };
            }

            if (!_sessions.TryGetValue(label, out var s))
                return new ErrorResponse($"Session '{label}' not found. It may have been cleared by script recompilation.");

            int recorderCount = s.Recorders.Count;
            DisposeSession(s);
            _sessions.Remove(label);

            return new
            {
                success = true,
                message = $"Stopped session '{label}' ({recorderCount} recorders).",
                data = new { label, recorders_stopped = recorderCount }
            };
        }

        // === sample_read ===

        internal static object SampleRead(JObject @params)
        {
            var p = new ToolParams(@params);
            string label = p.Get("label");

            if (string.IsNullOrEmpty(label))
                return new ErrorResponse("'label' parameter is required for sample_read.");

            if (!_sessions.TryGetValue(label, out var session))
                return new ErrorResponse($"Session '{label}' not found. It may have been cleared by script recompilation.");

            int? lastN = p.GetInt("last_n");

            var counters = new Dictionary<string, object>();
            int minSamples = int.MaxValue;

            foreach (var (name, category, recorder) in session.Recorders)
            {
                if (!recorder.Valid) continue;

                int count = recorder.Count;
                if (count == 0) continue;

                int start = 0;
                int length = count;
                if (lastN.HasValue && lastN.Value < count)
                {
                    start = count - lastN.Value;
                    length = lastN.Value;
                }

                var values = new long[length];
                for (int i = 0; i < length; i++)
                    values[i] = recorder.GetSample(start + i).Value;

                if (length < minSamples) minSamples = length;

                counters[name] = ComputeStats(values, recorder.UnitType);
            }

            if (minSamples == int.MaxValue) minSamples = 0;

            return new
            {
                success = true,
                message = $"Read {counters.Count} counters from session '{label}'.",
                data = new
                {
                    label,
                    sample_count = minSamples,
                    counters,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === sample_compare ===

        internal static object SampleCompare(JObject @params)
        {
            var p = new ToolParams(@params);
            string labelA = p.Get("label_a", "labelA");
            string labelB = p.Get("label_b", "labelB");
            float threshold = p.GetFloat("threshold_pct") ?? 5.0f;

            if (string.IsNullOrEmpty(labelA) || string.IsNullOrEmpty(labelB))
                return new ErrorResponse("'label_a' and 'label_b' parameters are required.");

            if (!_sessions.TryGetValue(labelA, out var sessionA))
                return new ErrorResponse($"Session '{labelA}' not found.");
            if (!_sessions.TryGetValue(labelB, out var sessionB))
                return new ErrorResponse($"Session '{labelB}' not found.");

            var lookupB = new Dictionary<string, (ProfilerRecorder recorder, string category)>();
            foreach (var (name, category, recorder) in sessionB.Recorders)
                lookupB[name] = (recorder, category);

            var comparisons = new List<object>();
            int improved = 0, regressed = 0, unchanged = 0;
            var summaryParts = new List<string>();

            foreach (var (name, category, recorderA) in sessionA.Recorders)
            {
                if (!recorderA.Valid || recorderA.Count == 0) continue;
                if (!lookupB.TryGetValue(name, out var bEntry) || !bEntry.recorder.Valid || bEntry.recorder.Count == 0) continue;

                double meanA = ComputeMean(recorderA);
                double meanB = ComputeMean(bEntry.recorder);

                double delta = meanB - meanA;
                double deltaPct = meanA != 0 ? (delta / meanA) * 100.0 : 0;

                string verdict;
                if (Math.Abs(deltaPct) < threshold)
                {
                    verdict = "unchanged";
                    unchanged++;
                }
                else if (deltaPct < 0)
                {
                    verdict = "improved";
                    improved++;
                    summaryParts.Add($"{name} {deltaPct:+0.#;-0.#}%");
                }
                else
                {
                    verdict = "regressed";
                    regressed++;
                    summaryParts.Add($"{name} {deltaPct:+0.#;-0.#}%");
                }

                comparisons.Add(new
                {
                    counter = name,
                    before_mean = Math.Round(meanA, 2),
                    after_mean = Math.Round(meanB, 2),
                    delta = Math.Round(delta, 2),
                    delta_pct = Math.Round(deltaPct, 1),
                    verdict
                });
            }

            string summary = $"{improved} improved, {regressed} regressed, {unchanged} unchanged.";
            if (summaryParts.Count > 0)
                summary += " Notable: " + string.Join(", ", summaryParts.Take(5));

            return new
            {
                success = true,
                message = summary,
                data = new
                {
                    label_a = labelA,
                    label_b = labelB,
                    threshold_pct = threshold,
                    comparison = comparisons,
                    summary
                }
            };
        }

        // === sample_list ===

        internal static object SampleList()
        {
            var sessions = _sessions.Values.Select(s => new
            {
                label = s.Label,
                counter_count = s.Recorders.Count,
                capacity = s.Capacity,
                frames_accumulated = s.Recorders.Count > 0 && s.Recorders[0].recorder.Valid
                    ? s.Recorders[0].recorder.Count : 0,
                started_at = s.StartTime.ToString("O")
            }).ToList();

            return new
            {
                success = true,
                message = $"{sessions.Count} active session(s).",
                data = new { sessions }
            };
        }

        // === frame_time_get (async, blocks for N frames) ===

        internal static async Task<object> FrameTimeGet(JObject @params)
        {
            var p = new ToolParams(@params);
            int frames = p.GetInt("frames") ?? 120;
            if (frames < 1) frames = 1;
            if (frames > 1500) frames = 1500;

            var mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", frames);
            var renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", frames);
            var gpuTime = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time", frames);

            try
            {
                await ProfilerHelpers.WaitForFrames(frames);

                int collected = Math.Min(mainThread.Count, Math.Min(renderThread.Count, gpuTime.Count));
                if (collected == 0)
                {
                    return new
                    {
                        success = true,
                        message = "No frame time data collected. Are you in Play mode?",
                        data = new { frames_analyzed = 0, play_mode = EditorApplication.isPlaying }
                    };
                }

                var mainStats = ComputeStatsFromRecorder(mainThread, collected, true);
                var renderStats = ComputeStatsFromRecorder(renderThread, collected, true);
                var gpuStats = ComputeStatsFromRecorder(gpuTime, collected, true);

                double mainMean = (double)mainStats["mean"];
                double renderMean = (double)renderStats["mean"];
                double gpuMean = (double)gpuStats["mean"];

                string bottleneck;
                double maxCpu = Math.Max(mainMean, renderMean);
                if (gpuMean > maxCpu * 1.1)
                    bottleneck = "GPU";
                else if (mainMean > renderMean * 1.1 && mainMean > gpuMean)
                    bottleneck = "CPU (Main Thread)";
                else if (renderMean > mainMean * 1.1 && renderMean > gpuMean)
                    bottleneck = "CPU (Render Thread)";
                else
                    bottleneck = "Balanced";

                double avgFrameTime = Math.Max(mainMean, Math.Max(renderMean, gpuMean));
                double meanFps = avgFrameTime > 0 ? 1000.0 / avgFrameTime : 0;

                return new
                {
                    success = true,
                    message = $"Frame time captured over {collected} frames. Bottleneck: {bottleneck}.",
                    data = new
                    {
                        frames_analyzed = collected,
                        main_thread_ms = mainStats,
                        render_thread_ms = renderStats,
                        gpu_ms = gpuStats,
                        fps = new { mean = Math.Round(meanFps, 1) },
                        bottleneck,
                        budget_60fps_ms = 16.67,
                        headroom_ms = Math.Round(16.67 - avgFrameTime, 2),
                        play_mode = EditorApplication.isPlaying
                    }
                };
            }
            finally
            {
                mainThread.Dispose();
                renderThread.Dispose();
                gpuTime.Dispose();
            }
        }

        // === counter_read (async, waits 2 frames for LastValue) ===

        internal static async Task<object> CounterRead(JObject @params)
        {
            var p = new ToolParams(@params);
            string countersParam = p.Get("counters");

            if (string.IsNullOrEmpty(countersParam))
                return new ErrorResponse("'counters' parameter is required. Pass a category name or JSON array of counter names.");

            var counterSpecs = ResolveCounters(countersParam, @params);
            if (counterSpecs.Count == 0)
                return new ErrorResponse($"No counters found for '{countersParam}'.");

            var recorders = new List<(string name, ProfilerRecorder recorder)>();
            try
            {
                foreach (var (name, category) in counterSpecs)
                {
                    try
                    {
                        var recorder = ProfilerRecorder.StartNew(
                            TryResolveCategory(category), name, 4);
                        if (recorder.Valid)
                            recorders.Add((name, recorder));
                        else
                            recorder.Dispose();
                    }
                    catch { }
                }

                if (recorders.Count == 0)
                    return new ErrorResponse($"Failed to create any recorders for '{countersParam}'.");

                // Wait 2 frames so LastValue has completed data
                await ProfilerHelpers.WaitForFrames(2);

                var values = new Dictionary<string, object>();
                foreach (var (name, recorder) in recorders)
                {
                    values[name] = new
                    {
                        value = recorder.LastValue,
                        unit = recorder.UnitType.ToString()
                    };
                }

                return new
                {
                    success = true,
                    message = $"Read {values.Count} counter(s).",
                    data = new
                    {
                        counters = values,
                        play_mode = EditorApplication.isPlaying
                    }
                };
            }
            finally
            {
                foreach (var (_, recorder) in recorders)
                    recorder.Dispose();
            }
        }

        // === physics_get (async, blocks for N frames) ===

        internal static async Task<object> PhysicsGet(JObject @params)
        {
            var p = new ToolParams(@params);
            int frames = p.GetInt("frames") ?? 120;
            if (frames < 1) frames = 1;
            if (frames > 1500) frames = 1500;

            // Resolve all Physics category counters dynamically
            var physicsCounters = ResolveCounters("physics", new JObject { ["counters"] = "physics" });
            if (physicsCounters.Count == 0)
                return new ErrorResponse("No Physics profiler counters found.");

            var recorders = new List<(string name, ProfilerRecorder recorder)>();
            try
            {
                foreach (var (name, category) in physicsCounters)
                {
                    try
                    {
                        var recorder = ProfilerRecorder.StartNew(
                            ProfilerCategory.Physics, name, frames);
                        if (recorder.Valid)
                            recorders.Add((name, recorder));
                        else
                            recorder.Dispose();
                    }
                    catch { }
                }

                if (recorders.Count == 0)
                    return new ErrorResponse("Failed to start any Physics recorders.");

                await ProfilerHelpers.WaitForFrames(frames);

                var counters = new Dictionary<string, object>();
                int minCount = int.MaxValue;
                foreach (var (name, recorder) in recorders)
                {
                    int count = recorder.Count;
                    if (count == 0) continue;
                    if (count < minCount) minCount = count;

                    var values = new long[count];
                    for (int i = 0; i < count; i++)
                        values[i] = recorder.GetSample(i).Value;
                    counters[name] = ComputeStats(values, recorder.UnitType);
                }

                if (minCount == int.MaxValue) minCount = 0;

                return new
                {
                    success = true,
                    message = $"Physics stats captured over {minCount} frames ({counters.Count} counters).",
                    data = new
                    {
                        frames_analyzed = minCount,
                        counters,
                        play_mode = EditorApplication.isPlaying
                    }
                };
            }
            finally
            {
                foreach (var (_, recorder) in recorders)
                    recorder.Dispose();
            }
        }

        // === counter_list ===

        internal static object CounterList(JObject @params)
        {
            var p = new ToolParams(@params);
            string category = p.Get("category");
            string search = p.Get("search");
            int pageSize = p.GetInt("page_size") ?? 50;
            int offset = 0;
            string cursor = p.Get("cursor");
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out int parsed))
                offset = parsed;

            var allHandles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(allHandles);

            var counters = new List<(string name, string cat, string unit)>();
            foreach (var handle in allHandles)
            {
                var desc = ProfilerRecorderHandle.GetDescription(handle);

                if (!string.IsNullOrEmpty(category))
                {
                    var resolved = TryResolveCategory(category);
                    if (!string.Equals(desc.Category.Name, resolved.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (!string.IsNullOrEmpty(search) &&
                    desc.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                counters.Add((desc.Name, desc.Category.Name, desc.UnitType.ToString()));
            }

            counters.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            var page = counters.Skip(offset).Take(pageSize)
                .Select(c => new { name = c.name, category = c.cat, unit = c.unit })
                .ToList();

            string nextCursor = (offset + pageSize < counters.Count)
                ? (offset + pageSize).ToString()
                : null;

            return new
            {
                success = true,
                message = $"Found {counters.Count} counter(s){(string.IsNullOrEmpty(category) ? "" : $" in '{category}'")}. Showing {offset}-{offset + page.Count}.",
                data = new
                {
                    counters = page,
                    total_count = counters.Count,
                    page_size = pageSize,
                    next_cursor = nextCursor
                }
            };
        }

        // === frame_timing_get (async -- collects frames via FrameTimingManager) ===

        internal static async Task<object> FrameTimingGet(JObject @params)
        {
            var p = new ToolParams(@params);
            int frames = p.GetInt("frames") ?? 120;
            if (frames < 1) frames = 1;
            if (frames > 1500) frames = 1500;

            if (!FrameTimingManager.IsFeatureEnabled())
            {
                return new ErrorResponse(
                    "FrameTimingManager is not enabled. It may not be supported on this platform or graphics API. "
                    + "Use frame_time_get (ProfilerRecorder-based) as an alternative.");
            }

            await ProfilerHelpers.WaitForFrames(frames);

            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[frames];
            uint retrieved = FrameTimingManager.GetLatestTimings((uint)frames, timings);

            if (retrieved == 0)
            {
                return new
                {
                    success = true,
                    message = "No frame timing data collected. Are you in Play mode?",
                    data = new { frames_analyzed = 0, play_mode = EditorApplication.isPlaying }
                };
            }

            float[] cpuFrame = new float[retrieved];
            float[] cpuMain = new float[retrieved];
            float[] cpuPresent = new float[retrieved];
            float[] cpuRender = new float[retrieved];
            float[] gpu = new float[retrieved];

            for (int i = 0; i < retrieved; i++)
            {
                cpuFrame[i] = timings[i].cpuFrameTime;
                cpuMain[i] = timings[i].cpuMainThreadFrameTime;
                cpuPresent[i] = timings[i].cpuMainThreadPresentWaitTime;
                cpuRender[i] = timings[i].cpuRenderThreadFrameTime;
                gpu[i] = timings[i].gpuFrameTime;
            }

            var cpuFrameStats = ComputeFloatStats(cpuFrame, (int)retrieved);
            var cpuMainStats = ComputeFloatStats(cpuMain, (int)retrieved);
            var cpuPresentStats = ComputeFloatStats(cpuPresent, (int)retrieved);
            var cpuRenderStats = ComputeFloatStats(cpuRender, (int)retrieved);
            var gpuStats = ComputeFloatStats(gpu, (int)retrieved);

            float meanCpuMain = cpuMainStats["mean"];
            float meanCpuRender = cpuRenderStats["mean"];
            float meanGpu = gpuStats["mean"];
            float meanPresent = cpuPresentStats["mean"];

            string bottleneck;
            float maxActive = Math.Max(meanCpuMain - meanPresent, meanCpuRender);
            if (meanGpu > maxActive * 1.1f)
                bottleneck = "GPU";
            else if ((meanCpuMain - meanPresent) > meanCpuRender * 1.1f)
                bottleneck = "CPU (Main Thread)";
            else if (meanCpuRender > (meanCpuMain - meanPresent) * 1.1f)
                bottleneck = "CPU (Render Thread)";
            else
                bottleneck = "Balanced";

            float avgFrameTime = cpuFrameStats["mean"];
            float meanFps = avgFrameTime > 0 ? 1000f / avgFrameTime : 0;

            float widthScale = timings[retrieved - 1].widthScale;
            float heightScale = timings[retrieved - 1].heightScale;

            return new
            {
                success = true,
                message = $"FrameTimingManager captured {retrieved} frames. Bottleneck: {bottleneck}.",
                data = new
                {
                    frames_analyzed = (int)retrieved,
                    cpu_frame_ms = cpuFrameStats,
                    cpu_main_thread_ms = cpuMainStats,
                    cpu_present_wait_ms = cpuPresentStats,
                    cpu_render_thread_ms = cpuRenderStats,
                    gpu_ms = gpuStats,
                    fps = new { mean = Math.Round(meanFps, 1) },
                    bottleneck,
                    budget_60fps_ms = 16.67,
                    headroom_ms = Math.Round(16.67 - avgFrameTime, 2),
                    dynamic_resolution = new { width_scale = Math.Round(widthScale, 3), height_scale = Math.Round(heightScale, 3) },
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // --- Helpers ---

        private static Dictionary<string, float> ComputeFloatStats(float[] values, int count)
        {
            if (count == 0)
                return new Dictionary<string, float> { ["mean"] = 0, ["min"] = 0, ["max"] = 0, ["p95"] = 0, ["p99"] = 0 };

            var sorted = new float[count];
            Array.Copy(values, sorted, count);
            Array.Sort(sorted);
            float mean = 0;
            for (int i = 0; i < count; i++) mean += sorted[i];
            mean /= count;

            return new Dictionary<string, float>
            {
                ["mean"] = (float)Math.Round(mean, 2),
                ["min"] = (float)Math.Round(sorted[0], 2),
                ["max"] = (float)Math.Round(sorted[count - 1], 2),
                ["p95"] = (float)Math.Round(sorted[Math.Min((int)(count * 0.95), count - 1)], 2),
                ["p99"] = (float)Math.Round(sorted[Math.Min((int)(count * 0.99), count - 1)], 2)
            };
        }

        internal static List<(string name, string category)> ResolveCounters(string countersParam, JObject @params)
        {
            var result = new List<(string name, string category)>();

            // Check if it's a JSON array of counter names
            var countersToken = @params?["counters"];
            if (countersToken is JArray arr)
            {
                foreach (var item in arr)
                {
                    string name = item.ToString();
                    result.Add((name, ""));
                }
                return result;
            }

            // Otherwise treat as category name preset
            string categoryName = countersParam.ToLowerInvariant().Trim();
            var category = TryResolveCategory(categoryName);

            var allHandles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(allHandles);

            foreach (var handle in allHandles)
            {
                var desc = ProfilerRecorderHandle.GetDescription(handle);
                if (string.Equals(desc.Category.Name, category.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((desc.Name, desc.Category.Name));
                }
            }

            return result;
        }

        private static ProfilerCategory TryResolveCategory(string name)
        {
            if (string.IsNullOrEmpty(name)) return ProfilerCategory.Render;
            switch (name.ToLowerInvariant())
            {
                case "render": return ProfilerCategory.Render;
                case "scripts": return ProfilerCategory.Scripts;
                case "memory": return ProfilerCategory.Memory;
                case "physics": return ProfilerCategory.Physics;
                case "animation": return ProfilerCategory.Animation;
                case "audio": return ProfilerCategory.Audio;
                case "lighting": return ProfilerCategory.Lighting;
                case "network": return ProfilerCategory.Network;
                case "gui": return ProfilerCategory.Gui;
                case "ai": return ProfilerCategory.Ai;
                case "video": return ProfilerCategory.Video;
                case "loading": return ProfilerCategory.Loading;
                case "input": return ProfilerCategory.Input;
                case "vr": return ProfilerCategory.Vr;
                case "particles": return ProfilerCategory.Particles;
                case "internal": return ProfilerCategory.Internal;
                default: return ProfilerCategory.Render;
            }
        }

        private static Dictionary<string, object> ComputeStats(long[] values, ProfilerMarkerDataUnit unitType)
        {
            if (values.Length == 0)
                return new Dictionary<string, object> { ["mean"] = 0, ["min"] = 0, ["max"] = 0, ["p95"] = 0, ["p99"] = 0 };

            var sorted = values.OrderBy(v => v).ToArray();
            double mean = values.Average();
            long min = sorted[0];
            long max = sorted[sorted.Length - 1];
            long p95 = sorted[Math.Min((int)(sorted.Length * 0.95), sorted.Length - 1)];
            long p99 = sorted[Math.Min((int)(sorted.Length * 0.99), sorted.Length - 1)];

            if (unitType == ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                double divisor = 1_000_000.0; // ns to ms
                return new Dictionary<string, object>
                {
                    ["mean"] = Math.Round(mean / divisor, 2),
                    ["min"] = Math.Round(min / divisor, 2),
                    ["max"] = Math.Round(max / divisor, 2),
                    ["p95"] = Math.Round(p95 / divisor, 2),
                    ["p99"] = Math.Round(p99 / divisor, 2),
                    ["unit"] = "ms"
                };
            }

            return new Dictionary<string, object>
            {
                ["mean"] = Math.Round(mean, 2),
                ["min"] = min,
                ["max"] = max,
                ["p95"] = p95,
                ["p99"] = p99,
                ["unit"] = unitType.ToString()
            };
        }

        private static Dictionary<string, object> ComputeStatsFromRecorder(ProfilerRecorder recorder, int count, bool convertToMs)
        {
            if (count == 0)
                return new Dictionary<string, object> { ["mean"] = 0.0, ["min"] = 0.0, ["max"] = 0.0, ["p95"] = 0.0, ["p99"] = 0.0 };

            int start = Math.Max(0, recorder.Count - count);
            int length = Math.Min(count, recorder.Count);
            var values = new double[length];
            for (int i = 0; i < length; i++)
            {
                double v = recorder.GetSample(start + i).Value;
                if (convertToMs) v /= 1_000_000.0; // ns to ms
                values[i] = v;
            }

            Array.Sort(values);
            double mean = values.Average();

            return new Dictionary<string, object>
            {
                ["mean"] = Math.Round(mean, 2),
                ["min"] = Math.Round(values[0], 2),
                ["max"] = Math.Round(values[length - 1], 2),
                ["p95"] = Math.Round(values[Math.Min((int)(length * 0.95), length - 1)], 2),
                ["p99"] = Math.Round(values[Math.Min((int)(length * 0.99), length - 1)], 2)
            };
        }

        private static double ComputeMean(ProfilerRecorder recorder)
        {
            int count = recorder.Count;
            if (count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < count; i++)
                sum += recorder.GetSample(i).Value;
            return sum / count;
        }
    }
}
