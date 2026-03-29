#if UNITY_6000_4_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.ProjectAuditor.Editor;

namespace MCPForUnity.Editor.Tools.ProjectAuditor
{
    internal static class IssueOps
    {
        // === list_issues ===

        internal static object ListIssues(JObject @params)
        {
            var report = AuditOps.CachedReport;
            if (report == null)
                return new ErrorResponse("No report loaded. Run audit or load_report first.");

            var p = new ToolParams(@params);

            // Start with all issues or filter by category
            IEnumerable<ReportItem> issues;
            string categoryStr = p.Get("category");
            if (!string.IsNullOrEmpty(categoryStr))
            {
                if (!ProjectAuditorHelpers.TryParseCategory(categoryStr, out var cat))
                    return new ErrorResponse($"Invalid category: '{categoryStr}'. Use list_categories for valid values.");
                issues = report.FindByCategory(cat);
            }
            else
            {
                issues = report.GetAllIssues();
            }

            // Severity filter (minimum threshold)
            string severityStr = p.Get("severity");
            if (!string.IsNullOrEmpty(severityStr))
            {
                if (!ProjectAuditorHelpers.TryParseSeverity(severityStr, out var minSev))
                    return new ErrorResponse($"Invalid severity: '{severityStr}'. Valid: Critical, Error, Major, Moderate, Minor, Warning, Info.");
                issues = issues.Where(i => i.Severity <= minSev); // Lower enum value = higher severity
            }

            // Area filter
            string areaStr = p.Get("area");
            if (!string.IsNullOrEmpty(areaStr))
            {
                if (!ProjectAuditorHelpers.TryParseArea(areaStr, out var areaFlag))
                    return new ErrorResponse($"Invalid area: '{areaStr}'. Use list_areas for valid values.");
                issues = issues.Where(i =>
                {
                    var desc = i.Id.IsValid() ? i.Id.GetDescriptor() : null;
                    return desc != null && (desc.Areas & areaFlag) != 0;
                });
            }

            // Path filter
            string pathFilter = p.Get("path_filter", "pathFilter");
            if (!string.IsNullOrEmpty(pathFilter))
                issues = issues.Where(i => i.RelativePath != null && i.RelativePath.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            // Search
            string search = p.Get("search");
            if (!string.IsNullOrEmpty(search))
                issues = issues.Where(i => i.Description != null && i.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

            // Materialize for count
            var filtered = issues.ToList();
            int totalMatching = filtered.Count;

            // Pagination
            int pageSize = p.GetInt("page_size") ?? 50;
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            int cursor = p.GetInt("cursor") ?? 0;
            cursor = Math.Max(cursor, 0);

            var page = filtered.Skip(cursor).Take(pageSize).ToList();
            int? nextCursor = (cursor + pageSize < totalMatching) ? cursor + pageSize : (int?)null;

            return new SuccessResponse(
                $"Showing issues {cursor + 1}-{cursor + page.Count} of {totalMatching}.",
                new
                {
                    issues = page.Select(ProjectAuditorHelpers.SerializeIssue).ToList(),
                    total_matching = totalMatching,
                    page_size = pageSize,
                    cursor,
                    next_cursor = nextCursor
                });
        }

        // === get_issue_detail ===

        internal static object GetIssueDetail(JObject @params)
        {
            var report = AuditOps.CachedReport;
            if (report == null)
                return new ErrorResponse("No report loaded. Run audit or load_report first.");

            var p = new ToolParams(@params);
            string idStr = p.Get("descriptor_id", "descriptorId");
            if (string.IsNullOrEmpty(idStr))
                return new ErrorResponse("'descriptor_id' parameter is required.");

            var descriptorId = new DescriptorId(idStr);
            if (!descriptorId.IsValid())
                return new ErrorResponse($"Invalid descriptor ID: '{idStr}'.");

            var descriptor = descriptorId.GetDescriptor();
            var occurrences = report.FindByDescriptorId(idStr);

            // Cap location list for response size
            const int maxLocations = 50;
            var locations = occurrences
                .Where(i => i.RelativePath != null)
                .Take(maxLocations)
                .Select(i => new { file = i.RelativePath, line = i.Line })
                .ToList();

            return new SuccessResponse($"Descriptor {idStr}: {occurrences.Count} occurrences.", new
            {
                descriptor_id = idStr,
                title = descriptor?.Title ?? "",
                description = descriptor?.Description ?? "",
                recommendation = descriptor?.Recommendation ?? "",
                documentation_url = descriptor?.DocumentationUrl ?? "",
                areas = descriptor?.Areas.ToString() ?? "",
                default_severity = descriptor?.DefaultSeverity.ToString() ?? "",
                occurrences = occurrences.Count,
                locations,
                locations_capped = occurrences.Count > maxLocations
            });
        }

        // === list_categories ===

        internal static object ListCategories()
        {
            var categories = Enum.GetValues(typeof(IssueCategory))
                .Cast<IssueCategory>()
                .Where(c => c != IssueCategory.Metadata && c != IssueCategory.FirstCustomCategory)
                .Select(c => c.ToString())
                .ToList();

            return new SuccessResponse($"{categories.Count} categories available.", new { categories });
        }

        // === list_areas ===

        internal static object ListAreas()
        {
            var areas = Enum.GetValues(typeof(Areas))
                .Cast<Areas>()
                .Where(a => a != Areas.None && a != Areas.All)
                .Select(a => a.ToString())
                .ToList();

            return new SuccessResponse($"{areas.Count} areas available.", new { areas });
        }
    }
}
#endif
