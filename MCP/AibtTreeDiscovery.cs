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
            public ScanResult(IReadOnlyList<TreeDocument> trees, IReadOnlyList<string> treePaths, IReadOnlyList<string> skippedFiles)
            {
                Trees = trees;
                TreePaths = treePaths;
                SkippedFiles = skippedFiles;
            }

            public IReadOnlyList<TreeDocument> Trees { get; }

            /// <summary>Absolute source file path for each entry in <see cref="Trees"/>, same index order.</summary>
            public IReadOnlyList<string> TreePaths { get; }

            /// <summary>Absolute paths of *.aibt.json files that failed to parse.</summary>
            public IReadOnlyList<string> SkippedFiles { get; }

            /// <summary>Resolves the absolute source path for a tree by ID, for authoring tools that need to write it back.</summary>
            public bool TryFindPath(TreeId treeId, out string path)
            {
                for (var index = 0; index < Trees.Count; index++)
                {
                    if (Trees[index].TreeId == treeId)
                    {
                        path = TreePaths[index];
                        return true;
                    }
                }

                path = null;
                return false;
            }
        }

        public static ScanResult Scan(string rootDirectory)
        {
            var trees = new List<TreeDocument>();
            var treePaths = new List<string>();
            var skipped = new List<string>();

            if (!Directory.Exists(rootDirectory))
            {
                return new ScanResult(trees, treePaths, skipped);
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
                    treePaths.Add(files[index]);
                }
                else
                {
                    skipped.Add(files[index]);
                }
            }

            return new ScanResult(trees, treePaths, skipped);
        }
    }
}
