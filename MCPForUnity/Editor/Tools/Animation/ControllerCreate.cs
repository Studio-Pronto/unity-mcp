using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class ControllerCreate
    {
        public static object Create(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return new { success = false, message = "'controllerPath' is required (e.g. 'Assets/Animations/Player.controller')" };

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return new { success = false, message = "Invalid asset path" };

            if (!controllerPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                controllerPath += ".controller";

            string dir = Path.GetDirectoryName(controllerPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                CreateFoldersRecursive(dir);

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existing != null)
                return new { success = false, message = $"AnimatorController already exists at '{controllerPath}'. Delete it first or use a different path." };

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Created AnimatorController at '{controllerPath}'",
                data = new
                {
                    path = controllerPath,
                    name = controller.name,
                    layerCount = controller.layers.Length,
                    parameterCount = controller.parameters.Length
                }
            };
        }

        public static object AddState(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            // Check for duplicate state name
            if (FindState(rootStateMachine, stateName) != null)
                return new { success = false, message = $"State '{stateName}' already exists in layer {layerIndex}" };

            var state = rootStateMachine.AddState(stateName);

            // Optionally assign a motion clip
            string clipPath = @params["clipPath"]?.ToString();
            if (!string.IsNullOrEmpty(clipPath))
            {
                string clipName = @params["clipName"]?.ToString();
                var (motion, error) = LoadMotionFromPath(clipPath, clipName);
                if (error != null)
                {
                    rootStateMachine.RemoveState(state);
                    return new { success = false, message = error };
                }
                state.motion = motion;
            }

            float speed = @params["speed"]?.ToObject<float>() ?? 1f;
            state.speed = speed;

            string tag = @params["tag"]?.ToString();
            if (!string.IsNullOrEmpty(tag))
                state.tag = tag;

            bool isDefault = @params["isDefault"]?.ToObject<bool>() ?? false;
            if (isDefault)
                rootStateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added state '{stateName}' to layer {layerIndex}",
                data = new
                {
                    stateName,
                    layerIndex,
                    hasMotion = state.motion != null,
                    motionName = state.motion?.name,
                    speed = state.speed,
                    tag = state.tag,
                    isDefault
                }
            };
        }

        public static object AddTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string fromStateName = @params["fromState"]?.ToString();
            string toStateName = @params["toState"]?.ToString();
            if (string.IsNullOrEmpty(fromStateName) || string.IsNullOrEmpty(toStateName))
                return new { success = false, message = "'fromState' and 'toState' are required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            // Check for AnyState as source
            bool isAnyState = string.Equals(fromStateName, "AnyState", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any State", StringComparison.OrdinalIgnoreCase);

            var toState = FindState(rootStateMachine, toStateName);
            if (toState == null)
                return new { success = false, message = $"State '{toStateName}' not found in layer {layerIndex}" };

            AnimatorStateTransition transition;
            if (isAnyState)
            {
                transition = rootStateMachine.AddAnyStateTransition(toState);
                fromStateName = "AnyState";
            }
            else
            {
                var fromState = FindState(rootStateMachine, fromStateName);
                if (fromState == null)
                    return new { success = false, message = $"State '{fromStateName}' not found in layer {layerIndex}" };

                transition = fromState.AddTransition(toState);
            }

            bool hasExitTime = @params["hasExitTime"]?.ToObject<bool>() ?? true;
            transition.hasExitTime = hasExitTime;

            float duration = @params["duration"]?.ToObject<float>() ?? 0.25f;
            transition.duration = duration;

            float exitTime = @params["exitTime"]?.ToObject<float>() ?? 0.75f;
            transition.exitTime = exitTime;

            // Add conditions
            JToken conditionsToken = @params["conditions"];
            int conditionCount = 0;
            if (conditionsToken is JArray conditionsArray)
            {
                foreach (var condItem in conditionsArray)
                {
                    if (condItem is not JObject condObj) continue;

                    string paramName = condObj["parameter"]?.ToString();
                    if (string.IsNullOrEmpty(paramName)) continue;

                    string modeStr = condObj["mode"]?.ToString()?.ToLowerInvariant() ?? "greater";
                    float threshold = condObj["threshold"]?.ToObject<float>() ?? 0f;

                    AnimatorConditionMode mode;
                    switch (modeStr)
                    {
                        case "greater": mode = AnimatorConditionMode.Greater; break;
                        case "less": mode = AnimatorConditionMode.Less; break;
                        case "equals": mode = AnimatorConditionMode.Equals; break;
                        case "notequal":
                        case "not_equal": mode = AnimatorConditionMode.NotEqual; break;
                        case "if":
                        case "true": mode = AnimatorConditionMode.If; break;
                        case "ifnot":
                        case "if_not":
                        case "false": mode = AnimatorConditionMode.IfNot; break;
                        default: mode = AnimatorConditionMode.Greater; break;
                    }

                    transition.AddCondition(mode, threshold, paramName);
                    conditionCount++;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added transition from '{fromStateName}' to '{toStateName}' with {conditionCount} conditions",
                data = new
                {
                    fromState = fromStateName,
                    toState = toStateName,
                    hasExitTime,
                    duration,
                    conditionCount
                }
            };
        }

        public static object AddParameter(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string paramName = @params["parameterName"]?.ToString();
            if (string.IsNullOrEmpty(paramName))
                return new { success = false, message = "'parameterName' is required" };

            string typeStr = @params["parameterType"]?.ToString()?.ToLowerInvariant() ?? "float";

            AnimatorControllerParameterType paramType;
            switch (typeStr)
            {
                case "float": paramType = AnimatorControllerParameterType.Float; break;
                case "int":
                case "integer": paramType = AnimatorControllerParameterType.Int; break;
                case "bool":
                case "boolean": paramType = AnimatorControllerParameterType.Bool; break;
                case "trigger": paramType = AnimatorControllerParameterType.Trigger; break;
                default:
                    return new { success = false, message = $"Unknown parameter type '{typeStr}'. Valid: float, int, bool, trigger" };
            }

            // Check for duplicate
            foreach (var existing in controller.parameters)
            {
                if (existing.name == paramName)
                    return new { success = false, message = $"Parameter '{paramName}' already exists" };
            }

            controller.AddParameter(paramName, paramType);

            // Set default value if provided
            JToken defaultValue = @params["defaultValue"];
            if (defaultValue != null)
            {
                var allParams = controller.parameters;
                var addedParam = allParams[allParams.Length - 1];

                switch (paramType)
                {
                    case AnimatorControllerParameterType.Float:
                        addedParam.defaultFloat = defaultValue.ToObject<float>();
                        break;
                    case AnimatorControllerParameterType.Int:
                        addedParam.defaultInt = defaultValue.ToObject<int>();
                        break;
                    case AnimatorControllerParameterType.Bool:
                        addedParam.defaultBool = defaultValue.ToObject<bool>();
                        break;
                }

                controller.parameters = allParams;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added {typeStr} parameter '{paramName}'",
                data = new
                {
                    parameterName = paramName,
                    parameterType = typeStr,
                    totalParameters = controller.parameters.Length
                }
            };
        }

        public static object GetInfo(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            var layers = new List<object>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                var states = new List<object>();
                foreach (var cs in layer.stateMachine.states)
                {
                    var transitions = new List<object>();
                    foreach (var t in cs.state.transitions)
                    {
                        var conditions = new List<object>();
                        foreach (var c in t.conditions)
                        {
                            conditions.Add(new
                            {
                                parameter = c.parameter,
                                mode = c.mode.ToString(),
                                threshold = c.threshold
                            });
                        }

                        transitions.Add(new
                        {
                            destinationState = t.destinationState?.name,
                            hasExitTime = t.hasExitTime,
                            exitTime = t.exitTime,
                            duration = t.duration,
                            offset = t.offset,
                            hasFixedDuration = t.hasFixedDuration,
                            canTransitionToSelf = t.canTransitionToSelf,
                            conditionCount = t.conditions.Length,
                            conditions
                        });
                    }

                    states.Add(new
                    {
                        name = cs.state.name,
                        tag = cs.state.tag,
                        speed = cs.state.speed,
                        hasMotion = cs.state.motion != null,
                        motionName = cs.state.motion?.name,
                        writeDefaultValues = cs.state.writeDefaultValues,
                        iKOnFeet = cs.state.iKOnFeet,
                        isDefault = layer.stateMachine.defaultState == cs.state,
                        transitionCount = cs.state.transitions.Length,
                        transitions
                    });
                }

                layers.Add(new
                {
                    index = i,
                    name = layer.name,
                    stateCount = layer.stateMachine.states.Length,
                    states
                });
            }

            var parameters = new List<object>();
            foreach (var p in controller.parameters)
            {
                parameters.Add(new
                {
                    name = p.name,
                    type = p.type.ToString(),
                    defaultFloat = p.defaultFloat,
                    defaultInt = p.defaultInt,
                    defaultBool = p.defaultBool
                });
            }

            return new
            {
                success = true,
                data = new
                {
                    path = AssetDatabase.GetAssetPath(controller),
                    name = controller.name,
                    layerCount = controller.layers.Length,
                    parameterCount = controller.parameters.Length,
                    layers,
                    parameters
                }
            };
        }

        public static object SetStateMotion(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;
            var targetState = FindState(rootStateMachine, stateName);
            if (targetState == null)
                return new { success = false, message = $"State '{stateName}' not found in layer {layerIndex}" };

            string clipPath = @params["clipPath"]?.ToString();
            Motion motion = null;
            if (!string.IsNullOrEmpty(clipPath))
            {
                string clipName = @params["clipName"]?.ToString();
                var (loaded, error) = LoadMotionFromPath(clipPath, clipName);
                if (error != null)
                    return new { success = false, message = error };
                motion = loaded;
            }

            targetState.motion = motion;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = motion != null
                    ? $"Set motion '{motion.name}' on state '{stateName}' in layer {layerIndex}"
                    : $"Cleared motion on state '{stateName}' in layer {layerIndex}",
                data = new
                {
                    stateName,
                    layerIndex,
                    hasMotion = motion != null,
                    motionName = motion?.name
                }
            };
        }

        public static object RemoveState(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;
            AnimatorStateMachine parentMachine;
            var targetState = FindState(rootStateMachine, stateName, out parentMachine);
            if (targetState == null)
                return new { success = false, message = $"State '{stateName}' not found in layer {layerIndex}" };

            parentMachine.RemoveState(targetState);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed state '{stateName}' from layer {layerIndex}",
                data = new
                {
                    stateName,
                    layerIndex,
                    remainingStates = rootStateMachine.states.Length
                }
            };
        }

        public static object RemoveTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string fromStateName = @params["fromState"]?.ToString();
            string toStateName = @params["toState"]?.ToString();
            if (string.IsNullOrEmpty(fromStateName) || string.IsNullOrEmpty(toStateName))
                return new { success = false, message = "'fromState' and 'toState' are required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            int? transitionIndex = @params["transitionIndex"]?.ToObject<int>();

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            bool isAnyState = string.Equals(fromStateName, "AnyState", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any State", StringComparison.OrdinalIgnoreCase);

            int removedCount = 0;

            if (isAnyState)
            {
                var matching = new List<AnimatorStateTransition>();
                foreach (var t in rootStateMachine.anyStateTransitions)
                {
                    if (t.destinationState != null && t.destinationState.name == toStateName)
                        matching.Add(t);
                }

                if (matching.Count == 0)
                    return new { success = false, message = $"No AnyState transition to '{toStateName}' found in layer {layerIndex}" };

                if (transitionIndex.HasValue)
                {
                    if (transitionIndex.Value < 0 || transitionIndex.Value >= matching.Count)
                        return new { success = false, message = $"Transition index {transitionIndex.Value} out of range ({matching.Count} matching transitions)" };

                    rootStateMachine.RemoveAnyStateTransition(matching[transitionIndex.Value]);
                    removedCount = 1;
                }
                else
                {
                    foreach (var t in matching)
                        rootStateMachine.RemoveAnyStateTransition(t);
                    removedCount = matching.Count;
                }

                fromStateName = "AnyState";
            }
            else
            {
                var fromState = FindState(rootStateMachine, fromStateName);
                if (fromState == null)
                    return new { success = false, message = $"State '{fromStateName}' not found in layer {layerIndex}" };

                var matching = new List<AnimatorStateTransition>();
                foreach (var t in fromState.transitions)
                {
                    if (t.destinationState != null && t.destinationState.name == toStateName)
                        matching.Add(t);
                }

                if (matching.Count == 0)
                    return new { success = false, message = $"No transition from '{fromStateName}' to '{toStateName}' found in layer {layerIndex}" };

                if (transitionIndex.HasValue)
                {
                    if (transitionIndex.Value < 0 || transitionIndex.Value >= matching.Count)
                        return new { success = false, message = $"Transition index {transitionIndex.Value} out of range ({matching.Count} matching transitions)" };

                    fromState.RemoveTransition(matching[transitionIndex.Value]);
                    removedCount = 1;
                }
                else
                {
                    foreach (var t in matching)
                        fromState.RemoveTransition(t);
                    removedCount = matching.Count;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed {removedCount} transition(s) from '{fromStateName}' to '{toStateName}' in layer {layerIndex}",
                data = new
                {
                    fromState = fromStateName,
                    toState = toStateName,
                    layerIndex,
                    removedCount
                }
            };
        }

        public static object RemoveParameter(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string paramName = @params["parameterName"]?.ToString();
            if (string.IsNullOrEmpty(paramName))
                return new { success = false, message = "'parameterName' is required" };

            int paramIndex = -1;
            var allParams = controller.parameters;
            for (int i = 0; i < allParams.Length; i++)
            {
                if (allParams[i].name == paramName)
                {
                    paramIndex = i;
                    break;
                }
            }

            if (paramIndex < 0)
                return new { success = false, message = $"Parameter '{paramName}' not found" };

            controller.RemoveParameter(paramIndex);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed parameter '{paramName}'",
                data = new
                {
                    parameterName = paramName,
                    totalParameters = controller.parameters.Length
                }
            };
        }

        public static object ModifyState(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string stateName = @params["stateName"]?.ToString();
            if (string.IsNullOrEmpty(stateName))
                return new { success = false, message = "'stateName' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;
            var targetState = FindState(rootStateMachine, stateName);
            if (targetState == null)
                return new { success = false, message = $"State '{stateName}' not found in layer {layerIndex}" };

            var changed = new List<string>();

            if (@params["tag"] != null)
            {
                targetState.tag = @params["tag"].ToString();
                changed.Add("tag");
            }
            if (@params["speed"] != null)
            {
                targetState.speed = @params["speed"].ToObject<float>();
                changed.Add("speed");
            }
            if (@params["writeDefaultValues"] != null)
            {
                targetState.writeDefaultValues = @params["writeDefaultValues"].ToObject<bool>();
                changed.Add("writeDefaultValues");
            }
            if (@params["iKOnFeet"] != null)
            {
                targetState.iKOnFeet = @params["iKOnFeet"].ToObject<bool>();
                changed.Add("iKOnFeet");
            }
            if (@params["mirror"] != null)
            {
                targetState.mirror = @params["mirror"].ToObject<bool>();
                changed.Add("mirror");
            }
            if (@params["cycleOffset"] != null)
            {
                targetState.cycleOffset = @params["cycleOffset"].ToObject<float>();
                changed.Add("cycleOffset");
            }
            if (@params["speedParameter"] != null)
            {
                string val = @params["speedParameter"].ToString();
                targetState.speedParameter = val;
                targetState.speedParameterActive = !string.IsNullOrEmpty(val);
                changed.Add("speedParameter");
            }
            if (@params["cycleOffsetParameter"] != null)
            {
                string val = @params["cycleOffsetParameter"].ToString();
                targetState.cycleOffsetParameter = val;
                targetState.cycleOffsetParameterActive = !string.IsNullOrEmpty(val);
                changed.Add("cycleOffsetParameter");
            }
            if (@params["mirrorParameter"] != null)
            {
                string val = @params["mirrorParameter"].ToString();
                targetState.mirrorParameter = val;
                targetState.mirrorParameterActive = !string.IsNullOrEmpty(val);
                changed.Add("mirrorParameter");
            }
            if (@params["timeParameter"] != null)
            {
                string val = @params["timeParameter"].ToString();
                targetState.timeParameter = val;
                targetState.timeParameterActive = !string.IsNullOrEmpty(val);
                changed.Add("timeParameter");
            }

            if (changed.Count == 0)
                return new { success = false, message = "No recognized state properties provided. Supported: tag, speed, writeDefaultValues, iKOnFeet, mirror, cycleOffset, speedParameter, cycleOffsetParameter, mirrorParameter, timeParameter" };

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Modified {changed.Count} property(s) on state '{stateName}': {string.Join(", ", changed)}",
                data = new
                {
                    stateName,
                    layerIndex,
                    modifiedProperties = changed
                }
            };
        }

        public static object ModifyTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string fromStateName = @params["fromState"]?.ToString();
            string toStateName = @params["toState"]?.ToString();
            if (string.IsNullOrEmpty(fromStateName) || string.IsNullOrEmpty(toStateName))
                return new { success = false, message = "'fromState' and 'toState' are required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            int transitionIndex = @params["transitionIndex"]?.ToObject<int>() ?? 0;

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            bool isAnyState = string.Equals(fromStateName, "AnyState", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(fromStateName, "Any State", StringComparison.OrdinalIgnoreCase);

            // Find the target transition
            AnimatorStateTransition transition = null;
            if (isAnyState)
            {
                var matching = new List<AnimatorStateTransition>();
                foreach (var t in rootStateMachine.anyStateTransitions)
                {
                    if (t.destinationState != null && t.destinationState.name == toStateName)
                        matching.Add(t);
                }

                if (matching.Count == 0)
                    return new { success = false, message = $"No AnyState transition to '{toStateName}' found in layer {layerIndex}" };

                if (transitionIndex < 0 || transitionIndex >= matching.Count)
                    return new { success = false, message = $"Transition index {transitionIndex} out of range ({matching.Count} matching transitions)" };

                transition = matching[transitionIndex];
                fromStateName = "AnyState";
            }
            else
            {
                var fromState = FindState(rootStateMachine, fromStateName);
                if (fromState == null)
                    return new { success = false, message = $"State '{fromStateName}' not found in layer {layerIndex}" };

                var matching = new List<AnimatorStateTransition>();
                foreach (var t in fromState.transitions)
                {
                    if (t.destinationState != null && t.destinationState.name == toStateName)
                        matching.Add(t);
                }

                if (matching.Count == 0)
                    return new { success = false, message = $"No transition from '{fromStateName}' to '{toStateName}' found in layer {layerIndex}" };

                if (transitionIndex < 0 || transitionIndex >= matching.Count)
                    return new { success = false, message = $"Transition index {transitionIndex} out of range ({matching.Count} matching transitions)" };

                transition = matching[transitionIndex];
            }

            var changed = new List<string>();

            if (@params["hasExitTime"] != null)
            {
                transition.hasExitTime = @params["hasExitTime"].ToObject<bool>();
                changed.Add("hasExitTime");
            }
            if (@params["exitTime"] != null)
            {
                transition.exitTime = @params["exitTime"].ToObject<float>();
                changed.Add("exitTime");
            }
            if (@params["duration"] != null)
            {
                transition.duration = @params["duration"].ToObject<float>();
                changed.Add("duration");
            }
            if (@params["offset"] != null)
            {
                transition.offset = @params["offset"].ToObject<float>();
                changed.Add("offset");
            }
            if (@params["hasFixedDuration"] != null)
            {
                transition.hasFixedDuration = @params["hasFixedDuration"].ToObject<bool>();
                changed.Add("hasFixedDuration");
            }
            if (@params["interruptionSource"] != null)
            {
                string srcStr = @params["interruptionSource"].ToString().ToLowerInvariant();
                TransitionInterruptionSource src;
                switch (srcStr)
                {
                    case "none": src = TransitionInterruptionSource.None; break;
                    case "source": src = TransitionInterruptionSource.Source; break;
                    case "destination": src = TransitionInterruptionSource.Destination; break;
                    case "sourcethendestination": src = TransitionInterruptionSource.SourceThenDestination; break;
                    case "destinationthensource": src = TransitionInterruptionSource.DestinationThenSource; break;
                    default:
                        return new { success = false, message = $"Unknown interruptionSource '{srcStr}'. Valid: none, source, destination, sourceThenDestination, destinationThenSource" };
                }
                transition.interruptionSource = src;
                changed.Add("interruptionSource");
            }
            if (@params["orderedInterruption"] != null)
            {
                transition.orderedInterruption = @params["orderedInterruption"].ToObject<bool>();
                changed.Add("orderedInterruption");
            }
            if (@params["canTransitionToSelf"] != null)
            {
                transition.canTransitionToSelf = @params["canTransitionToSelf"].ToObject<bool>();
                changed.Add("canTransitionToSelf");
            }

            // Handle conditions replacement
            if (@params["conditions"] is JArray conditionsArray)
            {
                // Clear existing conditions by setting to empty array
                transition.conditions = new AnimatorCondition[0];

                foreach (var condItem in conditionsArray)
                {
                    if (condItem is not JObject condObj) continue;

                    string paramName = condObj["parameter"]?.ToString();
                    if (string.IsNullOrEmpty(paramName)) continue;

                    string modeStr = condObj["mode"]?.ToString()?.ToLowerInvariant() ?? "greater";
                    float threshold = condObj["threshold"]?.ToObject<float>() ?? 0f;

                    AnimatorConditionMode mode;
                    switch (modeStr)
                    {
                        case "greater": mode = AnimatorConditionMode.Greater; break;
                        case "less": mode = AnimatorConditionMode.Less; break;
                        case "equals": mode = AnimatorConditionMode.Equals; break;
                        case "notequal":
                        case "not_equal": mode = AnimatorConditionMode.NotEqual; break;
                        case "if":
                        case "true": mode = AnimatorConditionMode.If; break;
                        case "ifnot":
                        case "if_not":
                        case "false": mode = AnimatorConditionMode.IfNot; break;
                        default: mode = AnimatorConditionMode.Greater; break;
                    }

                    transition.AddCondition(mode, threshold, paramName);
                }

                changed.Add("conditions");
            }

            if (changed.Count == 0)
                return new { success = false, message = "No recognized transition properties provided. Supported: hasExitTime, exitTime, duration, offset, hasFixedDuration, interruptionSource, orderedInterruption, canTransitionToSelf, conditions" };

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Modified {changed.Count} property(s) on transition from '{fromStateName}' to '{toStateName}': {string.Join(", ", changed)}",
                data = new
                {
                    fromState = fromStateName,
                    toState = toStateName,
                    layerIndex,
                    transitionIndex,
                    modifiedProperties = changed
                }
            };
        }

        public static object AssignToGameObject(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            var go = ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
            if (go == null)
                return new { success = false, message = "Target GameObject not found" };

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                Undo.RecordObject(go, "Add Animator Component");
                animator = Undo.AddComponent<Animator>(go);
            }

            Undo.RecordObject(animator, "Assign AnimatorController");
            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Assigned controller '{controller.name}' to '{go.name}'",
                data = new
                {
                    gameObject = go.name,
                    controllerName = controller.name,
                    controllerPath = AssetDatabase.GetAssetPath(controller)
                }
            };
        }

        /// <summary>
        /// Finds a state by name in a state machine. Currently searches root only;
        /// will be extended to recurse into sub-state machines when that CRUD is added.
        /// </summary>
        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (var cs in stateMachine.states)
            {
                if (cs.state.name == stateName)
                    return cs.state;
            }
            return null;
        }

        /// <summary>
        /// Finds a state by name and returns its parent state machine (needed for RemoveState).
        /// Currently searches root only; will recurse into sub-state machines later.
        /// </summary>
        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName, out AnimatorStateMachine parentMachine)
        {
            foreach (var cs in stateMachine.states)
            {
                if (cs.state.name == stateName)
                {
                    parentMachine = stateMachine;
                    return cs.state;
                }
            }
            parentMachine = null;
            return null;
        }

        private static AnimatorController LoadController(JObject @params)
        {
            string controllerPath = @params["controllerPath"]?.ToString();
            if (string.IsNullOrEmpty(controllerPath))
                return null;

            controllerPath = AssetPathUtility.SanitizeAssetPath(controllerPath);
            if (controllerPath == null)
                return null;

            return AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        }

        private static object ControllerNotFoundError(JObject @params)
        {
            string path = @params["controllerPath"]?.ToString() ?? "(not specified)";
            return new { success = false, message = $"AnimatorController not found at '{path}'. Provide a valid 'controllerPath'." };
        }

        private static (Motion motion, string error) LoadMotionFromPath(string rawClipPath, string clipName)
        {
            string clipPath = AssetPathUtility.SanitizeAssetPath(rawClipPath);
            if (clipPath == null)
                return (null, $"Invalid asset path '{rawClipPath}'");

            var motion = AssetDatabase.LoadAssetAtPath<Motion>(clipPath);
            if (motion != null)
                return (motion, null);

            // Asset exists but main asset isn't a Motion (e.g. FBX) — enumerate sub-assets
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(clipPath);
            if (allAssets == null || allAssets.Length == 0)
                return (null, $"No asset found at '{rawClipPath}'");

            AnimationClip firstClip = null;
            foreach (var asset in allAssets)
            {
                if (!(asset is AnimationClip clip))
                    continue;
                if (clip.name.StartsWith("__preview__"))
                    continue;

                if (!string.IsNullOrEmpty(clipName))
                {
                    if (string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                        return (clip, null);
                }
                else if (firstClip == null)
                {
                    firstClip = clip;
                }
            }

            if (firstClip != null)
                return (firstClip, null);

            if (!string.IsNullOrEmpty(clipName))
            {
                var clipNames = new List<string>();
                foreach (var asset in allAssets)
                {
                    if (asset is AnimationClip c && !c.name.StartsWith("__preview__"))
                        clipNames.Add(c.name);
                }
                return (null, $"Clip '{clipName}' not found in '{rawClipPath}'. Available clips: {string.Join(", ", clipNames)}");
            }

            return (null, $"No AnimationClip found in '{rawClipPath}'");
        }

        private static void CreateFoldersRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && parent != "Assets" && !AssetDatabase.IsValidFolder(parent))
                CreateFoldersRecursive(parent);

            string folderName = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
                AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
