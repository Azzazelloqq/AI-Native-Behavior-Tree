using System.Collections.Generic;
using System.IO;
using AIBT.Authoring;

namespace AIBT.Mcp
{
    /// <summary>
    /// A minimal, disclosed-as-heuristic project tree scanner: no project-wide tree index
    /// exists anywhere in AIBT yet, so this globs for *.aibt.json under a root directory and
    /// parses each with the real CanonicalTreeJson.Parse. A file that fails to parse is skipped
    /// (recorded, never silently dropped without a trace), not treated as a fatal error for the
    /// whole scan.
    /// </summary>
    public static class AibtTreeDiscovery
    {
        public readonly struct ScanResult
        {
            public ScanResult(IReadOnlyList<TreeDocument> trees, IReadOnlyList<string> skippedFiles)
            {
                Trees = trees;
                SkippedFiles = skippedFiles;
            }

            public IReadOnlyList<TreeDocument> Trees { get; }

            /// <summary>Absolute paths of *.aibt.json files that failed to parse.</summary>
            public IReadOnlyList<string> SkippedFiles { get; }
        }

        public static ScanResult Scan(string rootDirectory)
        {
            var trees = new List<TreeDocument>();
            var skipped = new List<string>();

            if (!Directory.Exists(rootDirectory))
            {
                return new ScanResult(trees, skipped);
            }

            var files = Directory.GetFiles(rootDirectory, "*.aibt.json", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);

            for (var index = 0; index < files.Length; index++)
            {
                string text;
                try
                {
                    text = File.ReadAllText(files[index]);
                }
                catch (System.IO.IOException)
                {
                    skipped.Add(files[index]);
                    continue;
                }

                var result = CanonicalTreeJson.Parse(text, documentId: files[index]);
                if (result.Success)
                {
                    trees.Add(result.Document);
                }
                else
                {
                    skipped.Add(files[index]);
                }
            }

            return new ScanResult(trees, skipped);
        }
    }
}
