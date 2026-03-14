using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageAssetModelImporterTests
    {
        private const string TempRoot = "Assets/Temp/ModelImporterTests";

        // Minimal OBJ content for a single triangle
        private const string MinimalObj =
            "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            CleanupEmptyParentFolders(TempRoot);
        }

        private string CreateTestObj(string name = null)
        {
            name = name ?? $"TestModel_{Guid.NewGuid():N}";
            string objPath = $"{TempRoot}/{name}.obj";
            string fullDiskPath = Path.Combine(Directory.GetCurrentDirectory(), objPath);
            File.WriteAllText(fullDiskPath, MinimalObj);
            AssetDatabase.ImportAsset(objPath, ImportAssetOptions.ForceSynchronousImport);
            return objPath;
        }

        // =============================================================================
        // ModelImporter - Simple Property Setting
        // =============================================================================

        [Test]
        public void ModifyAsset_ModelImporter_SetsGlobalScale()
        {
            string objPath = CreateTestObj();

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["path"] = objPath,
                ["properties"] = new JObject { ["globalScale"] = 0.5f }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var importer = AssetImporter.GetAtPath(objPath) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(0.5f, importer.globalScale, 0.001f);
        }

        [Test]
        public void ModifyAsset_ModelImporter_SetsBooleanProperties()
        {
            string objPath = CreateTestObj();

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["path"] = objPath,
                ["properties"] = new JObject
                {
                    ["importAnimation"] = false,
                    ["isReadable"] = true
                }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var importer = AssetImporter.GetAtPath(objPath) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.IsFalse(importer.importAnimation);
            Assert.IsTrue(importer.isReadable);
        }

        // =============================================================================
        // ModelImporter - Material Remap
        // =============================================================================

        [Test]
        public void ModifyAsset_ModelImporter_MaterialRemap()
        {
            string objPath = CreateTestObj();

            // Create a target material
            string matPath = $"{TempRoot}/RemapTarget_{Guid.NewGuid():N}.mat";
            var shader = FindFallbackShader();
            Assert.IsNotNull(shader, "No shader found for test material");
            var mat = new Material(shader) { name = "RemapTarget" };
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["path"] = objPath,
                ["properties"] = new JObject
                {
                    ["materialRemap"] = new JObject
                    {
                        ["Default-Material"] = matPath
                    }
                }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var importer = AssetImporter.GetAtPath(objPath) as ModelImporter;
            Assert.IsNotNull(importer);
            var externalMap = importer.GetExternalObjectMap();
            bool found = externalMap.Any(kvp =>
                kvp.Key.name == "Default-Material" && kvp.Value is Material);
            Assert.IsTrue(found, "Material remap was not applied");
        }

        [Test]
        public void ModifyAsset_ModelImporter_RemoveMaterialRemap()
        {
            string objPath = CreateTestObj();

            // Create and apply a remap first
            string matPath = $"{TempRoot}/RemapMat_{Guid.NewGuid():N}.mat";
            var shader = FindFallbackShader();
            Assert.IsNotNull(shader, "No shader found for test material");
            var mat = new Material(shader) { name = "RemapMat" };
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();

            ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["path"] = objPath,
                ["properties"] = new JObject
                {
                    ["materialRemap"] = new JObject
                    {
                        ["Default-Material"] = matPath
                    }
                }
            });

            // Now remove the remap by passing null
            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["path"] = objPath,
                ["properties"] = new JObject
                {
                    ["materialRemap"] = new JObject
                    {
                        ["Default-Material"] = JValue.CreateNull()
                    }
                }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var importer = AssetImporter.GetAtPath(objPath) as ModelImporter;
            var externalMap = importer.GetExternalObjectMap();
            bool found = externalMap.Any(kvp => kvp.Key.name == "Default-Material");
            Assert.IsFalse(found, "Material remap should have been removed");
        }

        [Test]
        public void ModifyAsset_ModelImporter_InvalidMaterialPath_NoError()
        {
            string objPath = CreateTestObj();

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "modify",
                ["path"] = objPath,
                ["properties"] = new JObject
                {
                    ["materialRemap"] = new JObject
                    {
                        ["Default-Material"] = "Assets/NonExistent/FakeMaterial.mat"
                    }
                }
            }));
            // Should not crash — returns success with no changes
            Assert.IsNotNull(result);
        }
    }
}
