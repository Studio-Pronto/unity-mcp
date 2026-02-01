using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// JSON converter for arrays and lists of [Serializable] structs.
    /// Handles recursive resolution of nested find instructions for Unity object references.
    /// </summary>
    public class SerializableStructArrayConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            // Handle T[] where T is a [Serializable] struct
            if (objectType.IsArray)
                return IsSerializableStruct(objectType.GetElementType());

            // Handle List<T> where T is a [Serializable] struct
            if (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(List<>))
                return IsSerializableStruct(objectType.GetGenericArguments()[0]);

            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            JArray arr = JArray.Load(reader);
            Type elementType = objectType.IsArray
                ? objectType.GetElementType()
                : objectType.GetGenericArguments()[0];

            var results = new List<object>();
            foreach (var item in arr)
            {
                if (item.Type == JTokenType.Null)
                {
                    results.Add(null);
                }
                else
                {
                    results.Add(DeserializeStruct(item as JObject, elementType, serializer));
                }
            }

            if (objectType.IsArray)
            {
                var array = Array.CreateInstance(elementType, results.Count);
                for (int i = 0; i < results.Count; i++)
                    array.SetValue(results[i], i);
                return array;
            }
            else
            {
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = (IList)Activator.CreateInstance(listType);
                foreach (var item in results)
                    list.Add(item);
                return list;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // Writing is handled by GameObjectSerializer.SerializeStructValue
            throw new NotImplementedException("Writing struct arrays is handled by GameObjectSerializer");
        }

        public override bool CanWrite => false;

        private static object DeserializeStruct(JObject obj, Type structType, JsonSerializer serializer)
        {
            if (obj == null)
                return Activator.CreateInstance(structType);

            object instance = Activator.CreateInstance(structType);
            var fields = structType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                // Skip non-serialized fields
                if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))
                    continue;

                if (!obj.TryGetValue(field.Name, out JToken token) || token.Type == JTokenType.Null)
                    continue;

                try
                {
                    object val;

                    // Check for find instruction in Unity object fields
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                        && token is JObject fo && fo.ContainsKey("find"))
                    {
                        val = ObjectResolver.Resolve(fo, field.FieldType);
                    }
                    // Recursively deserialize nested structs
                    else if (IsSerializableStruct(field.FieldType) && token is JObject nested)
                    {
                        val = DeserializeStruct(nested, field.FieldType, serializer);
                    }
                    // Use standard deserialization for other types
                    else
                    {
                        val = token.ToObject(field.FieldType, serializer);
                    }

                    if (val != null)
                        field.SetValue(instance, val);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[StructArrayConverter] Error deserializing field '{field.Name}': {ex.Message}");
                }
            }

            return instance;
        }

        private static bool IsSerializableStruct(Type type)
        {
            if (type == null || !type.IsValueType || type.IsPrimitive || type.IsEnum)
                return false;
            return type.IsDefined(typeof(SerializableAttribute), false);
        }
    }
}
