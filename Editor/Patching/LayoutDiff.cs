using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AIBT.Editor.Layout;

namespace AIBT.Editor.Patching
{
    // Purpose-built field-level diff over LayoutDocument, per ADR-P6-002 -- not a generic
    // deep-diff library.
    public sealed class LayoutDiff
    {
        private readonly ReadOnlyCollection<LayoutDiffEntry> _entries;

        private LayoutDiff(IReadOnlyList<LayoutDiffEntry> entries)
        {
            _entries = new List<LayoutDiffEntry>(entries).AsReadOnly();
        }

        public IReadOnlyList<LayoutDiffEntry> Entries => _entries;

        public bool IsEmpty => _entries.Count == 0;

        public static LayoutDiff Empty { get; } = new LayoutDiff(Array.Empty<LayoutDiffEntry>());

        public static LayoutDiff Between(LayoutDocument before, LayoutDocument after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var entries = new List<LayoutDiffEntry>();

            foreach (var kvp in after.Nodes)
            {
                if (!before.Nodes.TryGetValue(kvp.Key, out var beforePlacement))
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Node, kvp.Key.Value, LayoutDiffKind.Added));
                    continue;
                }

                if (beforePlacement.Pinned != kvp.Value.Pinned)
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Node, kvp.Key.Value, LayoutDiffKind.PinChanged));
                }

                if (!beforePlacement.Position.Equals(kvp.Value.Position))
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Node, kvp.Key.Value, LayoutDiffKind.Moved));
                }
            }

            foreach (var kvp in before.Nodes)
            {
                if (!after.Nodes.ContainsKey(kvp.Key))
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Node, kvp.Key.Value, LayoutDiffKind.Removed));
                }
            }

            DiffMap(before.Groups, after.Groups, LayoutDiffTarget.Group, GroupsEqual, entries);
            DiffMap(before.Notes, after.Notes, LayoutDiffTarget.Note, NotesEqual, entries);
            DiffReroutes(before.Reroutes, after.Reroutes, entries);

            entries.Sort((left, right) =>
            {
                var target = left.Target.CompareTo(right.Target);
                return target != 0 ? target : string.CompareOrdinal(left.Key, right.Key);
            });
            return entries.Count == 0 ? Empty : new LayoutDiff(entries);
        }

        private static void DiffMap<TValue>(
            IReadOnlyDictionary<string, TValue> before,
            IReadOnlyDictionary<string, TValue> after,
            LayoutDiffTarget target,
            Func<TValue, TValue, bool> equal,
            List<LayoutDiffEntry> entries)
        {
            foreach (var kvp in after)
            {
                if (!before.TryGetValue(kvp.Key, out var beforeValue))
                {
                    entries.Add(new LayoutDiffEntry(target, kvp.Key, LayoutDiffKind.Added));
                }
                else if (!equal(beforeValue, kvp.Value))
                {
                    entries.Add(new LayoutDiffEntry(target, kvp.Key, LayoutDiffKind.Changed));
                }
            }

            foreach (var kvp in before)
            {
                if (!after.ContainsKey(kvp.Key))
                {
                    entries.Add(new LayoutDiffEntry(target, kvp.Key, LayoutDiffKind.Removed));
                }
            }
        }

        private static void DiffReroutes(
            IReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> before,
            IReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> after,
            List<LayoutDiffEntry> entries)
        {
            foreach (var kvp in after)
            {
                if (!before.TryGetValue(kvp.Key, out var beforeValue))
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Reroute, kvp.Key.ToKeyString(), LayoutDiffKind.Added));
                }
                else if (!kvp.Value.Waypoints.SequenceEqual(beforeValue.Waypoints))
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Reroute, kvp.Key.ToKeyString(), LayoutDiffKind.Changed));
                }
            }

            foreach (var kvp in before)
            {
                if (!after.ContainsKey(kvp.Key))
                {
                    entries.Add(new LayoutDiffEntry(LayoutDiffTarget.Reroute, kvp.Key.ToKeyString(), LayoutDiffKind.Removed));
                }
            }
        }

        private static bool GroupsEqual(LayoutGroup a, LayoutGroup b)
        {
            return string.Equals(a.Title, b.Title, StringComparison.Ordinal)
                && string.Equals(a.Description, b.Description, StringComparison.Ordinal)
                && string.Equals(a.Color, b.Color, StringComparison.Ordinal)
                && a.Locked == b.Locked
                && a.MemberNodeIds.SequenceEqual(b.MemberNodeIds);
        }

        private static bool NotesEqual(LayoutNote a, LayoutNote b)
        {
            return string.Equals(a.Text, b.Text, StringComparison.Ordinal)
                && a.Position.Equals(b.Position)
                && a.Size.Equals(b.Size)
                && string.Equals(a.Color, b.Color, StringComparison.Ordinal);
        }
    }
}
