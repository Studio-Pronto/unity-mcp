using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ComponentOpsLayerMaskTests
    {
        private GameObject testGameObject;
        private Camera camera;

        [SetUp]
        public void SetUp()
        {
            testGameObject = new GameObject("LayerMaskTestObject");
            camera = testGameObject.AddComponent<Camera>();
            CommandRegistry.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (testGameObject != null)
            {
                Object.DestroyImmediate(testGameObject);
            }
        }

        private static JObject AsJObject(object result) => result as JObject ?? JObject.FromObject(result);

        [Test]
        public void SetProperty_LayerMask_IntegerValue_SetsRawBitmask()
        {
            var setParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "Camera",
                ["property"] = "cullingMask",
                ["value"] = 1
            };

            LogAssert.ignoreFailingMessages = true;
            object result;
            try
            {
                result = ManageComponents.HandleCommand(setParams);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsNotNull(result);
            var r = AsJObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            Assert.AreEqual(1, camera.cullingMask);
        }

        [Test]
        public void SetProperty_LayerMask_StringArray_ConvertsToMask()
        {
            var setParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "Camera",
                ["property"] = "cullingMask",
                ["value"] = new JArray("Default")
            };

            LogAssert.ignoreFailingMessages = true;
            object result;
            try
            {
                result = ManageComponents.HandleCommand(setParams);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsNotNull(result);
            var r = AsJObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            Assert.AreEqual(LayerMask.GetMask("Default"), camera.cullingMask);
        }

        [Test]
        public void SetProperty_LayerMask_SingleString_ConvertsToMask()
        {
            var setParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "Camera",
                ["property"] = "cullingMask",
                ["value"] = "Default"
            };

            LogAssert.ignoreFailingMessages = true;
            object result;
            try
            {
                result = ManageComponents.HandleCommand(setParams);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsNotNull(result);
            var r = AsJObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            Assert.AreEqual(LayerMask.GetMask("Default"), camera.cullingMask);
        }

        [Test]
        public void SetProperty_LayerMask_InvalidName_ReturnsError()
        {
            var setParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "Camera",
                ["property"] = "cullingMask",
                ["value"] = "NonexistentLayer123"
            };

            LogAssert.ignoreFailingMessages = true;
            object result;
            try
            {
                result = ManageComponents.HandleCommand(setParams);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsNotNull(result);
            var r = AsJObject(result);
            Assert.IsFalse(r.Value<bool>("success"), r.ToString());
            Assert.That(r["data"]?["errors"]?.ToString(), Does.Contain("NonexistentLayer123"));
        }

        [Test]
        public void SetProperty_LayerMask_ArrayWithInvalidName_ReturnsError()
        {
            var setParams = new JObject
            {
                ["action"] = "set_property",
                ["target"] = testGameObject.name,
                ["componentType"] = "Camera",
                ["property"] = "cullingMask",
                ["value"] = new JArray("Default", "NonexistentLayer123")
            };

            LogAssert.ignoreFailingMessages = true;
            object result;
            try
            {
                result = ManageComponents.HandleCommand(setParams);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsNotNull(result);
            var r = AsJObject(result);
            Assert.IsFalse(r.Value<bool>("success"), r.ToString());
            Assert.That(r["data"]?["errors"]?.ToString(), Does.Contain("NonexistentLayer123"));
        }
    }
}
