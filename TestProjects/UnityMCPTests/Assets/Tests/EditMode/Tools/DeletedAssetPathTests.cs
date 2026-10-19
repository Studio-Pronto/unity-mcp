using System;
using System.IO;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Unity keeps a deleted asset's GUID until the editor closes, so the default
    /// AssetDatabase.AssetPathToGUID overload still answers for a path freed this session.
    /// A freed path must read as free (#47).
    /// </summary>
    public class DeletedAssetPathTests
    {
        private const string TempRoot = "Assets/Temp/DeletedAssetPathTests";

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

        [Test]
        public void Move_IntoPathDeletedThisSession_Succeeds()
        {
            string freedPath = CreateMaterial("Freed");
            DeleteAndAssertGuidRetained(freedPath);
            string sourcePath = CreateMaterial("Source");
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "move",
                ["path"] = sourcePath,
                ["destination"] = freedPath
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(sourceGuid, AssetDatabase.AssetPathToGUID(freedPath, AssetPathToGUIDOptions.OnlyExistingAssets),
                "The moved asset should keep its GUID at the freed path.");
        }

        [Test]
        public void Duplicate_IntoPathDeletedThisSession_Succeeds()
        {
            string freedPath = CreateMaterial("Freed");
            DeleteAndAssertGuidRetained(freedPath);
            string sourcePath = CreateMaterial("Source");

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["path"] = sourcePath,
                ["destination"] = freedPath
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(freedPath));
        }

        [Test]
        public void Create_AtPathDeletedThisSession_Succeeds()
        {
            string freedPath = CreateMaterial("Freed");
            DeleteAndAssertGuidRetained(freedPath);

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["path"] = freedPath,
                ["assetType"] = "Material",
                ["properties"] = new JObject { ["shader"] = "Universal Render Pipeline/Lit" }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(freedPath));
        }

        [Test]
        public void CreateFolder_AtPathDeletedThisSession_Succeeds()
        {
            string freedPath = $"{TempRoot}/Freed_{Guid.NewGuid():N}";
            EnsureFolder(freedPath);
            DeleteAndAssertGuidRetained(freedPath);

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "create_folder",
                ["path"] = freedPath
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(AssetDatabase.IsValidFolder(freedPath));
        }

        [Test]
        public void GetInfo_OnPathDeletedThisSession_ReturnsNotFound()
        {
            string freedPath = CreateMaterial("Freed");
            DeleteAndAssertGuidRetained(freedPath);

            var result = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "get_info",
                ["path"] = freedPath
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Asset not found", result.Value<string>("error"));
        }

        [Test]
        public void TextureSetImportSettings_OnPathDeletedThisSession_ReturnsNotFound()
        {
            string freedPath = $"{TempRoot}/Freed_{Guid.NewGuid():N}.png";
            var texture = new Texture2D(2, 2);
            try
            {
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), freedPath), texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(freedPath, ImportAssetOptions.ForceSynchronousImport);
            DeleteAndAssertGuidRetained(freedPath);

            var result = ToJObject(ManageTexture.HandleCommand(new JObject
            {
                ["action"] = "set_import_settings",
                ["path"] = freedPath,
                ["as_sprite"] = true
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Texture not found", result.Value<string>("error"));
        }

        private static string CreateMaterial(string name)
        {
            string path = $"{TempRoot}/{name}_{Guid.NewGuid():N}.mat";
            AssetDatabase.CreateAsset(new Material(FindFallbackShader()), path);
            return path;
        }

        // Deletes through the tool, as an agent would. Without Unity still holding the deleted
        // GUID, the tests above would pass against the unfixed check and prove nothing.
        private static void DeleteAndAssertGuidRetained(string path)
        {
            var deleted = ToJObject(ManageAsset.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["path"] = path
            }));
            Assert.IsTrue(deleted.Value<bool>("success"), deleted.ToString());
            Assert.IsNotEmpty(AssetDatabase.AssetPathToGUID(path),
                "Precondition: Unity should still return the deleted asset's GUID for its path this session.");
        }
    }
}
