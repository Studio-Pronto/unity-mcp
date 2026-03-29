#if UNITY_6000_4_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.ProjectAuditor.Editor;
using Unity.ProjectAuditor.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectAuditor
{
    /// <summary>
    /// Lightweight snapshot of a ReportItem, decoupled from Unity's Report lifecycle.
    /// The Report object's internal issue collections become inaccessible after domain
    /// reloads even when the object itself is freshly loaded. This struct holds the
    /// data we actually need for queries.
    /// </summary>
    internal struct CachedIssue
    {
        public string DescriptorId;
        public string Description;
        public IssueCategory Category;
        public Severity Severity;
        public Areas Areas;
        public string RelativePath;
        public string Filename;
        public int Line;
    }

    internal static class AuditOps
    {
        private static List<CachedIssue> _cachedIssues;
        private static string _reportDisplayName;
        private static bool _auditInProgress;
        private static DateTime _auditStartTime;
        private static string _auditError;

        private const double WatchdogTimeoutSeconds = 300.0; // 5 minutes

        private static readonly string AutosavePath =
            Path.Combine("Library", "projectauditor-report-autosave.projectauditor");

        internal static List<CachedIssue> CachedIssues
        {
            get
            {
                if (_cachedIssues == null)
                    TryRecoverFromDisk();
                return _cachedIssues;
            }
        }

        [InitializeOnLoadMethod]
        private static void ResetOnDomainReload()
        {
            bool wasInProgress = _auditInProgress;
            _auditInProgress = false;
            UnregisterWatchdog();

            if (wasInProgress)
            {
                _auditError = "Audit interrupted by domain reload. Use load_report if autosave exists.";
                McpLog.Info("[AuditOps] Domain reload during audit. Use load_report to recover results.");
            }
        }

        // === audit ===

        internal static object RunAudit(JObject @params)
        {
            if (_auditInProgress)
                return new ErrorResponse("Audit already in progress.");

            var p = new ToolParams(@params);

            var analysisParams = new AnalysisParams
            {
                OnCompleted = OnAuditCompleted
            };

            // Category filter
            string categoriesStr = p.Get("categories");
            if (!string.IsNullOrEmpty(categoriesStr))
            {
                var cats = ProjectAuditorHelpers.ParseCategories(categoriesStr);
                if (cats == null)
                    return new ErrorResponse($"Invalid categories: '{categoriesStr}'. Use list_categories for valid values.");
                analysisParams.Categories = cats
                    .Select(c => new SerializableEnum<IssueCategory>(c))
                    .ToArray();
            }

            // Assembly filter
            string assembliesStr = p.Get("assemblies");
            if (!string.IsNullOrEmpty(assembliesStr))
            {
                analysisParams.AssemblyNames = assembliesStr
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();
            }

            // Platform
            string platformStr = p.Get("platform");
            if (!string.IsNullOrEmpty(platformStr))
            {
                if (Enum.TryParse<BuildTarget>(platformStr, true, out var target))
                    analysisParams.Platform = target;
                else
                    McpLog.Warn($"[AuditOps] Unknown platform '{platformStr}', using active platform.");
            }

            _auditInProgress = true;
            _auditStartTime = DateTime.UtcNow;
            _cachedIssues = null;
            _reportDisplayName = null;
            _auditError = null;

            try
            {
                new Unity.ProjectAuditor.Editor.ProjectAuditor().AuditAsync(analysisParams);
            }
            catch (Exception ex)
            {
                _auditInProgress = false;
                return new ErrorResponse($"Failed to start audit: {ex.Message}");
            }

            RegisterWatchdog();

            return new SuccessResponse("Audit started. Poll status to check progress.", new
            {
                audit_in_progress = true
            });
        }

        private static void OnAuditCompleted(Report report)
        {
            _auditInProgress = false;
            _auditError = null;
            UnregisterWatchdog();

            SnapshotReport(report);

            // Save to disk for domain-reload recovery
            try { report.Save(AutosavePath); }
            catch (Exception ex) { McpLog.Warn($"[AuditOps] Failed to save report: {ex.Message}"); }
            EditorPrefs.SetString(EditorPrefKeys.LastAuditReportPath, AutosavePath);

            var duration = (DateTime.UtcNow - _auditStartTime).TotalSeconds;
            McpLog.Info($"[AuditOps] Audit completed: {_cachedIssues.Count} issues in {duration:F1}s");
        }

        // === Watchdog ===

        private static void RegisterWatchdog()
        {
            EditorApplication.update += AuditWatchdog;
        }

        private static void UnregisterWatchdog()
        {
            EditorApplication.update -= AuditWatchdog;
        }

        private static void AuditWatchdog()
        {
            if (!_auditInProgress)
            {
                UnregisterWatchdog();
                return;
            }
            if ((DateTime.UtcNow - _auditStartTime).TotalSeconds > WatchdogTimeoutSeconds)
            {
                McpLog.Warn("[AuditOps] Audit watchdog: timed out after 5 minutes. Resetting.");
                _auditInProgress = false;
                _auditError = "Audit timed out after 5 minutes.";
                UnregisterWatchdog();
            }
        }

        // === load_report ===

        internal static object LoadReport(JObject @params)
        {
            var p = new ToolParams(@params);
            string path = p.Get("report_path", "reportPath");

            if (string.IsNullOrEmpty(path))
            {
                path = AutosavePath;
            }

            if (!File.Exists(path))
                return new ErrorResponse($"Report file not found: '{path}'");

            var report = Report.Load(path, out string error);
            if (report == null || !string.IsNullOrEmpty(error))
                return new ErrorResponse($"Failed to load report: {error ?? "unknown error"}");

            SnapshotReport(report);
            EditorPrefs.SetString(EditorPrefKeys.LastAuditReportPath, path);

            return new SuccessResponse(
                $"Report loaded. {_cachedIssues.Count} issues.",
                SummarizeFromCache());
        }

        // === get_summary ===

        internal static object GetSummary()
        {
            var issues = CachedIssues;
            if (issues == null)
                return new ErrorResponse("No report loaded. Run audit or load_report first.");

            return new SuccessResponse(
                $"Report summary: {issues.Count} total issues.",
                SummarizeFromCache());
        }

        // === status ===

        internal static object Status()
        {
            int ruleCount = RuleOps.GetRuleCount();
            var issues = CachedIssues;

            return new SuccessResponse(_auditInProgress ? "Audit in progress..." : "Project Auditor ready.", new
            {
                available = true,
                unity_version = Application.unityVersion,
                report_loaded = issues != null,
                report_issue_count = issues?.Count ?? 0,
                report_display_name = _reportDisplayName ?? "",
                autosave_exists = File.Exists(AutosavePath),
                rule_count = ruleCount,
                audit_in_progress = _auditInProgress,
                audit_error = _auditError
            });
        }

        // === Snapshot ===

        private static void SnapshotReport(Report report)
        {
            _reportDisplayName = report.DisplayName ?? "";
            _cachedIssues = new List<CachedIssue>();

            foreach (var item in report.GetAllIssues())
            {
                var desc = item.Id.IsValid() ? item.Id.GetDescriptor() : null;
                _cachedIssues.Add(new CachedIssue
                {
                    DescriptorId = item.Id.IsValid() ? item.Id.AsString() : "",
                    Description = item.Description ?? "",
                    Category = item.Category,
                    Severity = item.Severity,
                    Areas = desc?.Areas ?? Areas.None,
                    RelativePath = item.RelativePath ?? "",
                    Filename = item.Filename ?? "",
                    Line = item.Line
                });
            }
        }

        private static void TryRecoverFromDisk()
        {
            try
            {
                string path = EditorPrefs.GetString(EditorPrefKeys.LastAuditReportPath, "");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    path = AutosavePath;
                    if (!File.Exists(path))
                        return;
                }

                var report = Report.Load(path, out string error);
                if (report == null || !string.IsNullOrEmpty(error))
                    return;

                SnapshotReport(report);
                McpLog.Info($"[AuditOps] Auto-recovered report from {path} ({_cachedIssues.Count} issues).");
            }
            catch
            {
                // Recovery is best-effort
            }
        }

        // === Helpers ===

        private static object SummarizeFromCache(double? durationSeconds = null)
        {
            var issues = _cachedIssues;
            if (issues == null || issues.Count == 0)
            {
                return new Dictionary<string, object>
                {
                    ["total_issues"] = 0,
                    ["by_category"] = new List<object>(),
                    ["by_severity"] = new Dictionary<string, int>()
                };
            }

            var byCategory = new List<object>();
            foreach (var g in issues.GroupBy(i => i.Category))
            {
                if (g.Key == IssueCategory.Metadata || g.Key == IssueCategory.FirstCustomCategory)
                    continue;
                int count = g.Count();
                if (count == 0)
                    continue;
                byCategory.Add(new { category = g.Key.ToString(), count });
            }

            var bySeverity = new Dictionary<string, int>();
            foreach (var g in issues.GroupBy(i => i.Severity))
                bySeverity[g.Key.ToString()] = g.Count();

            var result = new Dictionary<string, object>
            {
                ["total_issues"] = issues.Count,
                ["by_category"] = byCategory,
                ["by_severity"] = bySeverity
            };

            if (durationSeconds.HasValue)
                result["duration_seconds"] = Math.Round(durationSeconds.Value, 2);

            return result;
        }
    }
}
#endif
