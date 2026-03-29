import click
from cli.utils.connection import handle_unity_errors, run_command, get_config
from cli.utils.output import format_output


@click.group("auditor")
def auditor():
    """Project Auditor commands for static analysis (Unity 6.4+)."""
    pass


# --- Audit ---

@auditor.command("audit")
@click.option("--categories", "-c", default=None, help="Comma-separated IssueCategory names (e.g. 'Code,Shader').")
@click.option("--assemblies", "-a", default=None, help="Comma-separated assembly names.")
@click.option("--platform", "-p", default=None, help="BuildTarget name (e.g. 'StandaloneWindows64').")
@handle_unity_errors
def audit(categories, assemblies, platform):
    """Run Project Auditor analysis."""
    config = get_config()
    params = {"action": "audit"}
    if categories is not None:
        params["categories"] = categories
    if assemblies is not None:
        params["assemblies"] = assemblies
    if platform is not None:
        params["platform"] = platform
    result = run_command("manage_project_auditor", params, config)
    click.echo(format_output(result, config.format))


@auditor.command("load-report")
@click.option("--path", default=None, help="Path to .projectauditor report file.")
@handle_unity_errors
def load_report(path):
    """Load a report from disk (defaults to autosave)."""
    config = get_config()
    params = {"action": "load_report"}
    if path is not None:
        params["report_path"] = path
    result = run_command("manage_project_auditor", params, config)
    click.echo(format_output(result, config.format))


# --- Query ---

@auditor.command("summary")
@handle_unity_errors
def summary():
    """Show issue counts by category and severity."""
    config = get_config()
    result = run_command("manage_project_auditor", {"action": "get_summary"}, config)
    click.echo(format_output(result, config.format))


@auditor.command("issues")
@click.option("--category", "-c", default=None, help="IssueCategory filter.")
@click.option("--severity", "-s", default=None, help="Minimum severity: Critical, Major, Moderate, Minor, Warning, Info.")
@click.option("--area", "-a", default=None, help="Area filter: CPU, GPU, Memory, BuildSize, etc.")
@click.option("--path-filter", default=None, help="File path substring filter.")
@click.option("--search", default=None, help="Description text search.")
@click.option("--page-size", type=int, default=None, help="Results per page (default 50).")
@click.option("--cursor", default=None, help="Pagination cursor (offset).")
@handle_unity_errors
def issues(category, severity, area, path_filter, search, page_size, cursor):
    """List issues with filtering and pagination."""
    config = get_config()
    params = {"action": "list_issues"}
    if category is not None:
        params["category"] = category
    if severity is not None:
        params["severity"] = severity
    if area is not None:
        params["area"] = area
    if path_filter is not None:
        params["path_filter"] = path_filter
    if search is not None:
        params["search"] = search
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    result = run_command("manage_project_auditor", params, config)
    click.echo(format_output(result, config.format))


@auditor.command("detail")
@click.argument("descriptor_id")
@handle_unity_errors
def detail(descriptor_id):
    """Show full descriptor info and occurrences for a descriptor ID."""
    config = get_config()
    result = run_command("manage_project_auditor", {"action": "get_issue_detail", "descriptor_id": descriptor_id}, config)
    click.echo(format_output(result, config.format))


@auditor.command("categories")
@handle_unity_errors
def categories():
    """List all available IssueCategory values."""
    config = get_config()
    result = run_command("manage_project_auditor", {"action": "list_categories"}, config)
    click.echo(format_output(result, config.format))


@auditor.command("areas")
@handle_unity_errors
def areas():
    """List all available Areas flag values."""
    config = get_config()
    result = run_command("manage_project_auditor", {"action": "list_areas"}, config)
    click.echo(format_output(result, config.format))


# --- Rules ---

@auditor.command("rules")
@handle_unity_errors
def rules():
    """List current suppression/severity rules."""
    config = get_config()
    result = run_command("manage_project_auditor", {"action": "list_rules"}, config)
    click.echo(format_output(result, config.format))


@auditor.command("add-rule")
@click.argument("descriptor_id")
@click.option("--severity", "-s", required=True, help="Severity: None (suppress), Info, Minor, Moderate, Major, Critical.")
@click.option("--filter", "rule_filter", default=None, help="Optional location scope (e.g. 'Assets/ThirdParty/').")
@handle_unity_errors
def add_rule(descriptor_id, severity, rule_filter):
    """Add a suppression or severity rule for a descriptor ID."""
    config = get_config()
    params = {"action": "add_rule", "descriptor_id": descriptor_id, "rule_severity": severity}
    if rule_filter is not None:
        params["rule_filter"] = rule_filter
    result = run_command("manage_project_auditor", params, config)
    click.echo(format_output(result, config.format))


@auditor.command("remove-rule")
@click.argument("descriptor_id")
@click.option("--filter", "rule_filter", default=None, help="Location scope of rule to remove.")
@handle_unity_errors
def remove_rule(descriptor_id, rule_filter):
    """Remove a rule by descriptor ID."""
    config = get_config()
    params = {"action": "remove_rule", "descriptor_id": descriptor_id}
    if rule_filter is not None:
        params["rule_filter"] = rule_filter
    result = run_command("manage_project_auditor", params, config)
    click.echo(format_output(result, config.format))


# --- Status ---

@auditor.command("status")
@handle_unity_errors
def status():
    """Check Project Auditor availability and report state."""
    config = get_config()
    result = run_command("manage_project_auditor", {"action": "status"}, config)
    click.echo(format_output(result, config.format))
