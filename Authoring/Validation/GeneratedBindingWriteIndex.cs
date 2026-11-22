using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AIBT.Authoring
{
    /// <summary>
    /// P11-003: which generated (native) blackboard bindings of a node type write a Tree-scope key,
    /// so <see cref="TreeValidator"/> can tell a key a native node writes through its
    /// <c>bindings</c> from an input key (AIBT2053). The node manifest has no access data for
    /// bindings (its reads/writes are empty for generated nodes); the access lives only in
    /// <see cref="GeneratedNodeDescriptor.Bindings"/>, and the validator is given only a
    /// <see cref="NodeRegistry"/>. A registry entry built from a descriptor holds the descriptor's
    /// own manifest instance, so the index is keyed by that instance: each descriptor registers
    /// itself on construction, and the validator looks the entry's manifest up.
    /// <para>
    /// Only members whose access is Write or ReadWrite and whose scope is Tree are recorded --
    /// the same members whose compiled access record makes the native host refuse an external
    /// write (<c>TryResolveExternalTreeWrite</c>). A manifest that was not registered (managed or
    /// test-fixture nodes, a manifest rebuilt from JSON) is unknown here, and the validator keeps
    /// its earlier behaviour for it. When several descriptors share one manifest instance, only the
    /// members every one of them writes are kept (intersection), so an ambiguous member never
    /// removes the hint.
    /// </para>
    /// </summary>
    internal static class GeneratedBindingWriteIndex
    {
        private static readonly ConditionalWeakTable<NodeManifest, Entry> Entries = new ConditionalWeakTable<NodeManifest, Entry>();

        internal static void Register(NodeManifest manifest, IReadOnlyList<GeneratedBindingDescriptor> bindings)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            var written = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding.Scope == BlackboardScope.Tree
                    && (binding.Kind == GeneratedBindingKind.BlackboardWrite || binding.Kind == GeneratedBindingKind.BlackboardReadWrite))
                    written.Add(binding.BindingId);
            }

            // GetValue creates the entry atomically (ConditionalWeakTable.Add would throw on a
            // second descriptor for the same manifest instance); the merge runs under the entry lock.
            var entry = Entries.GetValue(manifest, _ => new Entry());
            lock (entry)
            {
                if (entry.Written == null) entry.Written = written;
                else entry.Written.IntersectWith(written);
            }
        }

        /// <summary>
        /// True when <paramref name="manifest"/> was registered by a generated descriptor;
        /// <paramref name="written"/> then holds a snapshot of its Tree-scope Write/ReadWrite binding IDs.
        /// </summary>
        internal static bool TryGetWrittenBindings(NodeManifest manifest, out HashSet<string> written)
        {
            written = null;
            if (manifest == null || !Entries.TryGetValue(manifest, out var entry)) return false;
            lock (entry)
            {
                if (entry.Written == null) return false;
                written = new HashSet<string>(entry.Written, StringComparer.Ordinal);
                return true;
            }
        }

        private sealed class Entry
        {
            internal HashSet<string> Written;
        }
    }
}
