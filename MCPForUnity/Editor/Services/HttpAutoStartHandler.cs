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
    /// Automatically starts an MCP session on Unity launch when the HTTP server is already running.
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

        // How long to wait for server to become available
        private const float MaxWaitSeconds = 10f;

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
            // Wait for initial delay before starting checks
            double startTime = EditorApplication.timeSinceStartup + InitialDelaySeconds;
            double endTime = startTime + MaxWaitSeconds;

            void CheckAndStart()
            {
                // Abort if we've exceeded the wait time
                if (EditorApplication.timeSinceStartup > endTime)
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
                if (EditorApplication.timeSinceStartup < startTime)
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
                if (!MCPServiceLocator.Server.IsLocalHttpServerReachable())
                {
                    // Server not ready yet, try again after interval
                    double nextCheck = EditorApplication.timeSinceStartup + CheckIntervalSeconds;
                    void RetryCheck()
                    {
                        if (EditorApplication.timeSinceStartup >= nextCheck)
                        {
                            CheckAndStart();
                        }
                        else
                        {
                            EditorApplication.delayCall += RetryCheck;
                        }
                    }
                    EditorApplication.delayCall += RetryCheck;
                    return;
                }

                // Server is running and no session active - auto-start
                TryStartSession();
            }

            EditorApplication.delayCall += CheckAndStart;
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
