using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class MemoryOps
    {
        // --- Static state for labeled snapshots ---

        private static readonly Dictionary<string, MemorySnapshotData> _snapshots = new();

        [UnityEditor.InitializeOnLoadMethod]
        private static void CleanupOnDomainReload()
        {
            _snapshots.Clear();
        }

        // === memory_snapshot ===

        internal static object Snapshot(JObject @params)
        {
            var p = new ToolParams(@params);
            string label = p.Get("label");

            double totalAllocMb = Math.Round(Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 2);
            double totalReservedMb = Math.Round(Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0), 2);
            double unusedReservedMb = Math.Round(Profiler.GetTotalUnusedReservedMemoryLong() / (1024.0 * 1024.0), 2);
            double monoUsedMb = Math.Round(Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 2);
            double monoHeapMb = Math.Round(Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0), 2);
            double graphicsMb = Math.Round(Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024.0 * 1024.0), 2);
            double tempAllocMb = Math.Round(Profiler.GetTempAllocatorSize() / (1024.0 * 1024.0), 2);
            int gcCollections = GC.CollectionCount(0);

            // Read GC alloc per frame via ProfilerRecorder (LastValue)
            long gcAllocPerFrame = 0;
            try
            {
                using var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                gcAllocPerFrame = gcRecorder.CurrentValue;
            }
            catch { }

            var snapshot = new MemorySnapshotData
            {
                TotalAllocatedMb = totalAllocMb,
                TotalReservedMb = totalReservedMb,
                UnusedReservedMb = unusedReservedMb,
                MonoUsedMb = monoUsedMb,
                MonoHeapMb = monoHeapMb,
                GraphicsDriverMb = graphicsMb,
                TempAllocatorMb = tempAllocMb,
                GcCollections = gcCollections,
                GcAllocPerFrameBytes = gcAllocPerFrame,
                Timestamp = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(label))
                _snapshots[label] = snapshot;

            string msg = string.IsNullOrEmpty(label)
                ? "Memory snapshot captured."
                : $"Memory snapshot captured (label: '{label}').";

            return new
            {
                success = true,
                message = msg,
                data = new
                {
                    label = label ?? "",
                    total_allocated_mb = totalAllocMb,
                    total_reserved_mb = totalReservedMb,
                    unused_reserved_mb = unusedReservedMb,
                    mono_used_mb = monoUsedMb,
                    mono_heap_mb = monoHeapMb,
                    graphics_driver_mb = graphicsMb,
                    temp_allocator_mb = tempAllocMb,
                    gc_collections = gcCollections,
                    gc_alloc_per_frame_bytes = gcAllocPerFrame,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === memory_compare ===

        internal static object Compare(JObject @params)
        {
            var p = new ToolParams(@params);
            string labelA = p.Get("label_a", "labelA");
            string labelB = p.Get("label_b", "labelB");

            if (string.IsNullOrEmpty(labelA) || string.IsNullOrEmpty(labelB))
                return new ErrorResponse("'label_a' and 'label_b' parameters are required.");

            if (!_snapshots.TryGetValue(labelA, out var a))
                return new ErrorResponse($"Snapshot '{labelA}' not found.");
            if (!_snapshots.TryGetValue(labelB, out var b))
                return new ErrorResponse($"Snapshot '{labelB}' not found.");

            var fields = new List<object>();
            void AddField(string name, double valA, double valB)
            {
                double delta = Math.Round(valB - valA, 2);
                double pct = valA != 0 ? Math.Round((valB - valA) / valA * 100, 1) : 0;
                fields.Add(new { field = name, before = valA, after = valB, delta_mb = delta, delta_pct = pct });
            }

            AddField("total_allocated_mb", a.TotalAllocatedMb, b.TotalAllocatedMb);
            AddField("total_reserved_mb", a.TotalReservedMb, b.TotalReservedMb);
            AddField("mono_used_mb", a.MonoUsedMb, b.MonoUsedMb);
            AddField("mono_heap_mb", a.MonoHeapMb, b.MonoHeapMb);
            AddField("graphics_driver_mb", a.GraphicsDriverMb, b.GraphicsDriverMb);

            double totalDelta = Math.Round(b.TotalAllocatedMb - a.TotalAllocatedMb, 2);
            string direction = totalDelta > 0 ? "increased" : totalDelta < 0 ? "decreased" : "unchanged";
            string summary = $"Total allocated {direction} by {Math.Abs(totalDelta)}MB ({labelA} → {labelB}).";

            return new
            {
                success = true,
                message = summary,
                data = new
                {
                    label_a = labelA,
                    label_b = labelB,
                    fields,
                    gc_collections_delta = b.GcCollections - a.GcCollections,
                    summary,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === memory_objects ===

        internal static object Objects(JObject @params)
        {
            var p = new ToolParams(@params);
            string typeName = p.Get("type");
            string target = p.Get("target");
            float minSizeKb = p.GetFloat("min_size_kb") ?? 0;
            int pageSize = p.GetInt("page_size") ?? 20;
            int offset = 0;
            string cursor = p.Get("cursor");
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out int parsed))
                offset = parsed;
            int maxObjects = p.GetInt("max_objects") ?? 10000;

            var allObjects = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            long minSizeBytes = (long)(minSizeKb * 1024);

            var filtered = new List<(string name, string type, long size, int instanceId)>();
            int scanned = 0;

            foreach (var obj in allObjects)
            {
                if (scanned >= maxObjects) break;
                scanned++;

                try
                {
                    string objType = obj.GetType().Name;
                    if (!string.IsNullOrEmpty(typeName) &&
                        !string.Equals(objType, typeName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string objName = obj.name;
                    if (!string.IsNullOrEmpty(target) &&
                        (string.IsNullOrEmpty(objName) || objName.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    long size = Profiler.GetRuntimeMemorySizeLong(obj);
                    if (size < minSizeBytes) continue;

                    filtered.Add((objName, objType, size, obj.GetInstanceID()));
                }
                catch { }
            }

            filtered.Sort((a, b) => b.size.CompareTo(a.size));

            var page = filtered.Skip(offset).Take(pageSize)
                .Select(o => new
                {
                    name = o.name,
                    type = o.type,
                    size_mb = Math.Round(o.size / (1024.0 * 1024.0), 3),
                    size_bytes = o.size,
                    instance_id = o.instanceId
                })
                .ToList();

            string nextCursor = (offset + pageSize < filtered.Count)
                ? (offset + pageSize).ToString()
                : null;

            return new
            {
                success = true,
                message = $"{filtered.Count} objects found (showing {offset}-{offset + page.Count}).",
                data = new
                {
                    objects = page,
                    total_count = filtered.Count,
                    page_size = pageSize,
                    next_cursor = nextCursor,
                    scanned,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === memory_type_summary ===

        internal static object TypeSummary(JObject @params)
        {
            var p = new ToolParams(@params);
            float minTotalMb = p.GetFloat("min_total_mb") ?? 1.0f;
            int maxObjects = p.GetInt("max_objects") ?? 10000;

            var allObjects = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            var typeGroups = new Dictionary<string, (int count, long totalBytes)>();
            int scanned = 0;

            foreach (var obj in allObjects)
            {
                if (scanned >= maxObjects) break;
                scanned++;

                try
                {
                    string typeName = obj.GetType().Name;
                    long size = Profiler.GetRuntimeMemorySizeLong(obj);

                    if (!typeGroups.ContainsKey(typeName))
                        typeGroups[typeName] = (0, 0);

                    var (count, total) = typeGroups[typeName];
                    typeGroups[typeName] = (count + 1, total + size);
                }
                catch { }
            }

            long minTotalBytes = (long)(minTotalMb * 1024 * 1024);
            var types = typeGroups
                .Where(kv => kv.Value.totalBytes >= minTotalBytes)
                .OrderByDescending(kv => kv.Value.totalBytes)
                .Select(kv => new
                {
                    type = kv.Key,
                    count = kv.Value.count,
                    total_mb = Math.Round(kv.Value.totalBytes / (1024.0 * 1024.0), 2),
                    avg_mb = kv.Value.count > 0
                        ? Math.Round(kv.Value.totalBytes / (double)kv.Value.count / (1024.0 * 1024.0), 3)
                        : 0.0
                })
                .ToList();

            double grandTotalMb = types.Sum(t => t.total_mb);

            return new
            {
                success = true,
                message = $"{types.Count} types above {minTotalMb}MB (total: {Math.Round(grandTotalMb, 1)}MB).",
                data = new
                {
                    types,
                    grand_total_mb = Math.Round(grandTotalMb, 1),
                    type_count = types.Count,
                    objects_scanned = scanned,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        internal sealed class MemorySnapshotData
        {
            public double TotalAllocatedMb;
            public double TotalReservedMb;
            public double UnusedReservedMb;
            public double MonoUsedMb;
            public double MonoHeapMb;
            public double GraphicsDriverMb;
            public double TempAllocatorMb;
            public int GcCollections;
            public long GcAllocPerFrameBytes;
            public DateTime Timestamp;
        }
    }
}
