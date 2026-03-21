using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class CaptureOps
    {
        private static string _currentCapturePath;

        // === capture_start ===

        internal static object Start(JObject @params)
        {
            var p = new ToolParams(@params);
            string outputPath = p.Get("output_path", "outputPath");

            if (!string.IsNullOrEmpty(_currentCapturePath))
                return new ErrorResponse($"Capture already in progress (writing to '{_currentCapturePath}'). Call capture_stop first.");

            if (string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.Combine(Application.dataPath, "..", "Profiler");
                Directory.CreateDirectory(dir);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                outputPath = Path.Combine(dir, $"capture_{timestamp}.raw");
            }

            string fullPath = Path.GetFullPath(outputPath);
            string parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            Profiler.logFile = fullPath;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            _currentCapturePath = fullPath;

            return new
            {
                success = true,
                message = $"Profiler capture started. Writing to: {fullPath}",
                data = new
                {
                    path = fullPath,
                    profiler_enabled = true,
                    binary_log_enabled = true,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === capture_stop ===

        internal static object Stop(JObject @params)
        {
            var p = new ToolParams(@params);
            bool keepEnabled = p.GetBool("keep_profiler_enabled", false);

            string path = Profiler.logFile;
            Profiler.enableBinaryLog = false;

            if (!keepEnabled)
                Profiler.enabled = false;

            long sizeBytes = 0;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { sizeBytes = new FileInfo(path).Length; }
                catch { }
            }

            _currentCapturePath = null;

            return new
            {
                success = true,
                message = $"Profiler capture stopped. File: {path} ({Math.Round(sizeBytes / (1024.0 * 1024.0), 2)}MB)",
                data = new
                {
                    path,
                    size_bytes = sizeBytes,
                    size_mb = Math.Round(sizeBytes / (1024.0 * 1024.0), 2),
                    profiler_enabled = Profiler.enabled,
                    play_mode = EditorApplication.isPlaying
                }
            };
        }

        // === capture_status ===

        internal static object Status(JObject @params)
        {
            return new
            {
                success = true,
                message = "Profiler capture status.",
                data = new
                {
                    profiler_enabled = Profiler.enabled,
                    binary_log_enabled = Profiler.enableBinaryLog,
                    log_file = Profiler.logFile ?? "",
                    deep_profiling = ProfilerDriver.deepProfiling,
                    current_capture_path = _currentCapturePath ?? "",
                    play_mode = EditorApplication.isPlaying
                }
            };
        }
    }
}
