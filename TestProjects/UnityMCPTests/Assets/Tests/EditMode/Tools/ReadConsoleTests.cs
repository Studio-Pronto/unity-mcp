using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ReadConsoleTests
    {
        /// <summary>
        /// Under -batchmode -nographics Unity does not record a log emitted inside a test into the
        /// console's LogEntries buffer: the message reaches Editor.log and the test's captured
        /// output, but ReadConsole never sees it. (Logs from ordinary editor code do land, so the
        /// buffer itself works.) Tests that need a live entry therefore seed one and skip when it
        /// is not observable, keeping full strength in the Test Runner window without failing the
        /// headless gate. The parsing tests below cover the same logic hermetically everywhere.
        /// </summary>
        private const string HeadlessSkip =
            "Editor console does not record test-scoped logs in headless batchmode.";

        /// <summary>
        /// Logs <paramref name="messageToLog"/> and returns the console entries matching
        /// <paramref name="filterMarker"/>, or null when the console did not record it.
        /// Filtering server-side rather than scanning a fixed window matters: GetConsoleEntries
        /// walks entries oldest-first and stops at count, so a busy console can push a freshly
        /// logged message out of an unfiltered window entirely.
        /// </summary>
        private static JArray FetchOwnLog(string messageToLog, string filterMarker)
        {
            Debug.Log(messageToLog);

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "error", "warning", "log" },
                ["format"] = "detailed",
                ["count"] = 1000,
                ["filterText"] = filterMarker,
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var entries = result["data"] as JArray;
            return entries != null && entries.Count > 0 ? entries : null;
        }

        [Test]
        public void HandleCommand_Clear_Works()
        {
            // Act
            var result = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "clear" }));

            // Assert
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify clear effect. Nothing else logs between these two calls: a [Test] body runs
            // synchronously inside a single editor frame.
            var getAfter = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get", ["types"] = new JArray { "error", "warning", "log" }, ["count"] = 10 }));
            Assert.IsTrue(getAfter.Value<bool>("success"), getAfter.ToString());
            var entriesAfter = getAfter["data"] as JArray;
            Assert.IsTrue(entriesAfter == null || entriesAfter.Count == 0, "Console should be empty after clear.");
        }

        [Test]
        public void HandleCommand_Get_Works()
        {
            string uniqueMessage = $"Test Log Message {Guid.NewGuid()}";

            var entries = FetchOwnLog(uniqueMessage, uniqueMessage);
            if (entries == null) Assert.Ignore(HeadlessSkip);

            // "detailed" returns structured entries, not bare strings.
            var entry = entries[0] as JObject;
            Assert.IsNotNull(entry, $"Expected a structured entry, got: {entries[0]}");
            StringAssert.Contains(uniqueMessage, entry["message"]?.ToString());
            Assert.AreEqual("Log", entry["type"]?.ToString(), "A Debug.Log should be reported as type Log.");
        }

        [Test]
        public void HandleCommand_Get_PreservesMultilineMessageBody()
        {
            string id = Guid.NewGuid().ToString();
            string firstLine = $"First line {id}";
            string secondLine = $"Second line {id}";

            var entries = FetchOwnLog($"{firstLine}\n\n{secondLine}", firstLine);
            if (entries == null) Assert.Ignore(HeadlessSkip);

            string message = null;
            foreach (var entry in entries)
            {
                string candidate = entry["message"]?.ToString();
                if (candidate != null && candidate.Contains(firstLine))
                {
                    message = candidate;
                    break;
                }
            }

            Assert.IsNotNull(message, "Multi-line log entry was not found.");
            StringAssert.Contains($"{firstLine}\n\n{secondLine}", message);
            StringAssert.DoesNotContain("UnityEngine.Debug", message);
        }

        // --- SplitMessageAndStackTrace: the body/stack split, without a live console ---

        [Test]
        public void SplitMessageAndStackTrace_PreservesBlankLineInsideBody()
        {
            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace(
                "First line\n\nSecond line\nUnityEngine.Debug:Log (object)\nThing:Run () (at Assets/Thing.cs:12)");

            Assert.AreEqual("First line\n\nSecond line", body);
            StringAssert.Contains("UnityEngine.Debug:Log (object)", stackTrace);
            StringAssert.Contains("(at Assets/Thing.cs:12)", stackTrace);
        }

        [Test]
        public void SplitMessageAndStackTrace_NormalizesWindowsLineEndings()
        {
            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace(
                "First line\r\n\r\nSecond line\r\nUnityEngine.Debug:Log (object)");

            Assert.AreEqual("First line\n\nSecond line", body);
            Assert.AreEqual("UnityEngine.Debug:Log (object)", stackTrace);
        }

        [Test]
        public void SplitMessageAndStackTrace_SingleLineHasNoStackTrace()
        {
            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace("Just one line");

            Assert.AreEqual("Just one line", body);
            Assert.IsNull(stackTrace);
        }

        [Test]
        public void SplitMessageAndStackTrace_KeepsMultilineBodyWithoutStackTrace()
        {
            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace("line one\nline two");

            Assert.AreEqual("line one\nline two", body);
            Assert.IsNull(stackTrace);
        }

        [Test]
        public void SplitMessageAndStackTrace_EmptyMessageIsReturnedUnchanged()
        {
            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace("");

            Assert.AreEqual("", body);
            Assert.IsNull(stackTrace);
        }

        // ──────────────────── LogEntry.mode severity mapping (issue #1348) ────────────────────

        // 0x804400 and 0x804100 were captured from a real Unity 6000.5.4f1 console via
        // reflection in issue #1348; 0x804200 is the same envelope with the ScriptingWarning
        // bit. The remaining cases exercise one ConsoleWindow.Mode bit each. The old table
        // was off by one for every scripting bit, surfacing Logs as Warnings and Warnings
        // as Errors.
        [TestCase(0x804400, LogType.Log, TestName = "ScriptingLog bit (1<<10) maps to Log")]
        [TestCase(0x804200, LogType.Warning, TestName = "ScriptingWarning bit (1<<9) maps to Warning")]
        [TestCase(0x804100, LogType.Error, TestName = "ScriptingError bit (1<<8) maps to Error")]
        [TestCase(1 << 2, LogType.Log, TestName = "Log bit (1<<2) maps to Log")]
        [TestCase(1 << 0, LogType.Error, TestName = "Error bit (1<<0) maps to Error")]
        [TestCase(1 << 1, LogType.Assert, TestName = "Assert bit (1<<1) maps to Assert")]
        [TestCase(1 << 4, LogType.Error, TestName = "Fatal bit (1<<4) maps to Error")]
        [TestCase(1 << 6, LogType.Error, TestName = "AssetImportError bit (1<<6) maps to Error")]
        [TestCase(1 << 7, LogType.Warning, TestName = "AssetImportWarning bit (1<<7) maps to Warning")]
        [TestCase(1 << 11, LogType.Error, TestName = "ScriptCompileError bit (1<<11) maps to Error")]
        [TestCase(1 << 12, LogType.Warning, TestName = "ScriptCompileWarning bit (1<<12) maps to Warning")]
        [TestCase(1 << 17, LogType.Exception, TestName = "ScriptingException bit (1<<17) maps to Exception")]
        [TestCase(1 << 21, LogType.Assert, TestName = "ScriptingAssertion bit (1<<21) maps to Assert")]
        public void GetLogTypeFromMode_MapsUnityConsoleModeBits(int mode, LogType expected)
        {
            Assert.AreEqual(expected, ReadConsole.GetLogTypeFromMode(mode));
        }

        [Test]
        public void GetLogTypeFromMode_ExceptionWins_WhenCombinedWithErrorBit()
        {
            // Unity sets the Error bit alongside ScriptingException; Exception must win.
            int mode = (1 << 17) | (1 << 0);
            Assert.AreEqual(LogType.Exception, ReadConsole.GetLogTypeFromMode(mode));
        }

        [Test]
        public void GetLogTypeFromMode_UnknownBits_FallBackToLog()
        {
            Assert.AreEqual(LogType.Log, ReadConsole.GetLogTypeFromMode(1 << 14));
        }

    }
}
