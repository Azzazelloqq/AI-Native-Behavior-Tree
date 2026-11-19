using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    /// <summary>
    /// Resolves subtree references against documents already in memory (tests, tools, trees
    /// built at runtime). Two documents with the same tree ID make that ID ambiguous.
    /// </summary>
    public sealed class InMemorySubtreeResolver : ISubtreeResolver
    {
        private readonly Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>> _documents
            = new Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>>();

        public InMemorySubtreeResolver()
        {
        }

        /// <param name="documents">Pairs of logical source ID and document.</param>
        public InMemorySubtreeResolver(IEnumerable<KeyValuePair<string, TreeDocument>> documents)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            foreach (var pair in documents) Add(pair.Value, pair.Key);
        }

        public InMemorySubtreeResolver Add(TreeDocument document, string sourceId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("A source ID is required.", nameof(sourceId));
            if (!_documents.TryGetValue(document.TreeId, out var entries))
            {
                entries = new List<KeyValuePair<string, TreeDocument>>();
                _documents.Add(document.TreeId, entries);
            }

            entries.Add(new KeyValuePair<string, TreeDocument>(sourceId, document));
            return this;
        }

        public SubtreeResolution Resolve(TreeId treeId)
        {
            if (!_documents.TryGetValue(treeId, out var entries)) return SubtreeResolution.NotFound();
            if (entries.Count == 1) return SubtreeResolution.Found(entries[0].Value, entries[0].Key);
            var sources = new List<string>(entries.Count);
            for (var index = 0; index < entries.Count; index++) sources.Add(entries[index].Key);
            return SubtreeResolution.Ambiguous(sources);
        }
    }
}
