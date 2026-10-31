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
    /// Tests for <see cref="TestGroupExpansion"/>: a run's patterns become the exact names that make Unity's own filter
    /// select what they would.
    /// </summary>
    public class TestGroupExpansionTests
    {
        /// <summary>The tree Unity retrieves, down to the members the expansion reads.</summary>
        private sealed class Node : ITestAdaptor
        {
            private readonly List<ITestAdaptor> _children = new List<ITestAdaptor>();

            public Node(string fullName, bool isSuite, bool isTestAssembly = false, string name = null,
                RunState runState = RunState.Runnable, object[] arguments = null)
            {
                FullName = fullName;
                Name = name ?? fullName;
                IsSuite = isSuite;
                IsTestAssembly = isTestAssembly;
                RunState = runState;
                Arguments = arguments;
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
            public object[] Arguments { get; }
            public string[] Categories => Array.Empty<string>();
            public bool IsTestAssembly { get; }
            public RunState RunState { get; }
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
                            new Node("Game.Tests.Water.Drains", false),
                            new Node("Game.Tests.Water.Survey", false, runState: RunState.Explicit)),
                        new Node("Game.Tests.Trail(Long)", true).With(
                            new Node("Game.Tests.Trail(Long).Walks", false)),
                        new Node("Game.Tests.ByHand", true, runState: RunState.Explicit).With(
                            new Node("Game.Tests.ByHand.Cranks", false),
                            new Node("Game.Tests.ByHand.Turns", false)),
                        new Node("Game.Tests.Repeat", true).With(
                            new Node("Game.Tests.Repeat.Same", false),
                            new Node("Game.Tests.Repeat.Same GeneratedTestCase2", false),
                            new Node("Game.Tests.Repeat.Case", true).With(
                                new Node("Game.Tests.Repeat.Case(1)", false, arguments: new object[] { 1 }),
                                new Node("Game.Tests.Repeat.Case(2)", false, arguments: new object[] { 2 }))),
                        new Node("Game.Tests.Twin", true).With(
                            new Node("Game.Tests.Twin(Thing)", true).With(
                                new Node("Game.Tests.Twin(Thing).Stands", false)),
                            new Node("Game.Tests.Twin(Thing GeneratedTestCase2)", true).With(
                                new Node("Game.Tests.Twin(Thing).Stands", false))))));

        private static Node WideTree(int tests) =>
            new Node("Project", true).With(
                new Node("Wide.dll", true, isTestAssembly: true).With(
                    new Node("Wide.Fixture", true).With(
                        Enumerable.Range(0, tests).Select(i => new Node($"Wide.Fixture.T{i}", false)).ToArray())));

        private static (string[] testNames, string[] groupNames) Expand(params string[] patterns) =>
            TestGroupExpansion.Expand(Tree(), patterns);

        private static string[] Names(params string[] patterns) => Expand(patterns).testNames;

        /// <summary>Would fail if a class pattern stopped reaching the methods of a plain or parameterized fixture.</summary>
        [Test]
        public void ClassPatterns_BecomeTheNamesOfWhatTheyMatch()
        {
            (string[] testNames, string[] groupNames) = Expand(@"Game\.Tests\.Water[.(]", @"Game\.Tests\.Trail[.(]");

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Game.Tests.Water.Fills", "Game.Tests.Water.Drains", "Game.Tests.Water.Survey",
                    "Game.Tests.Trail(Long)"
                },
                testNames);
            Assert.IsNull(groupNames, "No pattern should be left for Unity to evaluate.");
        }

        /// <summary>Would fail if the tests below a match were named too: each name is a scan of the tree, and a name on
        /// an [Explicit] test runs what Unity skips for a match on its parent.</summary>
        [Test]
        public void APatternMatchingAParent_NamesOnlyTheParent()
        {
            CollectionAssert.AreEqual(new[] { "Game.Tests.Water" }, Names("^Game.Tests.Water$"));
            CollectionAssert.AreEqual(new[] { "Game.Tests" }, Names(@"^Game\.Tests$"));
        }

        /// <summary>Would fail if an [Explicit] test lost its name to a match above it: Unity runs it for its own match
        /// and would skip it for the other.</summary>
        [Test]
        public void AnExplicitTest_KeepsItsOwnMatch_UnderAMatchAboveIt()
        {
            CollectionAssert.AreEquivalent(new[] { "Game.Tests.Water", "Game.Tests.Water.Survey" }, Names("Water"));
        }

        /// <summary>Would fail if a match below an [Explicit] fixture were dropped for one above it: it is what makes
        /// Unity run the fixture.</summary>
        [Test]
        public void AMatchBelowAnExplicitFixture_KeepsItsName_UnderAMatchAboveIt()
        {
            CollectionAssert.AreEquivalent(new[] { "Game.Tests", "Game.Tests.ByHand.Cranks" },
                Names(@"^Game\.Tests$", @"ByHand\.Cranks$"));
        }

        /// <summary>Would fail if an exclusion were dropped or turned into a name: a filter that has one never runs an
        /// [Explicit] test, even if it matches nothing.</summary>
        [Test]
        public void AnExclusion_StaysAPattern()
        {
            (string[] testNames, string[] groupNames) = Expand("^Game.Tests.Water$", "!NoSuchTest");

            CollectionAssert.AreEqual(new[] { "Game.Tests.Water" }, testNames);
            CollectionAssert.AreEqual(new[] { "!NoSuchTest" }, groupNames);
        }

        /// <summary>Would fail if a pattern were read against an assembly's absolute path, where "Assets" selects every
        /// test in the project; Unity's filter reads the file name.</summary>
        [Test]
        public void AnAssembly_GoesByItsFileName_NotItsPath()
        {
            Assert.IsNull(Names("Assets"));
            CollectionAssert.AreEqual(new[] { "Game.Tests.dll" }, Names(@"Game\.Tests\.dll"));
        }

        /// <summary>Would fail if a generated-case suffix were left in either of its two places: Unity's filter compares
        /// the name without it, so no pattern can match it and repeated cases share one name.</summary>
        [Test]
        public void GeneratedCaseSuffixes_AreStripped_WithOrWithoutParentheses()
        {
            Assert.IsNull(Names("GeneratedTestCase"));
            CollectionAssert.AreEqual(new[] { "Game.Tests.Repeat.Same" }, Names(@"Repeat\.Same"));
            CollectionAssert.AreEqual(new[] { "Game.Tests.Twin(Thing)" }, Names(@"Twin\(Thing"));
        }

        /// <summary>Would fail if a test case were named without its method: its arguments can differ each time the tree
        /// is built, so the name would select nothing and the test would silently leave the run.</summary>
        [Test]
        public void ATestCaseMatchedWithoutItsMethod_KeepsThePatterns()
        {
            (string[] testNames, string[] groupNames) = Expand("Water", @"Repeat\.Case\(");

            Assert.IsNull(testNames);
            CollectionAssert.AreEqual(new[] { "Water", @"Repeat\.Case\(" }, groupNames);
        }

        /// <summary>Would fail if a match on a method stopped reaching its cases, or needed their names.</summary>
        [Test]
        public void AMethodMatch_NamesTheMethod_NotItsCases()
        {
            CollectionAssert.AreEqual(new[] { "Game.Tests.Repeat.Case" }, Names(@"Repeat\.Case"));
        }

        /// <summary>Would fail if a pattern matching everything became no filter: Unity runs the [Explicit] tests a
        /// pattern matches, and skips them with no filter.</summary>
        [Test]
        public void APatternMatchingTheRoot_NamesItAndTheExplicitNodes()
        {
            CollectionAssert.AreEquivalent(new[] { string.Empty, "Game.Tests.Water.Survey", "Game.Tests.ByHand" }, Names(".*"));
        }

        /// <summary>Would fail if an empty selection of names went to Unity, which reads an empty list as no filter and
        /// runs the whole suite.</summary>
        [Test]
        public void NothingMatched_KeepsThePatterns()
        {
            (string[] testNames, string[] groupNames) = Expand("NoSuchTest", "!Water");

            Assert.IsNull(testNames);
            CollectionAssert.AreEqual(new[] { "NoSuchTest", "!Water" }, groupNames);
        }

        /// <summary>Would fail if a broad pattern became a long list of names, each a scan of the tree: one regex selecting
        /// 10,000 of 11,000 tests took 2.5 s, and its names 215 s.</summary>
        [Test]
        public void MoreNamesThanPatternsCanBeat_KeepsThePatterns()
        {
            string[] onePattern = { @"\.T\d+$" };
            string[] twoPatterns = { @"\.T\d+$", "NoSuchTest" };

            Assert.AreEqual(100, TestGroupExpansion.Expand(WideTree(100), onePattern).testNames.Length);

            (string[] testNames, string[] groupNames) = TestGroupExpansion.Expand(WideTree(101), onePattern);
            Assert.IsNull(testNames);
            CollectionAssert.AreEqual(onePattern, groupNames);

            Assert.AreEqual(101, TestGroupExpansion.Expand(WideTree(101), twoPatterns).testNames.Length,
                "The limit should grow with the patterns the names replace.");
        }

        /// <summary>Would fail if a bad pattern of either kind reached Unity, which reports only "An unexpected error
        /// happened while running tests."</summary>
        [Test]
        public void AnInvalidPattern_ThrowsWhicheverItsKind()
        {
            Assert.Catch<ArgumentException>(() => Expand("["));
            Assert.Catch<ArgumentException>(() => Expand("Water", "!["));
        }

        /// <summary>Would fail if a run of exact names were expanded, which Unity would intersect with the patterns, or
        /// one with nothing to select.</summary>
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
