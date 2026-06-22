using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Graphics
{
    /// <summary>
    /// Reflection bridge to URP/SRP RenderGraph debug data (Unity 6 / Core RP 17+).
    /// Every RenderGraph debug type is <c>internal</c>, so all access goes through reflection
    /// and degrades gracefully when the API/types are absent (built-in RP, older URP, or
    /// Compatibility Mode). Surfaces the pass list, per-pass resource read/write, resource
    /// creation/release lifetimes, and (NRP compiler) pass merge/break reasons + load/store
    /// actions — the redundant-pass / RT-bandwidth diagnostic, especially valuable on
    /// tile-based (Metal/Apple Silicon) GPUs.
    ///
    /// Capture is a two-call flow because debug data only populates after the editor renders
    /// the URP camera with a debug session active:
    ///   1. First call arms a local debug session and requests a repaint.
    ///   2. A follow-up call (after a Game/Scene view has rendered) returns the parsed data.
    /// Pass <c>stop=true</c> to end the session (debug-data generation has a per-frame cost).
    ///
    /// Schema mirrors UnityEngine.Rendering.RenderGraphModule.RenderGraph.DebugData (Core RP 17).
    /// </summary>
    internal static class RenderGraphOps
    {
        private const string RuntimeAsm = "Unity.RenderPipelines.Core.Runtime";
        private const string EditorAsm = "Unity.RenderPipelines.Core.Editor";

        private static readonly Type SessionType;       // RenderGraphDebugSession (Runtime)
        private static readonly Type LocalSessionType;  // RenderGraphEditorLocalDebugSession (Editor)
        private static readonly bool Available;

        static RenderGraphOps()
        {
            try
            {
                SessionType = Type.GetType(
                    $"UnityEngine.Rendering.RenderGraphModule.RenderGraphDebugSession, {RuntimeAsm}");
                LocalSessionType = Type.GetType(
                    $"UnityEngine.Rendering.RenderGraphModule.RenderGraphEditorLocalDebugSession, {EditorAsm}");
                Available = SessionType != null && LocalSessionType != null;
            }
            catch
            {
                Available = false;
            }
        }

        // === render_graph_get ===
        internal static object GetRenderGraph(JObject @params)
        {
            if (!Available)
                return new ErrorResponse(
                    "RenderGraph debug API not found. Requires URP/HDRP on Unity 6 / Core RP 17+ "
                    + "with Render Graph enabled (Project Settings > Graphics, not Compatibility Mode).");

            var p = new ToolParams(@params);

            // stop=true tears down the active debug session (data generation has a per-frame cost).
            if (p.GetBool("stop"))
            {
                if (IsSessionActive())
                    InvokeStatic("EndSession");
                return new { success = true, message = "Render Graph debug session stopped." };
            }

            // Arm: no active session yet -> create a local editor debug session and request a render.
            if (!IsSessionActive())
            {
                InvokeStatic("Create", LocalSessionType);
                RequestRepaint();
                return new
                {
                    success = true,
                    message = "Render Graph capture armed. Ensure a Game or Scene view is visible and "
                              + "rendering a URP camera, then call render_graph_get again to read the data.",
                    data = new { status = "capturing" }
                };
            }

            var graphs = ToStringList(InvokeStatic("GetRegisteredGraphs"));
            if (graphs.Count == 0)
            {
                RequestRepaint();
                return new
                {
                    success = true,
                    message = "Debug session active but no render graphs captured yet. Render a URP "
                              + "camera (Game/Scene view) and retry render_graph_get.",
                    data = new { status = "capturing", graphs }
                };
            }

            try
            {
                return ReadGraph(p, graphs);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to read RenderGraph debug data: {ex.Message}");
            }
        }

        private static object ReadGraph(ToolParams p, List<string> graphs)
        {
            int pageSize = p.GetInt("page_size") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;
            string selectGraph = p.Get("graph");
            string selectExecution = p.Get("execution");

            string graphName = !string.IsNullOrEmpty(selectGraph) && graphs.Contains(selectGraph)
                ? selectGraph : graphs[0];

            // Executions = per-camera render passes of this graph.
            var executions = InvokeStatic("GetExecutions", graphName) as IEnumerable;
            var execIds = new List<object>();
            var execNames = new List<string>();
            if (executions != null)
            {
                foreach (var e in executions)
                {
                    execIds.Add(GetMember(e, "id"));
                    execNames.Add(GetMember(e, "name") as string);
                }
            }

            if (execIds.Count == 0)
            {
                RequestRepaint();
                return new
                {
                    success = true,
                    message = $"Graph '{graphName}' has no executions captured yet. Retry after a render.",
                    data = new { status = "capturing", graphs, graph = graphName }
                };
            }

            int execIndex = 0;
            if (!string.IsNullOrEmpty(selectExecution))
            {
                int idx = execNames.FindIndex(n => string.Equals(n, selectExecution, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) execIndex = idx;
            }

            object debugData = InvokeStatic("GetDebugData", graphName, execIds[execIndex]);
            if (debugData == null)
                return new ErrorResponse($"No debug data for graph '{graphName}', execution '{execNames[execIndex]}'.");

            // --- Passes (paged) ---
            var passObjs = ToObjectList(GetMember(debugData, "passList"));
            int totalPasses = passObjs.Count;
            int end = Math.Min(cursor + pageSize, totalPasses);
            bool anyNrp = false;
            var passes = new List<object>();
            for (int i = cursor; i < end; i++)
            {
                var pass = passObjs[i];
                var entry = new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = GetMember(pass, "name"),
                    ["type"] = GetMember(pass, "type")?.ToString(),
                    ["culled"] = GetMember(pass, "culled"),
                    ["async"] = GetMember(pass, "async"),
                    ["native_subpass_index"] = GetMember(pass, "nativeSubPassIndex"),
                    ["sync_to_pass_index"] = GetMember(pass, "syncToPassIndex"),
                    ["reads_textures"] = ToIntList(Indexer(GetMember(pass, "resourceReadLists"), 0)),
                    ["writes_textures"] = ToIntList(Indexer(GetMember(pass, "resourceWriteLists"), 0)),
                };

                var nativePassInfo = GetMember(GetMember(pass, "nrpInfo"), "nativePassInfo");
                if (nativePassInfo != null)
                {
                    anyNrp = true;
                    var merge = new Dictionary<string, object>
                    {
                        ["break_reason"] = GetMember(nativePassInfo, "passBreakReasoning"),
                        ["merged_pass_ids"] = ToIntList(GetMember(nativePassInfo, "mergedPassIds")),
                        ["attachments"] = ReadAttachments(GetMember(nativePassInfo, "attachmentInfos")),
                    };
                    entry["native_render_pass"] = merge;
                }

                passes.Add(entry);
            }

            // --- Texture resources (lifetimes + size/format) ---
            var resources = ReadTextureResources(GetMember(debugData, "resourceLists"));

            var data = new Dictionary<string, object>
            {
                ["graphs"] = graphs,
                ["graph"] = graphName,
                ["executions"] = execNames,
                ["execution"] = execNames[execIndex],
                // NRP compiler present => merge/break + load/store data is available (Unity 6 native render pass).
                ["compiler_is_nrp"] = anyNrp,
                ["passes"] = passes,
                ["pass_count"] = totalPasses,
                ["page_size"] = pageSize,
                ["cursor"] = cursor,
                ["resources"] = resources,
            };
            if (end < totalPasses)
                data["next_cursor"] = end;

            return new
            {
                success = true,
                message = $"Render graph '{graphName}' / '{execNames[execIndex]}': "
                          + $"passes {cursor}-{Math.Max(cursor, end - 1)} of {totalPasses}"
                          + (anyNrp ? " (NRP: merge/break + load/store available)." : "."),
                data
            };
        }

        private static List<object> ReadAttachments(object attachmentInfos)
        {
            var result = new List<object>();
            foreach (var att in ToObjectList(attachmentInfos))
            {
                var inner = GetMember(att, "attachment");
                result.Add(new Dictionary<string, object>
                {
                    ["resource"] = GetMember(att, "resourceName"),
                    ["attachment_index"] = GetMember(att, "attachmentIndex"),
                    ["load_reason"] = GetMember(att, "loadReason"),
                    ["store_reason"] = GetMember(att, "storeReason"),
                    ["load_action"] = GetMember(inner, "loadAction")?.ToString(),
                    ["store_action"] = GetMember(inner, "storeAction")?.ToString(),
                    ["memoryless"] = GetMember(inner, "memoryless"),
                });
            }
            return result;
        }

        private static List<object> ReadTextureResources(object resourceLists)
        {
            var result = new List<object>();
            foreach (var rd in ToObjectList(Indexer(resourceLists, 0)))
            {
                var entry = new Dictionary<string, object>
                {
                    ["name"] = GetMember(rd, "name"),
                    ["imported"] = GetMember(rd, "imported"),
                    ["memoryless"] = GetMember(rd, "memoryless"),
                    ["creation_pass_index"] = GetMember(rd, "creationPassIndex"),
                    ["release_pass_index"] = GetMember(rd, "releasePassIndex"),
                };
                int? created = AsInt(GetMember(rd, "creationPassIndex"));
                int? released = AsInt(GetMember(rd, "releasePassIndex"));
                if (created.HasValue && released.HasValue && released.Value >= created.Value)
                    entry["lifetime_passes"] = released.Value - created.Value;

                var texData = GetMember(rd, "textureData");
                if (texData != null)
                {
                    entry["width"] = GetMember(texData, "width");
                    entry["height"] = GetMember(texData, "height");
                    entry["format"] = GetMember(texData, "format")?.ToString();
                    entry["samples"] = GetMember(texData, "samples");
                }
                result.Add(entry);
            }
            return result;
        }

        // ---------- reflection helpers ----------

        private static bool IsSessionActive()
        {
            var prop = SessionType.GetProperty("hasActiveDebugSession",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return prop?.GetValue(null) is bool b && b;
        }

        private static MethodInfo StaticMethod(string name, int argCount)
        {
            foreach (var m in SessionType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == argCount)
                    return m;
            return null;
        }

        private static object InvokeStatic(string name, params object[] args)
        {
            var m = StaticMethod(name, args?.Length ?? 0);
            return m?.Invoke(null, args);
        }

        // Field or property (public or non-public), searching the type hierarchy so inherited
        // private members (e.g. ResourceLists<T> backing fields) resolve.
        private static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
                var pr = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pr != null) return pr.GetValue(obj);
            }
            return null;
        }

        // ResourceLists<T> exposes `public List<T> this[int index]` keyed by RenderGraphResourceType
        // (0 = Texture, 1 = Buffer, 2 = AccelerationStructure).
        private static object Indexer(object listsObj, int index)
        {
            if (listsObj == null) return null;
            for (var t = listsObj.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod("get_Item",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(int) }, null);
                if (m != null) return m.Invoke(listsObj, new object[] { index });
            }
            return null;
        }

        private static List<object> ToObjectList(object enumerable)
        {
            var result = new List<object>();
            if (enumerable is IEnumerable e)
                foreach (var item in e)
                    result.Add(item);
            return result;
        }

        private static List<string> ToStringList(object enumerable)
        {
            var result = new List<string>();
            if (enumerable is IEnumerable e)
                foreach (var item in e)
                    result.Add(item?.ToString());
            return result;
        }

        private static List<int> ToIntList(object enumerable)
        {
            var result = new List<int>();
            if (enumerable is IEnumerable e)
                foreach (var item in e)
                {
                    var i = AsInt(item);
                    if (i.HasValue) result.Add(i.Value);
                }
            return result;
        }

        private static int? AsInt(object value)
        {
            if (value == null) return null;
            try { return Convert.ToInt32(value); }
            catch { return null; }
        }

        private static void RequestRepaint()
        {
            try { InternalEditorUtility.RepaintAllViews(); } catch { }
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
        }
    }
}
