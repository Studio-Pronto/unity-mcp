#if UNITY_6000_4_OR_NEWER
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.ProjectAuditor.Editor;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectAuditor
{
    internal static class RuleOps
    {
        private const string SettingsPath = "ProjectSettings/ProjectAuditorSettings.asset";

        // === list_rules ===

        internal static object ListRules()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(SettingsPath);
            if (asset == null)
                return new SuccessResponse("No ProjectAuditorSettings found.", new { rules = new List<object>(), count = 0 });

            var so = new SerializedObject(asset);
            var rulesProperty = so.FindProperty("Rules");
            if (rulesProperty == null)
                return new SuccessResponse("No rules configured.", new { rules = new List<object>(), count = 0 });

            var rulesArray = rulesProperty.FindPropertyRelative("rules");
            if (rulesArray == null || !rulesArray.isArray)
                return new SuccessResponse("No rules configured.", new { rules = new List<object>(), count = 0 });

            var rules = new List<object>();
            for (int i = 0; i < rulesArray.arraySize; i++)
            {
                var element = rulesArray.GetArrayElementAtIndex(i);
                rules.Add(SerializeRule(element));
            }

            return new SuccessResponse($"{rules.Count} rules configured.", new { rules, count = rules.Count });
        }

        // === add_rule ===

        internal static object AddRule(JObject @params)
        {
            var p = new ToolParams(@params);
            string idStr = p.Get("descriptor_id", "descriptorId");
            if (string.IsNullOrEmpty(idStr))
                return new ErrorResponse("'descriptor_id' parameter is required.");

            string severityStr = p.Get("rule_severity", "ruleSeverity");
            if (string.IsNullOrEmpty(severityStr))
                return new ErrorResponse("'rule_severity' parameter is required.");

            if (!ProjectAuditorHelpers.TryParseSeverity(severityStr, out var severity))
                return new ErrorResponse($"Invalid severity: '{severityStr}'. Valid: None, Info, Minor, Moderate, Major, Critical, Warning, Error.");

            string filter = p.Get("rule_filter", "ruleFilter") ?? "";

            var asset = AssetDatabase.LoadAssetAtPath<Object>(SettingsPath);
            if (asset == null)
                return new ErrorResponse("ProjectAuditorSettings.asset not found.");

            var so = new SerializedObject(asset);
            var rulesProperty = so.FindProperty("Rules");
            if (rulesProperty == null)
                return new ErrorResponse("Could not find Rules property in settings.");

            var rulesArray = rulesProperty.FindPropertyRelative("rules");
            if (rulesArray == null)
                return new ErrorResponse("Could not find rules array in settings.");

            // Check for duplicate
            for (int i = 0; i < rulesArray.arraySize; i++)
            {
                var existing = rulesArray.GetArrayElementAtIndex(i);
                string existingId = GetRuleFieldValue(existing, "Id");
                string existingFilter = GetRuleFieldValue(existing, "Filter");
                if (existingId == idStr && (existingFilter ?? "") == filter)
                {
                    // Update existing rule's severity
                    SetRuleFieldValue(existing, "Severity", severityStr);
                    so.ApplyModifiedProperties();
                    return new SuccessResponse(
                        $"Rule updated: {severityStr} for {idStr}.",
                        new { descriptor_id = idStr, severity = severityStr, filter, total_rules = rulesArray.arraySize });
                }
            }

            // Add new rule
            rulesArray.InsertArrayElementAtIndex(rulesArray.arraySize);
            var newRule = rulesArray.GetArrayElementAtIndex(rulesArray.arraySize - 1);
            SetRuleFieldValue(newRule, "Id", idStr);
            SetRuleFieldValue(newRule, "Severity", severityStr);
            SetRuleFieldValue(newRule, "Filter", filter);

            so.ApplyModifiedProperties();

            return new SuccessResponse(
                $"Rule added: {severityStr} for {idStr}.",
                new { descriptor_id = idStr, severity = severityStr, filter, total_rules = rulesArray.arraySize });
        }

        // === remove_rule ===

        internal static object RemoveRule(JObject @params)
        {
            var p = new ToolParams(@params);
            string idStr = p.Get("descriptor_id", "descriptorId");
            if (string.IsNullOrEmpty(idStr))
                return new ErrorResponse("'descriptor_id' parameter is required.");

            string filter = p.Get("rule_filter", "ruleFilter") ?? "";

            var asset = AssetDatabase.LoadAssetAtPath<Object>(SettingsPath);
            if (asset == null)
                return new ErrorResponse("ProjectAuditorSettings.asset not found.");

            var so = new SerializedObject(asset);
            var rulesProperty = so.FindProperty("Rules");
            if (rulesProperty == null)
                return new ErrorResponse("No rules property found.");

            var rulesArray = rulesProperty.FindPropertyRelative("rules");
            if (rulesArray == null || rulesArray.arraySize == 0)
                return new ErrorResponse($"No rules found to remove.");

            for (int i = 0; i < rulesArray.arraySize; i++)
            {
                var element = rulesArray.GetArrayElementAtIndex(i);
                string existingId = GetRuleFieldValue(element, "Id");
                string existingFilter = GetRuleFieldValue(element, "Filter");
                if (existingId == idStr && (existingFilter ?? "") == filter)
                {
                    rulesArray.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedProperties();
                    return new SuccessResponse(
                        $"Rule removed: {idStr}.",
                        new { descriptor_id = idStr, filter, total_rules = rulesArray.arraySize });
                }
            }

            return new ErrorResponse($"No rule found matching descriptor '{idStr}' with filter '{filter}'.");
        }

        // === Helpers ===

        internal static int GetRuleCount()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(SettingsPath);
            if (asset == null) return 0;

            var so = new SerializedObject(asset);
            var rulesProperty = so.FindProperty("Rules");
            if (rulesProperty == null) return 0;

            var rulesArray = rulesProperty.FindPropertyRelative("rules");
            return rulesArray?.arraySize ?? 0;
        }

        private static object SerializeRule(SerializedProperty element)
        {
            // Try common field name patterns for Rule serialization
            return new
            {
                descriptor_id = GetRuleFieldValue(element, "Id"),
                severity = GetRuleFieldValue(element, "Severity"),
                filter = GetRuleFieldValue(element, "Filter")
            };
        }

        /// <summary>
        /// Attempts to read a Rule field by trying multiple naming conventions.
        /// Unity's serialization may use PascalCase, camelCase, or m_ prefix.
        /// </summary>
        private static string GetRuleFieldValue(SerializedProperty element, string fieldName)
        {
            // Try: PascalCase, camelCase, m_PascalCase, m_camelCase
            string camel = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
            string[] candidates = { fieldName, camel, "m_" + fieldName, "m_" + camel };

            foreach (var name in candidates)
            {
                var prop = element.FindPropertyRelative(name);
                if (prop != null)
                {
                    // Handle both string and enum-like serialization
                    if (prop.propertyType == SerializedPropertyType.String)
                        return prop.stringValue;
                    if (prop.propertyType == SerializedPropertyType.Integer)
                        return prop.intValue.ToString();
                    if (prop.propertyType == SerializedPropertyType.Enum)
                        return prop.enumNames[prop.enumValueIndex];
                    // Check for nested m_String pattern (Unity custom serialization)
                    var nested = prop.FindPropertyRelative("m_String");
                    if (nested != null)
                        return nested.stringValue;
                    var nestedStr = prop.FindPropertyRelative("m_AsString");
                    if (nestedStr != null)
                        return nestedStr.stringValue;
                }
            }

            return null;
        }

        private static void SetRuleFieldValue(SerializedProperty element, string fieldName, string value)
        {
            string camel = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
            string[] candidates = { fieldName, camel, "m_" + fieldName, "m_" + camel };

            foreach (var name in candidates)
            {
                var prop = element.FindPropertyRelative(name);
                if (prop != null)
                {
                    if (prop.propertyType == SerializedPropertyType.String)
                    {
                        prop.stringValue = value;
                        return;
                    }
                    var nested = prop.FindPropertyRelative("m_String");
                    if (nested != null)
                    {
                        nested.stringValue = value;
                        return;
                    }
                    var nestedStr = prop.FindPropertyRelative("m_AsString");
                    if (nestedStr != null)
                    {
                        nestedStr.stringValue = value;
                        return;
                    }
                }
            }

            McpLog.Warn($"[RuleOps] Could not find serialized field '{fieldName}' on Rule element.");
        }
    }
}
#endif
