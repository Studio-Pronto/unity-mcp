using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ControllerBlendTrees
    {
        public static object CreateBlendTree1D(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return new { success = false, message = $"AnimatorController not found at '{controllerPath}'" };

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            string blendParameter = @params["blendParameter"]?.ToString();
            if (string.IsNullOrEmpty(blendParameter))
                return new { success = false, message = "'blendParameter' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (0-{layers.Length - 1})" };

            var stateMachine = layers[layerIndex].stateMachine;

            // Support creating blend tree in a sub-state machine
            string smPath = @params["stateMachinePath"]?.ToString();
            var targetMachine = stateMachine;
            if (!string.IsNullOrEmpty(smPath))
            {
                targetMachine = ControllerCreate.ResolveStateMachinePath(stateMachine, smPath);
                if (targetMachine == null)
                    return new { success = false, message = $"Sub-state machine path '{smPath}' not found in layer {layerIndex}" };
            }

            Undo.RecordObject(controller, "Create Blend Tree 1D");
            var state = targetMachine.AddState(stateName);
            var blendTree = new BlendTree
            {
                name = stateName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = blendParameter,
                hideFlags = HideFlags.HideInHierarchy
            };

            AssetDatabase.AddObjectToAsset(blendTree, controller);
            state.motion = blendTree;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created 1D blend tree state '{stateName}' in '{controllerPath}'",
                data = new
                {
                    controllerPath,
                    stateName,
                    layerIndex,
                    blendParameter,
                    blendType = "Simple1D"
                }
            };
        }

        public static object CreateBlendTree2D(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return new { success = false, message = $"AnimatorController not found at '{controllerPath}'" };

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            string blendParameterX = @params["blendParameterX"]?.ToString();
            string blendParameterY = @params["blendParameterY"]?.ToString();
            if (string.IsNullOrEmpty(blendParameterX) || string.IsNullOrEmpty(blendParameterY))
                return new { success = false, message = "'blendParameterX' and 'blendParameterY' are required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            string blendTypeStr = @params["blendType"]?.ToString()?.ToLowerInvariant() ?? "simpledirectional2d";

            BlendTreeType blendType = blendTypeStr switch
            {
                "freeformdirectional2d" => BlendTreeType.FreeformDirectional2D,
                "freeformcartesian2d" => BlendTreeType.FreeformCartesian2D,
                _ => BlendTreeType.SimpleDirectional2D
            };

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (0-{layers.Length - 1})" };

            var stateMachine = layers[layerIndex].stateMachine;

            // Support creating blend tree in a sub-state machine
            string smPath = @params["stateMachinePath"]?.ToString();
            var targetMachine = stateMachine;
            if (!string.IsNullOrEmpty(smPath))
            {
                targetMachine = ControllerCreate.ResolveStateMachinePath(stateMachine, smPath);
                if (targetMachine == null)
                    return new { success = false, message = $"Sub-state machine path '{smPath}' not found in layer {layerIndex}" };
            }

            Undo.RecordObject(controller, "Create Blend Tree 2D");
            var state = targetMachine.AddState(stateName);
            var blendTree = new BlendTree
            {
                name = stateName,
                blendType = blendType,
                blendParameter = blendParameterX,
                blendParameterY = blendParameterY,
                hideFlags = HideFlags.HideInHierarchy
            };

            AssetDatabase.AddObjectToAsset(blendTree, controller);
            state.motion = blendTree;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created 2D blend tree state '{stateName}' in '{controllerPath}'",
                data = new
                {
                    controllerPath,
                    stateName,
                    layerIndex,
                    blendParameterX,
                    blendParameterY,
                    blendType = blendType.ToString()
                }
            };
        }

        public static object AddBlendTreeChild(JObject @params)
        {
            var (blendTree, controller, controllerPath, error) = ResolveBlendTree(@params);
            if (error != null)
                return new { success = false, message = error };

            string stateName = @params["stateName"]?.ToString();

            string clipPath = @params["clipPath"]?.ToString();
            if (string.IsNullOrEmpty(clipPath))
                return new { success = false, message = "'clipPath' is required" };

            clipPath = AssetPathUtility.SanitizeAssetPath(clipPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return new { success = false, message = $"AnimationClip not found at '{clipPath}'" };

            Undo.RecordObject(blendTree, "Add Blend Tree Child");

            if (blendTree.blendType == BlendTreeType.Simple1D)
            {
                float? threshold = @params["threshold"]?.ToObject<float?>();
                if (!threshold.HasValue)
                    return new { success = false, message = "'threshold' is required for 1D blend trees" };

                blendTree.AddChild(clip, threshold.Value);

                EditorUtility.SetDirty(blendTree);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Added clip '{clip.name}' to blend tree '{stateName}' at threshold {threshold.Value}",
                    data = new
                    {
                        controllerPath,
                        stateName,
                        clipPath,
                        threshold = threshold.Value,
                        childCount = blendTree.children.Length
                    }
                };
            }
            else
            {
                JToken positionToken = @params["position"];
                if (positionToken == null || !(positionToken is JArray posArray) || posArray.Count < 2)
                    return new { success = false, message = "'position' is required for 2D blend trees as [x, y]" };

                float posX = posArray[0].ToObject<float>();
                float posY = posArray[1].ToObject<float>();
                Vector2 position = new Vector2(posX, posY);

                blendTree.AddChild(clip, position);

                EditorUtility.SetDirty(blendTree);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Added clip '{clip.name}' to blend tree '{stateName}' at position ({posX}, {posY})",
                    data = new
                    {
                        controllerPath,
                        stateName,
                        clipPath,
                        position = new { x = posX, y = posY },
                        childCount = blendTree.children.Length
                    }
                };
            }
        }

        public static object AddBlendTreeChildTree(JObject @params)
        {
            var (parentTree, controller, controllerPath, error) = ResolveBlendTree(@params);
            if (error != null)
                return new { success = false, message = error };

            string stateName = @params["stateName"]?.ToString();

            string childTreeName = @params["childTreeName"]?.ToString();
            if (string.IsNullOrEmpty(childTreeName))
                return new { success = false, message = "'childTreeName' is required" };

            string childBlendTypeStr = @params["childBlendType"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(childBlendTypeStr))
                return new { success = false, message = "'childBlendType' is required (1d, simpledirectional2d, freeformdirectional2d, freeformcartesian2d)" };

            BlendTreeType childBlendType;
            string childBlendParam = null;
            string childBlendParamX = null;
            string childBlendParamY = null;

            if (childBlendTypeStr == "1d")
            {
                childBlendType = BlendTreeType.Simple1D;
                childBlendParam = @params["childBlendParameter"]?.ToString();
                if (string.IsNullOrEmpty(childBlendParam))
                    return new { success = false, message = "'childBlendParameter' is required for 1D child blend trees" };
            }
            else
            {
                childBlendType = childBlendTypeStr switch
                {
                    "freeformdirectional2d" => BlendTreeType.FreeformDirectional2D,
                    "freeformcartesian2d" => BlendTreeType.FreeformCartesian2D,
                    _ => BlendTreeType.SimpleDirectional2D
                };
                childBlendParamX = @params["childBlendParameterX"]?.ToString();
                childBlendParamY = @params["childBlendParameterY"]?.ToString();
                if (string.IsNullOrEmpty(childBlendParamX) || string.IsNullOrEmpty(childBlendParamY))
                    return new { success = false, message = "'childBlendParameterX' and 'childBlendParameterY' are required for 2D child blend trees" };
            }

            Undo.RecordObject(parentTree, "Add Child Blend Tree");

            var childTree = new BlendTree
            {
                name = childTreeName,
                blendType = childBlendType,
                hideFlags = HideFlags.HideInHierarchy
            };

            if (childBlendType == BlendTreeType.Simple1D)
            {
                childTree.blendParameter = childBlendParam;
            }
            else
            {
                childTree.blendParameter = childBlendParamX;
                childTree.blendParameterY = childBlendParamY;
            }

            AssetDatabase.AddObjectToAsset(childTree, controller);

            if (parentTree.blendType == BlendTreeType.Simple1D)
            {
                float? threshold = @params["threshold"]?.ToObject<float?>();
                if (!threshold.HasValue)
                    return new { success = false, message = "'threshold' is required when parent is a 1D blend tree" };

                parentTree.AddChild(childTree, threshold.Value);

                EditorUtility.SetDirty(parentTree);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Added child blend tree '{childTreeName}' to '{stateName}' at threshold {threshold.Value}",
                    data = new
                    {
                        controllerPath,
                        stateName,
                        childTreeName,
                        childBlendType = childBlendType.ToString(),
                        threshold = threshold.Value,
                        childCount = parentTree.children.Length
                    }
                };
            }
            else
            {
                JToken positionToken = @params["position"];
                if (positionToken == null || !(positionToken is JArray posArray) || posArray.Count < 2)
                    return new { success = false, message = "'position' is required for 2D parent blend trees as [x, y]" };

                float posX = posArray[0].ToObject<float>();
                float posY = posArray[1].ToObject<float>();
                Vector2 position = new Vector2(posX, posY);

                parentTree.AddChild(childTree, position);

                EditorUtility.SetDirty(parentTree);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Added child blend tree '{childTreeName}' to '{stateName}' at position ({posX}, {posY})",
                    data = new
                    {
                        controllerPath,
                        stateName,
                        childTreeName,
                        childBlendType = childBlendType.ToString(),
                        position = new { x = posX, y = posY },
                        childCount = parentTree.children.Length
                    }
                };
            }
        }

        private static (BlendTree tree, AnimatorController controller, string controllerPath, string error) ResolveBlendTree(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return (null, null, null, "'controllerPath' is required");

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return (null, null, null, "Invalid asset path");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return (null, null, null, $"AnimatorController not found at '{controllerPath}'");

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return (null, null, null, "'stateName' is required");

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return (null, null, null, $"Layer index {layerIndex} out of range (0-{layers.Length - 1})");

            // Split stateName by '/' — first segment is the state, rest are nested blend tree children
            string[] segments = stateName.Split('/');
            string rootStateName = segments[0];

            var stateMachine = layers[layerIndex].stateMachine;

            // Support optional stateMachinePath to find states inside sub-state machines
            string smPath = @params["stateMachinePath"]?.ToString();
            AnimatorStateMachine searchMachine = stateMachine;
            if (!string.IsNullOrEmpty(smPath))
            {
                searchMachine = ControllerCreate.ResolveStateMachinePath(stateMachine, smPath);
                if (searchMachine == null)
                    return (null, null, null, $"Sub-state machine path '{smPath}' not found in layer {layerIndex}");
            }

            var state = ControllerCreate.FindState(searchMachine, rootStateName);
            if (state == null)
                return (null, null, null, $"State '{rootStateName}' not found in layer {layerIndex}");

            if (!(state.motion is BlendTree blendTree))
                return (null, null, null, $"State '{rootStateName}' does not have a BlendTree motion");

            // Walk nested blend tree path
            for (int i = 1; i < segments.Length; i++)
            {
                string childName = segments[i];
                BlendTree childTree = null;
                foreach (var child in blendTree.children)
                {
                    if (child.motion is BlendTree bt && bt.name == childName)
                    {
                        childTree = bt;
                        break;
                    }
                }

                if (childTree == null)
                    return (null, null, null, $"Child blend tree '{childName}' not found in '{blendTree.name}'");

                blendTree = childTree;
            }

            return (blendTree, controller, controllerPath, null);
        }
    }
}
