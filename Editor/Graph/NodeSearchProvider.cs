using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>Searchable node list for <see cref="SearchWindow"/>, grouped by node kind.</summary>
    internal sealed class NodeSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static Texture2D s_indent;

        internal NodeRegistry Registry { get; set; }

        internal string Title { get; set; } = "Create Node";

        internal Action<NodeManifest, Vector2> Selected { get; set; }

        /// <summary>Shows the "Subtree…" entry (ADR-P11-001 §4.7) at the top of the list.</summary>
        internal bool IncludeSubtree { get; set; }

        /// <summary>The "Subtree…" entry was chosen; the host opens the tree picker.</summary>
        internal Action<Vector2> SubtreeSelected { get; set; }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context) => BuildTree(Registry, Title, IncludeSubtree);

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is SubtreeEntry)
            {
                SubtreeSelected?.Invoke(context.screenMousePosition);
                return true;
            }
            if (!(entry.userData is NodeManifest manifest)) return false;
            Selected?.Invoke(manifest, context.screenMousePosition);
            return true;
        }

        /// <summary>Marks the "Subtree…" entry of the list.</summary>
        internal sealed class SubtreeEntry
        {
            internal static readonly SubtreeEntry Instance = new SubtreeEntry();

            private SubtreeEntry()
            {
            }
        }

        internal const string SubtreeEntryLabel = "Subtree…";

        internal static List<SearchTreeEntry> BuildTree(NodeRegistry registry, string title) => BuildTree(registry, title, false);

        /// <summary>The node list; with <paramref name="includeSubtree"/> a "Subtree…" entry comes first.</summary>
        internal static List<SearchTreeEntry> BuildTree(NodeRegistry registry, string title, bool includeSubtree)
        {
            var entries = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent(title), 0) };
            if (includeSubtree)
                entries.Add(new SearchTreeEntry(new GUIContent(SubtreeEntryLabel, Indent(), "Use another tree of the project here: pick it from the list."))
                    { level = 1, userData = SubtreeEntry.Instance });
            if (registry == null) return entries;
            var manifests = registry.Select(entry => entry.Manifest)
                .Where(manifest => string.IsNullOrEmpty(manifest.ReplacementTypeId))
                .ToList();
            var titles = manifests.ToDictionary(manifest => manifest.TypeId, manifest => BehaviorTreeNode.ReadableTitle(manifest.TypeId));
            foreach (var kind in new[] { NodeBehaviorKind.Composite, NodeBehaviorKind.Decorator, NodeBehaviorKind.Condition, NodeBehaviorKind.Action })
            {
                var group = manifests.Where(manifest => manifest.Kind == kind)
                    .OrderBy(manifest => titles[manifest.TypeId], StringComparer.OrdinalIgnoreCase)
                    .ThenBy(manifest => manifest.TypeId, StringComparer.Ordinal)
                    .ToList();
                if (group.Count == 0) continue;
                entries.Add(new SearchTreeGroupEntry(new GUIContent(GroupTitle(kind)), 1));
                if (kind == NodeBehaviorKind.Composite || kind == NodeBehaviorKind.Decorator)
                {
                    // Few structural nodes: one flat list, the category shown after the title.
                    foreach (var manifest in group) entries.Add(Entry(manifest, Label(manifest, group, titles, true), 2));
                    continue;
                }

                // Leaves are many: one sub-group per category (Movement, Blackboard, Animation...).
                foreach (var category in group.GroupBy(CategoryOf).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    entries.Add(new SearchTreeGroupEntry(new GUIContent(category.Key), 2));
                    foreach (var manifest in category) entries.Add(Entry(manifest, Label(manifest, group, titles, false), 3));
                }
            }

            return entries;
        }

        internal static string CategoryOf(NodeManifest manifest)
            => string.IsNullOrWhiteSpace(manifest.Category) ? "Other" : manifest.Category.Trim();

        private static string Label(NodeManifest manifest, List<NodeManifest> group, Dictionary<string, string> titles, bool withCategory)
        {
            var label = titles[manifest.TypeId];
            if (group.Count(other => titles[other.TypeId] == label) > 1) return label + "  (" + manifest.TypeId + ")";
            return withCategory && !string.IsNullOrEmpty(manifest.Category) ? label + "  · " + manifest.Category : label;
        }

        internal static SearchTreeEntry Entry(NodeManifest manifest, string label, int level)
            => new SearchTreeEntry(new GUIContent(label, Indent(), manifest.Summary)) { level = level, userData = manifest };

        private static string GroupTitle(NodeBehaviorKind kind)
        {
            switch (kind)
            {
                case NodeBehaviorKind.Composite: return "Composites (flow)";
                case NodeBehaviorKind.Decorator: return "Decorators";
                case NodeBehaviorKind.Condition: return "Conditions";
                default: return "Actions";
            }
        }

        // SearchWindow aligns entries without an icon differently from groups; a blank icon keeps them in line.
        internal static Texture2D Indent()
        {
            if (s_indent != null) return s_indent;
            s_indent = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            s_indent.SetPixel(0, 0, Color.clear);
            s_indent.Apply();
            return s_indent;
        }
    }
}

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// The tree picker behind the palette's "Subtree…" entry (ADR-P11-001 §4.7): the project trees
    /// the window's tree list shows, without the open tree itself.
    /// </summary>
    internal sealed class SubtreePickerProvider : ScriptableObject, ISearchWindowProvider
    {
        internal IReadOnlyList<TreeCatalogEntry> Entries { get; set; }

        internal string CurrentPath { get; set; }

        internal string CurrentTreeId { get; set; }

        internal Action<TreeCatalogEntry> Selected { get; set; }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context) => BuildTree(Entries, CurrentPath, CurrentTreeId);

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (!(entry.userData is TreeCatalogEntry tree)) return false;
            Selected?.Invoke(tree);
            return true;
        }

        internal static List<SearchTreeEntry> BuildTree(IEnumerable<TreeCatalogEntry> trees, string currentPath, string currentTreeId)
        {
            var entries = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Pick Subtree"), 0) };
            if (trees == null) return entries;
            foreach (var tree in trees
                         .Where(item => !string.IsNullOrEmpty(item.TreeId) && item.TreeId != currentTreeId
                                        && !string.Equals(item.Path, currentPath, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.TreeId, StringComparer.Ordinal))
            {
                entries.Add(new SearchTreeEntry(new GUIContent(tree.Name + "  (" + tree.TreeId + ")", NodeSearchProvider.Indent(),
                    tree.Path + "\n" + tree.Status)) { level = 1, userData = tree });
            }
            return entries;
        }
    }
}
