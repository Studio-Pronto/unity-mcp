using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEditor.TestTools.TestRunner.Api;
using RunState = UnityEditor.TestTools.TestRunner.Api.RunState;
using TestMode = UnityEditor.TestTools.TestRunner.Api.TestMode;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests for <see cref="TestGroupExpansion"/>: a pattern selects the tests Unity's own filter would, by exact name.
    /// </summary>
    public class TestGroupExpansionTests
    {
        /// <summary>The tree Unity retrieves, down to the members the expansion reads.</summary>
        private sealed class Node : ITestAdaptor
        {
            private readonly List<ITestAdaptor> _children = new List<ITestAdaptor>();

            public Node(string fullName, bool isSuite, bool isTestAssembly = false, string name = null)
            {
                FullName = fullName;
                Name = name ?? fullName;
                IsSuite = isSuite;
                IsTestAssembly = isTestAssembly;
            }

            public Node With(params Node[] children)
            {
                foreach (Node child in children)
                {
                    child.Parent = this;
                    _children.Add(child);
                }
                return this;
            }

            public string Id => FullName;
            public string Name { get; }
            public string FullName { get; }
            public int TestCaseCount => 0;
            public bool HasChildren => _children.Count > 0;
            public bool IsSuite { get; }
            public IEnumerable<ITestAdaptor> Children => _children;
            public ITestAdaptor Parent { get; private set; }
            public int TestCaseTimeout => 0;
            public ITypeInfo TypeInfo => null;
            public IMethodInfo Method => null;
            public object[] Arguments => null;
            public string[] Categories => Array.Empty<string>();
            public bool IsTestAssembly { get; }
            public RunState RunState => RunState.Runnable;
            public string Description => null;
            public string SkipReason => null;
            public string ParentId => null;
            public string ParentFullName => null;
            public string UniqueName => FullName;
            public string ParentUniqueName => null;
            public int ChildIndex => -1;
            public TestMode TestMode => TestMode.EditMode;
        }

        private static Node Tree() =>
            new Node("Project", true).With(
                new Node("/abs/Assets/Library/Game.Tests.dll", true, isTestAssembly: true, name: "Game.Tests.dll").With(
                    new Node("Game.Tests", true).With(
                        new Node("Game.Tests.Water", true).With(
                            new Node("Game.Tests.Water.Fills", false),
                            new Node("Game.Tests.Water.Drains", false)),
                        new Node("Game.Tests.Trail(Long)", true).With(
                            new Node("Game.Tests.Trail(Long).Walks", false)),
                        new Node("Game.Tests.Repeat", true).With(
                            new Node("Game.Tests.Repeat.Same GeneratedTestCase0", false),
                            new Node("Game.Tests.Repeat.Same GeneratedTestCase1", false)))));

        private static string[] Expand(params string[] patterns) =>
            TestGroupExpansion.Expand(Tree(), patterns, out _);

        /// <summary>Would fail if a class pattern stopped reaching the methods of a plain or parameterized fixture.</summary>
        [Test]
        public void ClassPatterns_SelectTheirMethods_ByExactName()
        {
            CollectionAssert.AreEquivalent(
                new[] { "Game.Tests.Water.Fills", "Game.Tests.Water.Drains", "Game.Tests.Trail(Long).Walks" },
                Expand(@"Game\.Tests\.Water[.(]", @"Game\.Tests\.Trail[.(]"));
        }

        /// <summary>Would fail if a pattern that matches only a parent stopped selecting the tests below it, as Unity's
        /// filter does.</summary>
        [Test]
        public void APatternMatchingAParent_SelectsEverythingBelowIt()
        {
            CollectionAssert.AreEquivalent(new[] { "Game.Tests.Water.Fills", "Game.Tests.Water.Drains" },
                Expand("^Game.Tests.Water$"));
        }

        /// <summary>Would fail if an exclusion stopped applying to what a pattern selected.</summary>
        [Test]
        public void AnExclusion_RemovesWhatAPatternSelected()
        {
            CollectionAssert.AreEquivalent(new[] { "Game.Tests.Water.Fills" }, Expand("Water", "!Drains"));
        }

        /// <summary>Would fail if a pattern were read against an assembly's absolute path, where "Assets" selects every
        /// test in the project; Unity's filter reads the file name.</summary>
        [Test]
        public void AnAssembly_GoesByItsFileName_NotItsPath()
        {
            CollectionAssert.IsEmpty(Expand("Assets"));
            Assert.AreEqual(4, Expand(@"Game\.Tests\.dll").Length, "the file name still selects its tests");
        }

        /// <summary>Would fail if a generated-case suffix reached Unity's filter, which matches nothing for it, and the
        /// test silently left the run; or if the two cases named the one test twice.</summary>
        [Test]
        public void GeneratedCaseSuffixes_AreStripped_AndTheirCasesNamedOnce()
        {
            CollectionAssert.AreEqual(new[] { "Game.Tests.Repeat.Same" }, Expand("Repeat"));
        }

        /// <summary>Would fail if the count that tells the caller "every test" drifted from the tests a run can name.</summary>
        [Test]
        public void TestCount_CountsEachNameOnce()
        {
            TestGroupExpansion.Expand(Tree(), new[] { ".*" }, out int testCount);
            Assert.AreEqual(4, testCount);
        }

        /// <summary>Would fail if an empty selection of names went to Unity, which reads an empty list as no filter and
        /// runs the whole suite, or if a run of exact names or exclusions were expanded.</summary>
        [Test]
        public void Applies_OnlyToSelectingPatterns_WithNoExactNames()
        {
            Assert.IsTrue(TestGroupExpansion.Applies(null, new[] { "Water" }));
            Assert.IsTrue(TestGroupExpansion.Applies(Array.Empty<string>(), new[] { "Water", "!Drains" }));
            Assert.IsFalse(TestGroupExpansion.Applies(new[] { "Game.Tests.Water.Fills" }, new[] { "Water" }));
            Assert.IsFalse(TestGroupExpansion.Applies(null, new[] { "!Drains" }));
            Assert.IsFalse(TestGroupExpansion.Applies(null, Array.Empty<string>()));
            Assert.IsFalse(TestGroupExpansion.Applies(null, null));
        }
    }
}
