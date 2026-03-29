using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.ProjectAuditor
{
    [McpForUnityTool("manage_project_auditor", AutoRegister = false, Group = "auditor")]
    public static class ManageProjectAuditor
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
#if !UNITY_6000_4_OR_NEWER
            return new ErrorResponse("Project Auditor requires Unity 6.4 or later.");
#else
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                switch (action)
                {
                    // --- Audit ---
                    case "audit":
                        return AuditOps.RunAudit(@params);
                    case "load_report":
                        return AuditOps.LoadReport(@params);

                    // --- Query ---
                    case "get_summary":
                        return AuditOps.GetSummary();
                    case "list_issues":
                        return IssueOps.ListIssues(@params);
                    case "get_issue_detail":
                        return IssueOps.GetIssueDetail(@params);
                    case "list_categories":
                        return IssueOps.ListCategories();
                    case "list_areas":
                        return IssueOps.ListAreas();

                    // --- Rules ---
                    case "list_rules":
                        return RuleOps.ListRules();
                    case "add_rule":
                        return RuleOps.AddRule(@params);
                    case "remove_rule":
                        return RuleOps.RemoveRule(@params);

                    // --- Status ---
                    case "status":
                        return AuditOps.Status();

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: "
                            + "audit, load_report, get_summary, list_issues, get_issue_detail, "
                            + "list_categories, list_areas, list_rules, add_rule, remove_rule, status");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageProjectAuditor] Error in action '{action}': {ex}");
                return new ErrorResponse($"Error in '{action}': {ex.Message}");
            }
#endif
        }
    }
}
