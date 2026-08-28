using System;
using System.IO;
using AIBT.Authoring;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// Load/save of a semantic *.aibt.json tree document for MCP authoring tools. Mirrors
    /// <see cref="AIBT.Editor.Organization.LayoutPersistenceController"/>'s load/save shape for
    /// the layout side, built on the same already-existing <see cref="CanonicalTreeJson"/>
    /// parse/serialize primitives -- no semantic-tree equivalent existed anywhere before this
    /// card (only the layout side had one).
    /// </summary>
    internal static class TreeDocumentPersistence
    {
        internal sealed class TreeLoadResult
        {
            internal TreeLoadResult(TreeDocument document, DiagnosticCollection diagnostics)
            {
                Document = document;
                Diagnostics = diagnostics;
            }

            internal TreeDocument Document { get; }

            internal DiagnosticCollection Diagnostics { get; }

            internal bool Success => Document != null;
        }

        internal static TreeLoadResult Load(string path)
        {
            if (!File.Exists(path))
            {
                return new TreeLoadResult(null, DiagnosticCollection.Empty);
            }

            var text = File.ReadAllText(path);
            var result = CanonicalTreeJson.Parse(text, documentId: path);
            return new TreeLoadResult(result.Document, result.Diagnostics);
        }

        /// <summary>
        /// Serializes and writes <paramref name="document"/>, or returns write-blocking
        /// diagnostics without touching the file system. Uses the public
        /// <see cref="CanonicalTreeJson.Serialize"/> entry point (the tree writer itself is
        /// internal to <c>AIBT.Authoring</c>), which also performs the JSON-representability
        /// check -- never partially written.
        /// </summary>
        internal static DiagnosticCollection Save(string path, TreeDocument document)
        {
            var result = CanonicalTreeJson.Serialize(document);
            if (!result.Success)
            {
                return result.Diagnostics;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, result.Utf8);
            return DiagnosticCollection.Empty;
        }
    }
}
