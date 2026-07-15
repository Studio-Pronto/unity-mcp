using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Tests;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Starts a Unity Test Runner run asynchronously and returns a job id immediately.
    /// Use get_test_job(job_id) to poll status/results.
    /// </summary>
    [McpForUnityTool("run_tests", AutoRegister = false, Group = "testing")]
    public static class RunTests
    {
        public static Task<object> HandleCommand(JObject @params)
        {
            try
            {
                // Check for clear_stuck action first
                if (ParamCoercion.CoerceBool(@params?["clear_stuck"], false))
                {
                    bool wasCleared = TestJobManager.ClearStuckJob();
                    return Task.FromResult<object>(new SuccessResponse(
                        wasCleared ? "Stuck job cleared." : "No running job to clear.",
                        new { cleared = wasCleared }
                    ));
                }

                string modeStr = @params?["mode"]?.ToString();
                if (string.IsNullOrWhiteSpace(modeStr))
                {
                    modeStr = "EditMode";
                }

                if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
                {
                    return Task.FromResult<object>(new ErrorResponse(parseError));
                }

                var p = new ToolParams(@params);
                bool includeDetails = p.GetBool("includeDetails");
                bool includeFailedTests = p.GetBool("includeFailedTests");

                var filterOptions = GetFilterOptions(@params);
                long initTimeoutMs = p.GetInt("initTimeout") ?? 0;
                bool discardUntitled = p.GetBool("discardUntitledScenes"); // alias: discard_untitled_scenes

                // Fail-fast: a dirty untitled (pathless) scene makes UTF's SaveModifiedSceneTask pop a
                // native Save dialog that wedges the editor and all MCP traffic (EditMode and PlayMode).
                List<Scene> dirtyUntitled = CollectDirtyUntitledScenes();
                string[] discardedNames = null;
                if (dirtyUntitled.Count > 0)
                {
                    if (!discardUntitled)
                    {
                        var active = SceneManager.GetActiveScene();
                        return Task.FromResult<object>(new ErrorResponse("unsaved_untitled_scene", new
                        {
                            reason = "unsaved_untitled_scene",
                            scenes = dirtyUntitled.Select(s => new
                            {
                                name = s.name,
                                isActive = s == active,
                                rootCount = s.isLoaded ? s.rootCount : 0
                            }).ToArray(),
                            message = "One or more unsaved untitled scenes are open; starting a test run would "
                                    + "block the editor on a native Save dialog. Save them first "
                                    + "(manage_scene action=\"save\" with name/path) or re-run with "
                                    + "discard_untitled_scenes=true to discard them."
                        }));
                    }

                    // Discard opted in — but never destroy scene state for a run StartJob will reject.
                    if (TestJobManager.HasRunningJob)
                    {
                        return Task.FromResult<object>(new ErrorResponse("tests_running",
                            new { reason = "tests_running", retry_after_ms = 5000 }));
                    }

                    discardedNames = dirtyUntitled.Select(s => s.name).ToArray(); // capture before handles invalidate
                    DiscardDirtyUntitledScenes(dirtyUntitled);
                }

                string jobId = TestJobManager.StartJob(parsedMode.Value, filterOptions, initTimeoutMs);

                return Task.FromResult<object>(new SuccessResponse("Test job started.", new
                {
                    job_id = jobId,
                    status = "running",
                    mode = parsedMode.Value.ToString(),
                    include_details = includeDetails,
                    include_failed_tests = includeFailedTests,
                    discarded_untitled_scenes = discardedNames
                }));
            }
            catch (Exception ex)
            {
                // Normalize the already-running case to a stable error token.
                if (ex.Message != null && ex.Message.IndexOf("already in progress", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Task.FromResult<object>(new ErrorResponse("tests_running", new { reason = "tests_running", retry_after_ms = 5000 }));
                }
                return Task.FromResult<object>(new ErrorResponse($"Failed to start test job: {ex.Message}"));
            }
        }

        /// <summary>
        /// All loaded scenes that are dirty and have never been saved (empty path). Starting a
        /// test run with one open triggers Unity's blocking native Save dialog.
        /// Internal for tests (via InternalsVisibleTo).
        /// </summary>
        internal static List<Scene> CollectDirtyUntitledScenes()
        {
            var result = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty && string.IsNullOrEmpty(s.path))
                {
                    result.Add(s);
                }
            }
            return result;
        }

        /// <summary>
        /// Discards the given untitled scenes without prompting: closes each one, or replaces it
        /// with a fresh empty scene when it is the last loaded scene (Unity cannot close the last).
        /// Internal for tests (via InternalsVisibleTo).
        /// </summary>
        internal static void DiscardDirtyUntitledScenes(List<Scene> scenes)
        {
            foreach (var s in scenes)
            {
                if (SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(s, removeScene: true);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        private static TestFilterOptions GetFilterOptions(JObject @params)
        {
            if (@params == null)
            {
                return null;
            }

            var p = new ToolParams(@params);
            var testNames = p.GetStringArray("testNames");
            var groupNames = p.GetStringArray("groupNames");
            var categoryNames = p.GetStringArray("categoryNames");
            var assemblyNames = p.GetStringArray("assemblyNames");

            if (testNames == null && groupNames == null && categoryNames == null && assemblyNames == null)
            {
                return null;
            }

            return new TestFilterOptions
            {
                TestNames = testNames,
                GroupNames = groupNames,
                CategoryNames = categoryNames,
                AssemblyNames = assemblyNames
            };
        }
    }
}
