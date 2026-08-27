using AIBT.Authoring;

namespace AIBT.Editor.Patching
{
    public sealed class SemanticPatchResult
    {
        public SemanticPatchResult(TreeDocument document, bool accepted, DiagnosticCollection diagnostics, SemanticDiff diff)
        {
            Document = document;
            Accepted = accepted;
            Diagnostics = diagnostics;
            Diff = diff;
        }

        /// <summary>The resulting document: the accepted candidate if <see cref="Accepted"/>, otherwise the input unchanged.</summary>
        public TreeDocument Document { get; }

        public bool Accepted { get; }

        public DiagnosticCollection Diagnostics { get; }

        /// <summary>Empty when <see cref="Accepted"/> is false.</summary>
        public SemanticDiff Diff { get; }

        public ulong ResultRevision => Document.Revision.Value;
    }
}
