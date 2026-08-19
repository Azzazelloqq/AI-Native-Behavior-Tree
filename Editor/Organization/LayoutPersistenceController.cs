using System;
using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Layout;

namespace AIBT.Editor.Organization
{
    public sealed class LayoutLoadResult
    {
        public LayoutLoadResult(LayoutDocument document, DiagnosticCollection diagnostics, bool usedDefault)
        {
            Document = document;
            Diagnostics = diagnostics;
            UsedDefault = usedDefault;
        }

        public LayoutDocument Document { get; }

        public DiagnosticCollection Diagnostics { get; }

        /// <summary>True when no *.aibt.layout.json existed and P3-004's auto-layout produced a fresh default.</summary>
        public bool UsedDefault { get; }

        public bool Success => Document != null;
    }

    /// <summary>
    /// Orchestrates *.aibt.layout.json load/save next to a .aibt.json document. A missing layout
    /// file falls back to P3-004's deterministic auto-layout (never blocks on absence); a present
    /// but invalid file surfaces diagnostics rather than being silently discarded, per
    /// editor-and-layout.md's collaboration rules.
    /// </summary>
    public static class LayoutPersistenceController
    {
        private const string TreeSuffix = ".aibt.json";
        private const string LayoutSuffix = ".aibt.layout.json";

        public static string LayoutPathFor(string treeJsonPath)
        {
            if (treeJsonPath == null)
            {
                throw new ArgumentNullException(nameof(treeJsonPath));
            }

            if (!treeJsonPath.EndsWith(TreeSuffix, StringComparison.Ordinal))
            {
                throw new ArgumentException("Expected a path ending in '.aibt.json'.", nameof(treeJsonPath));
            }

            return treeJsonPath.Substring(0, treeJsonPath.Length - TreeSuffix.Length) + LayoutSuffix;
        }

        public static LayoutLoadResult Load(string treeJsonPath, TreeDocument semanticTree)
        {
            if (semanticTree == null)
            {
                throw new ArgumentNullException(nameof(semanticTree));
            }

            var layoutPath = LayoutPathFor(treeJsonPath);
            if (!File.Exists(layoutPath))
            {
                return new LayoutLoadResult(DeterministicAutoLayoutService.Layout(semanticTree), DiagnosticCollection.Empty, usedDefault: true);
            }

            var bytes = File.ReadAllBytes(layoutPath);
            var result = CanonicalLayoutJson.Parse(bytes, layoutPath, semanticTree);
            if (!result.Success)
            {
                return new LayoutLoadResult(null, result.Diagnostics, usedDefault: false);
            }

            // Cover any node the stored layout predates (added to the tree since the last save).
            var completed = DeterministicAutoLayoutService.Layout(semanticTree, result.Document);
            return new LayoutLoadResult(completed, DiagnosticCollection.Empty, usedDefault: false);
        }

        public static void Save(string treeJsonPath, LayoutDocument layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var layoutPath = LayoutPathFor(treeJsonPath);
            var bytes = CanonicalLayoutJsonWriter.Write(layout);
            File.WriteAllBytes(layoutPath, bytes);
        }
    }
}
