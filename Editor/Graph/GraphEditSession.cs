using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;

namespace AIBT.Editor.Graph
{
    // An editing session accepts representable drafts, but never changes compiler validation.
    internal sealed class GraphEditSession
    {
        private readonly SemanticEditHistory _history;
        private readonly NodeRegistry _registry;
        private readonly ReferenceCompilerOptions _options;
        private byte[] _savedBytes;
        private byte[] _savedDocumentBytes;

        internal GraphEditSession(TreeDocument document, NodeRegistry registry, ReferenceCompilerOptions options, byte[] savedBytes)
        {
            _history = new SemanticEditHistory(document);
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _savedBytes = savedBytes == null ? null : (byte[])savedBytes.Clone();
            var savedDocument = savedBytes == null ? null : CanonicalTreeJson.Parse(savedBytes).Document;
            _savedDocumentBytes = savedDocument == null ? null : CanonicalTreeJson.Serialize(savedDocument).Utf8;
            Refresh();
        }

        internal TreeDocument Document => _history.Current;
        internal DiagnosticCollection Diagnostics { get; private set; }
        internal bool CanCompile { get; private set; }
        internal bool CanUndo => _history.CanUndo;
        internal bool CanRedo => _history.CanRedo;
        internal bool IsDirty { get; private set; }
        internal byte[] SavedBytes => _savedBytes == null ? null : (byte[])_savedBytes.Clone();

        internal void Apply(Func<TreeDocument, TreeDocument> edit)
        {
            var candidate = edit(Document);
            var encoded = CanonicalTreeJson.Serialize(candidate);
            if (!encoded.Success)
                throw new InvalidOperationException(string.Join("\n", encoded.Diagnostics.Select(d => d.Message)));
            _history.Do(candidate);
            Refresh();
        }

        internal void Undo()
        {
            _history.Undo();
            Refresh();
        }

        internal void Redo()
        {
            _history.Redo();
            Refresh();
        }

        internal void Save(string path)
        {
            var encoded = CanonicalTreeJson.Serialize(Document);
            if (!encoded.Success)
                throw new InvalidOperationException(string.Join("\n", encoded.Diagnostics.Select(d => d.Message)));
            WriteIfUnchanged(path, _savedBytes, encoded.Utf8);
            _savedBytes = (byte[])encoded.Utf8.Clone();
            _savedDocumentBytes = (byte[])encoded.Utf8.Clone();
            Refresh();
        }

        internal static void WriteIfUnchanged(string path, byte[] expectedBytes, byte[] content)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (expectedBytes == null ? File.Exists(path) : !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(expectedBytes))
                throw new IOException("The file changed outside the editor. Reload before overwriting it.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(true);
                }
                if (expectedBytes == null) File.Move(temporary, path);
                else File.Replace(temporary, path, null);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void Refresh()
        {
            var result = ReferenceCompiler.Compile(Document, _registry, _options);
            Diagnostics = result.Diagnostics;
            CanCompile = result.Success;
            var encoded = CanonicalTreeJson.Serialize(Document);
            IsDirty = _savedDocumentBytes == null || !encoded.Success || !encoded.Utf8.SequenceEqual(_savedDocumentBytes);
        }
    }
}
