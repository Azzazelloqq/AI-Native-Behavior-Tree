using System;
using System.Collections.Generic;
using AIBT.Authoring;

namespace AIBT.Editor.Patching
{
    /// <summary>
    /// Implements ADR-P6-002's semantic-patch model: an expected-revision precondition around
    /// the already-accepted <see cref="SemanticEditTransaction"/> primitive, composing an
    /// ordered list of operations into one candidate and producing a structured diff. No second
    /// validation/compilation path -- every accept/reject decision is
    /// <see cref="SemanticEditTransaction.Apply"/>'s own.
    /// </summary>
    public static class SemanticPatchTransaction
    {
        public static SemanticPatchResult Apply(
            TreeDocument before,
            ulong expectedRevision,
            IReadOnlyList<Func<TreeDocument, TreeDocument>> operations,
            NodeRegistry registry,
            ReferenceCompilerOptions options)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            if (before.Revision.Value != expectedRevision)
            {
                var mismatch = new Diagnostic(
                    SemanticPatchDiagnostics.RevisionMismatch,
                    DiagnosticSeverity.Error,
                    "Expected revision " + expectedRevision + " but the document is at revision " + before.Revision.Value + ".",
                    new DiagnosticLocation(treeId: before.TreeId));
                return new SemanticPatchResult(before, accepted: false, new DiagnosticCollection(new[] { mismatch }), SemanticDiff.Empty);
            }

            var result = Editing.SemanticEditTransaction.Apply(before, Compose(operations), registry, options);
            var diff = result.Accepted ? SemanticDiff.Between(before, result.Document) : SemanticDiff.Empty;
            return new SemanticPatchResult(result.Document, result.Accepted, result.Diagnostics, diff);
        }

        private static Func<TreeDocument, TreeDocument> Compose(IReadOnlyList<Func<TreeDocument, TreeDocument>> operations)
        {
            return document =>
            {
                var current = document;
                for (var index = 0; index < operations.Count; index++)
                {
                    current = operations[index](current);
                }

                return current;
            };
        }
    }
}
