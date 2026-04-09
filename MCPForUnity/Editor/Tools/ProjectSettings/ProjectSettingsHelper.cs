using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.ProjectSettings
{
    public static class ProjectSettingsHelper
    {
        private const BindingFlags StaticPublic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase;

        private static readonly Dictionary<string, Type> Categories = new Dictionary<string, Type>
        {
            { "quality", typeof(QualitySettings) },
            { "physics", typeof(UnityEngine.Physics) },
            { "physics2d", typeof(UnityEngine.Physics2D) },
            { "time", typeof(Time) },
            { "editor", typeof(EditorSettings) },
        };

        public static object ReadProperty(string category, string property)
        {
            if (!Categories.TryGetValue(category.ToLowerInvariant(), out var type))
                return null;

            var prop = FindStaticProperty(type, property);
            if (prop == null || !prop.CanRead)
                return null;

            var value = prop.GetValue(null);
            return new { category, property, value, type = prop.PropertyType.Name };
        }

        public static string WriteProperty(string category, string property, string value)
        {
            if (!Categories.TryGetValue(category.ToLowerInvariant(), out var type))
                return $"Unknown category '{category}'. Valid: {string.Join(", ", Categories.Keys)}.";

            var prop = FindStaticProperty(type, property);
            if (prop == null)
                return $"Unknown property '{property}' in category '{category}'. "
                     + $"Use action='list' with category='{category}' to discover available properties.";

            if (!prop.CanWrite)
                return $"Property '{prop.Name}' in '{category}' is read-only.";

            try
            {
                JToken token = value.StartsWith("[") || value.StartsWith("{")
                    ? JToken.Parse(value)
                    : new JValue(value);
                var converted = PropertyConversion.ConvertToType(token, prop.PropertyType);
                prop.SetValue(null, converted);
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to set {prop.Name}: {ex.Message}";
            }
        }

        public static object ListProperties(string category)
        {
            if (!Categories.TryGetValue(category.ToLowerInvariant(), out var type))
                return null;

            var props = type
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

            return new { category, properties = props, count = props.Length };
        }

        public static object ListCategories()
        {
            var cats = Categories.Select(kv =>
            {
                int count = kv.Value
                    .GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Count(p => p.GetIndexParameters().Length == 0
                             && p.GetCustomAttribute<ObsoleteAttribute>() == null);
                return new { name = kv.Key, typeName = kv.Value.Name, propertyCount = count };
            }).ToArray();

            return new
            {
                categories = cats,
                note = "PlayerSettings available via manage_build(action='settings' or 'list_settings').",
            };
        }

        private static PropertyInfo FindStaticProperty(Type type, string property)
        {
            string normalized = ParamCoercion.NormalizePropertyName(property);

            var prop = type.GetProperty(property, StaticPublic)
                       ?? type.GetProperty(normalized, StaticPublic);

            if (prop == null || prop.GetIndexParameters().Length > 0)
                return null;

            return prop;
        }
    }
}
