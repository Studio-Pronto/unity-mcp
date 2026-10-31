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
    /// Tests for run_tests refusing a filter that is not a valid regex before a job exists. Unity reports one only as
    /// "An unexpected error happened while running tests", and a job's own error waits on the Editor being active.
    /// No test here starts a real run: a simulated running job stands in the way of any request that gets past the check.
    /// </summary>
    public class RunTestsInvalidPatternTests
    {
        private static readonly FieldInfo CurrentJobId =
            typeof(TestJobManager).GetField("_currentJobId", BindingFlags.NonPublic | BindingFlags.Static);

        private string _originalJobId;

        [SetUp]
        public void SetUp()
        {
            _originalJobId = CurrentJobId.GetValue(null) as string;
            CurrentJobId.SetValue(null, "fake-running-job");
        }

        [TearDown]
        public void TearDown()
        {
            CurrentJobId.SetValue(null, _originalJobId);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static JObject Run(JObject parameters)
        {
            object result = RunTests.HandleCommand(parameters).Result;
            return result as JObject ?? JObject.FromObject(result);
        }

        /// <summary>Would fail if a bad pattern of any kind were left for Unity to find. A category or an exclusion never
        /// passes through the expansion, and a run that names exact tests skips it.</summary>
        [TestCase("groupNames", "[")]
        [TestCase("groupNames", "![")]
        [TestCase("categoryNames", "[")]
        [TestCase("categoryNames", "![")]
        public void InvalidPattern_IsRefusedWithItsRegexError(string field, string pattern)
        {
            JObject response = Run(new JObject { ["mode"] = "EditMode", [field] = new JArray(pattern) });

            Assert.IsFalse(response.Value<bool>("success"), response.ToString());
            StringAssert.Contains("Unterminated", response.Value<string>("error"));
        }

        [Test]
        public void InvalidPattern_BesideExactNames_IsRefused()
        {
            JObject response = Run(new JObject
            {
                ["mode"] = "EditMode",
                ["testNames"] = new JArray("Some.Test"),
                ["groupNames"] = new JArray("[")
            });

            StringAssert.Contains("Unterminated", response.Value<string>("error"));
        }

        /// <summary>Would fail if an exact name were read as a regex, or an assembly name: neither is one.</summary>
        [Test]
        public void ExactNames_AreNotPatterns()
        {
            JObject response = Run(new JObject
            {
                ["mode"] = "EditMode",
                ["testNames"] = new JArray("Some.Test[1"),
                ["assemblyNames"] = new JArray("[")
            });

            Assert.AreEqual("tests_running", response.Value<string>("error"), "It should reach the simulated running job.");
        }

        /// <summary>Would fail if the check ran after the scene gate: a run that cannot start must not cost a scene. The
        /// pattern is a selecting one, so a request that got this far would fail at the expansion, not start a run.</summary>
        [Test]
        public void InvalidPattern_LeavesAnUntitledSceneAlone()
        {
            CurrentJobId.SetValue(null, null);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.MarkSceneDirty(scene);

            Run(new JObject
            {
                ["mode"] = "EditMode",
                ["groupNames"] = new JArray("["),
                ["discard_untitled_scenes"] = true
            });

            Assert.IsTrue(SceneManager.GetActiveScene().isDirty, "The scene must not be discarded for a run that cannot start.");
        }
    }
}
