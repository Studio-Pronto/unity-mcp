using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Server;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.Services.Server
{
    /// <summary>
    /// Tests for ServerProcessLauncher, the process launch behind StartLocalHttpServer: it must not
    /// hand the editor's handles to the server, and must otherwise launch as Process.Start does.
    /// </summary>
    [TestFixture]
    public class ServerProcessLauncherTests
    {
        private const uint HANDLE_FLAG_INHERIT = 0x1;
        private const int ERROR_MORE_DATA = 234;

        private string _tempDir;
        private Process _child;

        [SetUp]
        public void SetUp()
        {
            // The space keeps the launch log's quoting honest, as a project path with spaces would.
            _tempDir = Path.Combine(Path.GetTempPath(), "mcp launcher " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_child != null)
            {
                if (!_child.HasExited)
                {
                    // /T also ends the ping that cmd.exe started.
                    ExecPath.TryRun("taskkill", $"/F /T /PID {_child.Id}", null, out _, out _, 10000);
                    _child.WaitForExit(10000);
                }
                _child.Dispose();
                _child = null;
            }
            Directory.Delete(_tempDir, true);
        }

        /// <summary>
        /// Defends #50: the server started from an editor inherited the editor's open
        /// Logs/Editor.log handle and kept that log locked for as long as the server ran.
        /// </summary>
        [Test]
        public void Start_DoesNotHandTheEditorsInheritableHandlesToTheLaunchedProcess()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("Handle inheritance is a Windows CreateProcess behavior; Mono closes descriptors above 2 in a POSIX child.");

            string heldPath = Path.Combine(_tempDir, "held.log");
            using (var held = new FileStream(heldPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                // Unity opens its Editor.log with an inheritable handle; give this file the same.
                SetInheritable(held.SafeFileHandle, true);
                try
                {
                    // The server's own launch shape, running something that stays up long enough to inspect.
                    var startInfo = new TerminalLauncher().CreateHeadlessProcessStartInfo(
                        "ping -n 30 127.0.0.1", Path.Combine(_tempDir, "launch.log"));
                    _child = ServerProcessLauncher.Start(startInfo);
                }
                finally
                {
                    SetInheritable(held.SafeFileHandle, false);
                }

                Assert.IsFalse(_child.HasExited, "The launched process should still be running.");
                List<int> holders = ProcessesHoldingFile(heldPath);
                CollectionAssert.Contains(holders, Process.GetCurrentProcess().Id,
                    "Control: the Restart Manager should list this editor, which has the file open.");
                CollectionAssert.DoesNotContain(holders, _child.Id,
                    "The launched process holds a copy of the editor's file handle.");
            }
        }

        /// <summary>
        /// Defends Process.Start's non-inheritable handle to the launched process: the launcher gets
        /// its handle from Process.GetProcessById, which Mono opens inheritable, and an inheritable one
        /// would reach every process the editor starts later.
        /// </summary>
        [Test]
        public void Start_ReturnsANonInheritableHandleToTheLaunchedProcess()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("Handle inheritance is a Windows CreateProcess behavior; off Windows the launcher is Process.Start.");

            using (Process viaProcessStart = Process.Start(new TerminalLauncher().CreateHeadlessProcessStartInfo(
                       "exit", Path.Combine(_tempDir, "process start.log"))))
            {
                Assert.IsFalse(IsInheritable(viaProcessStart.SafeHandle), "Control: Process.Start's handle is not inheritable.");
                viaProcessStart.WaitForExit(30000);
            }

            _child = ServerProcessLauncher.Start(new TerminalLauncher().CreateHeadlessProcessStartInfo(
                "exit", Path.Combine(_tempDir, "launcher.log")));

            Assert.IsFalse(IsInheritable(_child.SafeHandle), "The editor's handle to the launched process is inheritable.");
        }

        /// <summary>
        /// Defends against the Windows launch drifting from Process.Start: the server must get the
        /// same command line, environment block and working directory it got before #50.
        /// </summary>
        [Test]
        public void Start_RunsTheSameLaunchAsProcessStart()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("Off Windows, ServerProcessLauncher is Process.Start.");

            string processStartLog = Path.Combine(_tempDir, "process start.log");
            string launcherLog = Path.Combine(_tempDir, "launcher.log");

            using (Process viaProcessStart = Process.Start(EnvironmentDumpStartInfo(processStartLog)))
            using (Process viaLauncher = ServerProcessLauncher.Start(EnvironmentDumpStartInfo(launcherLog)))
            {
                Assert.IsTrue(viaProcessStart.WaitForExit(30000), "The Process.Start launch did not exit.");
                Assert.IsTrue(viaLauncher.WaitForExit(30000), "The ServerProcessLauncher launch did not exit.");
                Assert.AreEqual(0, viaProcessStart.ExitCode, "Process.Start launch exit code");
                Assert.AreEqual(0, viaLauncher.ExitCode, "ServerProcessLauncher launch exit code");
            }

            string expected = File.ReadAllText(processStartLog);
            Assert.That(expected, Does.Contain("MCP_LAUNCHER_TEST_PROBE=1").IgnoreCase,
                "Control: the edited environment should reach the Process.Start launch.");
            Assert.AreEqual(expected, File.ReadAllText(launcherLog));
        }

        /// <summary>
        /// Defends the launcher's contract: a caller asking for something it does not implement
        /// (Process.Start's default shell execute, redirected output, another user, an argument
        /// list) must fail loudly instead of silently launching without it.
        /// </summary>
        [Test]
        public void Start_WithAnUnsupportedStartInfoField_ThrowsNotSupported()
        {
            // Not a real program: if the contract check were missing, the launch would fail
            // differently instead of starting anything.
            const string fileName = "mcp-launcher-contract-test-missing.exe";

            Assert.Throws<NotSupportedException>(() => ServerProcessLauncher.Start(
                new ProcessStartInfo(fileName)), "UseShellExecute left at Mono's default (true)");
            Assert.Throws<NotSupportedException>(() => ServerProcessLauncher.Start(
                new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true }), "RedirectStandardOutput");
            Assert.Throws<NotSupportedException>(() => ServerProcessLauncher.Start(
                new ProcessStartInfo(fileName) { UseShellExecute = false, UserName = "someone" }), "UserName");

            var withArgumentList = new ProcessStartInfo(fileName) { UseShellExecute = false };
            withArgumentList.ArgumentList.Add("x");
            Assert.Throws<NotSupportedException>(() => ServerProcessLauncher.Start(withArgumentList), "ArgumentList");
        }

        // cmd prints its working directory, then the environment block it received.
        private static ProcessStartInfo EnvironmentDumpStartInfo(string logPath)
        {
            var startInfo = new TerminalLauncher().CreateHeadlessProcessStartInfo("(cd & set)", logPath);
            // The same kind of edit StartLocalHttpServer makes: prepend to PATH.
            startInfo.EnvironmentVariables["PATH"] = @"C:\mcp launcher test\bin" + Path.PathSeparator
                + Environment.GetEnvironmentVariable("PATH");
            startInfo.EnvironmentVariables["MCP_LAUNCHER_TEST_PROBE"] = "1";
            return startInfo;
        }

        private static void SetInheritable(SafeHandle handle, bool inheritable)
        {
            if (!SetHandleInformation(handle, HANDLE_FLAG_INHERIT, inheritable ? HANDLE_FLAG_INHERIT : 0))
                throw new InvalidOperationException("SetHandleInformation failed (Win32 " + Marshal.GetLastWin32Error() + ")");
        }

        private static bool IsInheritable(SafeHandle handle)
        {
            if (!GetHandleInformation(handle, out uint flags))
                throw new InvalidOperationException("GetHandleInformation failed (Win32 " + Marshal.GetLastWin32Error() + ")");
            return (flags & HANDLE_FLAG_INHERIT) != 0;
        }

        // Every process with the file open, per the Restart Manager.
        private static List<int> ProcessesHoldingFile(string path)
        {
            int rc = RmStartSession(out uint session, 0, Guid.NewGuid().ToString());
            if (rc != 0)
                throw new InvalidOperationException("RmStartSession failed (" + rc + ")");
            try
            {
                rc = RmRegisterResources(session, 1, new[] { path }, 0, IntPtr.Zero, 0, null);
                if (rc != 0)
                    throw new InvalidOperationException("RmRegisterResources failed (" + rc + ")");

                RM_PROCESS_INFO[] processes = null;
                uint count = 0;
                while (true)
                {
                    uint rebootReasons = 0;
                    rc = RmGetList(session, out uint needed, ref count, processes, ref rebootReasons);
                    if (rc == 0)
                        break;
                    if (rc != ERROR_MORE_DATA)
                        throw new InvalidOperationException("RmGetList failed (" + rc + ")");
                    // The holder set changed between calls; size to the new count and ask again.
                    processes = new RM_PROCESS_INFO[needed];
                    count = needed;
                }

                var pids = new List<int>();
                for (int i = 0; i < count; i++)
                    pids.Add(processes[i].Process.dwProcessId);
                return pids;
            }
            finally
            {
                RmEndSession(session);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(SafeHandle hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetHandleInformation(SafeHandle hObject, out uint lpdwFlags);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint dwSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);
    }
}
