using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIBT.Authoring;

namespace AIBT.Editor.Patching
{
    // Purpose-built node-level diff, per ADR-P6-002 -- not a generic deep-diff library.
    public sealed class SemanticDiff
    {
        private readonly ReadOnlyCollection<SemanticDiffEntry> _entries;

        private SemanticDiff(IReadOnlyList<SemanticDiffEntry> entries)
        {
            _entries = new List<SemanticDiffEntry>(entries).AsReadOnly();
        }

        public IReadOnlyList<SemanticDiffEntry> Entries => _entries;

        public bool IsEmpty => _entries.Count == 0;

        public static SemanticDiff Empty { get; } = new SemanticDiff(Array.Empty<SemanticDiffEntry>());

        public static SemanticDiff Between(TreeDocument before, TreeDocument after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var beforeById = new Dictionary<NodeId, NodeDocument>();
            for (var index = 0; index < before.Nodes.Count; index++)
            {
                beforeById[before.Nodes[index].Id] = before.Nodes[index];
            }

            var afterIds = new HashSet<NodeId>();
            var entries = new List<SemanticDiffEntry>();

            for (var index = 0; index < after.Nodes.Count; index++)
            {
                var afterNode = after.Nodes[index];
                afterIds.Add(afterNode.Id);

                if (!beforeById.TryGetValue(afterNode.Id, out var beforeNode))
                {
                    entries.Add(new SemanticDiffEntry(afterNode.Id, SemanticDiffKind.Added));
                }
                else if (!beforeNode.Equals(afterNode))
                {
                    entries.Add(new SemanticDiffEntry(afterNode.Id, SemanticDiffKind.Changed));
                }
            }

            for (var index = 0; index < before.Nodes.Count; index++)
            {
                var beforeNode = before.Nodes[index];
                if (!afterIds.Contains(beforeNode.Id))
                {
                    entries.Add(new SemanticDiffEntry(beforeNode.Id, SemanticDiffKind.Removed));
                }
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.NodeId.Value, right.NodeId.Value));
            return entries.Count == 0 ? Empty : new SemanticDiff(entries);
        }
    }
}
