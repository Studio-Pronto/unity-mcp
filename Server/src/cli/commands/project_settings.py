"""Project settings CLI commands."""

import click
from typing import Optional

from cli.utils.config import get_config
from cli.utils.output import format_output
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def project_settings():
    """Read, write, and discover Unity project settings."""
    pass


@project_settings.command("get")
@click.argument("category")
@click.argument("property_name")
@handle_unity_errors
def get_setting(category: str, property_name: str):
    """Read a project setting.

    \b
    Categories: quality, physics, physics2d, time, editor

    \b
    Examples:
        unity-mcp project-settings get quality shadowDistance
        unity-mcp project-settings get physics gravity
        unity-mcp project-settings get time fixedDeltaTime
    """
    config = get_config()
    params = {"action": "get", "category": category, "property": property_name}
    result = run_command("manage_project_settings", params, config)
    click.echo(format_output(result, config.format))


@project_settings.command("set")
@click.argument("category")
@click.argument("property_name")
@click.option("--value", "-v", required=True, help="Value to set")
@handle_unity_errors
def set_setting(category: str, property_name: str, value: str):
    """Write a project setting.

    \b
    Categories: quality, physics, physics2d, time, editor

    \b
    Examples:
        unity-mcp project-settings set quality shadowDistance --value 100
        unity-mcp project-settings set physics gravity --value "[0, -20, 0]"
        unity-mcp project-settings set time fixedDeltaTime --value 0.01
        unity-mcp project-settings set editor serializationMode --value ForceText
    """
    config = get_config()
    params = {
        "action": "set",
        "category": category,
        "property": property_name,
        "value": value,
    }
    result = run_command("manage_project_settings", params, config)
    click.echo(format_output(result, config.format))


@project_settings.command("list")
@click.argument("category")
@handle_unity_errors
def list_properties(category: str):
    """List all properties for a settings category.

    \b
    Categories: quality, physics, physics2d, time, editor

    \b
    Examples:
        unity-mcp project-settings list quality
        unity-mcp project-settings list physics
    """
    config = get_config()
    params = {"action": "list", "category": category}
    result = run_command("manage_project_settings", params, config)
    click.echo(format_output(result, config.format))


@project_settings.command("categories")
@handle_unity_errors
def list_categories():
    """List all supported settings categories."""
    config = get_config()
    params = {"action": "list_categories"}
    result = run_command("manage_project_settings", params, config)
    click.echo(format_output(result, config.format))
