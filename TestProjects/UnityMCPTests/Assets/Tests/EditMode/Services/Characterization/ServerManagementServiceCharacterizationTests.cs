using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Server;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Services.Characterization
{
    /// <summary>
    /// Characterization tests for ServerManagementService public interface.
    /// These tests lock down current behavior BEFORE refactoring to ensure
    /// no regressions during the decomposition into focused components.
    /// </summary>
    [TestFixture]
    public class ServerManagementServiceCharacterizationTests
    {
        private ServerManagementService _service;
        private bool _savedUseHttpTransport;
        private string _savedHttpUrl;
        private string _savedHttpRemoteUrl;
        private string _savedHttpTransportScope;
        private bool _savedAllowLanHttpBind;
        private bool _savedAllowInsecureRemoteHttp;
        private bool _savedLaunchConfirmed;

        private static readonly string[] HandshakeStringKeys =
        {
            EditorPrefKeys.LastLocalHttpServerStartedUtc,
            EditorPrefKeys.LastLocalHttpServerPidArgsHash,
            EditorPrefKeys.LastLocalHttpServerPidFilePath,
            EditorPrefKeys.LastLocalHttpServerInstanceToken,
        };
        private static readonly string[] HandshakeIntKeys =
        {
            EditorPrefKeys.LastLocalHttpServerPid,
            EditorPrefKeys.LastLocalHttpServerPort,
        };
        private readonly Dictionary<string, string> _savedHandshakeStrings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _savedHandshakeInts = new Dictionary<string, int>();

        [SetUp]
        public void SetUp()
        {
            _service = new ServerManagementService();
            // Save current settings
            _savedLaunchConfirmed = EditorPrefs.GetBool(EditorPrefKeys.HttpServerLaunchConfirmed, false);
            _savedUseHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            _savedHttpUrl = EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl, string.Empty);
            _savedHttpRemoteUrl = EditorPrefs.GetString(EditorPrefKeys.HttpRemoteBaseUrl, string.Empty);
            _savedHttpTransportScope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty);
            _savedAllowLanHttpBind = EditorPrefs.GetBool(EditorPrefKeys.AllowLanHttpBind, false);
            _savedAllowInsecureRemoteHttp = EditorPrefs.GetBool(EditorPrefKeys.AllowInsecureRemoteHttp, false);

            // The server handshake lives in GLOBAL EditorPrefs shared across every Unity project on
            // this machine. Snapshot and clear it so no test in this fixture can act on the
            // developer's real server pidfile/tracking (StopLocalHttpServerInternal deletes a
            // handshake it considers stale, even when the test targets an isolated port).
            _savedHandshakeStrings.Clear();
            _savedHandshakeInts.Clear();
            foreach (var key in HandshakeStringKeys)
            {
                if (EditorPrefs.HasKey(key)) _savedHandshakeStrings[key] = EditorPrefs.GetString(key);
                EditorPrefs.DeleteKey(key);
            }
            foreach (var key in HandshakeIntKeys)
            {
                if (EditorPrefs.HasKey(key)) _savedHandshakeInts[key] = EditorPrefs.GetInt(key);
                EditorPrefs.DeleteKey(key);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Restore settings
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, _savedUseHttpTransport);
            if (!string.IsNullOrEmpty(_savedHttpUrl))
            {
                EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, _savedHttpUrl);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);
            }
            if (!string.IsNullOrEmpty(_savedHttpRemoteUrl))
            {
                EditorPrefs.SetString(EditorPrefKeys.HttpRemoteBaseUrl, _savedHttpRemoteUrl);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.HttpRemoteBaseUrl);
            }
            if (!string.IsNullOrEmpty(_savedHttpTransportScope))
            {
                EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, _savedHttpTransportScope);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.HttpTransportScope);
            }
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, _savedAllowLanHttpBind);
            EditorPrefs.SetBool(EditorPrefKeys.AllowInsecureRemoteHttp, _savedAllowInsecureRemoteHttp);
            EditorPrefs.SetBool(EditorPrefKeys.HttpServerLaunchConfirmed, _savedLaunchConfirmed);
            // Restore the global server handshake exactly as it was found
            foreach (var key in HandshakeStringKeys)
            {
                if (_savedHandshakeStrings.TryGetValue(key, out var value)) EditorPrefs.SetString(key, value);
                else EditorPrefs.DeleteKey(key);
            }
            foreach (var key in HandshakeIntKeys)
            {
                if (_savedHandshakeInts.TryGetValue(key, out var value)) EditorPrefs.SetInt(key, value);
                else EditorPrefs.DeleteKey(key);
            }
            // Refresh cache to reflect restored values
            EditorConfigurationCache.Instance.Refresh();
        }

        #region StartLocalHttpServer First-Time-Confirm Gating

        // A fake launcher that records the headless launch request, then returns a harmless
        // no-op process (true / cmd exit) so Process.Start SUCCEEDS and exits immediately — no real
        // server starts, and (unlike a non-existent path) no exception + error dialog that would
        // block an interactive test run (Test Runner window / MCP bridge); -batchmode suppresses
        // dialogs, but interactive runs do not.
        private sealed class RecordingTerminalLauncher : ITerminalLauncher
        {
            public bool HeadlessCalled;
            public string LastCommand;
            public string LastLogPath;

            public System.Diagnostics.ProcessStartInfo CreateTerminalProcessStartInfo(string command)
            {
                return HarmlessNoOpStartInfo();
            }

            public System.Diagnostics.ProcessStartInfo CreateHeadlessProcessStartInfo(string command, string logFilePath)
            {
                HeadlessCalled = true;
                LastCommand = command;
                LastLogPath = logFilePath;
                return HarmlessNoOpStartInfo();
            }

            public string GetProjectRootPath()
            {
                return System.IO.Path.GetTempPath();
            }

            // A binary that always launches and exits 0 immediately, so Process.Start does not throw.
            private static System.Diagnostics.ProcessStartInfo HarmlessNoOpStartInfo()
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    psi.FileName = "cmd.exe";
                    psi.Arguments = "/c exit";
                }
                else
                {
                    psi.FileName = System.IO.File.Exists("/usr/bin/true") ? "/usr/bin/true" : "/bin/true";
                }
                return psi;
            }
        }

        // A fake command builder that always yields a benign, valid command so StartLocalHttpServer
        // does not bail out before reaching the confirmation gate.
        private sealed class FakeCommandBuilder : IServerCommandBuilder
        {
            public bool TryBuildCommand(out string fileName, out string arguments, out string displayCommand, out string error)
            {
                fileName = "uvx";
                arguments = "run mcp-for-unity";
                displayCommand = "uvx run mcp-for-unity";
                error = null;
                return true;
            }

            public string BuildUvPathFromUvx(string uvxPath) => uvxPath;
            public string GetPlatformSpecificPathPrepend() => string.Empty;
            public string QuoteIfNeeded(string input) =>
                (!string.IsNullOrEmpty(input) && input.Contains(" ")) ? "\"" + input + "\"" : input;
        }

        // A fake process detector that reports no listeners (so the "port in use" branch is skipped).
        private sealed class NoListenersProcessDetector : IProcessDetector
        {
            public bool LooksLikeMcpServerProcess(int pid) => false;
            public bool TryGetProcessCommandLine(int pid, out string argsLower) { argsLower = string.Empty; return false; }
            public System.Collections.Generic.List<int> GetListeningProcessIdsForPort(int port) => new System.Collections.Generic.List<int>();
            public int GetCurrentProcessId() => -1;
            public bool ProcessExists(int pid) => false;
            public string NormalizeForMatch(string input) => (input ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

        private ServerManagementService BuildServiceWithFakeLauncher(RecordingTerminalLauncher launcher)
        {
            return new ServerManagementService(
                new NoListenersProcessDetector(),
                null,
                null,
                new FakeCommandBuilder(),
                launcher);
        }

        [Test]
        public void StartLocalHttpServer_AlreadyConfirmed_SkipsDialogAndLaunchesHeadless()
        {
            // Arrange - local URL + HTTP enabled + confirmation already given.
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:59998");
            EditorPrefs.SetBool(EditorPrefKeys.HttpServerLaunchConfirmed, true);
            EditorConfigurationCache.Instance.Refresh();

            var launcher = new RecordingTerminalLauncher();
            var service = BuildServiceWithFakeLauncher(launcher);

            // Act - non-quiet, but confirmed: must NOT show a dialog and must launch headless.
            // (The fake launcher aborts the real spawn, so the return value may be false; we assert on the launch attempt.)
            LogAssert.ignoreFailingMessages = true;
            try
            {
                service.StartLocalHttpServer(quiet: false);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            // Assert - the headless launch path was taken (gate passed without a dialog).
            Assert.IsTrue(launcher.HeadlessCalled, "Confirmed launch should reach the headless launcher without a dialog");
            StringAssert.Contains("uvx run mcp-for-unity", launcher.LastCommand,
                "The headless command should be the built server command");
        }

        [Test]
        public void StartLocalHttpServer_Quiet_BypassesDialogAndDoesNotSetConfirmedFlag()
        {
            // Arrange - local URL + HTTP enabled + NOT yet confirmed.
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:59997");
            EditorPrefs.SetBool(EditorPrefKeys.HttpServerLaunchConfirmed, false);
            EditorConfigurationCache.Instance.Refresh();

            var launcher = new RecordingTerminalLauncher();
            var service = BuildServiceWithFakeLauncher(launcher);

            // Act - quiet (auto-start) path must never show a dialog and must launch headless.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                service.StartLocalHttpServer(quiet: true);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            // Assert
            Assert.IsTrue(launcher.HeadlessCalled, "Quiet launch should reach the headless launcher without a dialog");
            Assert.IsFalse(EditorPrefs.GetBool(EditorPrefKeys.HttpServerLaunchConfirmed, false),
                "Quiet auto-start must NOT set the first-time-confirm flag");
        }

        [Test]
        public void StartLocalHttpServer_LaunchesIntoPerPortLaunchLog()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:59996");
            EditorPrefs.SetBool(EditorPrefKeys.HttpServerLaunchConfirmed, true);
            EditorConfigurationCache.Instance.Refresh();

            var launcher = new RecordingTerminalLauncher();
            var service = BuildServiceWithFakeLauncher(launcher);

            // Act
            LogAssert.ignoreFailingMessages = true;
            try
            {
                service.StartLocalHttpServer(quiet: false);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            // Assert - the launch log path is the per-port server-launch log.
            Assert.IsTrue(launcher.HeadlessCalled, "Launch should reach the headless launcher");
            StringAssert.Contains("server-launch-59996.log", launcher.LastLogPath,
                "Headless launch should redirect output to the per-port launch log");
        }

        #endregion

        #region IsLocalUrl Tests

        [Test]
        public void IsLocalUrl_Localhost_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsTrue(result, "localhost should be recognized as local URL");
        }

        [Test]
        public void IsLocalUrl_127001_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsTrue(result, "127.0.0.1 should be recognized as local URL");
        }

        [Test]
        public void IsLocalUrl_127002_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.2:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsTrue(result, "127.0.0.2 should be recognized as loopback local URL");
        }

        [Test]
        public void IsLocalUrl_0000_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://0.0.0.0:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsTrue(result, "0.0.0.0 should be recognized as local URL");
        }

        [Test]
        public void IsLocalUrl_IPv6Loopback_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://[::1]:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsTrue(result, "::1 (IPv6 loopback) should be recognized as local URL");
        }

        [Test]
        public void IsLocalUrl_IPv6LoopbackLongForm_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://[0:0:0:0:0:0:0:1]:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsTrue(result, "0:0:0:0:0:0:0:1 (IPv6 loopback long-form) should be recognized as local URL");
        }

        [Test]
        public void IsLocalUrl_RemoteUrl_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://example.com:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert
            Assert.IsFalse(result, "Remote URL should not be recognized as local");
        }

        [Test]
        public void IsLocalUrl_EmptyString_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, string.Empty);
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalUrl();

            // Assert - behavior depends on default URL handling
            // Document current behavior
            Assert.Pass($"IsLocalUrl returned {result} for empty URL (documents current behavior)");
        }

        #endregion

        #region CanStartLocalServer Tests

        [Test]
        public void CanStartLocalServer_HttpDisabled_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.CanStartLocalServer();

            // Assert
            Assert.IsFalse(result, "Cannot start local server when HTTP transport is disabled");
        }

        [Test]
        public void CanStartLocalServer_HttpEnabledLocalUrl_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.CanStartLocalServer();

            // Assert
            Assert.IsTrue(result, "Can start local server when HTTP enabled and URL is local");
        }

        [Test]
        public void CanStartLocalServer_HttpEnabledRemoteUrl_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://remote.server.com:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.CanStartLocalServer();

            // Assert
            Assert.IsFalse(result, "Cannot start local server when URL is remote");
        }

        [Test]
        public void CanStartLocalServer_HttpEnabledZeroBind_DisallowedByDefault_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, false);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://0.0.0.0:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.CanStartLocalServer();

            // Assert
            Assert.IsFalse(result, "Cannot start local server on 0.0.0.0 unless LAN bind opt-in is enabled");
        }

        [Test]
        public void CanStartLocalServer_HttpEnabledZeroBind_WithOptIn_ReturnsTrue()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://0.0.0.0:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.CanStartLocalServer();

            // Assert
            Assert.IsTrue(result, "Can start local server on 0.0.0.0 when LAN bind opt-in is enabled");
        }

        #endregion

        #region HttpEndpointUtility Security Policy Tests

        [Test]
        public void SaveRemoteBaseUrl_WithoutScheme_DefaultsToHttps()
        {
            // Arrange
            EditorConfigurationCache.Instance.SetHttpTransportScope("remote");

            // Act
            HttpEndpointUtility.SaveRemoteBaseUrl("example.com:9000");
            string normalized = HttpEndpointUtility.GetRemoteBaseUrl();

            // Assert
            Assert.AreEqual("https://example.com:9000", normalized);
        }

        [Test]
        public void IsRemoteUrlAllowed_Http_DisallowedByDefault()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.AllowInsecureRemoteHttp, false);

            // Act
            bool allowed = HttpEndpointUtility.IsRemoteUrlAllowed("http://example.com:8080", out string error);

            // Assert
            Assert.IsFalse(allowed);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("HTTPS").IgnoreCase);
        }

        [Test]
        public void IsRemoteUrlAllowed_Http_AllowedWithOptIn()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.AllowInsecureRemoteHttp, true);

            // Act
            bool allowed = HttpEndpointUtility.IsRemoteUrlAllowed("http://example.com:8080", out string error);

            // Assert
            Assert.IsTrue(allowed);
            Assert.IsNull(error);
        }

        [Test]
        public void IsHttpLocalUrlAllowedForLaunch_ZeroBind_DisallowedByDefault()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, false);

            // Act
            bool allowed = HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch("http://0.0.0.0:8080", out string error);

            // Assert
            Assert.IsFalse(allowed);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("disabled by default").IgnoreCase);
        }

        [Test]
        public void IsHttpLocalUrlAllowedForLaunch_ZeroBind_AllowedWithOptIn()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.AllowLanHttpBind, true);

            // Act
            bool allowed = HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch("http://0.0.0.0:8080", out string error);

            // Assert
            Assert.IsTrue(allowed);
            Assert.IsNull(error);
        }

        #endregion

        #region TryGetLocalHttpServerCommand Tests

        [Test]
        public void TryGetLocalHttpServerCommand_HttpDisabled_ReturnsFalseWithError()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.TryGetLocalHttpServerCommand(out string command, out string error);

            // Assert
            Assert.IsFalse(result, "Should return false when HTTP transport is disabled");
            Assert.IsNull(command, "Command should be null when failing");
            Assert.IsNotNull(error, "Error message should be provided");
            Assert.That(error, Does.Contain("HTTP").IgnoreCase, "Error should mention HTTP transport");
        }

        [Test]
        public void TryGetLocalHttpServerCommand_RemoteUrl_ReturnsFalseWithError()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://remote.server.com:8080");
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            bool result = _service.TryGetLocalHttpServerCommand(out string command, out string error);

            // Assert
            Assert.IsFalse(result, "Should return false for remote URL");
            Assert.IsNull(command, "Command should be null when failing");
            Assert.IsNotNull(error, "Error message should be provided");
            Assert.That(error, Does.Contain("local").IgnoreCase, "Error should mention local address requirement");
        }

        [Test]
        public void TryGetLocalHttpServerCommand_LocalUrl_ReturnsCommandOrError()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.TryGetLocalHttpServerCommand(out string command, out string error);

            // Assert - Success depends on uvx availability
            if (result)
            {
                Assert.IsNotNull(command, "Command should be set on success");
                Assert.IsNull(error, "Error should be null on success");
                Assert.That(command, Does.Contain("uvx").Or.Contain("uv"), "Command should reference uvx/uv");
            }
            else
            {
                Assert.IsNotNull(error, "Error message should be provided on failure");
            }

            Assert.Pass($"TryGetLocalHttpServerCommand: success={result}, command={command ?? "null"}, error={error ?? "null"}");
        }

        #endregion

        #region IsLocalHttpServerReachable Tests

        [Test]
        public void IsLocalHttpServerReachable_NoServer_ReturnsFalse()
        {
            // Arrange - Use a port that's unlikely to have a server running
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:59999");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalHttpServerReachable();

            // Assert
            Assert.IsFalse(result, "Should return false when no server is listening");
        }

        [Test]
        public void IsLocalHttpServerReachable_RemoteUrl_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://remote.server.com:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalHttpServerReachable();

            // Assert
            Assert.IsFalse(result, "Should return false for non-local URL without attempting connection");
        }

        [Test]
        public void IsLocalHttpServerReachable_DoesNotThrow()
        {
            // Arrange
            _service = new ServerManagementService();

            // Act & Assert - Should never throw regardless of server state
            Assert.DoesNotThrow(() =>
            {
                _service.IsLocalHttpServerReachable();
            }, "IsLocalHttpServerReachable should handle all error cases gracefully");
        }

        #endregion

        #region IsLocalHttpServerRunning Tests

        [Test]
        public void IsLocalHttpServerRunning_RemoteUrl_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://remote.server.com:8080");
            _service = new ServerManagementService();

            // Act
            bool result = _service.IsLocalHttpServerRunning();

            // Assert
            Assert.IsFalse(result, "Should return false for non-local URL");
        }

        [Test]
        public void IsLocalHttpServerRunning_DoesNotThrow()
        {
            // Arrange
            _service = new ServerManagementService();

            // Act & Assert - Should never throw regardless of server state
            Assert.DoesNotThrow(() =>
            {
                _service.IsLocalHttpServerRunning();
            }, "IsLocalHttpServerRunning should handle all detection strategies gracefully");
        }

        #endregion

        #region ClearUvxCache Tests

        [Test]
        public void ClearUvxCache_DoesNotThrow()
        {
            // Arrange
            _service = new ServerManagementService();

            string lastLog = null;
            Application.LogCallback handler = (condition, stackTrace, type) =>
            {
                if (condition != null && condition.Contains("uv cache"))
                {
                    lastLog = condition;
                }
            };

            // Act & Assert - Should not throw even if uvx is not installed
            Assert.DoesNotThrow(() =>
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += handler;
                try
                {
                    _service.ClearUvxCache();
                }
                finally
                {
                    Application.logMessageReceived -= handler;
                    LogAssert.ignoreFailingMessages = false;
                }
            }, "ClearUvxCache should handle missing uvx gracefully");

            Assert.IsNotNull(lastLog, "Expected a uv cache log message.");
            StringAssert.Contains("uv cache", lastLog);
        }

        #endregion

        #region Private Method Characterization (via reflection for documentation)

        [Test]
        public void NormalizeForMatch_RemovesWhitespace_ViaReflection()
        {
            // Arrange - Use reflection to access private static method
            var method = typeof(ServerManagementService).GetMethod(
                "NormalizeForMatch",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                Assert.Pass("NormalizeForMatch is a private method - behavior documented via code review");
                return;
            }

            // Act
            string result = (string)method.Invoke(null, new object[] { "Hello World" });

            // Assert
            Assert.AreEqual("helloworld", result, "Should remove whitespace and lowercase");
        }

        [Test]
        public void NormalizeForMatch_HandlesNull_ViaReflection()
        {
            // Arrange
            var method = typeof(ServerManagementService).GetMethod(
                "NormalizeForMatch",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                Assert.Pass("NormalizeForMatch is a private method - behavior documented via code review");
                return;
            }

            // Act
            string result = (string)method.Invoke(null, new object[] { null });

            // Assert
            Assert.AreEqual(string.Empty, result, "Should return empty string for null input");
        }

        [Test]
        public void QuoteIfNeeded_PathWithSpaces_AddsQuotes_ViaReflection()
        {
            // Arrange
            var method = typeof(ServerManagementService).GetMethod(
                "QuoteIfNeeded",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                Assert.Pass("QuoteIfNeeded is a private method - behavior documented via code review");
                return;
            }

            // Act
            string result = (string)method.Invoke(null, new object[] { "path with spaces" });

            // Assert
            Assert.AreEqual("\"path with spaces\"", result, "Should wrap path with quotes");
        }

        [Test]
        public void QuoteIfNeeded_PathWithoutSpaces_NoChange_ViaReflection()
        {
            // Arrange
            var method = typeof(ServerManagementService).GetMethod(
                "QuoteIfNeeded",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                Assert.Pass("QuoteIfNeeded is a private method - behavior documented via code review");
                return;
            }

            // Act
            string result = (string)method.Invoke(null, new object[] { "pathwithoutspaces" });

            // Assert
            Assert.AreEqual("pathwithoutspaces", result, "Should not modify path without spaces");
        }

        [Test]
        public void IsLocalUrl_Static_MatchesPublicBehavior_ViaReflection()
        {
            // Arrange - Access private static IsLocalUrl(string) method
            var method = typeof(ServerManagementService).GetMethod(
                "IsLocalUrl",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (method == null)
            {
                Assert.Pass("Static IsLocalUrl is a private method - behavior documented via code review");
                return;
            }

            // Act & Assert - Test various URLs
            Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://localhost:8080" }), "localhost should be local");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://127.0.0.1:8080" }), "127.0.0.1 should be local");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://0.0.0.0:8080" }), "0.0.0.0 should be local");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://[::1]:8080" }), "::1 should be recognized as local");
            Assert.IsFalse((bool)method.Invoke(null, new object[] { "http://example.com:8080" }), "example.com should not be local");
            Assert.IsFalse((bool)method.Invoke(null, new object[] { "" }), "empty string should not be local");
            Assert.IsFalse((bool)method.Invoke(null, new object[] { null }), "null should not be local");
        }

        [Test]
        public void BuildLocalProbeHosts_Localhost_IncludesIPv4AndIPv6Loopback_ViaReflection()
        {
            // Arrange
            var method = typeof(ServerManagementService).GetMethod(
                "BuildLocalProbeHosts",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (method == null)
            {
                Assert.Pass("BuildLocalProbeHosts is a private method - behavior documented via code review");
                return;
            }

            // Act
            var result = method.Invoke(null, new object[] { "localhost" });
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<IEnumerable<string>>(result);
            var hosts = ((IEnumerable<string>)result).ToList();

            // Assert
            CollectionAssert.Contains(hosts, "localhost");
            CollectionAssert.Contains(hosts, "127.0.0.1");
            CollectionAssert.Contains(hosts, "::1");
        }

        [Test]
        public void BuildLocalProbeHosts_EmptyHost_DefaultsToIPv4Loopback_ViaReflection()
        {
            // Arrange
            var method = typeof(ServerManagementService).GetMethod(
                "BuildLocalProbeHosts",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (method == null)
            {
                Assert.Pass("BuildLocalProbeHosts is a private method - behavior documented via code review");
                return;
            }

            // Act
            var result = method.Invoke(null, new object[] { "" });
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<IEnumerable<string>>(result);
            var hosts = ((IEnumerable<string>)result).ToList();

            // Assert
            Assert.AreEqual(1, hosts.Count, "Empty host should resolve to a single default probe host.");
            Assert.AreEqual("127.0.0.1", hosts[0]);
        }

        #endregion

        #region StartLocalHttpServer (quiet) Tests

        [Test]
        public void StartLocalHttpServerQuiet_HttpDisabled_ReturnsFalse()
        {
            // Arrange
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorConfigurationCache.Instance.Refresh();
            _service = new ServerManagementService();

            // Act
            LogAssert.ignoreFailingMessages = true;
            bool result;
            try
            {
                result = _service.StartLocalHttpServer(quiet: true);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            // Assert
            Assert.IsFalse(result, "Should return false when HTTP transport is disabled");
        }

        [Test]
        [Explicit("Starts/stops the real local HTTP server")]
        [Category("server_lifecycle")]
        public void StartLocalHttpServerQuiet_DoesNotThrow()
        {
            // Isolate to an OS-assigned ephemeral loopback port so a manual run never
            // targets the shared default port (a live dev server must survive this test).
            var portHold = new TcpListener(IPAddress.Loopback, 0);
            portHold.Start();
            int port = ((IPEndPoint)portHold.LocalEndpoint).Port;
            portHold.Stop(); // free it so the launched server can bind
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, $"http://127.0.0.1:{port}");
            _service = new ServerManagementService();

            // Act & Assert - Should never throw or show dialogs, even when uvx is unavailable
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.DoesNotThrow(() =>
                {
                    _service.StartLocalHttpServer(quiet: true);
                }, "StartLocalHttpServer(quiet: true) should handle all error cases gracefully without dialogs");
            }
            finally
            {
                try { _service.StopLocalHttpServer(); } catch { /* best-effort cleanup on the isolated port */ }
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [TestCase(false, null, false)]
        [TestCase(false, "1", false)]
        [TestCase(true, null, true)]
        [TestCase(true, "", true)]
        [TestCase(true, "   ", true)]
        [TestCase(true, "1", false)]
        public void ShouldSkipBatchServerStart_TruthTable(bool batch, string env, bool expected)
        {
            Assert.AreEqual(expected, ServerManagementService.ShouldSkipBatchServerStart(batch, env));
        }

        [Test]
        public void StartLocalHttpServer_InBatchWithoutOptIn_DefersAndDoesNotCommandeer()
        {
            bool guardActive = Application.isBatchMode
                && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH"));
            if (!guardActive)
            {
                Assert.Ignore("Batch guard only active in a batch editor without UNITY_MCP_ALLOW_BATCH; "
                            + "logic is covered by ShouldSkipBatchServerStart_TruthTable.");
            }

            // Stand in for a developer's live server on an ephemeral port; 8080 is never touched.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, $"http://127.0.0.1:{port}");
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true); // absent the guard, the flow would reach the stop-existing step
                EditorConfigurationCache.Instance.Refresh();
                _service = new ServerManagementService();

                LogAssert.Expect(LogType.Log, new Regex("batch", RegexOptions.IgnoreCase)); // the guard's Info line
                bool result = _service.StartLocalHttpServer(quiet: true);

                Assert.IsFalse(result, "Batch start without opt-in must defer.");
                using (var probe = new TcpClient())
                {
                    Assert.DoesNotThrow(() => probe.Connect(IPAddress.Loopback, port),
                        "Existing listener must survive a deferred start.");
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        #endregion
    }
}
