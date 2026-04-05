using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;

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

            var result = ManageComponents.HandleCommand(setParams);

            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SuccessResponse>(result);
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

            var result = ManageComponents.HandleCommand(setParams);

            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SuccessResponse>(result);
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

            var result = ManageComponents.HandleCommand(setParams);

            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SuccessResponse>(result);
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

            var result = ManageComponents.HandleCommand(setParams);

            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ErrorResponse>(result);
            var error = (ErrorResponse)result;
            Assert.That(error.Message, Does.Contain("NonexistentLayer123"));
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

            var result = ManageComponents.HandleCommand(setParams);

            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ErrorResponse>(result);
            var error = (ErrorResponse)result;
            Assert.That(error.Message, Does.Contain("NonexistentLayer123"));
        }
    }
}
