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
        private NodeRegistry _registry;
        private Func<TreeDocument, DiagnosticCollection> _validate;
        private readonly ReferenceCompilerOptions _options;
        private byte[] _savedBytes;
        private byte[] _savedDocumentBytes;

        internal GraphEditSession(TreeDocument document, NodeRegistry registry, ReferenceCompilerOptions options, byte[] savedBytes,
            Func<TreeDocument, DiagnosticCollection> validate = null)
        {
            _history = new SemanticEditHistory(document);
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _validate = validate;
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

        internal void RebindRegistry(NodeRegistry registry, Func<TreeDocument, DiagnosticCollection> validate)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _validate = validate;
            Refresh();
        }

        internal bool CanConnect(NodeId parentId, NodeId childId)
        {
            if (parentId == childId || childId == Document.Root) return false;
            var parent = Document.Nodes.FirstOrDefault(node => node.Id == parentId);
            if (parent == null || !_registry.TryGet(parent.TypeId, out var entry) || parent.Children.Contains(childId)) return false;
            if (entry.Manifest.ChildPolicy.Maximum.HasValue && parent.Children.Count >= entry.Manifest.ChildPolicy.Maximum.Value) return false;
            var pending = new System.Collections.Generic.Stack<NodeId>();
            var visited = new System.Collections.Generic.HashSet<NodeId>();
            if (!Document.Nodes.Any(node => node.Id == childId)) return false;
            pending.Push(childId);
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (id == parentId) return false;
                if (!visited.Add(id)) continue;
                var node = Document.Nodes.FirstOrDefault(candidate => candidate.Id == id);
                if (node != null) foreach (var child in node.Children) pending.Push(child);
            }
            return true;
        }

        internal bool TryConnect(NodeId parentId, NodeId childId)
        {
            if (!CanConnect(parentId, childId)) return false;
            Apply(document =>
            {
                var previous = document.Nodes.FirstOrDefault(node => node.Children.Contains(childId));
                if (previous != null) document = SemanticEditOperations.Disconnect(document, previous.Id, childId);
                return SemanticEditOperations.Connect(document, parentId, childId);
            });
            return true;
        }

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
            Diagnostics = _validate != null ? _validate(Document) : ReferenceCompiler.Compile(Document, _registry, _options).Diagnostics;
            CanCompile = !Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var encoded = CanonicalTreeJson.Serialize(Document);
            IsDirty = _savedDocumentBytes == null || !encoded.Success || !encoded.Utf8.SequenceEqual(_savedDocumentBytes);
        }
    }
}
