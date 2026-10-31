using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Rewrites the selecting <see cref="TestFilterOptions.GroupNames"/> of a run as exact names. Unity's filter builds a
    /// new regex for every comparison and reads each pattern against the whole test tree, through parents and
    /// descendants: on a project of 11,295 tests the same 92 tests took 177.7 s to run from 60 patterns and 5.7 s from
    /// their names. A name and a pattern differ only in how they compare, so the names of the nodes a pattern matches
    /// select what the pattern would, and Unity still decides what a match reaches: the tests below it, a leading
    /// <c>!</c>, and which <c>[Explicit]</c> tests run.
    /// </summary>
    internal static class TestGroupExpansion
    {
        private const string ExclusionPrefix = "!";

        // A name is a scan of the tree too: on 11,000 tests a pattern that matches little cost 3.3 s and a name 16 ms.
        // Both grow with the tree, so the ratio holds for any project, and past it the patterns are the faster filter.
        private const int NamesPerPattern = 100;

        // The Test Framework's adaptor adds this to a test whose name repeats a sibling's, inside a closing parenthesis
        // if there is one (TestExtensions.GetFullName), and its filter compares the name without it.
        private static readonly Regex GeneratedCaseSuffix = new Regex(@" GeneratedTestCase\d+(?=\)?$)");

        /// <summary>
        /// Whether expanding applies: the run names patterns, at least one selects rather than excludes, and it names no
        /// exact tests, which Unity would intersect with them.
        /// </summary>
        internal static bool Applies(string[] testNames, string[] groupNames) =>
            (testNames == null || testNames.Length == 0)
            && groupNames != null
            && groupNames.Any(pattern => !pattern.StartsWith(ExclusionPrefix));

        /// <summary>
        /// The filter to hand Unity for <paramref name="groupNames"/>: the names of the nodes under
        /// <paramref name="root"/> its selecting patterns match, beside its exclusions, which stay patterns because
        /// Unity never runs an <c>[Explicit]</c> test in a filter that has one. All the patterns stay when they match
        /// nothing, as Unity reads an empty list of names as no filter, when they match more than names can beat, or
        /// when they match a test case without its method. Throws <see cref="System.ArgumentException"/> on a pattern
        /// that is not a valid regex.
        /// </summary>
        internal static (string[] testNames, string[] groupNames) Expand(ITestAdaptor root, string[] groupNames)
        {
            var patterns = new List<Regex>();
            var exclusions = new List<string>();
            foreach (string pattern in groupNames)
            {
                if (pattern.StartsWith(ExclusionPrefix))
                {
                    // Compiled only to fail here: Unity reports a bad pattern as "An unexpected error happened".
                    _ = new Regex(pattern.Substring(ExclusionPrefix.Length));
                    exclusions.Add(pattern);
                }
                else
                {
                    patterns.Add(new Regex(pattern));
                }
            }

            var names = new HashSet<string>();
            bool nameable = Walk(root, patterns, covered: false, names);
            if (!nameable || names.Count == 0 || names.Count > patterns.Count * NamesPerPattern) return (null, groupNames);
            return (names.ToArray(), exclusions.Count > 0 ? exclusions.ToArray() : null);
        }

        // A match reaches everything below it, so a node under one needs no name and a broad pattern stays a few names.
        // An [Explicit] node is the exception: Unity runs it for a match on it or below it, never above it. Returns false
        // on a test case matched without its method: its arguments can differ each time the tree is built, as Unity does
        // again on entering Play Mode, and its name would then select nothing. A fixture's arguments are taken to hold:
        // they nearly always do, and giving up on them would lose the speedup for every parameterized fixture.
        private static bool Walk(ITestAdaptor node, List<Regex> patterns, bool covered, HashSet<string> names)
        {
            covered = covered && node.RunState != RunState.Explicit;
            if (!covered)
            {
                string name = FilterName(node);
                if (patterns.Any(pattern => pattern.IsMatch(name)))
                {
                    if (!node.IsSuite && node.Arguments?.Length > 0) return false;
                    names.Add(name);
                    covered = true;
                }
            }

            if (!node.IsSuite) return true;
            foreach (ITestAdaptor child in node.Children)
                if (!Walk(child, patterns, covered, names))
                    return false;
            return true;
        }

        // The name Unity's filter compares (TestExtensions.GetFullNameWithoutDllPath): the root has none, and an assembly
        // goes by its file name, not the absolute path a pattern could match by accident.
        private static string FilterName(ITestAdaptor node)
        {
            if (node.Parent == null) return string.Empty;
            return node.IsTestAssembly ? node.Name : GeneratedCaseSuffix.Replace(node.FullName, string.Empty);
        }
    }
}
