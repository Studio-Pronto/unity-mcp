using System;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Maintains a cached readiness snapshot (v2) so status reads remain fast even when Unity is busy.
    /// Updated on the main thread via Editor callbacks and periodic update ticks.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorStateCache
    {
        private static readonly object LockObj = new();
        private static long _sequence;
        private static long _observedUnixMs;

        private static bool _lastIsCompiling;
        private static long? _lastCompileStartedUnixMs;
        private static long? _lastCompileFinishedUnixMs;

        private static bool _domainReloadPending;
        private static long? _domainReloadBeforeUnixMs;
        private static long? _domainReloadAfterUnixMs;

        private static double _lastUpdateTimeSinceStartup;
        private const double MinUpdateIntervalSeconds = 1.0; // Reduced frequency: 1s instead of 0.25s

        // State tracking to detect when snapshot actually changes (checked BEFORE building)
        private static string _lastTrackedScenePath;
        private static string _lastTrackedSceneName;
        private static bool _lastTrackedIsFocused;
        private static bool _lastTrackedIsPlaying;
        private static bool _lastTrackedIsPaused;
        private static bool _lastTrackedIsUpdating;
        private static bool _lastTrackedTestsRunning;
        private static string _lastTrackedActivityPhase;

        // Play-mode transition tracking. True ONLY during an enter/exit transition window, driven by
        // playModeStateChanged events. Deliberately NOT EditorApplication.isPlayingOrWillChangePlaymode,
        // which Unity holds true for the entire Play session (and, per Unity docs, only signals entering
        // play, never exiting) — that superset is what made a settled Play session look like a transition.
        private static bool _isPlayModeChanging;

        // Phase-entry tracking so activity.since_unix_ms reports when the current phase began rather than
        // the snapshot build time. Owned exclusively by BuildSnapshot — see the note there for why this
        // must stay separate from _lastTrackedActivityPhase.
        private static long _activityPhaseSinceUnixMs;
        private static string _lastBuiltActivityPhase;

        /// <summary>
        /// Last known activity phase, safe to read from background threads (string reads are atomic).
        /// Used by WebSocket pong messages to inform the server of Unity's current state.
        /// </summary>
        public static string LastActivityPhase => _lastTrackedActivityPhase ?? "unknown";

        private static JObject _cached;

        private sealed class EditorStateSnapshot
        {
            [JsonProperty("schema_version")]
            public string SchemaVersion { get; set; }

            [JsonProperty("observed_at_unix_ms")]
            public long ObservedAtUnixMs { get; set; }

            [JsonProperty("sequence")]
            public long Sequence { get; set; }

            [JsonProperty("unity")]
            public EditorStateUnity Unity { get; set; }

            [JsonProperty("editor")]
            public EditorStateEditor Editor { get; set; }

            [JsonProperty("activity")]
            public EditorStateActivity Activity { get; set; }

            [JsonProperty("compilation")]
            public EditorStateCompilation Compilation { get; set; }

            [JsonProperty("assets")]
            public EditorStateAssets Assets { get; set; }

            [JsonProperty("tests")]
            public EditorStateTests Tests { get; set; }

            [JsonProperty("transport")]
            public EditorStateTransport Transport { get; set; }

            [JsonProperty("settings")]
            public EditorStateSettings Settings { get; set; }
        }

        private sealed class EditorStateUnity
        {
            [JsonProperty("instance_id")]
            public string InstanceId { get; set; }

            [JsonProperty("unity_version")]
            public string UnityVersion { get; set; }

            [JsonProperty("project_id")]
            public string ProjectId { get; set; }

            [JsonProperty("platform")]
            public string Platform { get; set; }

            [JsonProperty("is_batch_mode")]
            public bool? IsBatchMode { get; set; }
        }

        private sealed class EditorStateEditor
        {
            [JsonProperty("is_focused")]
            public bool? IsFocused { get; set; }

            [JsonProperty("play_mode")]
            public EditorStatePlayMode PlayMode { get; set; }

            [JsonProperty("active_scene")]
            public EditorStateActiveScene ActiveScene { get; set; }
        }

        private sealed class EditorStatePlayMode
        {
            [JsonProperty("is_playing")]
            public bool? IsPlaying { get; set; }

            [JsonProperty("is_paused")]
            public bool? IsPaused { get; set; }

            [JsonProperty("is_changing")]
            public bool? IsChanging { get; set; }
        }

        private sealed class EditorStateActiveScene
        {
            [JsonProperty("path")]
            public string Path { get; set; }

            [JsonProperty("guid")]
            public string Guid { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        private sealed class EditorStateActivity
        {
            [JsonProperty("phase")]
            public string Phase { get; set; }

            [JsonProperty("since_unix_ms")]
            public long SinceUnixMs { get; set; }

            [JsonProperty("reasons")]
            public string[] Reasons { get; set; }
        }

        private sealed class EditorStateCompilation
        {
            [JsonProperty("is_compiling")]
            public bool? IsCompiling { get; set; }

            [JsonProperty("is_domain_reload_pending")]
            public bool? IsDomainReloadPending { get; set; }

            [JsonProperty("last_compile_started_unix_ms")]
            public long? LastCompileStartedUnixMs { get; set; }

            [JsonProperty("last_compile_finished_unix_ms")]
            public long? LastCompileFinishedUnixMs { get; set; }

            [JsonProperty("last_domain_reload_before_unix_ms")]
            public long? LastDomainReloadBeforeUnixMs { get; set; }

            [JsonProperty("last_domain_reload_after_unix_ms")]
            public long? LastDomainReloadAfterUnixMs { get; set; }
        }

        private sealed class EditorStateAssets
        {
            [JsonProperty("is_updating")]
            public bool? IsUpdating { get; set; }

            [JsonProperty("external_changes_dirty")]
            public bool? ExternalChangesDirty { get; set; }

            [JsonProperty("external_changes_last_seen_unix_ms")]
            public long? ExternalChangesLastSeenUnixMs { get; set; }

            [JsonProperty("external_changes_dirty_since_unix_ms")]
            public long? ExternalChangesDirtySinceUnixMs { get; set; }

            [JsonProperty("external_changes_last_cleared_unix_ms")]
            public long? ExternalChangesLastClearedUnixMs { get; set; }

            [JsonProperty("refresh")]
            public EditorStateRefresh Refresh { get; set; }
        }

        private sealed class EditorStateRefresh
        {
            [JsonProperty("is_refresh_in_progress")]
            public bool? IsRefreshInProgress { get; set; }

            [JsonProperty("last_refresh_requested_unix_ms")]
            public long? LastRefreshRequestedUnixMs { get; set; }

            [JsonProperty("last_refresh_finished_unix_ms")]
            public long? LastRefreshFinishedUnixMs { get; set; }
        }

        private sealed class EditorStateTests
        {
            [JsonProperty("is_running")]
            public bool? IsRunning { get; set; }

            [JsonProperty("mode")]
            public string Mode { get; set; }

            [JsonProperty("current_job_id")]
            public string CurrentJobId { get; set; }

            [JsonProperty("started_unix_ms")]
            public long? StartedUnixMs { get; set; }

            [JsonProperty("started_by")]
            public string StartedBy { get; set; }

            [JsonProperty("last_run")]
            public EditorStateLastRun LastRun { get; set; }
        }

        private sealed class EditorStateLastRun
        {
            [JsonProperty("finished_unix_ms")]
            public long? FinishedUnixMs { get; set; }

            [JsonProperty("result")]
            public string Result { get; set; }

            [JsonProperty("counts")]
            public object Counts { get; set; }
        }

        private sealed class EditorStateTransport
        {
            [JsonProperty("unity_bridge_connected")]
            public bool? UnityBridgeConnected { get; set; }

            [JsonProperty("last_message_unix_ms")]
            public long? LastMessageUnixMs { get; set; }
        }

        private sealed class EditorStateSettings
        {
            [JsonProperty("batch_execute_max_commands")]
            public int BatchExecuteMaxCommands { get; set; }
        }

        static EditorStateCache()
        {
            try
            {
                _sequence = 0;
                _observedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Seed transition state so a domain reload that happens DURING play-mode entry
                // (ExitingEditMode → reload → EnteredPlayMode wipes static state) doesn't briefly report a
                // settled phase. Best-effort; corrected by the next playModeStateChanged event.
                _isPlayModeChanging = EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying;

                _cached = BuildSnapshot("init");

                EditorApplication.update += OnUpdate;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

                // Tracks whether an assembly compilation is actually running, for
                // GetActualIsCompiling's Play-mode check. Statics reset on domain reload
                // and this [InitializeOnLoad] ctor re-subscribes, so the flag is per-domain.
                UnityEditor.Compilation.CompilationPipeline.compilationStarted += _ => _pipelineCompilationRunning = true;
                UnityEditor.Compilation.CompilationPipeline.compilationFinished += _ => _pipelineCompilationRunning = false;

                AssemblyReloadEvents.beforeAssemblyReload += () =>
                {
                    _domainReloadPending = true;
                    _domainReloadBeforeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    ForceUpdate("before_domain_reload");
                };
                AssemblyReloadEvents.afterAssemblyReload += () =>
                {
                    _domainReloadPending = false;
                    _domainReloadAfterUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    ForceUpdate("after_domain_reload");
                };
            }
            catch (Exception ex)
            {
                McpLog.Error($"[EditorStateCache] Failed to initialise: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode: // pressed Play — transition into play begins
                case PlayModeStateChange.ExitingPlayMode: // pressed Stop — transition out of play begins
                    _isPlayModeChanging = true;
                    break;
                case PlayModeStateChange.EnteredPlayMode:  // settled, interactive in play
                case PlayModeStateChange.EnteredEditMode:  // settled back in edit
                    _isPlayModeChanging = false;
                    break;
            }
            // Unconditional: every _isPlayModeChanging flip must be captured in the snapshot, so the
            // OnUpdate hasChanges early-out can never strand a stale is_changing value.
            ForceUpdate("playmode");
        }

        private static void OnUpdate()
        {
            // Throttle to reduce overhead while keeping the snapshot fresh enough for polling clients.
            double now = EditorApplication.timeSinceStartup;
            // Use GetActualIsCompiling() to avoid Play mode false positives (issue #582)
            bool isCompiling = GetActualIsCompiling();

            // Check for compilation edge transitions (always update on these)
            bool compilationEdge = isCompiling != _lastIsCompiling;

            if (!compilationEdge && now - _lastUpdateTimeSinceStartup < MinUpdateIntervalSeconds)
            {
                return;
            }

            // Fast state-change detection BEFORE building snapshot.
            // This avoids the expensive BuildSnapshot() call entirely when nothing changed.
            // These checks are much cheaper than building a full JSON snapshot.
            var scene = EditorSceneManager.GetActiveScene();
            string scenePath = string.IsNullOrEmpty(scene.path) ? null : scene.path;
            string sceneName = scene.name ?? string.Empty;
            bool isFocused = InternalEditorUtility.isApplicationActive;
            bool isPlaying = EditorApplication.isPlaying;
            bool isPaused = EditorApplication.isPaused;
            bool isUpdating = EditorApplication.isUpdating;
            bool testsRunning = TestRunStatus.IsRunning;

            var activityPhase = ComputeActivityPhase(
                testsRunning, isCompiling, _domainReloadPending, isUpdating, _isPlayModeChanging, isPlaying);

            bool hasChanges = compilationEdge
                || _lastTrackedScenePath != scenePath
                || _lastTrackedSceneName != sceneName
                || _lastTrackedIsFocused != isFocused
                || _lastTrackedIsPlaying != isPlaying
                || _lastTrackedIsPaused != isPaused
                || _lastTrackedIsUpdating != isUpdating
                || _lastTrackedTestsRunning != testsRunning
                || _lastTrackedActivityPhase != activityPhase;

            if (!hasChanges)
            {
                // No state change - skip the expensive BuildSnapshot entirely.
                // This is the key optimization that prevents the 28ms GC spikes.
                return;
            }

            // Update tracked state
            _lastTrackedScenePath = scenePath;
            _lastTrackedSceneName = sceneName;
            _lastTrackedIsFocused = isFocused;
            _lastTrackedIsPlaying = isPlaying;
            _lastTrackedIsPaused = isPaused;
            _lastTrackedIsUpdating = isUpdating;
            _lastTrackedTestsRunning = testsRunning;
            _lastTrackedActivityPhase = activityPhase;

            _lastUpdateTimeSinceStartup = now;
            ForceUpdate("tick");
        }

        private static void ForceUpdate(string reason)
        {
            lock (LockObj)
            {
                _cached = BuildSnapshot(reason);
            }
        }

        /// <summary>
        /// Single source of truth for the activity phase, shared by OnUpdate's change-detection and
        /// BuildSnapshot's emitted snapshot so the two can never disagree. Precedence is first-match-wins.
        /// "playmode_transition" covers only the brief enter/exit window (see <see cref="_isPlayModeChanging"/>);
        /// a settled, interactive Play session reports the steady "playing" phase.
        /// </summary>
        internal static string ComputeActivityPhase(
            bool testsRunning, bool isCompiling, bool domainReloadPending,
            bool isUpdating, bool isPlayModeChanging, bool isPlaying)
        {
            if (testsRunning) return "running_tests";
            if (isCompiling) return "compiling";
            if (domainReloadPending) return "domain_reload";
            if (isUpdating) return "asset_import";
            if (isPlayModeChanging) return "playmode_transition";
            if (isPlaying) return "playing";
            return "idle";
        }

        private static JObject BuildSnapshot(string reason)
        {
            _sequence++;
            _observedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            bool isCompiling = GetActualIsCompiling();
            if (isCompiling && !_lastIsCompiling)
            {
                _lastCompileStartedUnixMs = _observedUnixMs;
            }
            else if (!isCompiling && _lastIsCompiling)
            {
                _lastCompileFinishedUnixMs = _observedUnixMs;
            }
            _lastIsCompiling = isCompiling;

            var scene = EditorSceneManager.GetActiveScene();
            string scenePath = string.IsNullOrEmpty(scene.path) ? null : scene.path;
            string sceneGuid = !string.IsNullOrEmpty(scenePath) ? AssetDatabase.AssetPathToGUID(scenePath) : null;

            bool testsRunning = TestRunStatus.IsRunning;
            var testsMode = TestRunStatus.Mode?.ToString();
            string currentJobId = TestJobManager.CurrentJobId;
            bool isFocused = InternalEditorUtility.isApplicationActive;

            var activityPhase = ComputeActivityPhase(
                testsRunning, isCompiling, _domainReloadPending,
                EditorApplication.isUpdating, _isPlayModeChanging, EditorApplication.isPlaying);

            // Stamp when the phase was ENTERED (not when this snapshot was built) so since_unix_ms is
            // meaningful. _lastBuiltActivityPhase is owned here and MUST stay separate from
            // _lastTrackedActivityPhase: OnUpdate updates that one before calling ForceUpdate → BuildSnapshot,
            // so reusing it would make the phase-entry edge invisible from inside BuildSnapshot.
            if (activityPhase != _lastBuiltActivityPhase)
            {
                _activityPhaseSinceUnixMs = _observedUnixMs;
                _lastBuiltActivityPhase = activityPhase;
            }

            var snapshot = new EditorStateSnapshot
            {
                SchemaVersion = "unity-mcp/editor_state@2",
                ObservedAtUnixMs = _observedUnixMs,
                Sequence = _sequence,
                Unity = new EditorStateUnity
                {
                    InstanceId = null,
                    UnityVersion = Application.unityVersion,
                    ProjectId = null,
                    Platform = Application.platform.ToString(),
                    IsBatchMode = Application.isBatchMode
                },
                Editor = new EditorStateEditor
                {
                    IsFocused = isFocused,
                    PlayMode = new EditorStatePlayMode
                    {
                        IsPlaying = EditorApplication.isPlaying,
                        IsPaused = EditorApplication.isPaused,
                        IsChanging = _isPlayModeChanging
                    },
                    ActiveScene = new EditorStateActiveScene
                    {
                        Path = scenePath,
                        Guid = sceneGuid,
                        Name = scene.name ?? string.Empty
                    }
                },
                Activity = new EditorStateActivity
                {
                    Phase = activityPhase,
                    SinceUnixMs = _activityPhaseSinceUnixMs,
                    Reasons = new[] { reason }
                },
                Compilation = new EditorStateCompilation
                {
                    IsCompiling = isCompiling,
                    IsDomainReloadPending = _domainReloadPending,
                    LastCompileStartedUnixMs = _lastCompileStartedUnixMs,
                    LastCompileFinishedUnixMs = _lastCompileFinishedUnixMs,
                    LastDomainReloadBeforeUnixMs = _domainReloadBeforeUnixMs,
                    LastDomainReloadAfterUnixMs = _domainReloadAfterUnixMs
                },
                Assets = new EditorStateAssets
                {
                    IsUpdating = EditorApplication.isUpdating,
                    ExternalChangesDirty = false,
                    ExternalChangesLastSeenUnixMs = null,
                    ExternalChangesDirtySinceUnixMs = null,
                    ExternalChangesLastClearedUnixMs = null,
                    Refresh = new EditorStateRefresh
                    {
                        IsRefreshInProgress = false,
                        LastRefreshRequestedUnixMs = null,
                        LastRefreshFinishedUnixMs = null
                    }
                },
                Tests = new EditorStateTests
                {
                    IsRunning = testsRunning,
                    Mode = testsMode,
                    CurrentJobId = string.IsNullOrEmpty(currentJobId) ? null : currentJobId,
                    StartedUnixMs = TestRunStatus.StartedUnixMs,
                    StartedBy = "unknown",
                    LastRun = TestRunStatus.FinishedUnixMs.HasValue
                        ? new EditorStateLastRun
                        {
                            FinishedUnixMs = TestRunStatus.FinishedUnixMs,
                            Result = "unknown",
                            Counts = null
                        }
                        : null
                },
                Transport = new EditorStateTransport
                {
                    UnityBridgeConnected = null,
                    LastMessageUnixMs = null
                },
                Settings = new EditorStateSettings
                {
                    BatchExecuteMaxCommands = Tools.BatchExecute.GetMaxCommandsPerBatch()
                }
            };

            return JObject.FromObject(snapshot);
        }

        public static JObject GetSnapshot()
        {
            lock (LockObj)
            {
                // Defensive: if something went wrong early, rebuild once.
                if (_cached == null)
                {
                    _cached = BuildSnapshot("rebuild");
                }

                // Always return a fresh clone to prevent mutation bugs.
                // The main GC optimization comes from state-change detection (OnUpdate)
                // which prevents unnecessary _cached rebuilds, not from caching the clone.
                var clone = (JObject)_cached.DeepClone();

                // When the editor's update loop is legitimately throttled — backgrounded, or
                // headless batchmode where EditorApplication.update only ticks on incoming activity —
                // OnUpdate stops refreshing the snapshot and observed_at_unix_ms grows stale even
                // though the data is current (answering this very request proves the main thread is
                // live). Re-stamp so the server's >2s staleness gate doesn't wrongly mark a responsive
                // editor not-ready. A focused, non-batch editor keeps the honest build-time stamp so
                // staleness still catches a genuinely wedged main thread.
                if (!InternalEditorUtility.isApplicationActive || Application.isBatchMode)
                {
                    clone["observed_at_unix_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }

                return clone;
            }
        }

        // Set/cleared by the CompilationPipeline.compilationStarted/Finished events
        // subscribed in the static ctor. NOTE: CompilationPipeline.isCompiling does not
        // exist on the supported Unity range (verified by reflection probe on 2021.3 and
        // 6000.4 — neither public nor non-public), so the reflection this replaced never
        // resolved and always fell back to the raw signal.
        private static bool _pipelineCompilationRunning;

        /// <summary>
        /// Returns the actual compilation state, working around a known Unity quirk where
        /// EditorApplication.isCompiling can return false positives in Play mode (e.g. a
        /// recompile deferred by Recompile-After-Finished-Playing keeps it true for the
        /// whole play session). See: https://github.com/CoplayDev/unity-mcp/issues/549
        /// </summary>
        internal static bool GetActualIsCompiling()
        {
            // If EditorApplication.isCompiling is false, Unity is definitely not compiling
            if (!EditorApplication.isCompiling)
            {
                return false;
            }

            // In Play mode, trust the event-tracked pipeline state instead: a deferred
            // recompile keeps EditorApplication.isCompiling true without any compilation
            // actually running.
            if (EditorApplication.isPlaying)
            {
                return _pipelineCompilationRunning;
            }

            // Outside Play mode the raw signal is reliable.
            return true;
        }
    }
}


