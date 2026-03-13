using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Automatically starts the MCP server and session on Unity launch when using HTTP Local transport.
    /// Phase 1: Quick check if server is already reachable.
    /// Phase 2: If not reachable, start the server and wait for it to come up.
    /// This complements the reload handlers which only resume sessions after domain reloads.
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpAutoStartHandler
    {
        // Use SessionState to ensure we only attempt auto-start once per Editor session.
        // This survives domain reloads but resets when Unity restarts.
        private const string AutoStartAttemptedKey = "MCPForUnity.HttpAutoStartAttemptedThisSession";

        // Delay before checking server availability to allow Unity to fully initialize
        private const float InitialDelaySeconds = 2f;

        // Phase 1: how long to wait if the server is already running (quick check)
        private const float Phase1MaxWaitSeconds = 5f;

        // Phase 2: how long to wait after starting a server for it to become reachable
        private const float Phase2MaxWaitSeconds = 25f;

        // Interval between server availability checks
        private const float CheckIntervalSeconds = 1f;

        static HttpAutoStartHandler()
        {
            // Skip in batch mode
            if (Application.isBatchMode)
                return;

            // Only attempt once per Editor session
            if (SessionState.GetBool(AutoStartAttemptedKey, false))
                return;

            // Only for HTTP transport
            if (!EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true))
                return;

            // Only for HTTP Local scope
            string scope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty);
            bool isLocal = string.IsNullOrEmpty(scope) || scope.Equals("local", StringComparison.OrdinalIgnoreCase);
            if (!isLocal)
                return;

            // Skip if this is a domain reload with an active resume in progress
            if (EditorPrefs.GetBool(EditorPrefKeys.ResumeHttpAfterReload, false))
                return;

            // Mark that we've attempted auto-start this session
            SessionState.SetBool(AutoStartAttemptedKey, true);

            // Schedule the auto-start check after Unity is fully initialized
            EditorApplication.delayCall += ScheduleAutoStart;
        }

        private static void ScheduleAutoStart()
        {
            double startTime = EditorApplication.timeSinceStartup + InitialDelaySeconds;
            double phase1EndTime = startTime + Phase1MaxWaitSeconds;
            double finalEndTime = startTime + Phase1MaxWaitSeconds + Phase2MaxWaitSeconds;
            bool serverLaunchAttempted = false;
            bool inPhase2 = false;

            void CheckAndStart()
            {
                double now = EditorApplication.timeSinceStartup;

                // Absolute timeout
                if (now > finalEndTime)
                {
                    McpLog.Debug("[HttpAutoStart] Timed out waiting for server");
                    return;
                }

                // Wait if Unity is still compiling
                if (EditorApplication.isCompiling)
                {
                    EditorApplication.delayCall += CheckAndStart;
                    return;
                }

                // Wait for initial delay
                if (now < startTime)
                {
                    EditorApplication.delayCall += CheckAndStart;
                    return;
                }

                // Skip if session already running (e.g., started manually or by another handler)
                if (MCPServiceLocator.Bridge.IsRunning)
                {
                    McpLog.Debug("[HttpAutoStart] Session already running, skipping auto-start");
                    return;
                }

                // Check if server is reachable
                if (MCPServiceLocator.Server.IsLocalHttpServerReachable())
                {
                    TryStartSession();
                    return;
                }

                // Phase 1 expired — try to start the server if it's not already running
                if (!inPhase2 && now > phase1EndTime)
                {
                    // Check if server process exists but isn't accepting connections yet
                    if (MCPServiceLocator.Server.IsLocalHttpServerRunning())
                    {
                        McpLog.Debug("[HttpAutoStart] Server process detected but not yet reachable, waiting...");
                        inPhase2 = true;
                    }
                    else if (!serverLaunchAttempted && MCPServiceLocator.Server.CanStartLocalServer())
                    {
                        serverLaunchAttempted = true;
                        inPhase2 = true;
                        McpLog.Info("[HttpAutoStart] No server detected, starting server...");
                        bool started = MCPServiceLocator.Server.StartLocalHttpServerSilent();
                        if (!started)
                        {
                            McpLog.Warn("[HttpAutoStart] Failed to start server");
                            return;
                        }
                        McpLog.Info("[HttpAutoStart] Server launch initiated, waiting for it to become reachable...");
                    }
                    else
                    {
                        McpLog.Debug("[HttpAutoStart] Server not reachable and cannot auto-start");
                        return;
                    }
                }

                // Schedule next check
                ScheduleRetry(CheckAndStart);
            }

            EditorApplication.delayCall += CheckAndStart;
        }

        private static void ScheduleRetry(Action callback)
        {
            double nextCheck = EditorApplication.timeSinceStartup + CheckIntervalSeconds;
            void Retry()
            {
                if (EditorApplication.timeSinceStartup >= nextCheck)
                    callback();
                else
                    EditorApplication.delayCall += Retry;
            }
            EditorApplication.delayCall += Retry;
        }

        private static async void TryStartSession()
        {
            try
            {
                McpLog.Info("[HttpAutoStart] Server detected, auto-starting session...");

                bool started = await MCPServiceLocator.Bridge.StartAsync();

                if (started)
                {
                    McpLog.Info("[HttpAutoStart] Session started successfully");
                    MCPForUnityEditorWindow.RequestHealthVerification();
                }
                else
                {
                    McpLog.Warn("[HttpAutoStart] Failed to start session");
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[HttpAutoStart] Error starting session: {ex.Message}");
            }
        }
    }
}
