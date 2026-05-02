using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MCPForUnity.Editor.Tools.Animation;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageAnimationTests
    {
        private const string TempRoot = "Assets/Temp/ManageAnimationTests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up scene objects
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.StartsWith("AnimTest_"))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            CleanupEmptyParentFolders(TempRoot);
        }

        // =============================================================================
        // Dispatch / Error Handling
        // =============================================================================

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var paramsObj = new JObject();
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Action is required"));
        }

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "bogus_action" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown action"));
        }

        [Test]
        public void HandleCommand_UnknownAnimatorAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "animator_nonexistent" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown animator action"));
        }

        [Test]
        public void HandleCommand_UnknownClipAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "clip_nonexistent" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown clip action"));
        }

        // =============================================================================
        // Animator: Get Info
        // =============================================================================

        [Test]
        public void AnimatorGetInfo_NoTarget_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "animator_get_info" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void AnimatorGetInfo_NoAnimator_ReturnsError()
        {
            var go = new GameObject("AnimTest_NoAnimator");
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_get_info",
                    ["target"] = "AnimTest_NoAnimator"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsFalse(result.Value<bool>("success"));
                Assert.That(result["message"].ToString(), Does.Contain("No Animator"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnimatorGetInfo_WithAnimator_ReturnsData()
        {
            var go = new GameObject("AnimTest_WithAnimator");
            go.AddComponent<Animator>();
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_get_info",
                    ["target"] = "AnimTest_WithAnimator"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                var data = result["data"] as JObject;
                Assert.IsNotNull(data);
                Assert.AreEqual("AnimTest_WithAnimator", data["gameObject"].ToString());
                Assert.IsNotNull(data["enabled"]);
                Assert.IsNotNull(data["speed"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Animator: Set Speed / Set Enabled
        // =============================================================================

        [Test]
        public void AnimatorSetSpeed_ChangesSpeed()
        {
            var go = new GameObject("AnimTest_Speed");
            var animator = go.AddComponent<Animator>();
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_set_speed",
                    ["target"] = "AnimTest_Speed",
                    ["speed"] = 2.5f
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                Assert.AreEqual(2.5f, animator.speed, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnimatorSetEnabled_DisablesAnimator()
        {
            var go = new GameObject("AnimTest_Enabled");
            var animator = go.AddComponent<Animator>();
            Assert.IsTrue(animator.enabled);
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_set_enabled",
                    ["target"] = "AnimTest_Enabled",
                    ["enabled"] = false
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                Assert.IsFalse(animator.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Clip: Create
        // =============================================================================

        [Test]
        public void ClipCreate_CreatesAsset()
        {
            string clipPath = $"{TempRoot}/TestClip_{Guid.NewGuid():N}.anim";

            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["clipPath"] = clipPath,
                ["length"] = 2.0f,
                ["loop"] = true
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip, "Clip asset should exist");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime, "Clip should be looping");
            Assert.AreEqual(2.0f, settings.stopTime, 0.01f);
        }

        [Test]
        public void ClipCreate_DuplicatePath_ReturnsError()
        {
            string clipPath = $"{TempRoot}/DuplicateClip.anim";

            // Create first
            var clip = new AnimationClip { name = "DuplicateClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            // Try to create again
            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["clipPath"] = clipPath,
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ClipCreate_MissingPath_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "clip_create" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("clipPath"));
        }

        // =============================================================================
        // Clip: Get Info
        // =============================================================================

        [Test]
        public void ClipGetInfo_ReturnsClipData()
        {
            string clipName = $"InfoClip_{Guid.NewGuid():N}";
            string clipPath = $"{TempRoot}/{clipName}.anim";
            var clip = new AnimationClip { name = clipName, frameRate = 30f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.stopTime = 1.5f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_get_info",
                ["clipPath"] = clipPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(clipName, data["name"].ToString());
            Assert.AreEqual(30f, data.Value<float>("frameRate"), 0.01f);
            Assert.IsTrue(data.Value<bool>("isLooping"));
        }

        [Test]
        public void ClipGetInfo_NotFound_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "clip_get_info",
                ["clipPath"] = "Assets/Nonexistent.anim"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        // =============================================================================
        // Clip: Add / Set Curve
        // =============================================================================

        [Test]
        public void ClipAddCurve_AddsKeyframes()
        {
            string clipPath = $"{TempRoot}/CurveClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "CurveClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_add_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.y",
                ["type"] = "Transform",
                ["keys"] = new JArray(
                    new JArray(0f, 0f),
                    new JArray(0.5f, 2f),
                    new JArray(1f, 0f)
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify curve was added
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            Assert.AreEqual("localPosition.y", bindings[0].propertyName);

            var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
            Assert.AreEqual(3, curve.length);
        }

        [Test]
        public void ClipSetCurve_ReplacesKeyframes()
        {
            string clipPath = $"{TempRoot}/SetCurveClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "SetCurveClip" };

            // Add initial curve
            var initialCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
            var binding = EditorCurveBinding.FloatCurve("", typeof(Transform), "localPosition.x");
            AnimationUtility.SetEditorCurve(clip, binding, initialCurve);

            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            // Replace with new keyframes
            var paramsObj = new JObject
            {
                ["action"] = "clip_set_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.x",
                ["type"] = "Transform",
                ["keys"] = new JArray(
                    new JArray(0f, 5f),
                    new JArray(2f, 10f)
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            Assert.AreEqual(2, curve.length);
            Assert.AreEqual(5f, curve.keys[0].value, 0.01f);
            Assert.AreEqual(10f, curve.keys[1].value, 0.01f);
        }

        [Test]
        public void ClipAddCurve_WithObjectFormat_ParsesKeyframes()
        {
            string clipPath = $"{TempRoot}/ObjFormatClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "ObjFormatClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_add_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.z",
                ["type"] = "Transform",
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = 0f, ["inTangent"] = 0f, ["outTangent"] = 1f },
                    new JObject { ["time"] = 1f, ["value"] = 5f }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
            Assert.AreEqual(2, curve.length);
            Assert.AreEqual(1f, curve.keys[0].outTangent, 0.01f);
        }

        [Test]
        public void ClipAddCurve_MissingKeys_ReturnsError()
        {
            string clipPath = $"{TempRoot}/NoKeysClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "NoKeysClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_add_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.y",
                ["type"] = "Transform",
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("keys"));
        }

        // =============================================================================
        // Clip: Assign
        // =============================================================================

        [Test]
        public void ClipAssign_AddsAnimationComponent()
        {
            string clipPath = $"{TempRoot}/AssignClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "AssignClip" };
            clip.legacy = true;
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var go = new GameObject("AnimTest_Assign");
            try
            {
                Assert.IsNull(go.GetComponent<UnityEngine.Animation>());
                Assert.IsNull(go.GetComponent<Animator>());

                var paramsObj = new JObject
                {
                    ["action"] = "clip_assign",
                    ["target"] = "AnimTest_Assign",
                    ["clipPath"] = clipPath
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                var anim = go.GetComponent<UnityEngine.Animation>();
                Assert.IsNotNull(anim, "Should have added Animation component");
                Assert.IsNotNull(anim.clip, "Should have assigned clip");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClipAssign_MissingClip_ReturnsError()
        {
            var go = new GameObject("AnimTest_AssignMissing");
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "clip_assign",
                    ["target"] = "AnimTest_AssignMissing",
                    ["clipPath"] = "Assets/Nonexistent.anim"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsFalse(result.Value<bool>("success"));
                Assert.That(result["message"].ToString(), Does.Contain("not found"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Parameter Normalization
        // =============================================================================

        [Test]
        public void HandleCommand_SnakeCaseParams_Normalized()
        {
            // Test that snake_case parameters like clip_path get normalized to clipPath
            string clipPath = $"{TempRoot}/SnakeCase_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["clip_path"] = clipPath,
                ["length"] = 1.0f
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip);
        }

        [Test]
        public void HandleCommand_PropertiesDict_Flattened()
        {
            // Test that properties dict is flattened into top-level params
            string clipPath = $"{TempRoot}/PropsFlat_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["properties"] = new JObject
                {
                    ["clipPath"] = clipPath,
                    ["length"] = 1.5f,
                    ["loop"] = true
                }
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime);
        }

        // =============================================================================
        // Controller: Dispatch
        // =============================================================================

        [Test]
        public void HandleCommand_UnknownControllerAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "controller_nonexistent" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown controller action"));
        }

        // =============================================================================
        // Controller: Create
        // =============================================================================

        [Test]
        public void ControllerCreate_CreatesAsset()
        {
            string controllerPath = $"{TempRoot}/TestController_{Guid.NewGuid():N}.controller";

            var paramsObj = new JObject
            {
                ["action"] = "controller_create",
                ["controllerPath"] = controllerPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsNotNull(controller, "Controller asset should exist");
        }

        [Test]
        public void ControllerCreate_DuplicatePath_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/DuplicateController.controller";

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_create",
                ["controllerPath"] = controllerPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ControllerCreate_MissingPath_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "controller_create" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("controllerPath"));
        }

        // =============================================================================
        // Controller: Add State
        // =============================================================================

        [Test]
        public void ControllerAddState_AddsState()
        {
            string controllerPath = $"{TempRoot}/StateController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var states = controller.layers[0].stateMachine.states;
            Assert.IsTrue(states.Any(s => s.state.name == "Walk"), "State 'Walk' should exist");
        }

        [Test]
        public void ControllerAddState_DuplicateName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/DupStateController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Idle");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Idle"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ControllerAddState_WithClip_AssignsMotion()
        {
            string controllerPath = $"{TempRoot}/MotionController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            string clipPath = $"{TempRoot}/MotionClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "MotionClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Run",
                ["clipPath"] = clipPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Run").state;
            Assert.IsNotNull(state.motion, "State should have motion assigned");
        }

        // =============================================================================
        // Controller: Add Transition
        // =============================================================================

        [Test]
        public void ControllerAddTransition_AddsTransition()
        {
            string controllerPath = $"{TempRoot}/TransController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["hasExitTime"] = false,
                ["duration"] = 0.1f
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(1, idleState.transitions.Length);
            Assert.AreEqual("Walk", idleState.transitions[0].destinationState.name);
            Assert.IsFalse(idleState.transitions[0].hasExitTime);
        }

        [Test]
        public void ControllerAddTransition_WithConditions_AddsConditions()
        {
            string controllerPath = $"{TempRoot}/CondController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray(
                    new JObject
                    {
                        ["parameter"] = "Speed",
                        ["mode"] = "greater",
                        ["threshold"] = 0.1f
                    }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(1, idleState.transitions[0].conditions.Length);
            Assert.AreEqual("Speed", idleState.transitions[0].conditions[0].parameter);
        }

        [Test]
        public void ControllerAddTransition_MissingState_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/MissStateController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Idle");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Nonexistent"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        // =============================================================================
        // Controller: Add Parameter
        // =============================================================================

        [Test]
        public void ControllerAddParameter_AddsParameter()
        {
            string controllerPath = $"{TempRoot}/ParamController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed",
                ["parameterType"] = "float",
                ["defaultValue"] = 1.5f
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsTrue(controller.parameters.Any(p => p.name == "Speed"), "Parameter 'Speed' should exist");
            var param = controller.parameters.First(p => p.name == "Speed");
            Assert.AreEqual(AnimatorControllerParameterType.Float, param.type);
            Assert.AreEqual(1.5f, param.defaultFloat, 0.01f);
        }

        [Test]
        public void ControllerAddParameter_DuplicateName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/DupParamController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed",
                ["parameterType"] = "float"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ControllerAddParameter_AllTypes()
        {
            string controllerPath = $"{TempRoot}/AllTypesController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            string[] types = { "float", "int", "bool", "trigger" };
            foreach (var t in types)
            {
                var paramsObj = new JObject
                {
                    ["action"] = "controller_add_parameter",
                    ["controllerPath"] = controllerPath,
                    ["parameterName"] = $"Param_{t}",
                    ["parameterType"] = t
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), $"Failed for type {t}: {result}");
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual(4, controller.parameters.Length);
        }

        // =============================================================================
        // Controller: Get Info
        // =============================================================================

        [Test]
        public void ControllerGetInfo_ReturnsData()
        {
            string controllerPath = $"{TempRoot}/InfoController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_get_info",
                ["controllerPath"] = controllerPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.Value<int>("parameterCount"));
            Assert.AreEqual(1, data.Value<int>("layerCount"));

            var layers = data["layers"] as JArray;
            Assert.IsNotNull(layers);
            Assert.AreEqual(1, layers.Count);
        }

        [Test]
        public void ControllerGetInfo_NotFound_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "controller_get_info",
                ["controllerPath"] = "Assets/Nonexistent.controller"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        // =============================================================================
        // Controller: Assign
        // =============================================================================

        [Test]
        public void ControllerAssign_AddsAnimatorAndAssigns()
        {
            string controllerPath = $"{TempRoot}/AssignController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var go = new GameObject("AnimTest_ControllerAssign");
            try
            {
                Assert.IsNull(go.GetComponent<Animator>());

                var paramsObj = new JObject
                {
                    ["action"] = "controller_assign",
                    ["controllerPath"] = controllerPath,
                    ["target"] = "AnimTest_ControllerAssign"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                var animator = go.GetComponent<Animator>();
                Assert.IsNotNull(animator, "Should have added Animator component");
                Assert.IsNotNull(animator.runtimeAnimatorController, "Should have assigned controller");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Controller: Set State Motion
        // =============================================================================

        [Test]
        public void ControllerSetStateMotion_SetsClip()
        {
            string controllerPath = $"{TempRoot}/SetMotionController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Walk");
            AssetDatabase.SaveAssets();

            string clipPath = $"{TempRoot}/WalkClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "WalkClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_set_state_motion",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk",
                ["clipPath"] = clipPath
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Walk").state;
            Assert.IsNotNull(state.motion, "State should have motion assigned");
        }

        [Test]
        public void ControllerSetStateMotion_ClearsMotion()
        {
            string controllerPath = $"{TempRoot}/ClearMotionController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Walk");

            string clipPath = $"{TempRoot}/ClearClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "ClearClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            state.motion = clip;
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_set_state_motion",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Walk").state;
            Assert.IsNull(state.motion, "State motion should be cleared");
        }

        [Test]
        public void ControllerSetStateMotion_ReplacesExistingMotion()
        {
            string controllerPath = $"{TempRoot}/ReplaceMotionController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Walk");

            string clipPath1 = $"{TempRoot}/OldClip_{Guid.NewGuid():N}.anim";
            var clip1 = new AnimationClip { name = "OldClip" };
            AssetDatabase.CreateAsset(clip1, clipPath1);
            state.motion = clip1;

            string clipPath2 = $"{TempRoot}/NewClip_{Guid.NewGuid():N}.anim";
            var clip2 = new AnimationClip { name = "NewClip" };
            AssetDatabase.CreateAsset(clip2, clipPath2);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_set_state_motion",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk",
                ["clipPath"] = clipPath2
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Walk").state;
            Assert.AreEqual("NewClip", state.motion.name);
        }

        [Test]
        public void ControllerSetStateMotion_StateNotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/SetMotionNotFound_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_set_state_motion",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "NonExistent"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        [Test]
        public void ControllerSetStateMotion_ClipNotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/SetMotionBadClip_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Walk");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_set_state_motion",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk",
                ["clipPath"] = "Assets/NonExistent/Fake.anim"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Motion not found"));
        }

        // =============================================================================
        // Controller: Remove State
        // =============================================================================

        [Test]
        public void ControllerRemoveState_RemovesState()
        {
            string controllerPath = $"{TempRoot}/RemoveStateController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var states = controller.layers[0].stateMachine.states;
            Assert.IsFalse(states.Any(s => s.state.name == "Walk"), "Walk should be removed");
            Assert.IsTrue(states.Any(s => s.state.name == "Idle"), "Idle should remain");
        }

        [Test]
        public void ControllerRemoveState_StateNotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/RemoveStateNotFound_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "NonExistent"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        [Test]
        public void ControllerRemoveState_WithTransitions_CleansUp()
        {
            string controllerPath = $"{TempRoot}/RemoveStateTransitions_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            idle.AddTransition(walk);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(0, idleState.transitions.Length, "Transitions to removed state should be cleaned up");
        }

        [Test]
        public void ControllerRemoveState_MissingName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/RemoveStateMissing_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_state",
                ["controllerPath"] = controllerPath
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("stateName"));
        }

        // =============================================================================
        // Controller: Remove Transition
        // =============================================================================

        [Test]
        public void ControllerRemoveTransition_RemovesTransition()
        {
            string controllerPath = $"{TempRoot}/RemoveTransController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            idle.AddTransition(walk);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(0, idleState.transitions.Length);
        }

        [Test]
        public void ControllerRemoveTransition_AnyState_RemovesTransition()
        {
            string controllerPath = $"{TempRoot}/RemoveAnyTrans_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var walk = sm.AddState("Walk");
            sm.AddAnyStateTransition(walk);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "AnyState",
                ["toState"] = "Walk"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual(0, controller.layers[0].stateMachine.anyStateTransitions.Length);
        }

        [Test]
        public void ControllerRemoveTransition_MultipleTransitions_RemovesAll()
        {
            string controllerPath = $"{TempRoot}/RemoveMultiTrans_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            idle.AddTransition(walk);
            idle.AddTransition(walk);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(2, idle.transitions.Length, "Should have 2 transitions before removal");

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(2, result["data"]["removedCount"].Value<int>());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(0, idleState.transitions.Length);
        }

        [Test]
        public void ControllerRemoveTransition_WithIndex_RemovesSpecific()
        {
            string controllerPath = $"{TempRoot}/RemoveIndexTrans_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            idle.AddTransition(walk);
            idle.AddTransition(walk);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["transitionIndex"] = 0
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, result["data"]["removedCount"].Value<int>());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(1, idleState.transitions.Length, "Should have 1 transition remaining");
        }

        [Test]
        public void ControllerRemoveTransition_NotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/RemoveTransNotFound_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("No transition"));
        }

        // =============================================================================
        // Controller: Remove Parameter
        // =============================================================================

        [Test]
        public void ControllerRemoveParameter_RemovesParameter()
        {
            string controllerPath = $"{TempRoot}/RemoveParamController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, controller.parameters.Length);

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual(0, controller.parameters.Length);
        }

        [Test]
        public void ControllerRemoveParameter_NotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/RemoveParamNotFound_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "NonExistent"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        [Test]
        public void ControllerRemoveParameter_MissingName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/RemoveParamMissing_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_parameter",
                ["controllerPath"] = controllerPath
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("parameterName"));
        }

        // =============================================================================
        // Controller: Modify State
        // =============================================================================

        [Test]
        public void ControllerModifyState_SetsTag()
        {
            string controllerPath = $"{TempRoot}/ModifyStateTag_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Attack");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Attack",
                ["tag"] = "Combat"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Attack").state;
            Assert.AreEqual("Combat", state.tag);
        }

        [Test]
        public void ControllerModifyState_SetsMultipleProperties()
        {
            string controllerPath = $"{TempRoot}/ModifyStateMulti_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Walk");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk",
                ["tag"] = "Locomotion",
                ["speed"] = 1.5f,
                ["writeDefaultValues"] = false
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Walk").state;
            Assert.AreEqual("Locomotion", state.tag);
            Assert.AreEqual(1.5f, state.speed, 0.001f);
            Assert.IsFalse(state.writeDefaultValues);
        }

        [Test]
        public void ControllerModifyState_ClearsTag()
        {
            string controllerPath = $"{TempRoot}/ModifyStateClear_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Attack");
            state.tag = "Combat";
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Attack",
                ["tag"] = ""
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Attack").state;
            Assert.AreEqual("", state.tag);
        }

        [Test]
        public void ControllerModifyState_StateNotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/ModifyStateNotFound_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "NonExistent",
                ["tag"] = "Foo"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        [Test]
        public void ControllerModifyState_MissingName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/ModifyStateMissing_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["tag"] = "Foo"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("stateName"));
        }

        // =============================================================================
        // Controller: Modify Transition
        // =============================================================================

        [Test]
        public void ControllerModifyTransition_ModifiesProperties()
        {
            string controllerPath = $"{TempRoot}/ModifyTransProps_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            idle.AddTransition(walk);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["hasExitTime"] = false,
                ["duration"] = 0.1f
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            var t = idleState.transitions[0];
            Assert.IsFalse(t.hasExitTime);
            Assert.AreEqual(0.1f, t.duration, 0.001f);
        }

        [Test]
        public void ControllerModifyTransition_ReplacesConditions()
        {
            string controllerPath = $"{TempRoot}/ModifyTransCond_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            var t = idle.AddTransition(walk);
            t.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Speed");
            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, idle.transitions[0].conditions.Length);

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray
                {
                    new JObject { ["parameter"] = "Grounded", ["mode"] = "if" }
                }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(1, idleState.transitions[0].conditions.Length);
            Assert.AreEqual("Grounded", idleState.transitions[0].conditions[0].parameter);
        }

        [Test]
        public void ControllerModifyTransition_ClearsConditions()
        {
            string controllerPath = $"{TempRoot}/ModifyTransClear_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var t = idle.AddTransition(walk);
            t.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Speed");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray()
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(0, idleState.transitions[0].conditions.Length);
        }

        [Test]
        public void ControllerModifyTransition_AnyState_Modifies()
        {
            string controllerPath = $"{TempRoot}/ModifyTransAny_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var walk = sm.AddState("Walk");
            var t = sm.AddAnyStateTransition(walk);
            t.duration = 0.5f;
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "AnyState",
                ["toState"] = "Walk",
                ["duration"] = 0.05f
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual(0.05f, controller.layers[0].stateMachine.anyStateTransitions[0].duration, 0.001f);
        }

        [Test]
        public void ControllerModifyTransition_NotFound_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/ModifyTransNotFound_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["duration"] = 0.1f
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("No transition"));
        }

        // =============================================================================
        // Controller: GetInfo Extended Properties
        // =============================================================================

        [Test]
        public void ControllerGetInfo_IncludesExtendedProperties()
        {
            string controllerPath = $"{TempRoot}/GetInfoExtended_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Attack");
            state.tag = "Combat";
            state.writeDefaultValues = false;
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_get_info",
                ["controllerPath"] = controllerPath
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var stateData = result["data"]["layers"][0]["states"][0];
            Assert.AreEqual("Combat", stateData["tag"].ToString());
            Assert.IsFalse(stateData["writeDefaultValues"].Value<bool>());
        }

        // =============================================================================
        // Clip: Set Vector Curve
        // =============================================================================

        [Test]
        public void ClipSetVectorCurve_Sets3Curves()
        {
            string clipPath = $"{TempRoot}/VectorClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "VectorClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_set_vector_curve",
                ["clipPath"] = clipPath,
                ["property"] = "localPosition",
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = new JArray(0f, 1f, -10f) },
                    new JObject { ["time"] = 1f, ["value"] = new JArray(2f, 1f, -10f) }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            // clip.SetCurve doesn't populate EditorCurve bindings — it uses legacy runtime curves
            // Verify via the data response
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(2, data.Value<int>("keyframeCount"));
            var curves = data["curves"] as JArray;
            Assert.IsNotNull(curves);
            Assert.AreEqual(3, curves.Count);
        }

        [Test]
        public void ClipSetVectorCurve_MissingProperty_ReturnsError()
        {
            string clipPath = $"{TempRoot}/NoPropertyClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "NoPropertyClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_set_vector_curve",
                ["clipPath"] = clipPath,
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = new JArray(0f, 0f, 0f) }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("property"));
        }

        [Test]
        public void ClipSetVectorCurve_InvalidValueFormat_ReturnsError()
        {
            string clipPath = $"{TempRoot}/BadValueClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "BadValueClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_set_vector_curve",
                ["clipPath"] = clipPath,
                ["property"] = "localPosition",
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = new JArray(0f, 1f) } // Only 2 elements
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("3-element"));
        }

        // =============================================================================
        // Clip: Create Preset
        // =============================================================================

        [Test]
        public void ClipCreatePreset_Bounce_CreatesClip()
        {
            string clipPath = $"{TempRoot}/BouncePreset_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath,
                ["preset"] = "bounce",
                ["duration"] = 2.0f,
                ["amplitude"] = 0.5f,
                ["loop"] = true
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip, "Bounce preset clip should exist");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime, "Should be looping");
        }

        [Test]
        public void ClipCreatePreset_AllPresetsCreateSuccessfully()
        {
            string[] presets = { "bounce", "rotate", "pulse", "fade", "shake", "hover", "spin" };
            foreach (var preset in presets)
            {
                string clipPath = $"{TempRoot}/{preset}Preset_{Guid.NewGuid():N}.anim";
                var paramsObj = new JObject
                {
                    ["action"] = "clip_create_preset",
                    ["clipPath"] = clipPath,
                    ["preset"] = preset
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), $"Preset '{preset}' failed: {result}");

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                Assert.IsNotNull(clip, $"Clip for preset '{preset}' should exist");
            }
        }

        [Test]
        public void ClipCreatePreset_InvalidPreset_ReturnsError()
        {
            string clipPath = $"{TempRoot}/BadPreset_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath,
                ["preset"] = "nonexistent"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown preset"));
        }

        [Test]
        public void ClipCreatePreset_MissingPreset_ReturnsError()
        {
            string clipPath = $"{TempRoot}/NoPreset_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("preset"));
        }

        [Test]
        public void ClipCreatePreset_DuplicatePath_ReturnsError()
        {
            string clipPath = $"{TempRoot}/ExistingPreset.anim";
            var clip = new AnimationClip { name = "ExistingPreset" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath,
                ["preset"] = "bounce"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        // =============================================================================
        // Blend Tree - Nested Child Trees
        // =============================================================================

        [Test]
        public void AddBlendTreeChildTree_1D_CreatesNestedTree()
        {
            string controllerPath = $"{TempRoot}/BT_Nested1D_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            // Create a 1D blend tree state
            var createResult = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_create_blend_tree_1d",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["blendParameter"] = "Speed"
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            // Add a nested 1D child blend tree
            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_blend_tree_child_tree",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["childTreeName"] = "WalkBlend",
                ["childBlendType"] = "1d",
                ["childBlendParameter"] = "Direction",
                ["threshold"] = 0.5f
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify the nested structure
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states[0].state;
            Assert.IsTrue(state.motion is BlendTree);
            var parentTree = (BlendTree)state.motion;
            Assert.AreEqual(1, parentTree.children.Length);
            Assert.IsTrue(parentTree.children[0].motion is BlendTree);
            var childTree = (BlendTree)parentTree.children[0].motion;
            Assert.AreEqual("WalkBlend", childTree.name);
            Assert.AreEqual(BlendTreeType.Simple1D, childTree.blendType);
        }

        [Test]
        public void AddBlendTreeChildTree_2D_CreatesNestedTree()
        {
            string controllerPath = $"{TempRoot}/BT_Nested2D_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("VelX", AnimatorControllerParameterType.Float);
            controller.AddParameter("VelZ", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            // Create a 2D blend tree state
            var createResult = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_create_blend_tree_2d",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Movement",
                ["blendParameterX"] = "VelX",
                ["blendParameterY"] = "VelZ"
            }));
            Assert.IsTrue(createResult.Value<bool>("success"), createResult.ToString());

            // Add a nested 2D child blend tree
            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_blend_tree_child_tree",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Movement",
                ["childTreeName"] = "DirectionBlend",
                ["childBlendType"] = "freeformdirectional2d",
                ["childBlendParameterX"] = "VelX",
                ["childBlendParameterY"] = "VelZ",
                ["position"] = new JArray(0f, 1f)
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states[0].state;
            var parentTree = (BlendTree)state.motion;
            Assert.AreEqual(1, parentTree.children.Length);
            var childTree = (BlendTree)parentTree.children[0].motion;
            Assert.AreEqual("DirectionBlend", childTree.name);
            Assert.AreEqual(BlendTreeType.FreeformDirectional2D, childTree.blendType);
        }

        [Test]
        public void AddBlendTreeChild_WithSlashPath_AddsClipToNestedTree()
        {
            string controllerPath = $"{TempRoot}/BT_SlashPath_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            // Create 1D blend tree state
            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_create_blend_tree_1d",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["blendParameter"] = "Speed"
            });

            // Add nested child tree
            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_blend_tree_child_tree",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["childTreeName"] = "WalkBlend",
                ["childBlendType"] = "1d",
                ["childBlendParameter"] = "Direction",
                ["threshold"] = 0.5f
            });

            // Create a clip to add
            string clipPath = $"{TempRoot}/TestClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "WalkLeft" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            // Add clip to the nested tree using slash path
            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_blend_tree_child",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion/WalkBlend",
                ["clipPath"] = clipPath,
                ["threshold"] = -1.0f
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify clip is on the nested tree, not the root
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var rootTree = (BlendTree)controller.layers[0].stateMachine.states[0].state.motion;
            var childTree = (BlendTree)rootTree.children[0].motion;
            Assert.AreEqual(1, childTree.children.Length);
            Assert.IsTrue(childTree.children[0].motion is AnimationClip);
        }

        [Test]
        public void AddBlendTreeChildTree_MissingChildTreeName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/BT_MissingName_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_create_blend_tree_1d",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["blendParameter"] = "Speed"
            });

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_blend_tree_child_tree",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["childBlendType"] = "1d",
                ["childBlendParameter"] = "Speed",
                ["threshold"] = 0.5f
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("childTreeName"));
        }

        [Test]
        public void AddBlendTreeChildTree_InvalidPath_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/BT_InvalidPath_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_create_blend_tree_1d",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion",
                ["blendParameter"] = "Speed"
            });

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_blend_tree_child_tree",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion/NonExistent",
                ["childTreeName"] = "Test",
                ["childBlendType"] = "1d",
                ["childBlendParameter"] = "Speed",
                ["threshold"] = 0.5f
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        // =============================================================================
        // Fail-loud validation: condition modes (B1, B2)
        // =============================================================================

        private static AnimatorController CreateControllerWithStatesAndParam(
            string controllerPath, string paramName, AnimatorControllerParameterType paramType)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter(paramName, paramType);
            controller.layers[0].stateMachine.AddState("Idle");
            controller.layers[0].stateMachine.AddState("Walk");
            AssetDatabase.SaveAssets();
            return controller;
        }

        [Test]
        public void AddTransition_UnknownConditionMode_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/CondMode_Unknown_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Speed", ["mode"] = "bogus", ["threshold"] = 0.5f } }
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown condition mode"));
        }

        [Test]
        public void AddTransition_GreaterModeOnBoolParam_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/CondMode_BoolGreater_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Jumping", AnimatorControllerParameterType.Bool);

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Jumping", ["mode"] = "greater", ["threshold"] = 0.5f } }
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Bool"));
        }

        [Test]
        public void AddTransition_IfModeOnFloatParam_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/CondMode_FloatIf_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Speed", ["mode"] = "if" } }
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Bool or Trigger"));
        }

        [Test]
        public void AddTransition_UnknownParameter_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/CondParam_Unknown_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Ghost", ["mode"] = "greater", ["threshold"] = 0.5f } }
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("unknown parameter"));
        }

        [Test]
        public void AddTransition_BadCondition_DoesNotCreateTransition()
        {
            string controllerPath = $"{TempRoot}/CondAtomic_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);

            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Ghost", ["mode"] = "greater", ["threshold"] = 0.5f } }
            });

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idle = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(0, idle.transitions.Length, "transition must not be created when conditions fail validation");
        }

        // =============================================================================
        // RemoveState integrity (B3)
        // =============================================================================

        [Test]
        public void RemoveState_CleansInboundTransitions()
        {
            string controllerPath = $"{TempRoot}/RemoveState_Inbound_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var a = sm.AddState("A");
            var b = sm.AddState("B");
            a.AddTransition(b);
            sm.AddAnyStateTransition(b);
            sm.AddEntryTransition(b);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "B"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(3, result["data"].Value<int>("removedTransitions"));

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var aState = controller.layers[0].stateMachine.states.First(s => s.state.name == "A").state;
            Assert.AreEqual(0, aState.transitions.Length);
            Assert.AreEqual(0, controller.layers[0].stateMachine.anyStateTransitions.Length);
            Assert.AreEqual(0, controller.layers[0].stateMachine.entryTransitions.Length);
        }

        [Test]
        public void RemoveState_ReassignsDefaultState()
        {
            string controllerPath = $"{TempRoot}/RemoveState_Default_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var a = sm.AddState("A");
            sm.AddState("B");
            sm.defaultState = a;
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "A"
            }));
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.IsTrue(result["data"].Value<bool>("defaultStateReassigned"));

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsNotNull(controller.layers[0].stateMachine.defaultState);
            Assert.AreEqual("B", controller.layers[0].stateMachine.defaultState.name);
        }

        // =============================================================================
        // RemoveParameter reference scan + force (B4)
        // =============================================================================

        [Test]
        public void RemoveParameter_WithReferences_BlocksWithoutForce()
        {
            string controllerPath = $"{TempRoot}/RemovePar_Block_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);
            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Speed", ["mode"] = "greater", ["threshold"] = 0.1f } }
            });

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("force"));
        }

        [Test]
        public void RemoveParameter_WithForce_StripsReferences()
        {
            string controllerPath = $"{TempRoot}/RemovePar_Force_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);
            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Speed", ["mode"] = "greater", ["threshold"] = 0.1f } }
            });

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_remove_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed",
                ["force"] = true
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, result["data"].Value<int>("strippedReferences"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual(0, controller.parameters.Length);
            var idle = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(0, idle.transitions[0].conditions.Length);
        }

        // =============================================================================
        // AddState scope + AnyState ambiguity (B5)
        // =============================================================================

        [Test]
        public void AddState_SameNameInSiblingSubMachine_Allowed()
        {
            string controllerPath = $"{TempRoot}/AddState_Sibling_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var root = controller.layers[0].stateMachine;
            var sub1 = root.AddStateMachine("Sub1");
            sub1.AddState("Idle");
            root.AddStateMachine("Sub2");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Sub2/Idle"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void AddTransition_AnyState_AmbiguousTarget_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/Ambig_AnyState_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Combat");
            sm.AddStateMachine("Combat");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "AnyState",
                ["toState"] = "Combat"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("ambiguous"));
        }

        // =============================================================================
        // Path notation '.' tolerance (C)
        // =============================================================================

        [Test]
        public void FindState_DotPath_ResolvesAsSlashPath()
        {
            string controllerPath = $"{TempRoot}/DotPath_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var root = controller.layers[0].stateMachine;
            var sub = root.AddStateMachine("Locomotion");
            sub.AddState("Walk");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Locomotion.Walk",
                ["speed"] = 2.0f
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var walk = controller.layers[0].stateMachine.stateMachines[0].stateMachine.states[0].state;
            Assert.AreEqual(2.0f, walk.speed);
        }

        // =============================================================================
        // Identity-preserving rename (D1, D2, D3)
        // =============================================================================

        [Test]
        public void ModifyState_NewName_PreservesAssetIdentity()
        {
            string controllerPath = $"{TempRoot}/ModifyState_Rename_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var s = sm.AddState("Idle");
            int originalId = s.GetInstanceID();
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Idle",
                ["newName"] = "IdleStanding"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var renamed = controller.layers[0].stateMachine.states[0].state;
            Assert.AreEqual("IdleStanding", renamed.name);
            Assert.AreEqual(originalId, renamed.GetInstanceID(), "AnimatorState sub-asset identity must be preserved across rename");
        }

        [Test]
        public void ModifyParameter_NewName_RewritesConditionRefs()
        {
            string controllerPath = $"{TempRoot}/ModifyPar_Rename_{Guid.NewGuid():N}.controller";
            CreateControllerWithStatesAndParam(controllerPath, "Speed", AnimatorControllerParameterType.Float);
            ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray { new JObject { ["parameter"] = "Speed", ["mode"] = "greater", ["threshold"] = 0.1f } }
            });

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_modify_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed",
                ["newName"] = "Velocity"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, result["data"].Value<int>("referencesRewritten"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual("Velocity", controller.parameters[0].name);
            var idle = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual("Velocity", idle.transitions[0].conditions[0].parameter);
        }

        [Test]
        public void RenameLayer_RenamesByIndex()
        {
            string controllerPath = $"{TempRoot}/RenameLayer_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddLayer("Upper");
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAnimation.HandleCommand(new JObject
            {
                ["action"] = "controller_rename_layer",
                ["controllerPath"] = controllerPath,
                ["layerIndex"] = 1,
                ["newName"] = "UpperBody"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual("UpperBody", controller.layers[1].name);
        }
    }
}
