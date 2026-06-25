using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Reflection bridge to Unity's public Play Mode Scenario API
    /// (<c>Unity.PlayMode.Editor.PlayModeScenarioManager</c>, assembly <c>UnityEditor.PlayModeModule</c>) — the
    /// Multiplayer Play Mode (MPPM) "Play Mode Scenarios" feature. Accessed via reflection (not a hard assembly
    /// reference) so MCPForUnity still compiles on Unity versions without the module (e.g. the 2021.3 floor) and
    /// degrades gracefully when MPPM is absent.
    ///
    /// Public API (decompile-verified, Unity 6000.4):
    ///   static PlayModeScenario ActiveScenario { get; set; }   // PlayModeScenario : ScriptableObject
    ///   static PlayModeScenarioState State { get; }            // enum { Idle, Starting, Running, Stopping }
    ///   static void Start();   // enters play under ActiveScenario
    ///   static void Stop();    // wedge-safe exit — exactly what the editor's Stop button calls for scenarios.
    ///
    /// Stopping a scenario with <c>EditorApplication.isPlaying = false</c> (the plain stop) instead of
    /// <c>Stop()</c> wedges the play-mode system: subsequent PlayMode test runs fail to initialize until a
    /// domain reload. <see cref="WedgeSafeStop"/> routes through <c>Stop()</c> whenever a scenario is running.
    /// </summary>
    internal static class PlayModeScenarioOps
    {
        private static readonly Type ScenarioType;
        private static readonly PropertyInfo ActiveScenarioProp;
        private static readonly PropertyInfo StateProp;
        private static readonly MethodInfo StartMethod;
        private static readonly MethodInfo StopMethod;
        private static readonly bool Available;

        static PlayModeScenarioOps()
        {
            try
            {
                Type mgrType = Type.GetType("Unity.PlayMode.Editor.PlayModeScenarioManager, UnityEditor.PlayModeModule")
                               ?? FindEditorType("PlayModeScenarioManager");
                ScenarioType = Type.GetType("Unity.PlayMode.Editor.PlayModeScenario, UnityEditor.PlayModeModule")
                               ?? FindEditorType("PlayModeScenario");

                if (mgrType != null)
                {
                    ActiveScenarioProp = mgrType.GetProperty("ActiveScenario", BindingFlags.Public | BindingFlags.Static);
                    StateProp = mgrType.GetProperty("State", BindingFlags.Public | BindingFlags.Static);
                    StartMethod = mgrType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    StopMethod = mgrType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                }

                Available = ActiveScenarioProp != null && StateProp != null && StartMethod != null && StopMethod != null;
            }
            catch
            {
                Available = false;
            }
        }

        /// <summary>Assign an MPPM Play Mode Scenario (by asset path or name) and enter play under it.</summary>
        internal static object PlayScenario(string scenario)
        {
            if (!Available)
                return new ErrorResponse(
                    "Play Mode Scenarios require Unity 6 with the Multiplayer Play Mode package "
                    + "(com.unity.multiplayer.playmode).");

            if (EditorApplication.isPlaying)
                return new SuccessResponse("Already in play mode. Stop first to switch scenarios.");

            var asset = ResolveScenarioAsset(scenario, out string resolvedName, out string error);
            if (asset == null)
                return new ErrorResponse(error);

            try
            {
                ActiveScenarioProp.SetValue(null, asset);
                StartMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Failed to start scenario '{resolvedName}': {e.InnerException?.Message ?? e.Message}");
            }

            return new SuccessResponse($"Entered play mode under scenario '{resolvedName}'.");
        }

        /// <summary>
        /// Stop play mode. If an MPPM scenario is active, route through the scenario manager's <c>Stop()</c>
        /// (wedge-safe); otherwise fall back to the plain <c>EditorApplication.isPlaying = false</c> behavior.
        /// </summary>
        internal static object WedgeSafeStop()
        {
            if (Available && IsScenarioRunning())
            {
                try
                {
                    StopMethod.Invoke(null, null);
                }
                catch (Exception e)
                {
                    return new ErrorResponse(
                        $"Error stopping play mode scenario: {e.InnerException?.Message ?? e.Message}");
                }
                return new SuccessResponse("Stopped play mode scenario.");
            }

            // No active scenario (or MPPM absent): preserve the original plain-stop behavior exactly.
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    return new SuccessResponse("Exited play mode.");
                }
                return new SuccessResponse("Already stopped (not in play mode).");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error stopping play mode: {e.Message}");
            }
        }

        private static bool IsScenarioRunning()
        {
            try
            {
                // PlayModeScenarioState: Idle | Starting | Running | Stopping. Anything but Idle is scenario-driven.
                var state = StateProp.GetValue(null)?.ToString();
                return !string.IsNullOrEmpty(state) && state != "Idle";
            }
            catch
            {
                return false;
            }
        }

        private static UnityEngine.Object ResolveScenarioAsset(string scenario, out string name, out string error)
        {
            name = null;
            error = null;

            if (string.IsNullOrWhiteSpace(scenario))
            {
                error = "'scenario' is empty.";
                return null;
            }

            // Direct asset path
            if (scenario.Contains('/') || scenario.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                var byPath = LoadScenario(scenario);
                if (byPath == null)
                {
                    error = $"No Play Mode Scenario asset at '{scenario}'.";
                    return null;
                }
                name = byPath.name;
                return byPath;
            }

            // Resolve by name among PlayModeScenario assets
            var available = new List<string>();
            string matchPath = null;
            foreach (var guid in AssetDatabase.FindAssets("t:PlayModeScenario"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var assetName = Path.GetFileNameWithoutExtension(path);
                available.Add(assetName);
                if (string.Equals(assetName, scenario, StringComparison.OrdinalIgnoreCase))
                    matchPath = path;
            }

            if (matchPath == null)
            {
                error = available.Count > 0
                    ? $"Scenario '{scenario}' not found. Available: {string.Join(", ", available)}."
                    : $"Scenario '{scenario}' not found and no Play Mode Scenario assets exist in the project.";
                return null;
            }

            var loaded = LoadScenario(matchPath);
            if (loaded == null)
            {
                error = $"Failed to load scenario asset at '{matchPath}'.";
                return null;
            }
            name = loaded.name;
            return loaded;
        }

        private static UnityEngine.Object LoadScenario(string path)
        {
            // Load typed as PlayModeScenario when the type is known (returns null if the asset isn't one),
            // else fall back to a plain ScriptableObject load.
            if (ScenarioType != null)
                return AssetDatabase.LoadAssetAtPath(path, ScenarioType);
            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        }

        private static Type FindEditorType(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name == simpleName && t.Namespace == "Unity.PlayMode.Editor")
                        return t;
                }
            }
            return null;
        }
    }
}
