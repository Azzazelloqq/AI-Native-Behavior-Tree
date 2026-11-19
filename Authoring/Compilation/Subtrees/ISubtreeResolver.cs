using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    /// <summary>
    /// Finds the document a subtree reference names (ADR-P11-001 §3(a) "Assets"): tree ID →
    /// document and its logical source ID. A resolver never picks one of several documents with
    /// the same tree ID; it reports them as ambiguous (<c>AIBT3021</c>).
    /// </summary>
    public interface ISubtreeResolver
    {
        SubtreeResolution Resolve(TreeId treeId);
    }

    public enum SubtreeResolutionStatus : byte
    {
        Found = 0,
        NotFound = 1,
        Ambiguous = 2,
    }

    /// <summary>The outcome of one <see cref="ISubtreeResolver.Resolve"/> call.</summary>
    public sealed class SubtreeResolution
    {
        private static readonly ReadOnlyCollection<string> NoSources = Array.AsReadOnly(Array.Empty<string>());

        private SubtreeResolution(
            SubtreeResolutionStatus status,
            TreeDocument document,
            string sourceId,
            IReadOnlyList<string> candidateSourceIds,
            string detail)
        {
            Status = status;
            Document = document;
            SourceId = sourceId;
            CandidateSourceIds = candidateSourceIds ?? NoSources;
            Detail = detail;
        }

        public SubtreeResolutionStatus Status { get; }

        /// <summary>The resolved document; null unless <see cref="Status"/> is Found.</summary>
        public TreeDocument Document { get; }

        /// <summary>
        /// The resolved document's logical source ID (relative, forward slashes, no ':'); it becomes
        /// the debug-map source path of the subtree's inner nodes.
        /// </summary>
        public string SourceId { get; }

        /// <summary>Every source that declares the tree ID, when ambiguous.</summary>
        public IReadOnlyList<string> CandidateSourceIds { get; }

        /// <summary>Optional human-readable detail appended to a not-found diagnostic.</summary>
        public string Detail { get; }

        public static SubtreeResolution Found(TreeDocument document, string sourceId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("A source ID is required.", nameof(sourceId));
            return new SubtreeResolution(SubtreeResolutionStatus.Found, document, sourceId, null, null);
        }

        public static SubtreeResolution NotFound(string detail = null)
            => new SubtreeResolution(SubtreeResolutionStatus.NotFound, null, null, null, detail);

        public static SubtreeResolution Ambiguous(IEnumerable<string> candidateSourceIds)
        {
            if (candidateSourceIds == null) throw new ArgumentNullException(nameof(candidateSourceIds));
            var candidates = new List<string>(candidateSourceIds);
            candidates.Sort(StringComparer.Ordinal);
            return new SubtreeResolution(SubtreeResolutionStatus.Ambiguous, null, null, candidates.AsReadOnly(), null);
        }
    }
}
