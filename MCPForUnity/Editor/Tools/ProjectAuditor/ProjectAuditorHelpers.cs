#if UNITY_6000_4_OR_NEWER
using System;
using System.Linq;
using Unity.ProjectAuditor.Editor;

namespace MCPForUnity.Editor.Tools.ProjectAuditor
{
    internal static class ProjectAuditorHelpers
    {
        /// <summary>
        /// Parse a comma-separated string of IssueCategory names into an array.
        /// Returns null if any name is invalid.
        /// </summary>
        internal static IssueCategory[] ParseCategories(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            var names = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new IssueCategory[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                if (!Enum.TryParse<IssueCategory>(names[i].Trim(), true, out result[i]))
                    return null;
            }

            return result;
        }

        internal static bool TryParseCategory(string input, out IssueCategory category)
        {
            return Enum.TryParse(input.Trim(), true, out category);
        }

        internal static bool TryParseSeverity(string input, out Severity severity)
        {
            return Enum.TryParse(input.Trim(), true, out severity);
        }

        internal static bool TryParseArea(string input, out Areas area)
        {
            return Enum.TryParse(input.Trim(), true, out area);
        }

        /// <summary>
        /// Serialize a ReportItem to a compact anonymous object for JSON responses.
        /// </summary>
        internal static object SerializeIssue(ReportItem item)
        {
            return new
            {
                descriptor_id = item.Id.IsValid() ? item.Id.AsString() : null,
                description = item.Description,
                category = item.Category.ToString(),
                severity = item.Severity.ToString(),
                filename = item.Filename,
                relative_path = item.RelativePath,
                line = item.Line
            };
        }
    }
}
#endif
