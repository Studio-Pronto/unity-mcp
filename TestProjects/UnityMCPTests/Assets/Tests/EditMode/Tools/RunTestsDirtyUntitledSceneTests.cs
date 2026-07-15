using System;
using System.Reflection;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for run_tests' dirty-untitled-scene fail-fast gate. A dirty untitled (pathless)
    /// scene would make UTF's SaveModifiedSceneTask pop a blocking native Save dialog, wedging
    /// unattended editors; run_tests must refuse (or explicitly discard) instead of starting.
    /// No test here starts a real run: the no-flag path returns before StartJob, the discard
    /// path is exercised against a simulated running job, and the discard mechanics are called
    /// directly via the internal helpers (InternalsVisibleTo).
    /// </summary>
    public class RunTestsDirtyUntitledSceneTests
    {
        private FieldInfo _currentJobIdField;
        private string _originalJobId;

        [SetUp]
        public void SetUp()
        {
            var managerType = typeof(MCPServiceLocator).Assembly.GetType("MCPForUnity.Editor.Services.TestJobManager");
            Assert.NotNull(managerType, "Could not find TestJobManager");
            _currentJobIdField = managerType.GetField("_currentJobId", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(_currentJobIdField, "Could not find _currentJobId field");
            _originalJobId = _currentJobIdField.GetValue(null) as string;
        }

        [TearDown]
        public void TearDown()
        {
            _currentJobIdField.SetValue(null, _originalJobId);
            // Replace whatever scene state a test created so dirt never leaks into the suite.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void RunTests_DirtyUntitledScene_FailsFastWithStructuredError()
        {
            MakeActiveSceneDirtyUntitled();

            var result = RunTests.HandleCommand(new JObject { ["mode"] = "EditMode" }).Result;
            var r = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(r.Value<bool>("success"), r.ToString());
            Assert.AreEqual("unsaved_untitled_scene", r.Value<string>("error"));
            var scenes = r["data"]?["scenes"] as JArray;
            Assert.IsNotNull(scenes, "error data should list the offending scenes");
            Assert.GreaterOrEqual(scenes.Count, 1);
        }

        [Test]
        public void RunTests_DiscardFlagWhileJobRunning_RefusesWithoutDiscarding()
        {
            MakeActiveSceneDirtyUntitled();
            _currentJobIdField.SetValue(null, "fake-running-job");

            var result = RunTests.HandleCommand(new JObject
            {
                ["mode"] = "EditMode",
                ["discard_untitled_scenes"] = true
            }).Result;
            var r = result as JObject ?? JObject.FromObject(result);

            Assert.IsFalse(r.Value<bool>("success"), r.ToString());
            Assert.AreEqual("tests_running", r.Value<string>("error"));
            Assert.IsTrue(SceneManager.GetActiveScene().isDirty,
                "scene must not be discarded for a run that cannot start");
        }

        [Test]
        public void CollectDirtyUntitledScenes_CleanUntitledScene_ReturnsEmpty()
        {
            // UTF parks editors on clean untitled scenes between runs; the gate must not block those.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Assert.IsEmpty(RunTests.CollectDirtyUntitledScenes());
        }

        [Test]
        public void DiscardDirtyUntitledScenes_ReplacesLastUntitledScene()
        {
            MakeActiveSceneDirtyUntitled();

            RunTests.DiscardDirtyUntitledScenes(RunTests.CollectDirtyUntitledScenes());

            Assert.IsEmpty(RunTests.CollectDirtyUntitledScenes());
            Assert.IsFalse(SceneManager.GetActiveScene().isDirty);
        }

        [Test]
        public void SaveDirtyScenesIfNeeded_DirtyUntitledScene_Throws()
        {
            MakeActiveSceneDirtyUntitled();

            var serviceType = typeof(MCPServiceLocator).Assembly.GetType("MCPForUnity.Editor.Services.TestRunnerService");
            Assert.NotNull(serviceType, "Could not find TestRunnerService");
            var method = serviceType.GetMethod("SaveDirtyScenesIfNeeded", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method, "Could not find SaveDirtyScenesIfNeeded");

            var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, null));
            Assert.IsInstanceOf<InvalidOperationException>(ex.InnerException);
            StringAssert.Contains("untitled", ex.InnerException.Message);
        }

        private static void MakeActiveSceneDirtyUntitled()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(string.IsNullOrEmpty(scene.path), "expected a pathless (untitled) scene");
            EditorSceneManager.MarkSceneDirty(scene);
            Assert.IsTrue(scene.isDirty, "expected MarkSceneDirty to dirty the scene");
        }
    }
}
