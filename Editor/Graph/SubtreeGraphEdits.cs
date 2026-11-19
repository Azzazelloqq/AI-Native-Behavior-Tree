using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using AIBT.Editor.Editing;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Graph-editor edits for subtree references (ADR-P11-001 §4.7): add a reference, edit its key
    /// map, create a missing parent key, extract nodes into a new subtree and unpack a reference.
    /// Pure functions on immutable documents, applied by the window as one undo step each.
    /// </summary>
    internal static class SubtreeGraphEdits
    {
        /// <summary>Generated reference IDs stay short: every inner ID of the instance is prefixed with it.</summary>
        internal const int MaximumGeneratedIdLength = 24;

        // ---------------------------------------------------------------- identities

        /// <summary>
        /// A short reference ID made from <paramref name="seed"/> (lowercase letters, digits and
        /// hyphens, no ':'), unique among the document's node IDs and not the first segment of an
        /// existing expanded-looking ID, with "-2", "-3"… appended when taken.
        /// </summary>
        internal static NodeId NewReferenceId(TreeDocument document, string seed)
        {
            var slug = Slug(seed, MaximumGeneratedIdLength - 3);
            if (slug.Length == 0) slug = "subtree";
            var ids = new HashSet<string>(document.Nodes.Select(node => node.Id.Value), StringComparer.Ordinal);
            for (var number = 1; ; number++)
            {
                var candidate = number == 1 ? slug : slug + "-" + number;
                if (ids.Contains(candidate) || ids.Any(id => id.StartsWith(candidate + ":", StringComparison.Ordinal))) continue;
                return new NodeId(candidate);
            }
        }

        /// <summary>The last segment of a tree ID: "combat.attack" → "attack".</summary>
        internal static string SeedFor(TreeId treeId)
        {
            var value = treeId.Value ?? string.Empty;
            var dot = value.LastIndexOf('.');
            return dot >= 0 && dot < value.Length - 1 ? value.Substring(dot + 1) : value;
        }

        internal static string Slug(string text, int maximumLength)
        {
            var builder = new StringBuilder();
            foreach (var character in (text ?? string.Empty).ToLowerInvariant())
            {
                if (character >= 'a' && character <= 'z' || character >= '0' && character <= '9') builder.Append(character);
                else if (builder.Length > 0 && builder[builder.Length - 1] != '-') builder.Append('-');
            }
            var slug = builder.ToString().Trim('-');
            if (slug.Length > maximumLength) slug = slug.Substring(0, maximumLength).TrimEnd('-');
            return slug;
        }

        // ---------------------------------------------------------------- live and diagnostic mapping

        /// <summary>
        /// The node of <paramref name="document"/> that shows the expanded node
        /// <paramref name="expandedId"/> of the instance <paramref name="prefix"/> ("" for the tree
        /// itself, "r:" or "r1:r2:" for a subtree opened from a parent): the node itself, or the
        /// reference node whose instance contains it (reference IDs contain no ':', so the first
        /// segment names the reference). Null when the ID belongs to another instance or to no node.
        /// </summary>
        internal static NodeId? MapExpandedId(TreeDocument document, string prefix, string expandedId)
        {
            if (document == null || string.IsNullOrEmpty(expandedId)) return null;
            var id = expandedId;
            if (!string.IsNullOrEmpty(prefix))
            {
                if (!id.StartsWith(prefix, StringComparison.Ordinal)) return null;
                id = id.Substring(prefix.Length);
            }
            if (id.Length == 0) return null;
            var direct = GraphDocumentEdits.Find(document, new NodeId(id));
            if (direct != null) return direct.Id;
            var colon = id.IndexOf(':');
            if (colon <= 0) return null;
            var reference = GraphDocumentEdits.Find(document, new NodeId(id.Substring(0, colon)));
            return reference != null && reference.Subtree != null ? reference.Id : (NodeId?)null;
        }

        /// <summary>
        /// True when every node of <paramref name="subtree"/> has its expanded counterpart in
        /// <paramref name="expandedIds"/> under <paramref name="prefix"/>: an ordinary node as
        /// <c>prefix + id</c>, a reference node as at least one <c>prefix + id + ":…"</c>.
        /// </summary>
        internal static bool InstanceMatches(TreeDocument subtree, string prefix, ICollection<string> expandedIds)
        {
            if (subtree == null || string.IsNullOrEmpty(prefix) || expandedIds == null) return false;
            foreach (var node in subtree.Nodes)
            {
                var id = prefix + node.Id.Value;
                if (node.Subtree == null)
                {
                    if (!expandedIds.Contains(id)) return false;
                }
                else if (!expandedIds.Any(candidate => candidate.StartsWith(id + ":", StringComparison.Ordinal))) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- add and bind

        /// <summary>The document in tree format version 3, needed for a reference node; unchanged when it already is.</summary>
        internal static TreeDocument WithSubtreeFormat(TreeDocument document)
            => document.FormatVersion >= TreeDocument.SubtreeFormatVersion
                ? document
                : GraphDocumentEdits.Rebuild(document, document.Nodes, document.Blackboard, TreeDocument.SubtreeFormatVersion);

        /// <summary>
        /// A reference node to <paramref name="subtree"/> written for its current interface version,
        /// with every interface key that has a same-named, identically typed Tree key in
        /// <paramref name="parent"/> connected to it.
        /// </summary>
        internal static NodeDocument CreateReference(TreeDocument parent, TreeDocument subtree, NodeId id)
        {
            var version = subtree.Interface?.Version ?? 1u;
            var reference = new SubtreeReference(subtree.TreeId, version == 0 ? 1u : version, SameNamedBindings(parent, subtree, null));
            return NodeDocument.CreateSubtreeReference(id, reference, tags: TagSet.Empty);
        }

        /// <summary>Adds <paramref name="reference"/> as the last child of <paramref name="parentId"/>, or unattached.</summary>
        internal static TreeDocument AddReference(TreeDocument document, NodeDocument reference, NodeId? parentId)
        {
            var upgraded = WithSubtreeFormat(document);
            return parentId.HasValue
                ? SemanticEditOperations.AddNode(upgraded, reference, parentId.Value)
                : GraphDocumentEdits.AddDetachedNode(upgraded, reference);
        }

        /// <summary>
        /// The existing bindings of <paramref name="existing"/> plus, for each still unconnected
        /// interface key of <paramref name="subtree"/>, a binding to the parent Tree key of the same
        /// ID when its type is identical.
        /// </summary>
        internal static List<SubtreeKeyBinding> SameNamedBindings(TreeDocument parent, TreeDocument subtree, IEnumerable<SubtreeKeyBinding> existing)
        {
            var result = existing?.ToList() ?? new List<SubtreeKeyBinding>();
            if (subtree?.Interface == null) return result;
            foreach (var key in subtree.Interface.Keys)
            {
                if (result.Any(binding => binding.InterfaceKey == key)) continue;
                var inner = subtree.Blackboard.FirstOrDefault(candidate => candidate.Id == key);
                var outer = parent.Blackboard.FirstOrDefault(candidate => candidate.Id == key && candidate.Scope == BlackboardScope.Tree);
                if (inner != null && outer != null && outer.Type == inner.Type) result.Add(SubtreeKeyBinding.ToParentKey(key, outer.Id));
            }
            return result;
        }

        /// <summary>Connects every still unconnected interface key to a same-named parent key where one fits.</summary>
        internal static TreeDocument ConnectSameNamedKeys(TreeDocument document, NodeId referenceId, TreeDocument subtree)
        {
            var node = Reference(document, referenceId);
            return WithReference(document, node, new SubtreeReference(node.Subtree.TreeId, node.Subtree.InterfaceVersion,
                SameNamedBindings(document, subtree, node.Subtree.Keys)));
        }

        /// <summary>Sets (or, with a null <paramref name="binding"/>, removes) one entry of the reference's key map.</summary>
        internal static TreeDocument SetKeyBinding(TreeDocument document, NodeId referenceId, string interfaceKey, SubtreeKeyBinding binding)
        {
            if (binding != null && binding.InterfaceKey != interfaceKey)
                throw new ArgumentException("The binding is for another interface key.", nameof(binding));
            var node = Reference(document, referenceId);
            var keys = node.Subtree.Keys.Where(item => item.InterfaceKey != interfaceKey).ToList();
            if (binding != null) keys.Add(binding);
            return WithReference(document, node, new SubtreeReference(node.Subtree.TreeId, node.Subtree.InterfaceVersion, keys));
        }

        /// <summary>Re-targets the reference to another interface version of its subtree.</summary>
        internal static TreeDocument SetInterfaceVersion(TreeDocument document, NodeId referenceId, uint version)
        {
            var node = Reference(document, referenceId);
            return WithReference(document, node, new SubtreeReference(node.Subtree.TreeId, version, node.Subtree.Keys));
        }

        /// <summary>
        /// Adds a parent Tree key with the ID, type and default of the subtree's interface key
        /// <paramref name="subtreeKey"/> and connects the interface key to it; one edit. When the
        /// parent already has that key with the same type it is only connected.
        /// </summary>
        internal static TreeDocument CreateParentKey(TreeDocument document, NodeId referenceId, BlackboardKeyDefinition subtreeKey)
        {
            if (subtreeKey == null) throw new ArgumentNullException(nameof(subtreeKey));
            Reference(document, referenceId);
            var existing = document.Blackboard.FirstOrDefault(key => key.Id == subtreeKey.Id);
            if (existing != null && (existing.Scope != BlackboardScope.Tree || existing.Type != subtreeKey.Type))
                throw new ArgumentException("The tree already has a key '" + subtreeKey.Id + "' of another type or scope.");
            var withKey = existing != null
                ? document
                : SemanticEditOperations.SetBlackboardKey(document, new BlackboardKeyDefinition(subtreeKey.Id, subtreeKey.Name ?? subtreeKey.Id,
                    subtreeKey.Type, BlackboardScope.Tree, subtreeKey.DefaultValue, subtreeKey.Description, subtreeKey.Reduction));
            return SetKeyBinding(withKey, referenceId, subtreeKey.Id, SubtreeKeyBinding.ToParentKey(subtreeKey.Id, subtreeKey.Id));
        }

        // ---------------------------------------------------------------- extract

        /// <summary>The result of <see cref="Extract"/>: the parent with a reference in place of the nodes, and the new subtree.</summary>
        internal sealed class Extraction
        {
            internal TreeDocument Parent;
            internal TreeDocument Subtree;
            internal NodeId ReferenceId;
        }

        /// <summary>
        /// Moves <paramref name="top"/> and every node below it into a new tree
        /// <paramref name="treeId"/> and puts one reference node in its place. The new tree keeps the
        /// node IDs, has <paramref name="top"/> as root and declares every parent Tree key the moved
        /// nodes use as an interface key (version 1); the reference connects each to the parent key of
        /// the same ID, so the expansion behaves as before (only node IDs gain the reference prefix).
        /// Agent and Shared keys come along with their scope's complete contract.
        /// </summary>
        internal static Extraction Extract(TreeDocument document, NodeId top, TreeId treeId, string name, NodeRegistry registry)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var topNode = GraphDocumentEdits.Find(document, top) ?? throw new ArgumentException("Node '" + top.Value + "' does not exist.");
            if (top == document.Root) throw new ArgumentException("The root cannot be extracted: a tree's root cannot be a subtree reference.");
            if (!treeId.IsValid) throw new ArgumentException("The new tree needs a valid tree ID.");
            if (treeId == document.TreeId) throw new ArgumentException("The new tree needs a tree ID of its own.");

            var moved = GraphDocumentEdits.Subtree(document, top);
            var movedNodes = document.Nodes.Where(node => moved.Contains(node.Id)).ToList();
            var used = UsedKeys(movedNodes, registry);
            var keys = document.Blackboard.Where(key => used.Contains(key.Id)).ToList();
            var treeKeys = keys.Where(key => key.Scope == BlackboardScope.Tree).ToList();
            var usesAgent = keys.Any(key => key.Scope == BlackboardScope.Agent);
            var usesShared = keys.Any(key => key.Scope == BlackboardScope.Shared);
            var subtreeKeys = new List<BlackboardKeyDefinition>(treeKeys);
            if (usesAgent) subtreeKeys.AddRange(document.Blackboard.Where(key => key.Scope == BlackboardScope.Agent));
            if (usesShared) subtreeKeys.AddRange(document.Blackboard.Where(key => key.Scope == BlackboardScope.Shared));

            var subtreeInterface = treeKeys.Count == 0 ? null : new SubtreeInterface(1, treeKeys.Select(key => key.Id));
            var subtree = new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.SubtreeFormatVersion, treeId,
                string.IsNullOrWhiteSpace(name) ? treeId.Value : name.Trim(), top, movedNodes, subtreeKeys, null, TagSet.Empty,
                SemanticObject.Empty, default, usesAgent ? document.AgentContract : null, usesShared ? document.SharedContract : null,
                subtreeInterface);

            var remaining = document.Nodes.Where(node => !moved.Contains(node.Id)).ToList();
            var probe = GraphDocumentEdits.Rebuild(document, remaining);
            var referenceId = NewReferenceId(probe, string.IsNullOrEmpty(topNode.DisplayName) ? SeedFor(treeId) : topNode.DisplayName);
            var reference = NodeDocument.CreateSubtreeReference(referenceId,
                new SubtreeReference(treeId, 1, treeKeys.Select(key => SubtreeKeyBinding.ToParentKey(key.Id, key.Id))),
                displayName: topNode.DisplayName, tags: TagSet.Empty);
            var nodes = new List<NodeDocument>();
            foreach (var node in remaining)
            {
                nodes.Add(node.Children.Contains(top)
                    ? node.WithChildren(node.Children.Select(child => child == top ? referenceId : child))
                    : node);
            }
            nodes.Add(reference);
            var parent = GraphDocumentEdits.Rebuild(document, nodes, document.Blackboard,
                Math.Max(document.FormatVersion, TreeDocument.SubtreeFormatVersion));
            return new Extraction { Parent = parent, Subtree = subtree, ReferenceId = referenceId };
        }

        /// <summary>Every blackboard key ID the nodes reach: manifest accesses, key parameters, bindings, watched keys, reference key maps.</summary>
        internal static HashSet<string> UsedKeys(IEnumerable<NodeDocument> nodes, NodeRegistry registry)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node.Subtree != null)
                {
                    foreach (var binding in node.Subtree.Keys)
                        if (!binding.IsConstant && binding.ParentKey != null) used.Add(binding.ParentKey);
                    continue;
                }
                if (registry != null && registry.TryGet(node.TypeId, out var entry))
                {
                    foreach (var access in entry.Manifest.Accesses) used.Add(access.Key);
                    foreach (var parameter in entry.Manifest.Parameters.Where(item => item.Type == NodeParameterType.BlackboardKey))
                        if (node.Parameters != null && node.Parameters.TryGetValue(parameter.Name, out var value) && value != null
                            && value.TryGetString(out var keyId) && !string.IsNullOrEmpty(keyId)) used.Add(keyId);
                }
                if (node.Bindings != null) foreach (var pair in node.Bindings.Values) used.Add(pair.Value);
                if (node.Observer != null) foreach (var key in node.Observer.WatchedKeys) if (key != null) used.Add(key);
            }
            return used;
        }

        // ---------------------------------------------------------------- unpack

        /// <summary>
        /// Replaces the reference <paramref name="referenceId"/> by a plain copy of its expansion, as
        /// the compiler would see it (ADR-P11-001 §4.4): inner node <c>n</c> becomes <c>r:n</c> with
        /// the label "reference / inner", private and constant-bound keys become Tree keys
        /// <c>r:key</c> (a constant becomes that key's default), interface keys bound to parent keys
        /// use those keys, and nested references are expanded too (an expanded ID contains ':',
        /// which a reference ID may not). Only this reference is expanded, so problems elsewhere in
        /// the tree do not block it. Returns null with the expansion's diagnostics when it fails.
        /// </summary>
        internal static TreeDocument Unpack(TreeDocument document, NodeId referenceId, ISubtreeResolver resolver, NodeRegistry registry,
            RegisteredBlackboardTypeCatalog registeredTypes, out DiagnosticCollection diagnostics, out NodeId innerRoot)
        {
            innerRoot = default;
            var reference = Reference(document, referenceId);
            var probeRootId = NewReferenceId(document, "aibt-unpack-root");
            var probeRoot = new NodeDocument(probeRootId, "aibt.core.memory-sequence", 1, children: new[] { referenceId },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var probe = new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.SubtreeFormatVersion, document.TreeId, document.Name,
                probeRootId, new[] { probeRoot, reference }, document.Blackboard, null, TagSet.Empty, SemanticObject.Empty, default,
                document.AgentContract, document.SharedContract, null);
            var expansion = SubtreeExpander.Expand(probe, resolver, registry, UnpackPolicy.CreateValidationOptions(EditorDocumentId), registeredTypes);
            if (!expansion.Success)
            {
                diagnostics = expansion.Diagnostics;
                return null;
            }

            var prefix = referenceId.Value + ":";
            var expandedRoot = GraphDocumentEdits.Find(expansion.Document, probeRootId);
            innerRoot = expandedRoot.Children[0];
            var inner = expansion.Document.Nodes.Where(node => node.Id.Value.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            var existingIds = new HashSet<NodeId>(document.Nodes.Select(node => node.Id));
            var collisions = inner.Where(node => existingIds.Contains(node.Id)).Select(node => node.Id.Value).ToList();
            var newKeys = expansion.Document.Blackboard.Where(key => document.Blackboard.All(existing => existing.Id != key.Id)).ToList();
            if (collisions.Count != 0)
            {
                diagnostics = new DiagnosticCollection(new[]
                {
                    new Diagnostic(new DiagnosticCode("AIBT3027"), DiagnosticSeverity.Error,
                        "Unpacking '" + referenceId.Value + "' would add node IDs the tree already has: " + string.Join(", ", collisions) + ".",
                        new DiagnosticLocation(EditorDocumentId, nodeId: referenceId)),
                });
                return null;
            }

            var root = innerRoot;
            var nodes = new List<NodeDocument>();
            foreach (var node in document.Nodes)
            {
                if (node.Id == referenceId) continue;
                nodes.Add(node.Children.Contains(referenceId)
                    ? node.WithChildren(node.Children.Select(child => child == referenceId ? root : child))
                    : node);
            }
            nodes.AddRange(inner);
            diagnostics = DiagnosticCollection.Empty;
            return GraphDocumentEdits.Rebuild(document, nodes, document.Blackboard.Concat(newKeys), document.FormatVersion);
        }

        /// <summary>The document ID the graph editor validates its open tree under.</summary>
        internal const string EditorDocumentId = "trees/editor.aibt.json";

        // Structural checks only, as at import (ProjectTreeIndex.Expand): policies apply when the result is compiled.
        private static readonly ReferenceCompilationPolicy UnpackPolicy = new ReferenceCompilationPolicy(
            allowManagedNodes: true, allowMainThreadNodes: true, requireDeterministicNodes: false,
            supportsAgentScope: true, supportsSharedScope: true);

        // ---------------------------------------------------------------- helpers

        private static NodeDocument Reference(TreeDocument document, NodeId id)
        {
            var node = GraphDocumentEdits.Find(document, id) ?? throw new ArgumentException("Node '" + id.Value + "' does not exist.");
            if (node.Subtree == null) throw new ArgumentException("Node '" + id.Value + "' is not a subtree reference.");
            return node;
        }

        private static TreeDocument WithReference(TreeDocument document, NodeDocument node, SubtreeReference reference)
        {
            if (Equals(node.Subtree, reference)) return document;
            var updated = node.WithSubtree(reference);
            return GraphDocumentEdits.Rebuild(document, document.Nodes.Select(candidate => candidate.Id == node.Id ? updated : candidate));
        }
    }
}
