using AIBT.Editor.Layout;

namespace AIBT.Editor.Patching
{
    public sealed class LayoutPatchResult
    {
        public LayoutPatchResult(LayoutDocument document, bool accepted, DiagnosticCollection diagnostics, LayoutDiff diff, string resultHash)
        {
            Document = document;
            Accepted = accepted;
            Diagnostics = diagnostics;
            Diff = diff;
            ResultHash = resultHash;
        }

        /// <summary>The resulting document: the accepted candidate if <see cref="Accepted"/>, otherwise the input unchanged.</summary>
        public LayoutDocument Document { get; }

        public bool Accepted { get; }

        public DiagnosticCollection Diagnostics { get; }

        /// <summary>Empty when <see cref="Accepted"/> is false.</summary>
        public LayoutDiff Diff { get; }

        /// <summary>The content hash of <see cref="Document"/> -- the expected-hash value for the next patch.</summary>
        public string ResultHash { get; }
    }
}
