using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Runtime.Serialization; // For Converters
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Handles serialization of GameObjects and Components for MCP responses.
    /// Includes reflection helpers and caching for performance.
    /// </summary> 
    public static class GameObjectSerializer
    {
        // --- Helper Methods for Enhanced Serialization ---

        /// <summary>
        /// Cache for MonoBehaviour script paths to avoid repeated AssetDatabase lookups.
        /// </summary>
        private static readonly Dictionary<Type, string> _scriptPathCache = new Dictionary<Type, string>();

        /// <summary>
        /// Gets the full hierarchy path of a transform (e.g., "Player/Armature/Hips/Spine").
        /// Walks UP the parent chain only - no recursion risk.
        /// </summary>
        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return null;
            var parts = new List<string>();
            Transform current = t;
            while (current != null)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Serializes a UnityEngine.Object reference with enriched info (instanceID, name, type, paths).
        /// Used for both single objects and array elements.
        /// </summary>
        private static Dictionary<string, object> SerializeUnityObjectReference(UnityEngine.Object unityObj)
        {
            if (unityObj == null) return null;

            var refInfo = new Dictionary<string, object>
            {
                { "instanceID", unityObj.GetInstanceID() },
                { "name", unityObj.name },
                { "type", unityObj.GetType().Name }
            };

            // Add hierarchy path for scene objects (GameObjects and Components)
            try
            {
                if (unityObj is GameObject refGo)
                {
                    refInfo["hierarchyPath"] = GetHierarchyPath(refGo.transform);
                }
                else if (unityObj is Component refComp && refComp.gameObject != null)
                {
                    refInfo["gameObjectName"] = refComp.gameObject.name;
                    refInfo["hierarchyPath"] = GetHierarchyPath(refComp.transform);
                }
            }
            catch { /* Silently fail - hierarchy info is optional */ }

            // Add asset path for assets (not scene objects)
            try
            {
                if (AssetDatabase.Contains(unityObj))
                {
                    refInfo["assetPath"] = AssetDatabase.GetAssetPath(unityObj);
                }
            }
            catch { /* Silently fail - asset path is optional */ }

            return refInfo;
        }

        /// <summary>
        /// Formats layer index with its name (e.g., "8 (Ragdoll)" instead of just "8").
        /// </summary>
        private static string GetLayerWithName(int layer)
        {
            string name = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(name) ? layer.ToString() : $"{layer} ({name})";
        }

        /// <summary>
        /// Gets the script file path for a MonoBehaviour type (e.g., "Assets/Scripts/PlayerController.cs").
        /// Uses caching to avoid repeated AssetDatabase lookups.
        /// </summary>
        private static string GetScriptPath(Type type)
        {
            if (type == null) return null;
            if (!typeof(MonoBehaviour).IsAssignableFrom(type) && !typeof(ScriptableObject).IsAssignableFrom(type))
                return null;

            if (_scriptPathCache.TryGetValue(type, out var cached))
                return cached;

            string result = null;
            try
            {
                var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script != null && script.GetClass() == type)
                    {
                        result = path;
                        break;
                    }
                }
            }
            catch { /* Silently fail - script path is optional enhancement */ }

            _scriptPathCache[type] = result;
            return result;
        }

        /// <summary>
        /// Gets prefab instance information for a GameObject.
        /// Returns null if not a prefab instance.
        /// </summary>
        private static object GetPrefabInfo(GameObject go)
        {
            if (go == null) return null;
            try
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    return null;

                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                return new Dictionary<string, object>
                {
                    { "isPrefabInstance", true },
                    { "status", PrefabUtility.GetPrefabInstanceStatus(go).ToString() },
                    { "prefabAssetPath", source != null ? AssetDatabase.GetAssetPath(source) : null },
                    { "hasOverrides", PrefabUtility.HasPrefabInstanceAnyOverrides(go, false) }
                };
            }
            catch
            {
                return null; // Silently fail - prefab info is optional enhancement
            }
        }

        /// <summary>
        /// Gets per-property prefab override status for a component.
        /// Uses SerializedObject/SerializedProperty to detect which properties differ from prefab.
        /// Returns null if not a prefab instance or on error.
        /// </summary>
        public static Dictionary<string, bool> GetPrefabPropertyOverrides(Component component)
        {
            if (component == null) return null;

            try
            {
                GameObject go = component.gameObject;
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    return null;

                var overrides = new Dictionary<string, bool>();
                var serializedObj = new SerializedObject(component);
                var prop = serializedObj.GetIterator();

                // Iterate all serialized properties
                while (prop.Next(true))
                {
                    // Skip internal Unity properties (m_Script, m_ObjectHideFlags, etc.)
                    if (prop.propertyPath.StartsWith("m_")) continue;

                    overrides[prop.propertyPath] = prop.prefabOverride;
                }

                serializedObj.Dispose();
                return overrides.Count > 0 ? overrides : null;
            }
            catch
            {
                return null;
            }
        }

        // --- End Helper Methods ---

        // --- Data Serialization ---

        /// <summary>
        /// Creates a serializable representation of a GameObject.
        /// </summary>
        public static object GetGameObjectData(GameObject go)
        {
            if (go == null)
                return null;
            return new
            {
                name = go.name,
                instanceID = go.GetInstanceID(),
                tag = go.tag,
                layer = go.layer,
                layerName = GetLayerWithName(go.layer),
                hierarchyPath = GetHierarchyPath(go.transform),
                prefabInfo = GetPrefabInfo(go),
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                isStatic = go.isStatic,
                scenePath = go.scene.path, // Identify which scene it belongs to
                transform = new // Serialize transform components carefully to avoid JSON issues
                {
                    // Serialize Vector3 components individually to prevent self-referencing loops.
                    // The default serializer can struggle with properties like Vector3.normalized.
                    position = new
                    {
                        x = go.transform.position.x,
                        y = go.transform.position.y,
                        z = go.transform.position.z,
                    },
                    localPosition = new
                    {
                        x = go.transform.localPosition.x,
                        y = go.transform.localPosition.y,
                        z = go.transform.localPosition.z,
                    },
                    rotation = new
                    {
                        x = go.transform.rotation.eulerAngles.x,
                        y = go.transform.rotation.eulerAngles.y,
                        z = go.transform.rotation.eulerAngles.z,
                    },
                    localRotation = new
                    {
                        x = go.transform.localRotation.eulerAngles.x,
                        y = go.transform.localRotation.eulerAngles.y,
                        z = go.transform.localRotation.eulerAngles.z,
                    },
                    scale = new
                    {
                        x = go.transform.localScale.x,
                        y = go.transform.localScale.y,
                        z = go.transform.localScale.z,
                    },
                    forward = new
                    {
                        x = go.transform.forward.x,
                        y = go.transform.forward.y,
                        z = go.transform.forward.z,
                    },
                    up = new
                    {
                        x = go.transform.up.x,
                        y = go.transform.up.y,
                        z = go.transform.up.z,
                    },
                    right = new
                    {
                        x = go.transform.right.x,
                        y = go.transform.right.y,
                        z = go.transform.right.z,
                    },
                },
                parentInstanceID = go.transform.parent?.gameObject.GetInstanceID() ?? 0, // 0 if no parent
                // Optionally include components, but can be large
                // components = go.GetComponents<Component>().Select(c => GetComponentData(c)).ToList()
                // Or just component names:
                componentNames = go.GetComponents<Component>()
                    .Select(c => c.GetType().FullName)
                    .ToList(),
            };
        }

        // --- Metadata Caching for Reflection ---
        private class CachedMetadata
        {
            public readonly List<PropertyInfo> SerializableProperties;
            public readonly List<FieldInfo> SerializableFields;

            public CachedMetadata(List<PropertyInfo> properties, List<FieldInfo> fields)
            {
                SerializableProperties = properties;
                SerializableFields = fields;
            }
        }
        // Key becomes Tuple<Type, bool>
        private static readonly Dictionary<Tuple<Type, bool>, CachedMetadata> _metadataCache = new Dictionary<Tuple<Type, bool>, CachedMetadata>();
        // --- End Metadata Caching ---

        /// <summary>
        /// Creates a serializable representation of a Component, attempting to serialize
        /// public properties and fields using reflection, with caching and control over non-public fields.
        /// </summary>
        // Add the flag parameter here
        public static object GetComponentData(Component c, bool includeNonPublicSerializedFields = true, bool includePrefabOverrides = false)
        {
            // --- Add Early Logging --- 
            // McpLog.Info($"[GetComponentData] Starting for component: {c?.GetType()?.FullName ?? "null"} (ID: {c?.GetInstanceID() ?? 0})");
            // --- End Early Logging ---

            if (c == null) return null;
            Type componentType = c.GetType();

            // --- Special handling for Transform to avoid reflection crashes and problematic properties --- 
            if (componentType == typeof(Transform))
            {
                Transform tr = c as Transform;
                // McpLog.Info($"[GetComponentData] Manually serializing Transform (ID: {tr.GetInstanceID()})");
                return new Dictionary<string, object>
                {
                    { "typeName", componentType.FullName },
                    { "instanceID", tr.GetInstanceID() },
                    { "hierarchyPath", GetHierarchyPath(tr) },
                    // Manually extract known-safe properties. Avoid Quaternion 'rotation' and 'lossyScale'.
                    { "position", CreateTokenFromValue(tr.position, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "localPosition", CreateTokenFromValue(tr.localPosition, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "eulerAngles", CreateTokenFromValue(tr.eulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject() }, // Use Euler angles
                    { "localEulerAngles", CreateTokenFromValue(tr.localEulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "localScale", CreateTokenFromValue(tr.localScale, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "right", CreateTokenFromValue(tr.right, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "up", CreateTokenFromValue(tr.up, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "forward", CreateTokenFromValue(tr.forward, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                    { "parentInstanceID", tr.parent?.gameObject.GetInstanceID() ?? 0 },
                    { "rootInstanceID", tr.root?.gameObject.GetInstanceID() ?? 0 },
                    { "childCount", tr.childCount },
                    // Include standard Object/Component properties
                    { "name", tr.name },
                    { "tag", tr.tag },
                    { "gameObjectInstanceID", tr.gameObject?.GetInstanceID() ?? 0 }
                };
            }
            // --- End Special handling for Transform --- 

            // --- Special handling for Camera to avoid matrix-related crashes ---
            if (componentType == typeof(Camera))
            {
                Camera cam = c as Camera;
                var cameraProperties = new Dictionary<string, object>();

                // List of safe properties to serialize
                var safeProperties = new Dictionary<string, Func<object>>
                {
                    { "nearClipPlane", () => cam.nearClipPlane },
                    { "farClipPlane", () => cam.farClipPlane },
                    { "fieldOfView", () => cam.fieldOfView },
                    { "renderingPath", () => (int)cam.renderingPath },
                    { "actualRenderingPath", () => (int)cam.actualRenderingPath },
                    { "allowHDR", () => cam.allowHDR },
                    { "allowMSAA", () => cam.allowMSAA },
                    { "allowDynamicResolution", () => cam.allowDynamicResolution },
                    { "forceIntoRenderTexture", () => cam.forceIntoRenderTexture },
                    { "orthographicSize", () => cam.orthographicSize },
                    { "orthographic", () => cam.orthographic },
                    { "opaqueSortMode", () => (int)cam.opaqueSortMode },
                    { "transparencySortMode", () => (int)cam.transparencySortMode },
                    { "depth", () => cam.depth },
                    { "aspect", () => cam.aspect },
                    { "cullingMask", () => cam.cullingMask },
                    { "eventMask", () => cam.eventMask },
                    { "backgroundColor", () => cam.backgroundColor },
                    { "clearFlags", () => (int)cam.clearFlags },
                    { "stereoEnabled", () => cam.stereoEnabled },
                    { "stereoSeparation", () => cam.stereoSeparation },
                    { "stereoConvergence", () => cam.stereoConvergence },
                    { "enabled", () => cam.enabled },
                    { "name", () => cam.name },
                    { "tag", () => cam.tag },
                    { "gameObject", () => new { name = cam.gameObject.name, instanceID = cam.gameObject.GetInstanceID() } }
                };

                foreach (var prop in safeProperties)
                {
                    try
                    {
                        var value = prop.Value();
                        if (value != null)
                        {
                            AddSerializableValue(cameraProperties, prop.Key, value.GetType(), value);
                        }
                    }
                    catch (Exception)
                    {
                        // Silently skip any property that fails
                        continue;
                    }
                }

                return new Dictionary<string, object>
                {
                    { "typeName", componentType.FullName },
                    { "instanceID", cam.GetInstanceID() },
                    { "hierarchyPath", GetHierarchyPath(cam.transform) },
                    { "properties", cameraProperties }
                };
            }
            // --- End Special handling for Camera ---

            var data = new Dictionary<string, object>
            {
                { "typeName", componentType.FullName },
                { "instanceID", c.GetInstanceID() },
                { "hierarchyPath", GetHierarchyPath(c.transform) }
            };

            // Add script path for MonoBehaviours
            var scriptPath = GetScriptPath(componentType);
            if (scriptPath != null)
            {
                data["scriptPath"] = scriptPath;
            }

            // --- Get Cached or Generate Metadata (using new cache key) ---
            Tuple<Type, bool> cacheKey = new Tuple<Type, bool>(componentType, includeNonPublicSerializedFields);
            if (!_metadataCache.TryGetValue(cacheKey, out CachedMetadata cachedData))
            {
                var propertiesToCache = new List<PropertyInfo>();
                var fieldsToCache = new List<FieldInfo>();

                // Traverse the hierarchy from the component type up to MonoBehaviour
                Type currentType = componentType;
                while (currentType != null && currentType != typeof(MonoBehaviour) && currentType != typeof(object))
                {
                    // Get properties declared only at the current type level
                    BindingFlags propFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    foreach (var propInfo in currentType.GetProperties(propFlags))
                    {
                        // Basic filtering (readable, not indexer, not transform which is handled elsewhere)
                        if (!propInfo.CanRead || propInfo.GetIndexParameters().Length > 0 || propInfo.Name == "transform") continue;
                        // Add if not already added (handles overrides - keep the most derived version)
                        if (!propertiesToCache.Any(p => p.Name == propInfo.Name))
                        {
                            propertiesToCache.Add(propInfo);
                        }
                    }

                    // Get fields declared only at the current type level (both public and non-public)
                    BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    var declaredFields = currentType.GetFields(fieldFlags);

                    // Process the declared Fields for caching
                    foreach (var fieldInfo in declaredFields)
                    {
                        if (fieldInfo.Name.EndsWith("k__BackingField")) continue; // Skip backing fields

                        // Add if not already added (handles hiding - keep the most derived version)
                        if (fieldsToCache.Any(f => f.Name == fieldInfo.Name)) continue;

                        bool shouldInclude = false;
                        if (includeNonPublicSerializedFields)
                        {
                            // If TRUE, include Public OR any NonPublic with [SerializeField] (private/protected/internal)
                            var hasSerializeField = fieldInfo.IsDefined(typeof(SerializeField), inherit: true);
                            shouldInclude = fieldInfo.IsPublic || (!fieldInfo.IsPublic && hasSerializeField);
                        }
                        else // includeNonPublicSerializedFields is FALSE
                        {
                            // If FALSE, include ONLY if it is explicitly Public.
                            shouldInclude = fieldInfo.IsPublic;
                        }

                        if (shouldInclude)
                        {
                            fieldsToCache.Add(fieldInfo);
                        }
                    }

                    // Move to the base type
                    currentType = currentType.BaseType;
                }
                // --- End Hierarchy Traversal ---

                cachedData = new CachedMetadata(propertiesToCache, fieldsToCache);
                _metadataCache[cacheKey] = cachedData; // Add to cache with combined key
            }
            // --- End Get Cached or Generate Metadata ---

            // --- Use cached metadata ---
            var serializablePropertiesOutput = new Dictionary<string, object>();

            // --- Add Logging Before Property Loop ---
            // McpLog.Info($"[GetComponentData] Starting property loop for {componentType.Name}...");
            // --- End Logging Before Property Loop ---

            // Use cached properties
            foreach (var propInfo in cachedData.SerializableProperties)
            {
                string propName = propInfo.Name;

                // --- Skip known obsolete/problematic Component shortcut properties ---
                bool skipProperty = false;
                if (propName == "rigidbody" || propName == "rigidbody2D" || propName == "camera" ||
                    propName == "light" || propName == "animation" || propName == "constantForce" ||
                    propName == "renderer" || propName == "audio" || propName == "networkView" ||
                    propName == "collider" || propName == "collider2D" || propName == "hingeJoint" ||
                    propName == "particleSystem" ||
                    // Also skip potentially problematic Matrix properties prone to cycles/errors
                    propName == "worldToLocalMatrix" || propName == "localToWorldMatrix")
                {
                    // McpLog.Info($"[GetComponentData] Explicitly skipping generic property: {propName}"); // Optional log
                    skipProperty = true;
                }
                // --- End Skip Generic Properties ---

                // --- Skip specific potentially problematic Camera properties ---
                if (componentType == typeof(Camera) &&
                    (propName == "pixelRect" ||
                     propName == "rect" ||
                     propName == "cullingMatrix" ||
                     propName == "useOcclusionCulling" ||
                     propName == "worldToCameraMatrix" ||
                     propName == "projectionMatrix" ||
                     propName == "nonJitteredProjectionMatrix" ||
                     propName == "previousViewProjectionMatrix" ||
                     propName == "cameraToWorldMatrix"))
                {
                    // McpLog.Info($"[GetComponentData] Explicitly skipping Camera property: {propName}");
                    skipProperty = true;
                }
                // --- End Skip Camera Properties ---

                // --- Skip specific potentially problematic Transform properties ---
                if (componentType == typeof(Transform) &&
                    (propName == "lossyScale" ||
                     propName == "rotation" ||
                     propName == "worldToLocalMatrix" ||
                     propName == "localToWorldMatrix"))
                {
                    // McpLog.Info($"[GetComponentData] Explicitly skipping Transform property: {propName}");
                    skipProperty = true;
                }
                // --- End Skip Transform Properties ---

                // Skip if flagged
                if (skipProperty)
                {
                    continue;
                }

                try
                {
                    // --- Add detailed logging --- 
                    // McpLog.Info($"[GetComponentData] Accessing: {componentType.Name}.{propName}");
                    // --- End detailed logging ---

                    // --- Special handling for material/mesh properties in edit mode ---
                    object value;
                    if (!Application.isPlaying && (propName == "material" || propName == "materials" || propName == "mesh"))
                    {
                        // In edit mode, use sharedMaterial/sharedMesh to avoid instantiation warnings
                        if ((propName == "material" || propName == "materials") && c is Renderer renderer)
                        {
                            if (propName == "material")
                                value = renderer.sharedMaterial;
                            else // materials
                                value = renderer.sharedMaterials;
                        }
                        else if (propName == "mesh" && c is MeshFilter meshFilter)
                        {
                            value = meshFilter.sharedMesh;
                        }
                        else
                        {
                            // Fallback to normal property access if type doesn't match
                            value = propInfo.GetValue(c);
                        }
                    }
                    else
                    {
                        value = propInfo.GetValue(c);
                    }
                    // --- End special handling ---

                    Type propType = propInfo.PropertyType;
                    AddSerializableValue(serializablePropertiesOutput, propName, propType, value);
                }
                catch (Exception)
                {
                    // McpLog.Warn($"Could not read property {propName} on {componentType.Name}");
                }
            }

            // --- Add Logging Before Field Loop ---
            // McpLog.Info($"[GetComponentData] Starting field loop for {componentType.Name}...");
            // --- End Logging Before Field Loop ---

            // Use cached fields
            foreach (var fieldInfo in cachedData.SerializableFields)
            {
                try
                {
                    // --- Add detailed logging for fields --- 
                    // McpLog.Info($"[GetComponentData] Accessing Field: {componentType.Name}.{fieldInfo.Name}");
                    // --- End detailed logging for fields ---
                    object value = fieldInfo.GetValue(c);
                    string fieldName = fieldInfo.Name;
                    Type fieldType = fieldInfo.FieldType;
                    AddSerializableValue(serializablePropertiesOutput, fieldName, fieldType, value);
                }
                catch (Exception)
                {
                    // McpLog.Warn($"Could not read field {fieldInfo.Name} on {componentType.Name}");
                }
            }
            // --- End Use cached metadata ---

            if (serializablePropertiesOutput.Count > 0)
            {
                data["properties"] = serializablePropertiesOutput;
            }

            // Add prefab property overrides if requested
            if (includePrefabOverrides)
            {
                var overrides = GetPrefabPropertyOverrides(c);
                if (overrides != null)
                {
                    data["prefabOverrides"] = overrides;
                }
            }

            return data;
        }

        // Safe types that won't cause recursion
        private static readonly HashSet<Type> _safeTypes = new HashSet<Type>
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float),
            typeof(double), typeof(decimal), typeof(char), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
            typeof(Color), typeof(Color32), typeof(Rect), typeof(Bounds),
            typeof(Vector2Int), typeof(Vector3Int), typeof(RectInt), typeof(BoundsInt),
            typeof(LayerMask), typeof(AnimationCurve)
        };

        private static bool IsSafeType(Type type)
        {
            if (type == null) return false;
            if (type.IsEnum) return true;
            if (type.IsPrimitive) return true;
            if (_safeTypes.Contains(type)) return true;
            // Allow arrays/lists of safe primitive types only
            if (type.IsArray && type.GetElementType().IsPrimitive) return true;
            return false;
        }

        // Helper function to decide how to serialize different types
        private static void AddSerializableValue(Dictionary<string, object> dict, string name, Type type, object value)
        {
            // Simplified: Directly use CreateTokenFromValue which uses the serializer
            if (value == null)
            {
                dict[name] = null;
                return;
            }

            // Skip complex types that could cause infinite recursion
            if (!IsSafeType(type))
            {
                // For Unity objects, store enriched reference info
                if (value is UnityEngine.Object unityObj)
                {
                    dict[name] = SerializeUnityObjectReference(unityObj);
                    return;
                }

                // Handle arrays of UnityEngine.Object
                if (type.IsArray && typeof(UnityEngine.Object).IsAssignableFrom(type.GetElementType()))
                {
                    var array = value as Array;
                    if (array == null)
                    {
                        dict[name] = null;
                        return;
                    }
                    var serializedArray = new List<object>();
                    foreach (var element in array)
                    {
                        if (element == null)
                            serializedArray.Add(null);
                        else if (element is UnityEngine.Object elementObj)
                            serializedArray.Add(SerializeUnityObjectReference(elementObj));
                        else
                            serializedArray.Add(element);
                    }
                    dict[name] = serializedArray;
                    return;
                }

                // Skip other complex types entirely to prevent recursion
                return;
            }

            try
            {
                // Use the helper that employs our custom serializer settings
                JToken token = CreateTokenFromValue(value, type);
                if (token != null) // Check if serialization succeeded in the helper
                {
                    // Convert JToken back to a basic object structure for the dictionary
                    dict[name] = ConvertJTokenToPlainObject(token);
                }
                // If token is null, it means serialization failed and a warning was logged.
            }
            catch (Exception e)
            {
                // Catch potential errors during JToken conversion or addition to dictionary
                McpLog.Warn($"[AddSerializableValue] Error processing value for '{name}' (Type: {type.FullName}): {e.Message}. Skipping.");
            }
        }

        // Helper to convert JToken back to basic object structure
        private static object ConvertJTokenToPlainObject(JToken token)
        {
            if (token == null) return null;

            switch (token.Type)
            {
                case JTokenType.Object:
                    var objDict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        objDict[prop.Name] = ConvertJTokenToPlainObject(prop.Value);
                    }
                    return objDict;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                    {
                        list.Add(ConvertJTokenToPlainObject(item));
                    }
                    return list;

                case JTokenType.Integer:
                    return token.ToObject<long>(); // Use long for safety
                case JTokenType.Float:
                    return token.ToObject<double>(); // Use double for safety
                case JTokenType.String:
                    return token.ToObject<string>();
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                case JTokenType.Date:
                    return token.ToObject<DateTime>();
                case JTokenType.Guid:
                    return token.ToObject<Guid>();
                case JTokenType.Uri:
                    return token.ToObject<Uri>();
                case JTokenType.TimeSpan:
                    return token.ToObject<TimeSpan>();
                case JTokenType.Bytes:
                    return token.ToObject<byte[]>();
                case JTokenType.Null:
                    return null;
                case JTokenType.Undefined:
                    return null; // Treat undefined as null

                default:
                    // Fallback for simple value types not explicitly listed
                    if (token is JValue jValue && jValue.Value != null)
                    {
                        return jValue.Value;
                    }
                    // McpLog.Warn($"Unsupported JTokenType encountered: {token.Type}. Returning null.");
                    return null;
            }
        }

        // --- Define custom JsonSerializerSettings for OUTPUT ---
        private static readonly JsonSerializerSettings _outputSerializerSettings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new UnityEngineObjectConverter() // Handles serialization of references
            },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 5, // Prevent stack overflow from deep/circular structures
            // ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } // Example if needed
        };
        private static readonly JsonSerializer _outputSerializer = JsonSerializer.Create(_outputSerializerSettings);
        // --- End Define custom JsonSerializerSettings ---

        // Track visited objects to prevent infinite recursion
        [ThreadStatic] private static HashSet<int> _visitedObjects;
        [ThreadStatic] private static int _serializationDepth;
        private const int MaxSerializationDepth = 5;

        // Helper to create JToken using the output serializer
        private static JToken CreateTokenFromValue(object value, Type type)
        {
            if (value == null) return JValue.CreateNull();

            // Prevent infinite recursion with depth limit
            if (_serializationDepth > MaxSerializationDepth)
            {
                if (value is UnityEngine.Object unityObj)
                    return JToken.FromObject(new { instanceID = unityObj.GetInstanceID(), name = unityObj.name });
                return JValue.CreateNull();
            }

            // Track visited Unity objects to break cycles
            if (value is UnityEngine.Object uo)
            {
                if (_visitedObjects == null) _visitedObjects = new HashSet<int>();
                int id = uo.GetInstanceID();
                if (_visitedObjects.Contains(id))
                    return JToken.FromObject(new { instanceID = id, name = uo.name, _circular = true });
                _visitedObjects.Add(id);
            }

            _serializationDepth++;
            try
            {
                // Use the pre-configured OUTPUT serializer instance
                return JToken.FromObject(value, _outputSerializer);
            }
            catch (JsonSerializationException e)
            {
                McpLog.Warn($"[GameObjectSerializer] Newtonsoft.Json Error serializing value of type {type.FullName}: {e.Message}. Skipping property/field.");
                return null; // Indicate serialization failure
            }
            catch (Exception e) // Catch other unexpected errors
            {
                McpLog.Warn($"[GameObjectSerializer] Unexpected error serializing value of type {type.FullName}: {e}. Skipping property/field.");
                return null; // Indicate serialization failure
            }
            finally
            {
                _serializationDepth--;
            }
        }

        /// <summary>
        /// Call at the start of a top-level serialization to reset tracking state.
        /// </summary>
        public static void ResetSerializationState()
        {
            _visitedObjects?.Clear();
            _serializationDepth = 0;
        }
    }
}
