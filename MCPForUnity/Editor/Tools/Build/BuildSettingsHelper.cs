using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Build
{
    public static class BuildSettingsHelper
    {
        private const BindingFlags StaticPublic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase;

        public static readonly IReadOnlyList<string> WellKnownProperties = new[]
        {
            "product_name", "company_name", "version", "bundle_id",
            "scripting_backend", "defines", "architecture"
        };

        public static object ReadProperty(string property, NamedBuildTarget namedTarget)
        {
            switch (property.ToLowerInvariant())
            {
                case "product_name":
                    return new { property, value = PlayerSettings.productName };
                case "company_name":
                    return new { property, value = PlayerSettings.companyName };
                case "version":
                    return new { property, value = PlayerSettings.bundleVersion };
                case "bundle_id":
                    return new { property, value = PlayerSettings.GetApplicationIdentifier(namedTarget) };
                case "scripting_backend":
                    var backend = PlayerSettings.GetScriptingBackend(namedTarget);
                    return new { property, value = backend == ScriptingImplementation.IL2CPP ? "il2cpp" : "mono" };
                case "defines":
                    return new { property, value = PlayerSettings.GetScriptingDefineSymbols(namedTarget) };
                case "architecture":
                    var arch = PlayerSettings.GetArchitecture(namedTarget);
                    string archName = arch switch { 0 => "x86_64", 1 => "arm64", 2 => "universal", _ => "unknown" };
                    return new { property, value = archName, raw = arch };
                default:
                    return ReadViaReflection(property);
            }
        }

        public static string WriteProperty(string property, string value, NamedBuildTarget namedTarget)
        {
            try
            {
                switch (property.ToLowerInvariant())
                {
                    case "product_name":
                        PlayerSettings.productName = value;
                        return null;
                    case "company_name":
                        PlayerSettings.companyName = value;
                        return null;
                    case "version":
                        PlayerSettings.bundleVersion = value;
                        return null;
                    case "bundle_id":
                        PlayerSettings.SetApplicationIdentifier(namedTarget, value);
                        return null;
                    case "scripting_backend":
                        var backendValue = value.ToLowerInvariant();
                        if (backendValue != "il2cpp" && backendValue != "mono")
                            return $"Unknown scripting_backend '{value}'. Valid: mono, il2cpp";
                        var impl = backendValue == "il2cpp"
                            ? ScriptingImplementation.IL2CPP
                            : ScriptingImplementation.Mono2x;
                        PlayerSettings.SetScriptingBackend(namedTarget, impl);
                        return null;
                    case "defines":
                        PlayerSettings.SetScriptingDefineSymbols(namedTarget, value);
                        return null;
                    case "architecture":
                        int arch = value.ToLowerInvariant() switch
                        {
                            "x86_64" or "none" or "default" => 0,
                            "arm64" => 1,
                            "universal" => 2,
                            _ => -1
                        };
                        if (arch < 0)
                            return $"Unknown architecture '{value}'. Valid: x86_64, arm64, universal";
                        PlayerSettings.SetArchitecture(namedTarget, arch);
                        return null;
                    default:
                        return WriteViaReflection(property, value);
                }
            }
            catch (Exception ex)
            {
                return $"Failed to set {property}: {ex.Message}";
            }
        }

        public static object ListProperties()
        {
            var props = typeof(PlayerSettings)
                .GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<ObsoleteAttribute>() == null)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    name = p.Name,
                    type = p.PropertyType.Name,
                    canRead = p.CanRead,
                    canWrite = p.CanWrite,
                })
                .ToArray();

            return new
            {
                well_known = WellKnownProperties,
                properties = props,
                count = props.Length,
            };
        }

        // ── reflection fallback ────────────────────────────────────────

        private static PropertyInfo FindStaticProperty(string property)
        {
            string normalized = ParamCoercion.NormalizePropertyName(property);

            var prop = typeof(PlayerSettings).GetProperty(property, StaticPublic)
                       ?? typeof(PlayerSettings).GetProperty(normalized, StaticPublic);

            if (prop == null || prop.GetIndexParameters().Length > 0)
                return null;

            return prop;
        }

        private static object ReadViaReflection(string property)
        {
            var prop = FindStaticProperty(property);
            if (prop == null || !prop.CanRead)
                return null;

            var value = prop.GetValue(null);
            return new { property, value, type = prop.PropertyType.Name };
        }

        private static string WriteViaReflection(string property, string value)
        {
            var prop = FindStaticProperty(property);
            if (prop == null)
                return $"Unknown property '{property}'. "
                     + $"Well-known shortcuts: {string.Join(", ", WellKnownProperties)}. "
                     + "Any PlayerSettings property name also works (e.g. bakeCollisionMeshes). "
                     + "Use action='list_settings' to discover all.";

            if (!prop.CanWrite)
                return $"Property '{prop.Name}' is read-only.";

            try
            {
                var converted = PropertyConversion.ConvertToType(new JValue(value), prop.PropertyType);
                prop.SetValue(null, converted);
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to set {prop.Name}: {ex.Message}";
            }
        }
    }
}
