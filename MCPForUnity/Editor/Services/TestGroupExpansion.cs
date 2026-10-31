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
    /// their names. On one tree a name and a pattern differ only in how they compare, so the names of the nodes a pattern
    /// matches select what the pattern would, and Unity still decides what a match reaches: the tests below it, a leading
    /// <c>!</c>, and which <c>[Explicit]</c> tests run. But the tree read here is the cached test list, and a run can
    /// build its own, as Test Framework 1.8.0 does on entering Play Mode, where a test case source can yield new cases or
    /// new names. So a pattern is only replaced by names that are sure to hold.
    /// </summary>
    internal static class TestGroupExpansion
    {
        private const string ExclusionPrefix = "!";

        // A name is a scan of the tree too: on 11,000 tests a pattern that matches little cost 3.3 s and a name 16 ms. Both
        // grow with the tree, so the ratio holds for any project. This keeps the names well under the pattern they
        // replace, and a pattern matching more costs less anyway: one matching 10,000 tests cost 2.3 s.
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
            && groupNames.Any(pattern => !IsExclusion(pattern));

        /// <summary>
        /// Throws <see cref="System.ArgumentException"/> on a value of <paramref name="patterns"/> that Unity would fail to
        /// read as a regex. It reports one only as "An unexpected error happened while running tests", and only once a
        /// run compares it.
        /// </summary>
        internal static void ThrowOnInvalidPattern(string[] patterns)
        {
            if (patterns == null) return;
            foreach (string pattern in patterns)
                _ = new Regex(IsExclusion(pattern) ? pattern.Substring(ExclusionPrefix.Length) : pattern);
        }

        /// <summary>
        /// What to hand Unity for <paramref name="groupNames"/>, one entry per filter: it runs what any of them selects.
        /// A selecting pattern becomes the names of the nodes under <paramref name="root"/> it matches. It stays a pattern
        /// when it matches nothing, as the run's tree can hold what the list does not, when it matches a test case without
        /// its method, or when it matches more names than it costs, and with no <paramref name="root"/>, as when the test
        /// list is unavailable. Exclusions stay patterns in every filter, because Unity never runs an <c>[Explicit]</c>
        /// test in a filter that has one. No filter comes back empty: Unity reads an empty list of names as no filter.
        /// </summary>
        internal static (string[] testNames, string[] groupNames)[] Expand(ITestAdaptor root, string[] groupNames)
        {
            if (root == null) return new[] { ((string[])null, groupNames) };

            string[] exclusions = groupNames.Where(IsExclusion).ToArray();
            var names = new HashSet<string>();
            var patterns = new List<string>();
            foreach (string pattern in groupNames.Where(pattern => !IsExclusion(pattern)))
            {
                var matched = new HashSet<string>();
                bool nameable = Walk(root, new Regex(pattern), covered: false, matched);
                if (nameable && matched.Count > 0 && matched.Count <= NamesPerPattern) names.UnionWith(matched);
                else patterns.Add(pattern);
            }

            var filters = new List<(string[] testNames, string[] groupNames)>();
            if (names.Count > 0) filters.Add((names.ToArray(), exclusions.Length > 0 ? exclusions : null));
            if (patterns.Count > 0) filters.Add((null, patterns.Concat(exclusions).ToArray()));
            return filters.ToArray();
        }

        private static bool IsExclusion(string pattern) => pattern.StartsWith(ExclusionPrefix);

        // The names that select what the pattern does. A match reaches everything below it, so a node under one needs no
        // name and a broad pattern stays a few names. An [Explicit] node is the exception: Unity runs it for a match on it
        // or below it, never above it. Returns false on a test case matched without its method, the one node whose parent
        // has a Method: its name can differ each time the tree is built and would then select nothing. A fixture's
        // arguments are taken to hold: they nearly always do, and giving up on them would lose the speedup for every
        // parameterized fixture.
        private static bool Walk(ITestAdaptor node, Regex pattern, bool covered, HashSet<string> names)
        {
            covered = covered && node.RunState != RunState.Explicit;
            if (!covered)
            {
                string name = FilterName(node);
                if (pattern.IsMatch(name))
                {
                    if (node.Parent?.Method != null) return false;
                    names.Add(name);
                    covered = true;
                }
            }

            if (!node.IsSuite) return true;
            foreach (ITestAdaptor child in node.Children)
                if (!Walk(child, pattern, covered, names))
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
