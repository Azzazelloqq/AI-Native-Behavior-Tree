using System;
using AIBT.Authoring;

namespace AIBT.Editor.Editing
{
    public sealed class SemanticEditResult
    {
        public SemanticEditResult(TreeDocument document, DiagnosticCollection diagnostics, bool accepted)
        {
            Document = document;
            Diagnostics = diagnostics;
            Accepted = accepted;
        }

        /// <summary>The resulting document: the edited candidate if accepted, otherwise <c>before</c> unchanged.</summary>
        public TreeDocument Document { get; }

        public DiagnosticCollection Diagnostics { get; }

        public bool Accepted { get; }
    }

    /// <summary>
    /// Gates every semantic edit through the same canonical parse/validate/compile pipeline an
    /// out-of-band `.aibt.json` edit or an AI domain operation would go through -- no separate,
    /// weaker in-editor validation path, per this card's acceptance criteria. An edit is applied
    /// speculatively, then accepted only if <see cref="ReferenceCompiler.Compile"/> (which itself
    /// calls <see cref="TreeValidator.Validate"/>) succeeds; otherwise the pre-edit document is
    /// returned untouched, with the real compiler/validator diagnostics attached.
    /// </summary>
    public static class SemanticEditTransaction
    {
        public static SemanticEditResult Apply(
            TreeDocument before,
            Func<TreeDocument, TreeDocument> edit,
            NodeRegistry registry,
            ReferenceCompilerOptions options)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            var candidate = edit(before);
            var compileResult = ReferenceCompiler.Compile(candidate, registry, options);

            return compileResult.Success
                ? new SemanticEditResult(candidate, compileResult.Diagnostics, accepted: true)
                : new SemanticEditResult(before, compileResult.Diagnostics, accepted: false);
        }
    }
}
