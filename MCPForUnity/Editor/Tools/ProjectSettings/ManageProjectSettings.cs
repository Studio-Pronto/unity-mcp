using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.ProjectSettings;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_project_settings", AutoRegister = false, Group = "core")]
    public static class ManageProjectSettings
    {
        private static readonly string[] ValidActions =
            { "get", "set", "list", "list_categories" };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            if (!ValidActions.Contains(action))
                return new ErrorResponse(
                    $"Unknown action '{action}'. Valid actions: {string.Join(", ", ValidActions)}");

            try
            {
                switch (action)
                {
                    case "get": return HandleGet(p);
                    case "set": return HandleSet(p);
                    case "list": return HandleList(p);
                    case "list_categories": return HandleListCategories();
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        private static object HandleGet(ToolParams p)
        {
            var catResult = p.GetRequired("category");
            if (!catResult.IsSuccess)
                return new ErrorResponse(catResult.ErrorMessage);

            var propResult = p.GetRequired("property");
            if (!propResult.IsSuccess)
                return new ErrorResponse(propResult.ErrorMessage);

            var result = ProjectSettingsHelper.ReadProperty(catResult.Value, propResult.Value);
            if (result == null)
                return new ErrorResponse(
                    $"Could not read property '{propResult.Value}' in category '{catResult.Value}'. "
                    + "Use action='list_categories' to see valid categories, "
                    + "or action='list' to see properties for a category.");
            return new SuccessResponse($"Read {catResult.Value}.{propResult.Value}.", result);
        }

        private static object HandleSet(ToolParams p)
        {
            var catResult = p.GetRequired("category");
            if (!catResult.IsSuccess)
                return new ErrorResponse(catResult.ErrorMessage);

            var propResult = p.GetRequired("property");
            if (!propResult.IsSuccess)
                return new ErrorResponse(propResult.ErrorMessage);

            var valResult = p.GetRequired("value");
            if (!valResult.IsSuccess)
                return new ErrorResponse(valResult.ErrorMessage);

            string writeErr = ProjectSettingsHelper.WriteProperty(
                catResult.Value, propResult.Value, valResult.Value);
            if (writeErr != null)
                return new ErrorResponse(writeErr);

            return new SuccessResponse(
                $"Set {catResult.Value}.{propResult.Value} = {valResult.Value}.",
                ProjectSettingsHelper.ReadProperty(catResult.Value, propResult.Value));
        }

        private static object HandleList(ToolParams p)
        {
            var catResult = p.GetRequired("category");
            if (!catResult.IsSuccess)
                return new ErrorResponse(catResult.ErrorMessage);

            var result = ProjectSettingsHelper.ListProperties(catResult.Value);
            if (result == null)
                return new ErrorResponse(
                    $"Unknown category '{catResult.Value}'. Use action='list_categories' to see valid categories.");
            return new SuccessResponse($"Listed properties for '{catResult.Value}'.", result);
        }

        private static object HandleListCategories()
        {
            return new SuccessResponse("Listed all settings categories.",
                ProjectSettingsHelper.ListCategories());
        }
    }
}
