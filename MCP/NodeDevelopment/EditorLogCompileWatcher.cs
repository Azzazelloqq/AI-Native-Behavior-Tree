using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace AIBT.Mcp.NodeDevelopment
{
    internal enum CompileWatchStatus
    {
        /// <summary>isCompiling is false and the log shows no compile-request marker since the recorded position -- either Unity has not yet noticed the write, or genuinely nothing changed. The caller should wait and check again.</summary>
        NotYetObserved,
        /// <summary>isCompiling is true. The caller should wait and check again -- never block a single call on this.</summary>
        StillCompiling,
        Compiled,
        Failed,
    }

    internal readonly struct CompileWatchResult
    {
        internal CompileWatchResult(CompileWatchStatus status, string logTail)
        {
            Status = status;
            LogTail = logTail;
        }

        internal CompileWatchStatus Status { get; }
        internal string LogTail { get; }
    }

    /// <summary>
    /// Instantaneous, non-blocking reads only -- deliberately no polling loop inside any single
    /// call. A domain reload can happen at any moment once a script write triggers Unity's own
    /// Auto Refresh, and it destroys the bridge's request-handling thread mid-flight (confirmed
    /// empirically this session; McpBridgeAutoRestart recovers the *bridge*, but not an in-flight
    /// request). The caller (MCP client) is expected to call <see cref="Check"/> repeatedly across
    /// separate requests until it stops reporting <see cref="CompileWatchStatus.NotYetObserved"/>
    /// or <see cref="CompileWatchStatus.StillCompiling"/> -- exactly the same "call again later"
    /// shape this project's own async job tools use elsewhere, applied here because compilation
    /// is the one operation in this project that can outlive the connection that requested it.
    /// </summary>
    internal static class EditorLogCompileWatcher
    {
        private const string CompileRequestedMarker = "[ScriptCompilation]";

        private static readonly Regex FailureMarker = new Regex(
            "error AIBT50\\d\\d|error CS\\d+|CS8032|AD0001|will not be loaded|Could not load file or assembly|Analyzer.+failed|Generator.+failed",
            RegexOptions.Compiled);

        /// <summary>Current Editor log length, to record as the "before" marker at generation time.</summary>
        internal static long CurrentLogPosition(string projectRoot)
        {
            var logPath = EditorLogPath(projectRoot);
            return File.Exists(logPath) ? new FileInfo(logPath).Length : 0L;
        }

        internal static CompileWatchResult Check(string projectRoot, long logPositionBefore)
        {
            var tail = ReadTail(EditorLogPath(projectRoot), logPositionBefore);
            var compileRequested = tail.Contains(CompileRequestedMarker);

            if (EditorApplication.isCompiling)
            {
                return new CompileWatchResult(CompileWatchStatus.StillCompiling, tail);
            }

            if (!compileRequested)
            {
                return new CompileWatchResult(CompileWatchStatus.NotYetObserved, tail);
            }

            return new CompileWatchResult(
                HasFailureMarker(tail) ? CompileWatchStatus.Failed : CompileWatchStatus.Compiled,
                tail);
        }

        internal static bool HasFailureMarker(string logTail) => logTail != null && FailureMarker.IsMatch(logTail);

        private static string EditorLogPath(string projectRoot)
        {
            var projectDirectory = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
            return Path.Combine(projectDirectory, "Logs", "Editor.log");
        }

        private static string ReadTail(string logPath, long fromPosition)
        {
            if (!File.Exists(logPath))
            {
                return string.Empty;
            }

            using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fromPosition > stream.Length)
                {
                    fromPosition = 0;
                }

                stream.Seek(fromPosition, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
