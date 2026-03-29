#if UNITY_6000_4_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.ProjectAuditor.Editor;
using Unity.ProjectAuditor.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectAuditor
{
    internal static class AuditOps
    {
        private static Report _cachedReport;
        private static bool _auditInProgress;
        private static DateTime _auditStartTime;
        private static string _auditError;

        private const double WatchdogTimeoutSeconds = 300.0; // 5 minutes

        internal static Report CachedReport => _cachedReport;

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
            _cachedReport = null;
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
            _cachedReport = report;
            _auditInProgress = false;
            _auditError = null;
            UnregisterWatchdog();
            var duration = (DateTime.UtcNow - _auditStartTime).TotalSeconds;
            McpLog.Info($"[AuditOps] Audit completed: {report.NumTotalIssues} issues in {duration:F1}s");
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
                // Default to autosave
                path = Path.Combine("Library", "projectauditor-report-autosave.projectauditor");
            }

            if (!File.Exists(path))
                return new ErrorResponse($"Report file not found: '{path}'");

            var report = Report.Load(path, out string error);
            if (report == null || !string.IsNullOrEmpty(error))
                return new ErrorResponse($"Failed to load report: {error ?? "unknown error"}");

            _cachedReport = report;
            return new SuccessResponse(
                $"Report loaded. {_cachedReport.NumTotalIssues} issues.",
                Summarize(_cachedReport));
        }

        // === get_summary ===

        internal static object GetSummary()
        {
            if (_cachedReport == null)
                return new ErrorResponse("No report loaded. Run audit or load_report first.");

            return new SuccessResponse(
                $"Report summary: {_cachedReport.NumTotalIssues} total issues.",
                Summarize(_cachedReport));
        }

        // === status ===

        internal static object Status()
        {
            string autosavePath = Path.Combine("Library", "projectauditor-report-autosave.projectauditor");
            int ruleCount = RuleOps.GetRuleCount();

            return new SuccessResponse(_auditInProgress ? "Audit in progress..." : "Project Auditor ready.", new
            {
                available = true,
                unity_version = Application.unityVersion,
                report_loaded = _cachedReport != null,
                report_issue_count = _cachedReport?.NumTotalIssues ?? 0,
                report_display_name = _cachedReport?.DisplayName ?? "",
                autosave_exists = File.Exists(autosavePath),
                rule_count = ruleCount,
                audit_in_progress = _auditInProgress,
                audit_error = _auditError
            });
        }

        // === Helpers ===

        private static object Summarize(Report report, double? durationSeconds = null)
        {
            var byCategory = new List<object>();
            var bySeverity = new Dictionary<string, int>();

            foreach (IssueCategory cat in Enum.GetValues(typeof(IssueCategory)))
            {
                if (cat == IssueCategory.Metadata || cat == IssueCategory.FirstCustomCategory)
                    continue;
                if (!report.HasCategory(cat))
                    continue;

                int count = report.GetNumIssues(cat);
                if (count == 0)
                    continue;

                byCategory.Add(new { category = cat.ToString(), count });
            }

            foreach (var item in report.GetAllIssues())
            {
                string sev = item.Severity.ToString();
                if (!bySeverity.ContainsKey(sev))
                    bySeverity[sev] = 0;
                bySeverity[sev]++;
            }

            var result = new Dictionary<string, object>
            {
                ["total_issues"] = report.NumTotalIssues,
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
