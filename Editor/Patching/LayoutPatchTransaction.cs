using System;
using System.Collections.Generic;
using AIBT.Editor.Layout;

namespace AIBT.Editor.Patching
{
    /// <summary>
    /// Implements ADR-P6-002's layout-patch model: a content-hash precondition (LayoutDocument
    /// has no revision field) around composed <see cref="AIBT.Editor.Organization.LayoutOrganizationOperations"/>
    /// calls. Unlike the semantic case, layout operations have no compiler/validator -- the one
    /// real failure mode is a thrown <see cref="ArgumentException"/> (e.g. a group-membership
    /// conflict), caught here and turned into the same accept-or-reject-unchanged contract
    /// <see cref="SemanticPatchTransaction"/> provides.
    /// </summary>
    public static class LayoutPatchTransaction
    {
        public static LayoutPatchResult Apply(
            LayoutDocument before,
            string expectedHash,
            IReadOnlyList<Func<LayoutDocument, LayoutDocument>> operations)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            var beforeHash = ComputeHash(before);
            if (!string.Equals(beforeHash, expectedHash, StringComparison.Ordinal))
            {
                var mismatch = new Diagnostic(
                    LayoutPatchDiagnostics.HashMismatch,
                    DiagnosticSeverity.Error,
                    "Expected layout hash " + expectedHash + " but the document hash is " + beforeHash + ".",
                    new DiagnosticLocation(treeId: before.TreeId));
                return new LayoutPatchResult(before, accepted: false, new DiagnosticCollection(new[] { mismatch }), LayoutDiff.Empty, beforeHash);
            }

            LayoutDocument candidate;
            try
            {
                candidate = before;
                for (var index = 0; index < operations.Count; index++)
                {
                    candidate = operations[index](candidate);
                }
            }
            catch (ArgumentException ex)
            {
                var rejected = new Diagnostic(
                    LayoutPatchDiagnostics.OperationRejected,
                    DiagnosticSeverity.Error,
                    ex.Message,
                    new DiagnosticLocation(treeId: before.TreeId));
                return new LayoutPatchResult(before, accepted: false, new DiagnosticCollection(new[] { rejected }), LayoutDiff.Empty, beforeHash);
            }

            var diff = LayoutDiff.Between(before, candidate);
            var afterHash = ComputeHash(candidate);
            return new LayoutPatchResult(candidate, accepted: true, new DiagnosticCollection(Array.Empty<Diagnostic>()), diff, afterHash);
        }

        private static string ComputeHash(LayoutDocument document)
        {
            return StableHash.Sha256Hex(CanonicalLayoutJsonWriter.Write(document));
        }
    }
}
