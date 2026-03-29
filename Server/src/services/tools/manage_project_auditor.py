from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

AUDIT_ACTIONS = [
    "audit", "load_report",
]

QUERY_ACTIONS = [
    "get_summary", "list_issues", "get_issue_detail", "list_categories", "list_areas",
]

RULE_ACTIONS = [
    "list_rules", "add_rule", "remove_rule",
]

STATUS_ACTIONS = [
    "status",
]

ALL_ACTIONS = AUDIT_ACTIONS + QUERY_ACTIONS + RULE_ACTIONS + STATUS_ACTIONS


@mcp_for_unity_tool(
    group="auditor",
    description=(
        "Unity Project Auditor: static analysis of code, assets, shaders, settings. "
        "Requires Unity 6.4+. Enable with manage_tools(action='activate', group='auditor').\n\n"
        "WORKFLOW: status (check availability) → audit or load_report → get_summary → "
        "list_issues (filtered + paged) → get_issue_detail. "
        "Use add_rule with severity='None' to suppress noisy issues by descriptor ID.\n\n"
        "AUDIT: audit (run analysis, filtered by categories/assemblies/platform), "
        "load_report (from autosave or custom path)\n\n"
        "QUERY: get_summary (counts by category & severity), "
        "list_issues (filter by category/severity/area/path/search, paged), "
        "get_issue_detail (descriptor info + occurrence locations), "
        "list_categories, list_areas\n\n"
        "RULES: list_rules, add_rule (suppress or change severity), remove_rule\n\n"
        "STATUS: status (availability, report loaded, counts)"
    ),
    annotations=ToolAnnotations(title="Manage Project Auditor"),
)
async def manage_project_auditor(
    ctx: Context,
    action: Annotated[str, "The project auditor action to perform."],
    # Audit filtering
    categories: Annotated[Optional[str], "Comma-separated IssueCategory names for audit (e.g. 'Code,Shader'). Omit for all."] = None,
    assemblies: Annotated[Optional[str], "Comma-separated assembly names to scope audit."] = None,
    platform: Annotated[Optional[str], "BuildTarget name for analysis (e.g. 'StandaloneWindows64'). Defaults to active."] = None,
    # Issue listing filters
    category: Annotated[Optional[str], "Single IssueCategory name to filter issues."] = None,
    severity: Annotated[Optional[str], "Minimum severity: Critical, Major, Moderate, Minor, Warning, Info."] = None,
    area: Annotated[Optional[str], "Area flag to filter: CPU, GPU, Memory, BuildSize, BuildTime, LoadTime, Quality."] = None,
    path_filter: Annotated[Optional[str], "File path substring to filter issues."] = None,
    search: Annotated[Optional[str], "Search term to match against issue description."] = None,
    # Pagination
    page_size: Annotated[Optional[int], "Results per page (default 50, max 200)."] = None,
    cursor: Annotated[Optional[str], "Pagination cursor (offset)."] = None,
    # Issue detail / rule management
    descriptor_id: Annotated[Optional[str], "Descriptor ID (e.g. 'PAC2000') for get_issue_detail, add_rule, remove_rule."] = None,
    rule_severity: Annotated[Optional[str], "Severity for rule: None (suppress), Info, Minor, Moderate, Major, Critical."] = None,
    rule_filter: Annotated[Optional[str], "Optional location scope for rule (e.g. 'Assets/ThirdParty/')."] = None,
    # Report load
    report_path: Annotated[Optional[str], "Path to .projectauditor report file. Defaults to autosave."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_lower}

    param_map = {
        "categories": categories, "assemblies": assemblies, "platform": platform,
        "category": category, "severity": severity, "area": area,
        "path_filter": path_filter, "search": search,
        "page_size": page_size, "cursor": cursor,
        "descriptor_id": descriptor_id, "rule_severity": rule_severity,
        "rule_filter": rule_filter, "report_path": report_path,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_project_auditor", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
