using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameDebuggerOps
    {
        private static readonly Type UtilType;
        private static readonly PropertyInfo EventCountProp;
        private static readonly MethodInfo EnableMethod;
        private static readonly MethodInfo GetFrameEventsMethod;
        private static readonly MethodInfo GetEventDataMethod;
        private static readonly MethodInfo GetEventInfoNameMethod;
        private static readonly Type EventDataType;
        private static readonly MethodInfo BatchBreakCauseStringsMethod;
        private static readonly bool Available;

        // Batch-break-cause index -> human-readable string (FrameDebuggerUtility.GetBatchBreakCauseStrings()).
        // Resolved lazily and cached on first use.
        private static string[] _batchBreakCauses;
        private static bool _batchBreakCausesResolved;

        static FrameDebuggerOps()
        {
            try
            {
                // Unity 6+: moved to FrameDebuggerInternal sub-namespace
                UtilType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility, UnityEditor");
                // Unity 2021–2022: original location
                UtilType ??= Type.GetType("UnityEditorInternal.FrameDebuggerUtility, UnityEditor");

                if (UtilType == null) return;

                EventCountProp = UtilType.GetProperty("count", BindingFlags.Public | BindingFlags.Static)
                              ?? UtilType.GetProperty("eventsCount", BindingFlags.Public | BindingFlags.Static);

                EnableMethod = UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static,
                                   null, new[] { typeof(bool), typeof(int) }, null)
                            ?? UtilType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static);

                GetFrameEventsMethod = UtilType.GetMethod("GetFrameEvents", BindingFlags.Public | BindingFlags.Static);
                GetEventInfoNameMethod = UtilType.GetMethod("GetFrameEventInfoName", BindingFlags.Public | BindingFlags.Static);

                // Unity 6: GetFrameEventData(int, FrameDebuggerEventData) — 2 params, returns bool
                // Older: GetFrameEventData(int) — 1 param, returns event data object
                EventDataType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData, UnityEditor")
                             ?? Type.GetType("UnityEditorInternal.FrameDebuggerEventData, UnityEditor");

                if (EventDataType != null)
                {
                    GetEventDataMethod = UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static,
                                             null, new[] { typeof(int), EventDataType }, null);
                }
                GetEventDataMethod ??= UtilType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static);

                BatchBreakCauseStringsMethod = UtilType.GetMethod("GetBatchBreakCauseStrings",
                                                   BindingFlags.Public | BindingFlags.Static);

                Available = EventCountProp != null && EnableMethod != null;
            }
            catch
            {
                Available = false;
            }
        }

        internal static object Enable(JObject @params)
        {
            if (!Available)
                return new ErrorResponse("FrameDebuggerUtility not found via reflection.");

            // Open the Frame Debugger window (required for event capture)
            EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");

            // Frame Debugger requires game to be paused before enabling to capture events.
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                return new ErrorResponse(
                    "Game must be paused before enabling Frame Debugger. "
                    + "Call manage_editor action=pause first, then retry frame_debugger_enable.");
            }

            try
            {
                InvokeSetEnabled(true);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to enable Frame Debugger: {ex.Message}");
            }

            int eventCount = GetEventCount();
            return new SuccessResponse("Frame Debugger enabled.", new
            {
                enabled = true,
                event_count = eventCount,
            });
        }

        internal static object Disable(JObject @params)
        {
            if (!Available)
                return new ErrorResponse("FrameDebuggerUtility not found via reflection.");

            try
            {
                InvokeSetEnabled(false);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to disable Frame Debugger: {ex.Message}");
            }

            return new SuccessResponse("Frame Debugger disabled.", new { enabled = false });
        }

        internal static object GetEvents(JObject @params)
        {
            if (!Available)
                return new ErrorResponse("FrameDebuggerUtility not found via reflection.");

            var p = new ToolParams(@params);
            int pageSize = p.GetInt("page_size") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;
            bool includeRenderState = p.GetBool("include_render_state");

            int totalEvents = GetEventCount();
            if (totalEvents == 0)
            {
                return new SuccessResponse("Frame Debugger has no events. Is it enabled?", new
                {
                    events = new List<object>(),
                    total_events = 0,
                });
            }

            // Try GetFrameEvents() for the event descriptor array (has type/name info)
            object[] frameEvents = null;
            if (GetFrameEventsMethod != null)
            {
                try
                {
                    var raw = GetFrameEventsMethod.Invoke(null, null);
                    if (raw is Array arr)
                    {
                        frameEvents = new object[arr.Length];
                        arr.CopyTo(frameEvents, 0);
                    }
                }
                catch { /* fall through */ }
            }

            var events = new List<object>();
            int end = Math.Min(cursor + pageSize, totalEvents);

            for (int i = cursor; i < end; i++)
            {
                var entry = new Dictionary<string, object> { ["index"] = i };

                // Get event name
                if (GetEventInfoNameMethod != null)
                {
                    try { entry["name"] = (string)GetEventInfoNameMethod.Invoke(null, new object[] { i }); }
                    catch { /* skip */ }
                }

                // Get fields from FrameDebuggerEvent descriptor
                if (frameEvents != null && i < frameEvents.Length)
                {
                    var desc = frameEvents[i];
                    var descType = desc.GetType();
                    TryAddField(descType, desc, "type", entry, "event_type");
                    TryAddField(descType, desc, "gameObjectInstanceID", entry);
                }

                // Get detailed event data
                if (GetEventDataMethod != null)
                {
                    try
                    {
                        var paramInfos = GetEventDataMethod.GetParameters();
                        object eventData;

                        if (paramInfos.Length == 2 && EventDataType != null)
                        {
                            // Unity 6: bool GetFrameEventData(int, FrameDebuggerEventData)
                            eventData = Activator.CreateInstance(EventDataType);
                            var args = new object[] { i, eventData };
                            var ok = GetEventDataMethod.Invoke(null, args);
                            eventData = (ok is true) ? args[1] : null;
                        }
                        else
                        {
                            // Older: FrameDebuggerEventData GetFrameEventData(int)
                            eventData = GetEventDataMethod.Invoke(null, new object[] { i });
                        }

                        if (eventData != null)
                        {
                            var edType = eventData.GetType();
                            // Field names differ across Unity versions: 2021/2022 use unprefixed
                            // names, Unity 6 uses m_-prefixed names. Try each candidate in order;
                            // the first member present maps to a stable output key.
                            TryAddFieldAny(edType, eventData, entry, "shaderName", "shaderName", "m_RealShaderName", "m_OriginalShaderName");
                            TryAddFieldAny(edType, eventData, entry, "passName", "passName", "m_PassName");
                            TryAddFieldAny(edType, eventData, entry, "rtName", "rtName", "m_RenderTargetName");
                            TryAddFieldAny(edType, eventData, entry, "rtWidth", "rtWidth", "m_RenderTargetWidth");
                            TryAddFieldAny(edType, eventData, entry, "rtHeight", "rtHeight", "m_RenderTargetHeight");
                            TryAddFieldAny(edType, eventData, entry, "vertexCount", "vertexCount", "m_VertexCount");
                            TryAddFieldAny(edType, eventData, entry, "indexCount", "indexCount", "m_IndexCount");
                            TryAddFieldAny(edType, eventData, entry, "instanceCount", "instanceCount", "m_InstanceCount");
                            TryAddFieldAny(edType, eventData, entry, "meshName", "meshName");
                            TryAddFieldAny(edType, eventData, entry, "draw_call_count", "drawCallCount", "m_DrawCallCount");
                            TryAddFieldAny(edType, eventData, entry, "pass_light_mode", "passLightMode", "m_PassLightMode");
                            TryAddFieldAny(edType, eventData, entry, "shader_keywords", "shaderKeywords", "m_ShaderKeywords", "keywords");

                            AddBatchBreakCause(edType, eventData, entry);

                            if (includeRenderState)
                                AddRenderState(edType, eventData, entry);
                        }
                    }
                    catch { /* skip event data for this index */ }
                }

                events.Add(entry);
            }

            var result = new Dictionary<string, object>
            {
                ["events"] = events,
                ["total_events"] = totalEvents,
                ["page_size"] = pageSize,
                ["cursor"] = cursor,
            };
            if (end < totalEvents)
                result["next_cursor"] = end;

            return new SuccessResponse($"Frame Debugger events {cursor}-{end - 1} of {totalEvents}.", result);
        }

        private static void InvokeSetEnabled(bool value)
        {
            int paramCount = EnableMethod.GetParameters().Length;
            if (paramCount == 2)
                EnableMethod.Invoke(null, new object[] { value, 0 });
            else if (paramCount == 1)
                EnableMethod.Invoke(null, new object[] { value });
            else
                throw new InvalidOperationException($"SetEnabled has unexpected {paramCount} parameters.");
        }

        private static int GetEventCount()
        {
            try { return (int)EventCountProp.GetValue(null); }
            catch { return 0; }
        }

        private static object GetMember(Type type, object obj, string memberName)
        {
            try
            {
                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance)
                         ?? type.GetField(memberName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(obj);
                var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)
                        ?? type.GetProperty(memberName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetValue(obj);
            }
            catch { /* skip unavailable members */ }
            return null;
        }

        private static void TryAddField(Type type, object obj, string fieldName, Dictionary<string, object> dict, string outputKey = null)
        {
            var val = GetMember(type, obj, fieldName);
            if (val != null)
                dict[outputKey ?? fieldName] = val.GetType().IsEnum ? val.ToString() : val;
        }

        // Adds the first present member (by exact name) under a stable output key.
        private static void TryAddFieldAny(Type type, object obj, Dictionary<string, object> dict, string outputKey, params string[] candidateNames)
        {
            if (dict.ContainsKey(outputKey)) return;
            foreach (var name in candidateNames)
            {
                var val = GetMember(type, obj, name);
                if (val != null)
                {
                    dict[outputKey] = val.GetType().IsEnum ? val.ToString() : val;
                    return;
                }
            }
        }

        private static void AddBatchBreakCause(Type edType, object eventData, Dictionary<string, object> entry)
        {
            object raw = GetMember(edType, eventData, "m_BatchBreakCause")
                       ?? GetMember(edType, eventData, "batchBreakCause");
            if (raw == null) return;

            entry["batch_break_cause"] = raw.GetType().IsEnum ? raw.ToString() : raw;

            int causeIndex;
            try { causeIndex = Convert.ToInt32(raw); }
            catch { return; }

            var causes = GetBatchBreakCauses();
            if (causes != null && causeIndex >= 0 && causeIndex < causes.Length)
                entry["batch_break_cause_text"] = causes[causeIndex];
        }

        private static void AddRenderState(Type edType, object eventData, Dictionary<string, object> entry)
        {
            var renderState = new Dictionary<string, object>();
            AddState(edType, eventData, renderState, "blend", "m_BlendState", "blendState");
            AddState(edType, eventData, renderState, "raster", "m_RasterState", "rasterState");
            AddState(edType, eventData, renderState, "depth", "m_DepthState", "depthState");
            AddState(edType, eventData, renderState, "stencil", "m_StencilState", "stencilState");
            if (renderState.Count > 0)
                entry["render_state"] = renderState;
        }

        private static void AddState(Type edType, object eventData, Dictionary<string, object> renderState, string key, params string[] candidateNames)
        {
            object state = null;
            foreach (var name in candidateNames)
            {
                state = GetMember(edType, eventData, name);
                if (state != null) break;
            }
            var serialized = SerializeState(state);
            if (serialized != null)
                renderState[key] = serialized;
        }

        // Reflects a render-state struct's fields into a dict, stripping the m_ prefix and
        // stringifying enums (BlendMode, CullMode, CompareFunction, StencilOp, etc.).
        private static Dictionary<string, object> SerializeState(object state)
        {
            if (state == null) return null;
            var result = new Dictionary<string, object>();
            foreach (var f in state.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object val;
                try { val = f.GetValue(state); }
                catch { continue; }
                if (val == null) continue;
                result[StripFieldPrefix(f.Name)] = val.GetType().IsEnum ? val.ToString() : val;
            }
            return result.Count > 0 ? result : null;
        }

        private static string StripFieldPrefix(string name)
        {
            string n = name;
            if (n.StartsWith("m_") && n.Length > 2)
                n = n.Substring(2);
            if (n.Length > 0 && char.IsUpper(n[0]))
                n = char.ToLowerInvariant(n[0]) + n.Substring(1);
            return n;
        }

        private static string[] GetBatchBreakCauses()
        {
            if (_batchBreakCausesResolved) return _batchBreakCauses;
            _batchBreakCausesResolved = true;
            try
            {
                if (BatchBreakCauseStringsMethod != null)
                    _batchBreakCauses = BatchBreakCauseStringsMethod.Invoke(null, null) as string[];
            }
            catch { _batchBreakCauses = null; }
            return _batchBreakCauses;
        }

    }
}
