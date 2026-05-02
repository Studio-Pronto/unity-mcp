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

            // Resolve target state machine (supports path notation: "SubMachine/StateName")
            AnimatorStateMachine targetMachine = rootStateMachine;
            string actualStateName = stateName;
            if (stateName.Contains("/"))
            {
                int lastSlash = stateName.LastIndexOf('/');
                string smPath = stateName.Substring(0, lastSlash);
                actualStateName = stateName.Substring(lastSlash + 1);

                targetMachine = ResolveStateMachinePath(rootStateMachine, smPath);
                if (targetMachine == null)
                    return new { success = false, message = $"Sub-state machine path '{smPath}' not found in layer {layerIndex}" };
            }

            // Check for duplicate state name in the target state machine only.
            // Same name in a sibling sub-state machine is allowed (Unity supports this);
            // ambiguity is surfaced at transition-resolution time, not creation time.
            foreach (var cs in targetMachine.states)
            {
                if (cs.state.name == actualStateName)
                    return new { success = false, message = $"State '{actualStateName}' already exists in target state machine" };
            }

            var state = targetMachine.AddState(actualStateName);

            // Optionally assign a motion clip
            string clipPath = @params["clipPath"]?.ToString();
            if (!string.IsNullOrEmpty(clipPath))
            {
                string clipName = @params["clipName"]?.ToString();
                var (motion, error) = LoadMotionFromPath(clipPath, clipName);
                if (error != null)
                {
                    targetMachine.RemoveState(state);
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
                targetMachine.defaultState = state;

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
            AnimatorStateMachine toStateMachine = null;
            if (toState == null)
            {
                // Fallback: check if toState is a sub-state machine name
                toStateMachine = FindStateMachine(rootStateMachine, toStateName);
                if (toStateMachine == null)
                    return new { success = false, message = $"State or sub-state machine '{toStateName}' not found in layer {layerIndex}" };
            }
            else
            {
                // Ambiguity: same name resolves as BOTH a state and a sub-state machine.
                var maybeSm = FindStateMachine(rootStateMachine, toStateName);
                if (maybeSm != null)
                    return new { success = false, message = $"'{toStateName}' is ambiguous in layer {layerIndex}: matches both a state and a sub-state machine. Use a path like 'Parent/{toStateName}' to disambiguate." };
            }

            // Validate conditions UP FRONT so we never create a transition that can't be fully populated.
            JToken conditionsToken = @params["conditions"];
            List<(AnimatorConditionMode mode, float threshold, string paramName)> parsedConditions = null;
            if (conditionsToken is JArray conditionsArray)
            {
                var (parsed, error) = ParseConditions(controller, conditionsArray);
                if (error != null) return error;
                parsedConditions = parsed;
            }

            AnimatorStateTransition transition;
            if (isAnyState)
            {
                transition = toState != null
                    ? rootStateMachine.AddAnyStateTransition(toState)
                    : rootStateMachine.AddAnyStateTransition(toStateMachine);
                fromStateName = "AnyState";
            }
            else
            {
                var fromState = FindState(rootStateMachine, fromStateName);
                if (fromState == null)
                    return new { success = false, message = $"State '{fromStateName}' not found in layer {layerIndex}" };

                transition = toState != null
                    ? fromState.AddTransition(toState)
                    : fromState.AddTransition(toStateMachine);
            }

            bool hasExitTime = @params["hasExitTime"]?.ToObject<bool>() ?? true;
            transition.hasExitTime = hasExitTime;

            float duration = @params["duration"]?.ToObject<float>() ?? 0.25f;
            transition.duration = duration;

            float exitTime = @params["exitTime"]?.ToObject<float>() ?? 0.75f;
            transition.exitTime = exitTime;

            if (@params["offset"] != null)
                transition.offset = @params["offset"].ToObject<float>();
            if (@params["hasFixedDuration"] != null)
                transition.hasFixedDuration = @params["hasFixedDuration"].ToObject<bool>();
            if (@params["interruptionSource"] != null)
            {
                string srcStr = @params["interruptionSource"].ToString().ToLowerInvariant();
                switch (srcStr)
                {
                    case "none": transition.interruptionSource = TransitionInterruptionSource.None; break;
                    case "source": transition.interruptionSource = TransitionInterruptionSource.Source; break;
                    case "destination": transition.interruptionSource = TransitionInterruptionSource.Destination; break;
                    case "sourcethendestination": transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination; break;
                    case "destinationthensource": transition.interruptionSource = TransitionInterruptionSource.DestinationThenSource; break;
                    default:
                        return new { success = false, message = $"Unknown interruptionSource '{srcStr}'. Valid: none, source, destination, sourceThenDestination, destinationThenSource" };
                }
            }
            if (@params["orderedInterruption"] != null)
                transition.orderedInterruption = @params["orderedInterruption"].ToObject<bool>();
            if (@params["canTransitionToSelf"] != null)
                transition.canTransitionToSelf = @params["canTransitionToSelf"].ToObject<bool>();

            int conditionCount = 0;
            if (parsedConditions != null)
            {
                foreach (var (mode, threshold, paramName) in parsedConditions)
                {
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
                var smData = SerializeStateMachine(layer.stateMachine);

                layers.Add(new
                {
                    index = i,
                    name = layer.name,
                    stateCount = layer.stateMachine.states.Length,
                    subStateMachineCount = layer.stateMachine.stateMachines.Length,
                    defaultState = layer.stateMachine.defaultState?.name,
                    states = smData.states,
                    stateMachines = smData.stateMachines,
                    anyStateTransitions = smData.anyStateTransitions,
                    entryTransitions = smData.entryTransitions
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

        private static object SerializeStateTransition(AnimatorStateTransition t)
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

            return new
            {
                destinationState = t.destinationState?.name,
                destinationStateMachine = t.destinationState == null ? t.destinationStateMachine?.name : null,
                isExit = t.isExit,
                hasExitTime = t.hasExitTime,
                exitTime = t.exitTime,
                duration = t.duration,
                offset = t.offset,
                hasFixedDuration = t.hasFixedDuration,
                canTransitionToSelf = t.canTransitionToSelf,
                conditionCount = t.conditions.Length,
                conditions
            };
        }

        private static object SerializeEntryTransition(AnimatorTransition t)
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

            return new
            {
                destinationState = t.destinationState?.name,
                destinationStateMachine = t.destinationState == null ? t.destinationStateMachine?.name : null,
                isExit = t.isExit,
                conditionCount = t.conditions.Length,
                conditions
            };
        }

        private static (List<object> states, List<object> stateMachines, List<object> anyStateTransitions, List<object> entryTransitions) SerializeStateMachine(AnimatorStateMachine sm)
        {
            var states = new List<object>();
            foreach (var cs in sm.states)
            {
                var transitions = new List<object>();
                foreach (var t in cs.state.transitions)
                    transitions.Add(SerializeStateTransition(t));

                states.Add(new
                {
                    name = cs.state.name,
                    tag = cs.state.tag,
                    speed = cs.state.speed,
                    hasMotion = cs.state.motion != null,
                    motionName = cs.state.motion?.name,
                    writeDefaultValues = cs.state.writeDefaultValues,
                    iKOnFeet = cs.state.iKOnFeet,
                    isDefault = sm.defaultState == cs.state,
                    transitionCount = cs.state.transitions.Length,
                    transitions
                });
            }

            var stateMachines = new List<object>();
            foreach (var csm in sm.stateMachines)
            {
                var childData = SerializeStateMachine(csm.stateMachine);
                stateMachines.Add(new
                {
                    name = csm.stateMachine.name,
                    stateCount = csm.stateMachine.states.Length,
                    subStateMachineCount = csm.stateMachine.stateMachines.Length,
                    defaultState = csm.stateMachine.defaultState?.name,
                    states = childData.states,
                    stateMachines = childData.stateMachines,
                    anyStateTransitions = childData.anyStateTransitions,
                    entryTransitions = childData.entryTransitions
                });
            }

            var anyStateTransitions = new List<object>();
            foreach (var t in sm.anyStateTransitions)
                anyStateTransitions.Add(SerializeStateTransition(t));

            var entryTransitions = new List<object>();
            foreach (var t in sm.entryTransitions)
                entryTransitions.Add(SerializeEntryTransition(t));

            return (states, stateMachines, anyStateTransitions, entryTransitions);
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

            var warnings = new List<string>();

            // Pre-scan and clean transitions pointing at the about-to-be-removed state
            // (Unity does not auto-clean inbound transitions on RemoveState).
            int removedTransitions = CleanTransitionsToState(controller, targetState);
            if (removedTransitions > 0)
                warnings.Add($"Removed {removedTransitions} transition(s) pointing to '{stateName}'");

            bool wasDefault = parentMachine.defaultState == targetState;

            parentMachine.RemoveState(targetState);

            if (wasDefault)
            {
                var remaining = parentMachine.states;
                if (remaining.Length > 0)
                {
                    parentMachine.defaultState = remaining[0].state;
                    warnings.Add($"Reassigned defaultState to '{remaining[0].state.name}' (was '{stateName}')");
                }
                else
                {
                    parentMachine.defaultState = null;
                    warnings.Add("Cleared defaultState (no states remaining in parent state machine)");
                }
            }

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
                    remainingStates = parentMachine.states.Length,
                    removedTransitions,
                    defaultStateReassigned = wasDefault,
                    warnings = warnings.ToArray()
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

            // For path-based toState like "Combat/Attack", match the leaf name against destinationState
            string toLeafName = toStateName.Contains("/") ? toStateName.Substring(toStateName.LastIndexOf('/') + 1) : toStateName;

            if (isAnyState)
            {
                var matching = new List<AnimatorStateTransition>();
                foreach (var t in rootStateMachine.anyStateTransitions)
                {
                    if ((t.destinationState != null && t.destinationState.name == toLeafName) ||
                        (t.destinationStateMachine != null && t.destinationStateMachine.name == toLeafName))
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
                    if ((t.destinationState != null && t.destinationState.name == toLeafName) ||
                        (t.destinationStateMachine != null && t.destinationStateMachine.name == toLeafName))
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

        public static object ModifyParameter(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string paramName = @params["parameterName"]?.ToString();
            if (string.IsNullOrEmpty(paramName))
                return new { success = false, message = "'parameterName' is required" };

            var allParams = controller.parameters;
            int paramIndex = -1;
            for (int i = 0; i < allParams.Length; i++)
            {
                if (allParams[i].name == paramName) { paramIndex = i; break; }
            }
            if (paramIndex < 0)
                return new { success = false, message = $"Parameter '{paramName}' not found" };

            var changed = new List<string>();
            int referencesRewritten = 0;

            string newName = @params["newName"]?.ToString();
            if (!string.IsNullOrEmpty(newName) && newName != paramName)
            {
                for (int i = 0; i < allParams.Length; i++)
                {
                    if (i != paramIndex && allParams[i].name == newName)
                        return new { success = false, message = $"Parameter '{newName}' already exists" };
                }

                allParams[paramIndex].name = newName;
                controller.parameters = allParams;
                referencesRewritten = RewriteParameterReferences(controller, paramName, newName);
                paramName = newName;
                changed.Add("name");
            }

            string typeStr = @params["parameterType"]?.ToString()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(typeStr))
            {
                AnimatorControllerParameterType newType;
                switch (typeStr)
                {
                    case "float": newType = AnimatorControllerParameterType.Float; break;
                    case "int":
                    case "integer": newType = AnimatorControllerParameterType.Int; break;
                    case "bool": newType = AnimatorControllerParameterType.Bool; break;
                    case "trigger": newType = AnimatorControllerParameterType.Trigger; break;
                    default:
                        return new { success = false, message = $"Unknown parameterType '{typeStr}'. Valid: float, int, bool, trigger" };
                }
                if (allParams[paramIndex].type != newType)
                {
                    allParams[paramIndex].type = newType;
                    controller.parameters = allParams;
                    changed.Add("type");
                }
            }

            JToken defaultValueToken = @params["defaultValue"];
            if (defaultValueToken != null)
            {
                var p = allParams[paramIndex];
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float: p.defaultFloat = defaultValueToken.ToObject<float>(); break;
                    case AnimatorControllerParameterType.Int: p.defaultInt = defaultValueToken.ToObject<int>(); break;
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger: p.defaultBool = defaultValueToken.ToObject<bool>(); break;
                }
                allParams[paramIndex] = p;
                controller.parameters = allParams;
                changed.Add("defaultValue");
            }

            if (changed.Count == 0)
                return new { success = false, message = "No recognized properties provided. Supported: newName, parameterType, defaultValue" };

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Modified parameter '{paramName}': {string.Join(", ", changed)}",
                data = new
                {
                    parameterName = paramName,
                    modifiedProperties = changed,
                    referencesRewritten
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

            bool force = @params["force"]?.ToObject<bool>() ?? false;
            int refCount = CountParameterReferences(controller, paramName, strip: false);

            if (refCount > 0 && !force)
                return new
                {
                    success = false,
                    message = $"Cannot remove parameter '{paramName}': {refCount} reference(s) in transition conditions or state bindings. Pass `force: true` to strip references and remove, or use controller_modify_parameter to rename instead."
                };

            var warnings = new List<string>();
            if (refCount > 0)
            {
                CountParameterReferences(controller, paramName, strip: true);
                warnings.Add($"Stripped {refCount} reference(s) to parameter '{paramName}'");
            }

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
                    totalParameters = controller.parameters.Length,
                    strippedReferences = refCount,
                    warnings = warnings.ToArray()
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

            // Rename in place: preserves the AnimatorState sub-asset and its fileID,
            // so transitions and external refs (Timeline, animation events) survive.
            string newName = @params["newName"]?.ToString();
            if (!string.IsNullOrEmpty(newName))
            {
                targetState.name = newName;
                changed.Add("name");
            }

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
                return new { success = false, message = "No recognized state properties provided. Supported: newName, tag, speed, writeDefaultValues, iKOnFeet, mirror, cycleOffset, speedParameter, cycleOffsetParameter, mirrorParameter, timeParameter" };

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

            // For path-based toState like "Combat/Attack", match the leaf name
            string toLeafName = toStateName.Contains("/") ? toStateName.Substring(toStateName.LastIndexOf('/') + 1) : toStateName;

            // Find the target transition
            AnimatorStateTransition transition = null;
            if (isAnyState)
            {
                var matching = new List<AnimatorStateTransition>();
                foreach (var t in rootStateMachine.anyStateTransitions)
                {
                    if ((t.destinationState != null && t.destinationState.name == toLeafName) ||
                        (t.destinationStateMachine != null && t.destinationStateMachine.name == toLeafName))
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
                    if ((t.destinationState != null && t.destinationState.name == toLeafName) ||
                        (t.destinationStateMachine != null && t.destinationStateMachine.name == toLeafName))
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

            // Handle conditions replacement (validate before clearing so a bad call doesn't wipe existing conditions)
            if (@params["conditions"] is JArray conditionsArray)
            {
                var (parsedConditions, condError) = ParseConditions(controller, conditionsArray);
                if (condError != null) return condError;

                transition.conditions = new AnimatorCondition[0];
                foreach (var (mode, threshold, paramName) in parsedConditions)
                    transition.AddCondition(mode, threshold, paramName);

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

        public static object AddSubStateMachine(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string name = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return new { success = false, message = "'name' is required" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            // Resolve parent sub-state machine if specified
            string parentPath = @params["parentPath"]?.ToString();
            var parentMachine = rootStateMachine;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentMachine = ResolveStateMachinePath(rootStateMachine, parentPath);
                if (parentMachine == null)
                    return new { success = false, message = $"Parent sub-state machine path '{parentPath}' not found in layer {layerIndex}" };
            }

            // Duplicate check
            foreach (var csm in parentMachine.stateMachines)
            {
                if (csm.stateMachine.name == name)
                    return new { success = false, message = $"Sub-state machine '{name}' already exists in '{parentPath ?? "root"}'" };
            }

            parentMachine.AddStateMachine(name);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added sub-state machine '{name}' to {(string.IsNullOrEmpty(parentPath) ? "root" : $"'{parentPath}'")} in layer {layerIndex}",
                data = new
                {
                    name,
                    parentPath = parentPath ?? "",
                    layerIndex
                }
            };
        }

        public static object RemoveSubStateMachine(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string name = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return new { success = false, message = "'name' is required (e.g. 'Combat' or 'Combat/Melee')" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            AnimatorStateMachine parentMachine;
            var target = FindStateMachine(rootStateMachine, name, out parentMachine);
            if (target == null)
                return new { success = false, message = $"Sub-state machine '{name}' not found in layer {layerIndex}" };

            parentMachine.RemoveStateMachine(target);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed sub-state machine '{name}' from layer {layerIndex}",
                data = new
                {
                    name,
                    layerIndex
                }
            };
        }

        public static object ModifySubStateMachine(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            string name = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return new { success = false, message = "'name' is required (path to the sub-state machine, e.g. 'Combat' or 'Combat/Melee')" };

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range (controller has {controller.layers.Length} layers)" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            AnimatorStateMachine parentMachine;
            var target = FindStateMachine(rootStateMachine, name, out parentMachine);
            if (target == null)
                return new { success = false, message = $"Sub-state machine '{name}' not found in layer {layerIndex}" };

            var changed = new List<string>();

            // Rename
            string newName = @params["newName"]?.ToString();
            if (!string.IsNullOrEmpty(newName))
            {
                target.name = newName;
                changed.Add("name");
            }

            // Default state
            string defaultStateName = @params["defaultState"]?.ToString();
            if (!string.IsNullOrEmpty(defaultStateName))
            {
                var state = FindState(target, defaultStateName);
                if (state == null)
                    return new { success = false, message = $"State '{defaultStateName}' not found in sub-state machine '{name}'" };
                target.defaultState = state;
                changed.Add("defaultState");
            }

            // Position (requires struct array copy-modify-reassign)
            JToken posToken = @params["position"];
            if (posToken is JArray posArray && posArray.Count >= 2)
            {
                float x = posArray[0].ToObject<float>();
                float y = posArray[1].ToObject<float>();
                float z = posArray.Count >= 3 ? posArray[2].ToObject<float>() : 0f;

                var machines = parentMachine.stateMachines;
                for (int i = 0; i < machines.Length; i++)
                {
                    if (machines[i].stateMachine == target)
                    {
                        machines[i].position = new Vector3(x, y, z);
                        break;
                    }
                }
                parentMachine.stateMachines = machines;
                changed.Add("position");
            }

            if (changed.Count == 0)
                return new { success = false, message = "No recognized properties provided. Supported: newName, defaultState, position" };

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Modified {changed.Count} property(s) on sub-state machine '{name}': {string.Join(", ", changed)}",
                data = new
                {
                    name = target.name,
                    layerIndex,
                    modifiedProperties = changed
                }
            };
        }

        public static object AddEntryTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            // Resolve which state machine to add the entry transition on
            string smPath = @params["stateMachinePath"]?.ToString();
            var targetMachine = rootStateMachine;
            if (!string.IsNullOrEmpty(smPath))
            {
                targetMachine = ResolveStateMachinePath(rootStateMachine, smPath);
                if (targetMachine == null)
                    return new { success = false, message = $"Sub-state machine path '{smPath}' not found in layer {layerIndex}" };
            }

            string toStateName = @params["toState"]?.ToString();
            if (string.IsNullOrEmpty(toStateName))
                return new { success = false, message = "'toState' is required" };

            // Try to find as state first, then as sub-state machine
            var toState = FindState(targetMachine, toStateName);
            AnimatorStateMachine toStateMachine = null;
            if (toState == null)
            {
                toStateMachine = FindStateMachine(targetMachine, toStateName);
                if (toStateMachine == null)
                    return new { success = false, message = $"State or sub-state machine '{toStateName}' not found" };
            }
            else
            {
                var maybeSm = FindStateMachine(targetMachine, toStateName);
                if (maybeSm != null)
                    return new { success = false, message = $"'{toStateName}' is ambiguous: matches both a state and a sub-state machine. Use a path to disambiguate." };
            }

            // Validate conditions UP FRONT
            JToken conditionsToken = @params["conditions"];
            List<(AnimatorConditionMode mode, float threshold, string paramName)> parsedConditions = null;
            if (conditionsToken is JArray conditionsArray)
            {
                var (parsed, error) = ParseConditions(controller, conditionsArray);
                if (error != null) return error;
                parsedConditions = parsed;
            }

            AnimatorTransition transition = toState != null
                ? targetMachine.AddEntryTransition(toState)
                : targetMachine.AddEntryTransition(toStateMachine);

            int conditionCount = 0;
            if (parsedConditions != null)
            {
                foreach (var (mode, threshold, paramName) in parsedConditions)
                {
                    transition.AddCondition(mode, threshold, paramName);
                    conditionCount++;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added entry transition to '{toStateName}'{(string.IsNullOrEmpty(smPath) ? "" : $" in '{smPath}'")} with {conditionCount} conditions",
                data = new
                {
                    stateMachinePath = smPath ?? "",
                    toState = toStateName,
                    conditionCount
                }
            };
        }

        public static object RemoveEntryTransition(JObject @params)
        {
            var controller = LoadController(@params);
            if (controller == null)
                return ControllerNotFoundError(@params);

            int layerIndex = @params["layerIndex"]?.ToObject<int>() ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return new { success = false, message = $"Layer index {layerIndex} out of range" };

            var rootStateMachine = controller.layers[layerIndex].stateMachine;

            string smPath = @params["stateMachinePath"]?.ToString();
            var targetMachine = rootStateMachine;
            if (!string.IsNullOrEmpty(smPath))
            {
                targetMachine = ResolveStateMachinePath(rootStateMachine, smPath);
                if (targetMachine == null)
                    return new { success = false, message = $"Sub-state machine path '{smPath}' not found in layer {layerIndex}" };
            }

            string toStateName = @params["toState"]?.ToString();
            if (string.IsNullOrEmpty(toStateName))
                return new { success = false, message = "'toState' is required" };

            int? transitionIndex = @params["transitionIndex"]?.ToObject<int>();

            string toLeafName = toStateName.Contains("/") ? toStateName.Substring(toStateName.LastIndexOf('/') + 1) : toStateName;

            var matching = new List<AnimatorTransition>();
            foreach (var t in targetMachine.entryTransitions)
            {
                if ((t.destinationState != null && t.destinationState.name == toLeafName) ||
                    (t.destinationStateMachine != null && t.destinationStateMachine.name == toLeafName))
                    matching.Add(t);
            }

            if (matching.Count == 0)
                return new { success = false, message = $"No entry transition to '{toStateName}' found{(string.IsNullOrEmpty(smPath) ? "" : $" in '{smPath}'")} in layer {layerIndex}" };

            int removedCount;
            if (transitionIndex.HasValue)
            {
                if (transitionIndex.Value < 0 || transitionIndex.Value >= matching.Count)
                    return new { success = false, message = $"Transition index {transitionIndex.Value} out of range ({matching.Count} matching transitions)" };

                targetMachine.RemoveEntryTransition(matching[transitionIndex.Value]);
                removedCount = 1;
            }
            else
            {
                foreach (var t in matching)
                    targetMachine.RemoveEntryTransition(t);
                removedCount = matching.Count;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed {removedCount} entry transition(s) to '{toStateName}'{(string.IsNullOrEmpty(smPath) ? "" : $" in '{smPath}'")} in layer {layerIndex}",
                data = new
                {
                    stateMachinePath = smPath ?? "",
                    toState = toStateName,
                    removedCount
                }
            };
        }

        /// <summary>
        /// Resolves a slash-delimited path to a nested sub-state machine.
        /// E.g. "Combat/Melee" walks root → Combat → Melee.
        /// Returns null if any segment is not found.
        /// </summary>
        internal static AnimatorStateMachine ResolveStateMachinePath(AnimatorStateMachine root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;

            string[] segments = path.Split('/');
            var current = root;
            foreach (string seg in segments)
            {
                AnimatorStateMachine child = null;
                foreach (var csm in current.stateMachines)
                {
                    if (csm.stateMachine.name == seg)
                    {
                        child = csm.stateMachine;
                        break;
                    }
                }
                if (child == null)
                    return null;
                current = child;
            }
            return current;
        }

        /// <summary>
        /// Finds a sub-state machine by name or slash-delimited path.
        /// </summary>
        internal static AnimatorStateMachine FindStateMachine(AnimatorStateMachine root, string name)
        {
            return FindStateMachine(root, name, out _);
        }

        /// <summary>
        /// Finds a sub-state machine by name or slash-delimited path,
        /// returning its parent state machine.
        /// Falls back to dot-as-separator on miss (so 'Combat.Melee' resolves like 'Combat/Melee').
        /// </summary>
        internal static AnimatorStateMachine FindStateMachine(AnimatorStateMachine root, string name, out AnimatorStateMachine parentMachine)
        {
            var result = FindStateMachineLiteral(root, name, out parentMachine);
            if (result == null && CanRetryWithDotPath(name))
                result = FindStateMachineLiteral(root, name.Replace('.', '/'), out parentMachine);
            return result;
        }

        private static AnimatorStateMachine FindStateMachineLiteral(AnimatorStateMachine root, string name, out AnimatorStateMachine parentMachine)
        {
            if (string.IsNullOrEmpty(name))
            {
                parentMachine = null;
                return null;
            }

            if (name.Contains("/"))
            {
                int lastSlash = name.LastIndexOf('/');
                string parentPath = name.Substring(0, lastSlash);
                string leafName = name.Substring(lastSlash + 1);

                var parent = ResolveStateMachinePath(root, parentPath);
                if (parent == null)
                {
                    parentMachine = null;
                    return null;
                }

                foreach (var csm in parent.stateMachines)
                {
                    if (csm.stateMachine.name == leafName)
                    {
                        parentMachine = parent;
                        return csm.stateMachine;
                    }
                }

                parentMachine = null;
                return null;
            }

            // No slash — search direct children of root
            foreach (var csm in root.stateMachines)
            {
                if (csm.stateMachine.name == name)
                {
                    parentMachine = root;
                    return csm.stateMachine;
                }
            }

            // Recurse depth-first
            foreach (var csm in root.stateMachines)
            {
                var result = FindStateMachineLiteral(csm.stateMachine, name, out parentMachine);
                if (result != null)
                    return result;
            }

            parentMachine = null;
            return null;
        }

        // Only retry with '.' → '/' if the path contains a dot AND no slash —
        // otherwise the original was already a slash-form path or had no separator.
        private static bool CanRetryWithDotPath(string path)
            => !string.IsNullOrEmpty(path) && path.IndexOf('.') >= 0 && path.IndexOf('/') < 0;

        /// <summary>
        /// Finds a state by name in a state machine, supporting:
        /// - Path notation: "SubMachine/StateName" resolves the sub-state machine first.
        /// - Bare names: searches root states first, then recurses depth-first into sub-state machines.
        /// </summary>
        internal static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            return FindState(stateMachine, stateName, out _);
        }

        /// <summary>
        /// Finds a state by name and returns its parent state machine (needed for RemoveState).
        /// Supports path notation and depth-first recursion.
        /// Falls back to dot-as-separator on miss (so 'Sub.Inner' resolves like 'Sub/Inner').
        /// </summary>
        internal static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName, out AnimatorStateMachine parentMachine)
        {
            var result = FindStateLiteral(stateMachine, stateName, out parentMachine);
            if (result == null && CanRetryWithDotPath(stateName))
                result = FindStateLiteral(stateMachine, stateName.Replace('.', '/'), out parentMachine);
            return result;
        }

        private static AnimatorState FindStateLiteral(AnimatorStateMachine stateMachine, string stateName, out AnimatorStateMachine parentMachine)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                parentMachine = null;
                return null;
            }

            // Path notation: "SubMachine/StateName"
            if (stateName.Contains("/"))
            {
                int lastSlash = stateName.LastIndexOf('/');
                string smPath = stateName.Substring(0, lastSlash);
                string leafState = stateName.Substring(lastSlash + 1);

                var targetMachine = ResolveStateMachinePath(stateMachine, smPath);
                if (targetMachine == null)
                {
                    parentMachine = null;
                    return null;
                }

                foreach (var cs in targetMachine.states)
                {
                    if (cs.state.name == leafState)
                    {
                        parentMachine = targetMachine;
                        return cs.state;
                    }
                }

                parentMachine = null;
                return null;
            }

            // Bare name — search root states first (backwards compat)
            foreach (var cs in stateMachine.states)
            {
                if (cs.state.name == stateName)
                {
                    parentMachine = stateMachine;
                    return cs.state;
                }
            }

            // Recurse depth-first into sub-state machines (literal — wrapper handles dot-fallback once at the top)
            foreach (var csm in stateMachine.stateMachines)
            {
                var result = FindStateLiteral(csm.stateMachine, stateName, out parentMachine);
                if (result != null)
                    return result;
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

        // Parses a transition `conditions` array into a validated list, ready to AddCondition().
        // Validates: parameter exists, mode is known, mode is compatible with parameter type.
        // Returns (parsed, null) on success or (null, errorResponse) on failure.
        internal static (List<(AnimatorConditionMode mode, float threshold, string paramName)> parsed, object error)
            ParseConditions(AnimatorController controller, JArray conditionsArray)
        {
            var paramTypes = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
            foreach (var p in controller.parameters) paramTypes[p.name] = p.type;

            var parsed = new List<(AnimatorConditionMode, float, string)>();

            foreach (var condItem in conditionsArray)
            {
                if (condItem is not JObject condObj) continue;

                string paramName = condObj["parameter"]?.ToString();
                if (string.IsNullOrEmpty(paramName)) continue;

                if (!paramTypes.TryGetValue(paramName, out var paramType))
                    return (null, new { success = false, message = $"Condition references unknown parameter '{paramName}'. Add it via controller_add_parameter first." });

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
                    default:
                        return (null, new { success = false, message = $"Unknown condition mode '{modeStr}'. Valid: if, ifNot, equals, notEqual, greater, less" });
                }

                string typeError = CheckConditionModeForParam(mode, paramType, paramName);
                if (typeError != null)
                    return (null, new { success = false, message = typeError });

                parsed.Add((mode, threshold, paramName));
            }

            return (parsed, null);
        }

        // Rewrites all references to oldName → newName: transition conditions and
        // state-level parameter bindings (speed/cycleOffset/mirror/time).
        private static int RewriteParameterReferences(AnimatorController controller, string oldName, string newName)
        {
            int count = 0;
            foreach (var layer in controller.layers)
                count += RewriteParameterReferencesInMachine(layer.stateMachine, oldName, newName);
            return count;
        }

        private static int RewriteParameterReferencesInMachine(AnimatorStateMachine sm, string oldName, string newName)
        {
            int count = 0;

            foreach (var cs in sm.states)
            {
                var state = cs.state;
                if (state.speedParameter == oldName) { state.speedParameter = newName; count++; }
                if (state.cycleOffsetParameter == oldName) { state.cycleOffsetParameter = newName; count++; }
                if (state.mirrorParameter == oldName) { state.mirrorParameter = newName; count++; }
                if (state.timeParameter == oldName) { state.timeParameter = newName; count++; }

                foreach (var t in state.transitions)
                    count += RewriteConditionRefs(t, oldName, newName);
            }

            foreach (var t in sm.anyStateTransitions)
                count += RewriteConditionRefs(t, oldName, newName);

            foreach (var t in sm.entryTransitions)
                count += RewriteConditionRefs(t, oldName, newName);

            foreach (var sub in sm.stateMachines)
                count += RewriteParameterReferencesInMachine(sub.stateMachine, oldName, newName);

            return count;
        }

        private static int RewriteConditionRefs(AnimatorTransitionBase t, string oldName, string newName)
        {
            var conditions = t.conditions;
            int count = 0;
            bool any = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter == oldName)
                {
                    var c = conditions[i];
                    c.parameter = newName;
                    conditions[i] = c;
                    count++;
                    any = true;
                }
            }
            if (any) t.conditions = conditions;
            return count;
        }

        // Counts (and optionally strips) references to a parameter across the controller:
        // transition conditions (state-to-state, AnyState, entry) and state-level
        // parameter bindings (speed/cycleOffset/mirror/time).
        private static int CountParameterReferences(AnimatorController controller, string paramName, bool strip)
        {
            int count = 0;
            foreach (var layer in controller.layers)
                count += CountParameterReferencesInMachine(layer.stateMachine, paramName, strip);
            return count;
        }

        private static int CountParameterReferencesInMachine(AnimatorStateMachine sm, string paramName, bool strip)
        {
            int count = 0;

            foreach (var cs in sm.states)
            {
                var state = cs.state;

                if (state.speedParameterActive && state.speedParameter == paramName)
                {
                    count++;
                    if (strip) { state.speedParameterActive = false; state.speedParameter = ""; }
                }
                if (state.cycleOffsetParameterActive && state.cycleOffsetParameter == paramName)
                {
                    count++;
                    if (strip) { state.cycleOffsetParameterActive = false; state.cycleOffsetParameter = ""; }
                }
                if (state.mirrorParameterActive && state.mirrorParameter == paramName)
                {
                    count++;
                    if (strip) { state.mirrorParameterActive = false; state.mirrorParameter = ""; }
                }
                if (state.timeParameterActive && state.timeParameter == paramName)
                {
                    count++;
                    if (strip) { state.timeParameterActive = false; state.timeParameter = ""; }
                }

                foreach (var t in state.transitions)
                    count += CountAndMaybeStripConditions(t, paramName, strip);
            }

            foreach (var t in sm.anyStateTransitions)
                count += CountAndMaybeStripConditions(t, paramName, strip);

            foreach (var t in sm.entryTransitions)
                count += CountAndMaybeStripConditions(t, paramName, strip);

            foreach (var sub in sm.stateMachines)
                count += CountParameterReferencesInMachine(sub.stateMachine, paramName, strip);

            return count;
        }

        private static int CountAndMaybeStripConditions(AnimatorTransitionBase t, string paramName, bool strip)
        {
            int count = 0;
            foreach (var c in t.conditions)
                if (c.parameter == paramName) count++;
            if (count > 0 && strip)
            {
                var keepers = new List<AnimatorCondition>();
                foreach (var c in t.conditions)
                    if (c.parameter != paramName) keepers.Add(c);
                t.conditions = keepers.ToArray();
            }
            return count;
        }

        // Walks every state machine in every layer and removes transitions whose
        // destinationState == removedState. Unity's RemoveState only drops the state
        // itself; inbound transitions are left dangling otherwise.
        private static int CleanTransitionsToState(AnimatorController controller, AnimatorState removedState)
        {
            int removed = 0;
            foreach (var layer in controller.layers)
                removed += CleanTransitionsToStateInMachine(layer.stateMachine, removedState);
            return removed;
        }

        private static int CleanTransitionsToStateInMachine(AnimatorStateMachine sm, AnimatorState removedState)
        {
            int removed = 0;

            foreach (var cs in sm.states)
            {
                var toRemove = new List<AnimatorStateTransition>();
                foreach (var t in cs.state.transitions)
                    if (t.destinationState == removedState) toRemove.Add(t);
                foreach (var t in toRemove) { cs.state.RemoveTransition(t); removed++; }
            }

            var anyToRemove = new List<AnimatorStateTransition>();
            foreach (var t in sm.anyStateTransitions)
                if (t.destinationState == removedState) anyToRemove.Add(t);
            foreach (var t in anyToRemove) { sm.RemoveAnyStateTransition(t); removed++; }

            var entryToRemove = new List<AnimatorTransition>();
            foreach (var t in sm.entryTransitions)
                if (t.destinationState == removedState) entryToRemove.Add(t);
            foreach (var t in entryToRemove) { sm.RemoveEntryTransition(t); removed++; }

            foreach (var sub in sm.stateMachines)
                removed += CleanTransitionsToStateInMachine(sub.stateMachine, removedState);

            return removed;
        }

        private static string CheckConditionModeForParam(AnimatorConditionMode mode, AnimatorControllerParameterType paramType, string paramName)
        {
            switch (mode)
            {
                case AnimatorConditionMode.If:
                case AnimatorConditionMode.IfNot:
                    if (paramType != AnimatorControllerParameterType.Bool && paramType != AnimatorControllerParameterType.Trigger)
                        return $"Condition mode '{mode}' requires a Bool or Trigger parameter; '{paramName}' is {paramType}.";
                    break;
                case AnimatorConditionMode.Greater:
                case AnimatorConditionMode.Less:
                    if (paramType != AnimatorControllerParameterType.Float && paramType != AnimatorControllerParameterType.Int)
                        return $"Condition mode '{mode}' requires a Float or Int parameter; '{paramName}' is {paramType}.";
                    break;
                case AnimatorConditionMode.Equals:
                case AnimatorConditionMode.NotEqual:
                    if (paramType != AnimatorControllerParameterType.Int)
                        return $"Condition mode '{mode}' requires an Int parameter; '{paramName}' is {paramType}.";
                    break;
            }
            return null;
        }
    }
}
