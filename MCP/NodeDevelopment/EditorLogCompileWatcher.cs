using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AIBT.Mcp.NodeDevelopment
{
    // Supporting diagnostics only. Compilation events, never log markers, establish success.
    internal static class EditorLogCompileWatcher
    {
        private static readonly Regex FailureMarker = new Regex(
            "error AIBT50\\d\\d|error CS\\d+|CS8032|AD0001|will not be loaded|Could not load file or assembly|Analyzer.+failed|Generator.+failed",
            RegexOptions.Compiled);

        internal static bool HasFailureMarker(string line) => line != null && FailureMarker.IsMatch(line);

        internal static long Capture(string path, out string warning)
        {
            warning = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new IOException("Editor log is unavailable.");
                return new FileInfo(path).Length;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { warning = exception.Message; return -1; }
        }

        internal static string ReadTail(string path, long position, out string warning)
        {
            warning = null;
            try
            {
                if (position < 0 || string.IsNullOrEmpty(path)) throw new IOException("Editor log was unavailable at compile start.");
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (position > stream.Length) throw new IOException("Editor log was truncated or rotated; its tail cannot identify this attempt.");
                    stream.Seek(position, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { warning = exception.Message; return string.Empty; }
        }
    }
}
