using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Turns the <see cref="TestFilterOptions.GroupNames"/> patterns of a run into the exact names of the tests they
    /// select. Unity's filter gives each pattern a regex that it evaluates against the whole test tree, through parents
    /// and descendants, and an exact name costs a string comparison: on a project of 11,295 tests the same 92 tests took
    /// 177.7 s to run from 60 patterns and 5.7 s from their names. Each pattern here is compiled once and read once per
    /// node. A pattern selects what Unity's filter would select: the nodes it matches and everything below them, so
    /// one that names a namespace or fixture selects its tests, and a leading <c>!</c> excludes.
    /// </summary>
    internal static class TestGroupExpansion
    {
        private const string ExclusionPrefix = "!";

        // The Test Framework's adaptor appends this to a test whose display name repeats (TestExtensions.GetFullName),
        // and its filter compares the name without it.
        private static readonly Regex GeneratedCaseSuffix = new Regex(@" GeneratedTestCase\d+$");

        /// <summary>
        /// Whether expanding applies: the run names patterns, at least one selects rather than excludes, and it names no
        /// exact tests, which Unity would intersect with them. An exclusion alone stays a pattern: its names would be
        /// nearly the whole suite, far costlier than the regex.
        /// </summary>
        internal static bool Applies(string[] testNames, string[] groupNames) =>
            (testNames == null || testNames.Length == 0)
            && groupNames != null
            && groupNames.Any(pattern => !pattern.StartsWith(ExclusionPrefix));

        /// <summary>
        /// The exact names of the tests <paramref name="groupNames"/> select under <paramref name="root"/>, one each, and
        /// through <paramref name="testCount"/> how many tests the tree holds. Throws <see cref="System.ArgumentException"/>
        /// on a pattern that is not a valid regex.
        /// </summary>
        internal static string[] Expand(ITestAdaptor root, IEnumerable<string> groupNames, out int testCount)
        {
            var include = new List<Regex>();
            var exclude = new List<Regex>();
            foreach (string pattern in groupNames)
            {
                if (pattern.StartsWith(ExclusionPrefix))
                    exclude.Add(new Regex(pattern.Substring(ExclusionPrefix.Length)));
                else
                    include.Add(new Regex(pattern));
            }

            var names = new List<string>();
            var seen = new HashSet<string>();
            testCount = 0;
            Walk(root, include, exclude, included: false, excluded: false, names, seen, ref testCount);
            return names.ToArray();
        }

        private static void Walk(ITestAdaptor node, List<Regex> include, List<Regex> exclude, bool included, bool excluded,
            List<string> names, HashSet<string> seen, ref int testCount)
        {
            string name = FilterName(node);
            included = included || Matches(include, name);
            excluded = excluded || Matches(exclude, name);

            if (!node.IsSuite)
            {
                if (!seen.Add(name)) return;
                testCount++;
                if (included && !excluded) names.Add(name);
                return;
            }

            foreach (ITestAdaptor child in node.Children)
                Walk(child, include, exclude, included, excluded, names, seen, ref testCount);
        }

        // The name Unity's filter compares (TestExtensions.GetFullNameWithoutDllPath): the root has none, and an assembly
        // goes by its file name, not the absolute path a pattern could match by accident.
        private static string FilterName(ITestAdaptor node)
        {
            if (node.Parent == null) return string.Empty;
            return node.IsTestAssembly ? node.Name : GeneratedCaseSuffix.Replace(node.FullName, string.Empty);
        }

        private static bool Matches(List<Regex> patterns, string name)
        {
            foreach (Regex pattern in patterns)
                if (pattern.IsMatch(name))
                    return true;
            return false;
        }
    }
}
