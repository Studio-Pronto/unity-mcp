using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Tools.Profiler;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageProfilerCounterTests
    {
        private const string Label = "ManageProfilerCounterTests";

        // Registered in every editor, and neither is a Render counter (Internal and Memory).
        private const string TwoNames = "[\"Main Thread\",\"GC Allocated In Frame\"]";

        [TearDown]
        public void TearDown()
        {
            Run(new JObject { ["action"] = "sample_stop", ["label"] = Label });
        }

        // =====================================================================
        // sample_start
        // =====================================================================

        [Test]
        public void SampleStart_JsonArrayString_RecordsOnlyTheNamedCounters()
        {
            var result = SampleStart(TwoNames);

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(2, result["data"].Value<int>("counters_started"));
        }

        [Test]
        public void SampleStart_JsonArray_RecordsOnlyTheNamedCounters()
        {
            var result = SampleStart(JArray.Parse(TwoNames));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(2, result["data"].Value<int>("counters_started"));
        }

        [Test]
        public void SampleStart_NameInOtherCase_RecordsTheRegisteredCounter()
        {
            var result = SampleStart("[\"main thread\"]");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1, result["data"].Value<int>("counters_started"));
        }

        [Test]
        public void SampleStart_UnknownCounterName_FailsAndNamesIt()
        {
            var result = SampleStart("[\"Main Thread\",\"NoSuchCounter_McpTest\"]");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result.Value<string>("error"), Does.Contain("'NoSuchCounter_McpTest'"));
            Assert.IsFalse(SessionExists(), "a failed sample_start must not leave a session behind");
        }

        [Test]
        public void SampleStart_UnknownCategory_FailsAndListsTheRegisteredOnes()
        {
            var result = SampleStart("phsyics");

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result.Value<string>("error"), Does.Contain("'phsyics'").And.Contain("Physics"));
            Assert.IsFalse(SessionExists(), "a failed sample_start must not leave a session behind");
        }

        [Test]
        public void SampleStart_CategoryOutsideTheOldPresetList_RecordsThatCategory()
        {
            int gcCounters = RegisteredCount("GC");
            Assume.That(gcCounters, Is.GreaterThan(0), "this editor registers no GC counters");

            var result = SampleStart("gc");

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.Greater(data.Value<int>("counters_started"), 0);
            Assert.AreEqual(gcCounters, data.Value<int>("counters_started") + data.Value<int>("counters_failed"));
        }

        // =====================================================================
        // counter_list
        // =====================================================================

        [Test]
        public void CounterList_UnknownCategory_ReturnsError()
        {
            var result = Run(new JObject { ["action"] = "counter_list", ["category"] = "nosuch" });

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result.Value<string>("error"), Does.Contain("'nosuch'"));
        }

        [Test]
        public void CounterList_Category_ListsOnlyThatCategory()
        {
            Assume.That(RegisteredCount("GC"), Is.GreaterThan(0), "this editor registers no GC counters");

            var result = Run(new JObject { ["action"] = "counter_list", ["category"] = "gc", ["page_size"] = 1000 });

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var categories = result["data"]["counters"].Select(c => c.Value<string>("category")).Distinct();
            CollectionAssert.AreEqual(new[] { "GC" }, categories);
        }

        // =====================================================================
        // counter_read / physics_get (async)
        // =====================================================================

        [UnityTest]
        public IEnumerator CounterRead_JsonArrayString_ReadsOnlyTheNamedCounters()
        {
            var task = ManageProfiler.HandleCommand(new JObject { ["action"] = "counter_read", ["counters"] = TwoNames });
            yield return Complete(task);

            var result = ToJObject(task.Result);
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            CollectionAssert.AreEquivalent(new[] { "Main Thread", "GC Allocated In Frame" },
                ((JObject)result["data"]["counters"]).Properties().Select(p => p.Name));
        }

        [UnityTest]
        public IEnumerator PhysicsGet_ResolvesThePhysicsCategory()
        {
            var task = ManageProfiler.HandleCommand(new JObject { ["action"] = "physics_get", ["frames"] = 2 });
            yield return Complete(task);

            var result = ToJObject(task.Result);
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static JObject Run(JObject @params)
        {
            var task = ManageProfiler.HandleCommand(@params);
            Assert.IsTrue(task.IsCompleted, "synchronous profiler actions must not defer");
            return ToJObject(task.Result);
        }

        private static JObject SampleStart(JToken counters)
        {
            return Run(new JObject { ["action"] = "sample_start", ["label"] = Label, ["counters"] = counters });
        }

        private static bool SessionExists()
        {
            return Run(new JObject { ["action"] = "sample_list" })["data"]["sessions"]
                .Any(s => s.Value<string>("label") == Label);
        }

        private static int RegisteredCount(string category)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            return handles.Count(h => ProfilerRecorderHandle.GetDescription(h).Category.Name == category);
        }

        private static IEnumerator Complete(Task task)
        {
            double deadline = EditorApplication.timeSinceStartup + 30.0;
            while (!task.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    Assert.Fail("profiler action never completed");
                yield return null;
            }
        }
    }
}
