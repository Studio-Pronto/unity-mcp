using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Starts the local HTTP server's launch process without handing it the editor's handles.
    ///
    /// On Windows, Mono's Process.Start calls CreateProcessW with bInheritHandles = TRUE, so the
    /// launch process, and the uvx, uv and python processes it starts, get a copy of every
    /// inheritable handle the editor holds. That includes the editor's open Logs/Editor.log. The
    /// server outlives the editor by design, so the copy keeps that log locked: every later editor
    /// of the project fails to open it and writes to the machine-global Editor.log instead (#50).
    /// This launcher makes the CreateProcessW call Mono makes, with inheritance off.
    ///
    /// Differences from Process.Start on Windows, all deliberate:
    /// - No handle is inherited.
    /// - No standard handles are passed. Mono passes the editor's own, which a GUI editor does not
    ///   have (likely the invalid stdin behind upstream #1279). cmd.exe uses its own hidden
    ///   console's instead; the command's output still goes where its redirects send it.
    /// - The environment block is always built from EnvironmentVariables. When a start info never
    ///   touched them, Mono passes the editor's live block; this passes a snapshot of it, which
    ///   lacks Windows' hidden "=C:" per-drive directory entries.
    ///
    /// Every platform honours FileName, Arguments, WorkingDirectory, CreateNoWindow and
    /// EnvironmentVariables, and rejects the rest of what Process.Start implements (shell execute,
    /// redirects, another user, ArgumentList) with NotSupportedException, so no caller silently
    /// loses one. Off Windows this is Process.Start: Mono closes descriptors above 2 in a POSIX
    /// child, so nothing leaks there.
    /// </summary>
    internal static class ServerProcessLauncher
    {
        private const uint CREATE_SUSPENDED = 0x00000004;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;

        internal static Process Start(ProcessStartInfo startInfo)
        {
            if (startInfo.UseShellExecute || startInfo.RedirectStandardInput || startInfo.RedirectStandardOutput
                || startInfo.RedirectStandardError || startInfo.UserName.Length != 0 || startInfo.ArgumentList.Count != 0)
            {
                throw new NotSupportedException(
                    "ServerProcessLauncher honours only FileName, Arguments, WorkingDirectory, CreateNoWindow and " +
                    "EnvironmentVariables. Set UseShellExecute = false and leave redirects, UserName and ArgumentList unset.");
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return Process.Start(startInfo);
            }

            return StartOnWindows(startInfo);
        }

        private static Process StartOnWindows(ProcessStartInfo startInfo)
        {
            // Mono's command line: the file name with one pair of surrounding quotes stripped and
            // re-added, then the arguments.
            string fileName = startInfo.FileName;
            if (fileName.EndsWith("\"", StringComparison.Ordinal)) fileName = fileName.Substring(0, fileName.Length - 1);
            if (fileName.StartsWith("\"", StringComparison.Ordinal)) fileName = fileName.Substring(1);
            var commandLine = new StringBuilder().Append('"').Append(fileName).Append('"');
            if (startInfo.Arguments.Length != 0)
            {
                commandLine.Append(' ').Append(startInfo.Arguments);
            }

            // Mono's environment block: EnvironmentVariables in enumeration order, null values
            // skipped, each entry NUL-terminated and the block closed by one more NUL.
            var environment = new StringBuilder();
            foreach (DictionaryEntry entry in startInfo.EnvironmentVariables)
            {
                if (entry.Value == null) continue;
                environment.Append((string)entry.Key).Append('=').Append((string)entry.Value).Append('\0');
            }
            environment.Append('\0');

            uint flags = CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED;
            if (startInfo.CreateNoWindow) flags |= CREATE_NO_WINDOW;
            string workingDirectory = startInfo.WorkingDirectory.Length != 0 ? startInfo.WorkingDirectory : null;
            var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

            PROCESS_INFORMATION info;
            IntPtr environmentBlock = Marshal.StringToHGlobalUni(environment.ToString());
            try
            {
                if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                        environmentBlock, workingDirectory, ref startupInfo, out info))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            Process process = null;
            try
            {
                // Opened while the process is still suspended, so it cannot exit (and its id be
                // reused) before the Process object exists.
                process = Process.GetProcessById(info.dwProcessId);
                // Mono opens that handle inheritable; Process.Start's own handle is not, so the
                // editor's later child processes do not get one.
                if (!SetHandleInformation(process.SafeHandle, HANDLE_FLAG_INHERIT, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (ResumeThread(info.hThread) == uint.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return process;
            }
            catch
            {
                TerminateProcess(info.hProcess, 1);
                process?.Dispose();
                throw;
            }
            finally
            {
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
        private static extern bool CreateProcess(string lpApplicationName, StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory, [In] ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(SafeHandle hObject, uint dwMask, uint dwFlags);
    }
}
