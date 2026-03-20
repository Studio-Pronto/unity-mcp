"""Tests for manage_prefabs tool - component_properties parameter."""

import inspect

from services.tools.manage_prefabs import manage_prefabs


class TestManagePrefabsComponentProperties:
    """Tests for the component_properties parameter on manage_prefabs."""

    def test_component_properties_parameter_exists(self):
        """The manage_prefabs tool should have a component_properties parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "component_properties" in sig.parameters

    def test_component_properties_parameter_is_optional(self):
        """component_properties should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["component_properties"]
        assert param.default is None

    def test_tool_description_mentions_component_properties(self):
        """The tool description should mention component_properties."""
        from services.registry import get_registered_tools
        tools = get_registered_tools()
        prefab_tool = next(
            (t for t in tools if t["name"] == "manage_prefabs"), None
        )
        assert prefab_tool is not None
        # Description is stored at top level or in kwargs depending on how the decorator stores it
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get("description", "")
        assert "component_properties" in desc

    def test_required_params_include_modify_contents(self):
        """modify_contents should be a valid action requiring prefab_path."""
        from services.tools.manage_prefabs import REQUIRED_PARAMS
        assert "modify_contents" in REQUIRED_PARAMS
        assert "prefab_path" in REQUIRED_PARAMS["modify_contents"]


class TestManagePrefabsOverrides:
    """Tests for get_overrides and revert_overrides actions."""

    def test_get_overrides_in_required_params(self):
        from services.tools.manage_prefabs import REQUIRED_PARAMS
        assert "get_overrides" in REQUIRED_PARAMS
        assert "prefab_path" in REQUIRED_PARAMS["get_overrides"]

    def test_revert_overrides_in_required_params(self):
        from services.tools.manage_prefabs import REQUIRED_PARAMS
        assert "revert_overrides" in REQUIRED_PARAMS
        assert "prefab_path" in REQUIRED_PARAMS["revert_overrides"]
        assert "revert_scope" in REQUIRED_PARAMS["revert_overrides"]

    def test_revert_scope_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "revert_scope" in sig.parameters
        assert sig.parameters["revert_scope"].default is None

    def test_component_type_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "component_type" in sig.parameters
        assert sig.parameters["component_type"].default is None

    def test_property_path_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "property_path" in sig.parameters
        assert sig.parameters["property_path"].default is None

    def test_action_literal_includes_new_actions(self):
        import typing
        sig = inspect.signature(manage_prefabs)
        annotation = sig.parameters["action"].annotation
        args = typing.get_args(annotation)
        literal_type = args[0] if typing.get_origin(annotation) is typing.Annotated else annotation
        literal_args = typing.get_args(literal_type)
        assert "get_overrides" in literal_args
        assert "revert_overrides" in literal_args

    def test_tool_description_mentions_overrides(self):
        from services.registry import get_registered_tools
        tools = get_registered_tools()
        prefab_tool = next((t for t in tools if t["name"] == "manage_prefabs"), None)
        assert prefab_tool is not None
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get("description", "")
        assert "get_overrides" in desc
        assert "revert_overrides" in desc
