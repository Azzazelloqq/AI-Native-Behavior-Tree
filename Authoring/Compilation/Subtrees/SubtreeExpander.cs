using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    /// <summary>
    /// The result of <see cref="SubtreeExpander.Expand"/>: the expanded document the compiler sees
    /// (ADR-P11-001 §4.4) and, for every node that came from a subtree, that subtree's source ID.
    /// </summary>
    public sealed class SubtreeExpansion
    {
        private static readonly IReadOnlyDictionary<NodeId, string> NoSources
            = new ReadOnlyDictionary<NodeId, string>(new Dictionary<NodeId, string>());

        internal SubtreeExpansion(
            TreeDocument sourceDocument,
            TreeDocument document,
            IDictionary<NodeId, string> nodeSourceIds,
            DiagnosticCollection diagnostics)
        {
            SourceDocument = sourceDocument;
            Document = document;
            NodeSourceIds = nodeSourceIds == null || nodeSourceIds.Count == 0
                ? NoSources
                : new ReadOnlyDictionary<NodeId, string>(new Dictionary<NodeId, string>(nodeSourceIds));
            Diagnostics = diagnostics ?? DiagnosticCollection.Empty;
        }

        /// <summary>The document that was expanded.</summary>
        public TreeDocument SourceDocument { get; }

        /// <summary>
        /// The expanded document (format version 2, no references, no interface); null on failure.
        /// The same object as <see cref="SourceDocument"/> when that has no reference node and no
        /// interface (the identity rule).
        /// </summary>
        public TreeDocument Document { get; }

        /// <summary>Source ID of every node that came from a subtree; the parent's own nodes are absent.</summary>
        public IReadOnlyDictionary<NodeId, string> NodeSourceIds { get; }

        /// <summary>Errors of the expansion; empty on success.</summary>
        public DiagnosticCollection Diagnostics { get; }

        public bool Success => Document != null;

        /// <summary>True when the identity rule applied (<see cref="Document"/> is <see cref="SourceDocument"/>).</summary>
        public bool IsIdentity => Document != null && ReferenceEquals(Document, SourceDocument);
    }

    /// <summary>
    /// Authoring-time subtree expansion (ADR-P11-001, decision AIBT-049): every subtree reference
    /// node <c>r</c> is replaced by a renamed copy of the referenced tree — inner node <c>n</c>
    /// becomes <c>r:n</c>, private Tree keys become per-instance <c>r:key</c>, interface keys are
    /// connected to parent Tree keys or constants, Agent/Shared keys are shared by identical scope
    /// contract. Executors, the compiled program format and the trace format never see a reference.
    /// </summary>
    public static class SubtreeExpander
    {
        /// <summary>
        /// Maximum subtree nesting depth (ADR-P11-001 §8 decision 4). A fixed constant of the
        /// expansion contract, deliberately not a compilation-policy field.
        /// </summary>
        public const int MaximumNestingDepth = 8;

        private const int MaximumIdLength = 128;

        /// <summary>
        /// Format version of every expansion (ADR-P11-001 §4.4): always version 2, independent of
        /// <see cref="TreeDocument.LatestFormatVersion"/>, so expanded programs keep their hashes.
        /// </summary>
        internal const int ExpandedFormatVersion = 2;

        /// <summary>True when <paramref name="document"/> has a reference node or an interface.</summary>
        public static bool RequiresExpansion(TreeDocument document)
            => document != null && document.UsesSubtreeFeatures;

        /// <summary>
        /// Expands <paramref name="document"/>. A document with no reference node and no interface is
        /// returned unchanged (the same object) without further checks; otherwise the document and
        /// every referenced subtree are validated on their own (with <paramref name="options"/>,
        /// each under its own source ID) before they are combined. Without a
        /// <paramref name="resolver"/> every reference reports <c>AIBT3020</c>.
        /// </summary>
        /// <param name="options">Validation options; their document ID is the parent's source ID.</param>
        public static SubtreeExpansion Expand(
            TreeDocument document,
            ISubtreeResolver resolver,
            NodeRegistry registry,
            ValidationOptions options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (!RequiresExpansion(document)) return new SubtreeExpansion(document, document, null, null);
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            options = options ?? ValidationOptions.Phase1;

            var context = new Context(resolver, registry, options);
            var parentErrors = Errors(TreeValidator.Validate(document, registry, options));
            if (parentErrors.Count != 0)
                return new SubtreeExpansion(document, null, null, new DiagnosticCollection(parentErrors));

            var chain = new List<TreeId> { document.TreeId };
            var flat = context.ExpandDocument(document, options, 0, chain, new List<DiagnosticLocation>());
            if (flat == null || context.Diagnostics.Count != 0)
                return new SubtreeExpansion(document, null, null, new DiagnosticCollection(context.Diagnostics));

            var expanded = new TreeDocument(
                TreeDocument.CurrentFormat,
                ExpandedFormatVersion,
                document.TreeId,
                document.Name,
                document.Root,
                flat.Nodes,
                flat.Keys,
                document.Description,
                document.Tags,
                document.Metadata,
                default,
                document.AgentContract,
                document.SharedContract);
            return new SubtreeExpansion(document, expanded, flat.Sources, null);
        }

        private static List<Diagnostic> Errors(DiagnosticCollection diagnostics)
        {
            var result = new List<Diagnostic>();
            for (var index = 0; index < diagnostics.Count; index++)
                if (diagnostics[index].Severity == DiagnosticSeverity.Error) result.Add(diagnostics[index]);
            return result;
        }

        private static string Label(NodeDocument node)
            => string.IsNullOrEmpty(node.DisplayName) ? node.Id.Value : node.DisplayName;

        private static string NodePointer(string id) => "/nodes/" + Escape(id);

        private static string Escape(string value) => (value ?? string.Empty).Replace("~", "~0").Replace("/", "~1");

        private sealed class Flat
        {
            internal NodeId Root;
            internal List<NodeDocument> Nodes;
            internal List<BlackboardKeyDefinition> Keys;
            internal Dictionary<NodeId, string> Sources;
        }

        private sealed class Instance
        {
            internal NodeDocument Reference;
            internal DiagnosticLocation ReferenceLocation;
            internal string Root;
            internal List<NodeDocument> Nodes = new List<NodeDocument>();
            internal List<BlackboardKeyDefinition> Keys = new List<BlackboardKeyDefinition>();
            internal Dictionary<NodeId, string> Sources = new Dictionary<NodeId, string>();
        }

        private sealed class LoadedSubtree
        {
            internal SubtreeResolution Resolution;
            internal ValidationOptions Options;
            internal List<Diagnostic> ValidationErrors;
        }

        private sealed class Context
        {
            private readonly ISubtreeResolver _resolver;
            private readonly NodeRegistry _registry;
            private readonly ValidationOptions _baseOptions;
            private readonly Dictionary<TreeId, LoadedSubtree> _loaded = new Dictionary<TreeId, LoadedSubtree>();

            internal Context(ISubtreeResolver resolver, NodeRegistry registry, ValidationOptions baseOptions)
            {
                _resolver = resolver;
                _registry = registry;
                _baseOptions = baseOptions;
            }

            internal List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

            /// <summary>Flattens one already-validated document; null when any reference failed.</summary>
            internal Flat ExpandDocument(
                TreeDocument document,
                ValidationOptions options,
                int level,
                List<TreeId> chain,
                List<DiagnosticLocation> outer)
            {
                var references = new List<NodeDocument>();
                for (var index = 0; index < document.Nodes.Count; index++)
                    if (document.Nodes[index].Subtree != null) references.Add(document.Nodes[index]);
                references.Sort((left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));

                var parentKeys = new Dictionary<string, BlackboardKeyDefinition>(StringComparer.Ordinal);
                for (var index = 0; index < document.Blackboard.Count; index++)
                    parentKeys[document.Blackboard[index].Id] = document.Blackboard[index];

                var instances = new List<Instance>();
                var failed = false;
                foreach (var reference in references)
                {
                    var instance = ExpandReference(document, options, parentKeys, reference, level, chain, outer);
                    if (instance == null) failed = true;
                    else instances.Add(instance);
                }

                if (failed) return null;
                return Assemble(document, options, instances, outer);
            }

            private Instance ExpandReference(
                TreeDocument document,
                ValidationOptions options,
                IReadOnlyDictionary<string, BlackboardKeyDefinition> parentKeys,
                NodeDocument reference,
                int level,
                List<TreeId> chain,
                List<DiagnosticLocation> outer)
            {
                var subtreeId = reference.Subtree.TreeId;
                var referencePointer = NodePointer(reference.Id.Value) + "/subtree";
                var referenceLocation = new DiagnosticLocation(options.DocumentId, referencePointer, treeId: document.TreeId, nodeId: reference.Id);

                var cycleStart = chain.IndexOf(subtreeId);
                if (cycleStart >= 0)
                {
                    var names = new List<string>();
                    for (var index = cycleStart; index < chain.Count; index++) names.Add(chain[index].Value);
                    names.Add(subtreeId.Value);
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeCycle,
                        "Subtree references form a cycle: " + string.Join(" -> ", names) + ". A subtree may not use itself, directly or indirectly.",
                        referenceLocation, outer);
                    return null;
                }

                if (level + 1 > MaximumNestingDepth)
                {
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeDepth,
                        "Subtree '" + subtreeId.Value + "' would be nested at depth " + (level + 1) + "; subtrees can be nested at most "
                        + MaximumNestingDepth + " deep.",
                        referenceLocation, outer);
                    return null;
                }

                var loaded = Load(subtreeId);
                var resolution = loaded.Resolution;
                if (resolution == null)
                {
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeNotFound,
                        "Reference '" + reference.Id.Value + "' uses subtree '" + subtreeId.Value
                        + "', but no subtree resolver was given; compile through an overload that takes an ISubtreeResolver.",
                        referenceLocation, outer);
                    return null;
                }

                if (resolution.Status == SubtreeResolutionStatus.NotFound)
                {
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeNotFound,
                        "No tree with ID '" + subtreeId.Value + "' was found for reference '" + reference.Id.Value + "'."
                        + (string.IsNullOrEmpty(resolution.Detail) ? string.Empty : " " + resolution.Detail),
                        referenceLocation, outer);
                    return null;
                }

                if (resolution.Status == SubtreeResolutionStatus.Ambiguous)
                {
                    var related = new List<DiagnosticLocation>(outer);
                    for (var index = 0; index < resolution.CandidateSourceIds.Count; index++)
                        related.Add(new DiagnosticLocation(resolution.CandidateSourceIds[index], treeId: subtreeId));
                    Diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                        SubtreeExpansionDiagnosticCodes.SubtreeAmbiguous,
                        "Tree ID '" + subtreeId.Value + "' used by reference '" + reference.Id.Value + "' is declared by "
                        + resolution.CandidateSourceIds.Count + " documents: " + string.Join(", ", resolution.CandidateSourceIds)
                        + ". Tree IDs must be unique.",
                        referenceLocation.DocumentId, referenceLocation.JsonPointer, document.TreeId, reference.Id, related));
                    return null;
                }

                if (loaded.ValidationErrors.Count != 0)
                {
                    for (var index = 0; index < loaded.ValidationErrors.Count; index++)
                        Diagnostics.Add(WithRelated(loaded.ValidationErrors[index], referenceLocation, outer));
                    return null;
                }

                var subtree = resolution.Document;
                var innerChain = new List<TreeId>(chain) { subtreeId };
                var innerOuter = new List<DiagnosticLocation>(outer) { referenceLocation };
                var flat = ExpandDocument(subtree, loaded.Options, level + 1, innerChain, innerOuter);
                if (flat == null) return null;

                return Instantiate(document, parentKeys, reference, referenceLocation, subtree, resolution.SourceId, flat, outer);
            }

            private LoadedSubtree Load(TreeId treeId)
            {
                if (_loaded.TryGetValue(treeId, out var loaded)) return loaded;
                loaded = new LoadedSubtree();
                if (_resolver != null)
                {
                    loaded.Resolution = _resolver.Resolve(treeId) ?? SubtreeResolution.NotFound();
                    if (loaded.Resolution.Status == SubtreeResolutionStatus.Found)
                    {
                        loaded.Options = new ValidationOptions(
                            loaded.Resolution.SourceId,
                            _baseOptions.UnreachableNodes,
                            _baseOptions.SupportsAgentScope,
                            _baseOptions.SupportsSharedScope,
                            _baseOptions.Policy);
                        loaded.ValidationErrors = Errors(TreeValidator.Validate(loaded.Resolution.Document, _registry, loaded.Options));
                        if (loaded.ValidationErrors.Count == 0 && loaded.Resolution.Document.TreeId != treeId)
                        {
                            loaded.ValidationErrors.Add(ReferenceCompilerDiagnostics.Create(
                                SubtreeExpansionDiagnosticCodes.SubtreeNotFound,
                                "The resolver returned tree '" + loaded.Resolution.Document.TreeId.Value + "' for tree ID '" + treeId.Value + "'.",
                                loaded.Resolution.SourceId, "/treeId", loaded.Resolution.Document.TreeId));
                        }
                    }
                }

                _loaded.Add(treeId, loaded);
                return loaded;
            }

            private Instance Instantiate(
                TreeDocument parent,
                IReadOnlyDictionary<string, BlackboardKeyDefinition> parentKeys,
                NodeDocument reference,
                DiagnosticLocation referenceLocation,
                TreeDocument subtree,
                string subtreeSourceId,
                Flat flat,
                List<DiagnosticLocation> outer)
            {
                var errorCount = Diagnostics.Count;
                var prefix = reference.Id.Value + ":";
                var bindings = reference.Subtree.Keys;
                var subtreeInterface = subtree.Interface;
                var subtreeName = subtree.TreeId.Value;
                var referencePointer = referenceLocation.JsonPointer;

                // AIBT3024: the interface the reference was written for.
                if (subtreeInterface == null)
                {
                    if (bindings.Count != 0)
                    {
                        Add(SubtreeExpansionDiagnosticCodes.SubtreeInterfaceVersion,
                            "Subtree '" + subtreeName + "' declares no interface, but reference '" + reference.Id.Value + "' binds keys.",
                            At(referenceLocation, referencePointer + "/keys"), outer, Related(subtreeSourceId, "/interface", subtree.TreeId));
                        return null;
                    }
                }
                else if (reference.Subtree.InterfaceVersion != subtreeInterface.Version)
                {
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeInterfaceVersion,
                        "Reference '" + reference.Id.Value + "' was written for interface version " + reference.Subtree.InterfaceVersion
                        + " of subtree '" + subtreeName + "', which declares interface version " + subtreeInterface.Version + ".",
                        At(referenceLocation, referencePointer + "/interfaceVersion"), outer,
                        Related(subtreeSourceId, "/interface/version", subtree.TreeId));
                    return null;
                }

                // AIBT3025: exactly one entry per interface key.
                var interfaceKeys = new HashSet<string>(StringComparer.Ordinal);
                if (subtreeInterface != null)
                {
                    for (var index = 0; index < subtreeInterface.Keys.Count; index++)
                    {
                        var key = subtreeInterface.Keys[index];
                        interfaceKeys.Add(key);
                        if (!reference.Subtree.TryGetBinding(key, out _))
                        {
                            Add(SubtreeExpansionDiagnosticCodes.SubtreeKeySet,
                                "Interface key '" + key + "' of subtree '" + subtreeName + "' is not connected by reference '"
                                + reference.Id.Value + "'; connect it to a parent Tree key or give a constant.",
                                At(referenceLocation, referencePointer + "/keys"), outer);
                        }
                    }
                }

                for (var index = 0; index < bindings.Count; index++)
                {
                    if (interfaceKeys.Contains(bindings[index].InterfaceKey)) continue;
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeKeySet,
                        "Reference '" + reference.Id.Value + "' binds '" + bindings[index].InterfaceKey
                        + "', which is not an interface key of subtree '" + subtreeName + "'.",
                        At(referenceLocation, referencePointer + "/keys/" + Escape(bindings[index].InterfaceKey)), outer);
                }

                if (Diagnostics.Count != errorCount) return null;

                var instance = new Instance { Reference = reference, ReferenceLocation = referenceLocation };
                var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in flat.Keys)
                {
                    if (key.Scope != BlackboardScope.Tree)
                    {
                        keyMap[key.Id] = key.Id;
                        continue;
                    }

                    if (reference.Subtree.TryGetBinding(key.Id, out var binding) && interfaceKeys.Contains(key.Id))
                    {
                        var keyPointer = referencePointer + "/keys/" + Escape(key.Id);
                        if (!binding.IsConstant)
                        {
                            var target = parentKeys[binding.ParentKey];
                            if (target.Type != key.Type)
                            {
                                Add(SubtreeExpansionDiagnosticCodes.SubtreeKeyType,
                                    "Interface key '" + key.Id + "' of subtree '" + subtreeName + "' has type " + Describe(key.Type)
                                    + ", but parent key '" + target.Id + "' has type " + Describe(target.Type)
                                    + "; type, type version and enum contract must be identical.",
                                    At(referenceLocation, keyPointer), outer,
                                    Related(subtreeSourceId, "/blackboard/" + Escape(key.Id), subtree.TreeId));
                                continue;
                            }

                            keyMap[key.Id] = target.Id;
                            continue;
                        }

                        if (!CanonicalTreeJson.TryConvertConstant(binding.Constant, key.Type, out var constant, out var message))
                        {
                            Add(SubtreeExpansionDiagnosticCodes.SubtreeKeyType,
                                "The constant for interface key '" + key.Id + "' of subtree '" + subtreeName + "' is not a canonical "
                                + Describe(key.Type) + " value: " + message,
                                At(referenceLocation, keyPointer + "/value"), outer,
                                Related(subtreeSourceId, "/blackboard/" + Escape(key.Id), subtree.TreeId));
                            continue;
                        }

                        var constantId = Qualify(prefix, key.Id, "key", referenceLocation, outer);
                        if (constantId == null) continue;
                        keyMap[key.Id] = constantId;
                        instance.Keys.Add(new BlackboardKeyDefinition(constantId, constantId, key.Type, BlackboardScope.Tree,
                            constant, key.Description, key.Reduction));
                        continue;
                    }

                    var privateId = Qualify(prefix, key.Id, "key", referenceLocation, outer);
                    if (privateId == null) continue;
                    keyMap[key.Id] = privateId;
                    instance.Keys.Add(new BlackboardKeyDefinition(privateId, privateId, key.Type, BlackboardScope.Tree,
                        key.DefaultValue, key.Description, key.Reduction));
                }

                CheckScopeContract(BlackboardScope.Agent, parent, subtree, flat, referenceLocation, subtreeSourceId, outer);
                CheckScopeContract(BlackboardScope.Shared, parent, subtree, flat, referenceLocation, subtreeSourceId, outer);
                if (Diagnostics.Count != errorCount) return null;

                var referenceLabel = Label(reference);
                foreach (var node in flat.Nodes)
                {
                    var nodeLocation = new DiagnosticLocation(subtreeSourceId, NodePointer(node.Id.Value), treeId: subtree.TreeId, nodeId: node.Id);
                    var manifest = _registry.TryGet(node.TypeId, out var entry) ? entry.Manifest : null;
                    if (manifest != null)
                    {
                        for (var index = 0; index < manifest.Accesses.Count; index++)
                        {
                            var accessKey = manifest.Accesses[index].Key;
                            if (keyMap.TryGetValue(accessKey, out var mapped) && string.Equals(mapped, accessKey, StringComparison.Ordinal)) continue;
                            Add(SubtreeExpansionDiagnosticCodes.SubtreeKeyType,
                                "Node '" + node.Id.Value + "' of subtree '" + subtreeName + "' (type '" + node.TypeId
                                + "') reaches blackboard key '" + accessKey + "' by a fixed name in its manifest, and that key would become '"
                                + (mapped ?? prefix + accessKey) + "' in instance '" + reference.Id.Value
                                + "'. Connect interface key '" + accessKey + "' to a parent key with the same name.",
                                nodeLocation, outer, referenceLocation);
                        }
                    }

                    var nodeId = Qualify(prefix, node.Id.Value, "node", referenceLocation, outer);
                    if (nodeId == null) continue;
                    var children = new List<NodeId>(node.Children.Count);
                    for (var index = 0; index < node.Children.Count; index++)
                    {
                        var child = Qualify(prefix, node.Children[index].Value, "node", referenceLocation, outer);
                        if (child != null) children.Add(new NodeId(child));
                    }

                    var copy = new NodeDocument(
                        new NodeId(nodeId),
                        node.TypeId,
                        node.TypeVersion,
                        children,
                        RenameParameters(node.Parameters, manifest, keyMap),
                        RenameObserver(node.Observer, keyMap),
                        referenceLabel + " / " + Label(node),
                        node.Description,
                        node.Tags,
                        RenameBindings(node.Bindings, keyMap));
                    instance.Nodes.Add(copy);
                    instance.Sources[copy.Id] = flat.Sources.TryGetValue(node.Id, out var innerSource) ? innerSource : subtreeSourceId;
                }

                instance.Root = prefix + flat.Root.Value;
                return Diagnostics.Count == errorCount ? instance : null;
            }

            private void CheckScopeContract(
                BlackboardScope scope,
                TreeDocument parent,
                TreeDocument subtree,
                Flat flat,
                DiagnosticLocation referenceLocation,
                string subtreeSourceId,
                List<DiagnosticLocation> outer)
            {
                var subtreeContract = scope == BlackboardScope.Agent ? subtree.AgentContract : subtree.SharedContract;
                if (subtreeContract == null) return;
                var parentContract = scope == BlackboardScope.Agent ? parent.AgentContract : parent.SharedContract;
                var scopeName = scope == BlackboardScope.Agent ? "agent" : "shared";
                var related = Related(subtreeSourceId, "/blackboardContracts/" + scopeName, subtree.TreeId);
                if (parentContract == null
                    || !string.Equals(parentContract.ContractId, subtreeContract.ContractId, StringComparison.Ordinal)
                    || parentContract.ContractVersion != subtreeContract.ContractVersion)
                {
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeScopeContract,
                        "Subtree '" + subtree.TreeId.Value + "' uses " + scopeName + " contract '" + subtreeContract.ContractId + "' version "
                        + subtreeContract.ContractVersion + ", but the parent "
                        + (parentContract == null
                            ? "declares no " + scopeName + " contract."
                            : "declares '" + parentContract.ContractId + "' version " + parentContract.ContractVersion + ".")
                        + " The contracts must be identical.",
                        referenceLocation, outer, related);
                    return;
                }

                var parentScopeKeys = ScopeKeys(parent.Blackboard, scope);
                var subtreeScopeKeys = ScopeKeys(flat.Keys, scope);
                var mismatch = parentScopeKeys.Count != subtreeScopeKeys.Count;
                foreach (var pair in subtreeScopeKeys)
                {
                    if (mismatch) break;
                    mismatch = !parentScopeKeys.TryGetValue(pair.Key, out var parentKey) || !SameDefinition(parentKey, pair.Value);
                }

                if (mismatch)
                {
                    Add(SubtreeExpansionDiagnosticCodes.SubtreeScopeContract,
                        "Subtree '" + subtree.TreeId.Value + "' and its parent both use " + scopeName + " contract '" + subtreeContract.ContractId
                        + "' version " + subtreeContract.ContractVersion + ", but their " + scopeName
                        + " key declarations differ; a scope contract is the complete identical set of its keys.",
                        referenceLocation, outer, related);
                }
            }

            private Flat Assemble(
                TreeDocument document,
                ValidationOptions options,
                List<Instance> instances,
                List<DiagnosticLocation> outer)
            {
                var errorCount = Diagnostics.Count;
                var replacements = new Dictionary<NodeId, NodeId>();
                for (var index = 0; index < instances.Count; index++)
                    replacements.Add(instances[index].Reference.Id, new NodeId(instances[index].Root));

                var nodeOwners = new Dictionary<string, DiagnosticLocation>(StringComparer.Ordinal);
                var nodes = new List<NodeDocument>();
                foreach (var node in document.Nodes)
                {
                    if (node.Subtree != null) continue;
                    var copy = node;
                    if (replacements.Count != 0)
                    {
                        var changed = false;
                        var children = new List<NodeId>(node.Children.Count);
                        for (var index = 0; index < node.Children.Count; index++)
                        {
                            if (replacements.TryGetValue(node.Children[index], out var replacement))
                            {
                                children.Add(replacement);
                                changed = true;
                            }
                            else children.Add(node.Children[index]);
                        }

                        if (changed) copy = node.WithChildren(children);
                    }

                    nodes.Add(copy);
                    nodeOwners[node.Id.Value] = new DiagnosticLocation(options.DocumentId, NodePointer(node.Id.Value), treeId: document.TreeId, nodeId: node.Id);
                }

                var keyOwners = new Dictionary<string, DiagnosticLocation>(StringComparer.Ordinal);
                var keys = new List<BlackboardKeyDefinition>(document.Blackboard);
                foreach (var key in document.Blackboard)
                    keyOwners[key.Id] = new DiagnosticLocation(options.DocumentId, "/blackboard/" + Escape(key.Id), treeId: document.TreeId);

                var sources = new Dictionary<NodeId, string>();
                foreach (var instance in instances)
                {
                    foreach (var node in instance.Nodes)
                    {
                        if (nodeOwners.TryGetValue(node.Id.Value, out var owner))
                        {
                            Add(SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity,
                                "Expanded node ID '" + node.Id.Value + "' of instance '" + instance.Reference.Id.Value
                                + "' collides with an existing node ID; rename the node or the reference.",
                                instance.ReferenceLocation, outer, owner);
                            continue;
                        }

                        nodeOwners.Add(node.Id.Value, instance.ReferenceLocation);
                        nodes.Add(node);
                        sources.Add(node.Id, instance.Sources[node.Id]);
                    }

                    foreach (var key in instance.Keys)
                    {
                        if (keyOwners.TryGetValue(key.Id, out var owner))
                        {
                            Add(SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity,
                                "Expanded blackboard key ID '" + key.Id + "' of instance '" + instance.Reference.Id.Value
                                + "' collides with an existing key ID; rename the key or the reference.",
                                instance.ReferenceLocation, outer, owner);
                            continue;
                        }

                        keyOwners.Add(key.Id, instance.ReferenceLocation);
                        keys.Add(key);
                    }
                }

                if (Diagnostics.Count != errorCount) return null;
                return new Flat { Root = document.Root, Nodes = nodes, Keys = keys, Sources = sources };
            }

            private string Qualify(string prefix, string id, string kind, DiagnosticLocation referenceLocation, List<DiagnosticLocation> outer)
            {
                var qualified = prefix + id;
                if (qualified.Length <= MaximumIdLength) return qualified;
                Add(SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity,
                    "Expanded " + kind + " ID '" + qualified + "' is " + qualified.Length + " characters long; IDs are limited to "
                    + MaximumIdLength + ". Use shorter reference or inner IDs.",
                    referenceLocation, outer);
                return null;
            }

            private SemanticObject RenameParameters(SemanticObject parameters, NodeManifest manifest, IReadOnlyDictionary<string, string> keyMap)
            {
                if (parameters == null || manifest == null || parameters.Properties.Count == 0) return parameters;
                List<SemanticProperty> renamed = null;
                for (var index = 0; index < parameters.Properties.Count; index++)
                {
                    var property = parameters.Properties[index];
                    if (property == null || !IsBlackboardKeyParameter(manifest, property.Name)
                        || property.Value == null || !property.Value.TryGetString(out var keyId)
                        || keyId == null || !keyMap.TryGetValue(keyId, out var mapped)
                        || string.Equals(mapped, keyId, StringComparison.Ordinal)) continue;
                    if (renamed == null) renamed = new List<SemanticProperty>(parameters.Properties);
                    renamed[index] = new SemanticProperty(property.Name, SemanticValue.FromString(mapped));
                }

                return renamed == null ? parameters : new SemanticObject(renamed);
            }

            private static bool IsBlackboardKeyParameter(NodeManifest manifest, string name)
            {
                for (var index = 0; index < manifest.Parameters.Count; index++)
                {
                    var contract = manifest.Parameters[index];
                    if (string.Equals(contract.Name, name, StringComparison.Ordinal))
                        return contract.Type == NodeParameterType.BlackboardKey;
                }

                return false;
            }

            private static NodeObserver RenameObserver(NodeObserver observer, IReadOnlyDictionary<string, string> keyMap)
            {
                if (observer == null) return null;
                var keys = new List<string>(observer.WatchedKeys.Count);
                for (var index = 0; index < observer.WatchedKeys.Count; index++)
                {
                    var key = observer.WatchedKeys[index];
                    keys.Add(key != null && keyMap.TryGetValue(key, out var mapped) ? mapped : key);
                }

                return new NodeObserver(observer.Mode, keys);
            }

            private static NodeBindingMap RenameBindings(NodeBindingMap bindings, IReadOnlyDictionary<string, string> keyMap)
            {
                if (bindings == null) return null;
                var pairs = new List<KeyValuePair<string, string>>(bindings.Values.Count);
                foreach (var pair in bindings.Values)
                    pairs.Add(new KeyValuePair<string, string>(pair.Key, keyMap.TryGetValue(pair.Value, out var mapped) ? mapped : pair.Value));
                return new NodeBindingMap(pairs);
            }

            private static Dictionary<string, BlackboardKeyDefinition> ScopeKeys(IEnumerable<BlackboardKeyDefinition> keys, BlackboardScope scope)
            {
                var result = new Dictionary<string, BlackboardKeyDefinition>(StringComparer.Ordinal);
                foreach (var key in keys)
                    if (key != null && key.Scope == scope) result[key.Id] = key;
                return result;
            }

            private static bool SameDefinition(BlackboardKeyDefinition left, BlackboardKeyDefinition right)
            {
                if (left.Type != right.Type || left.Scope != right.Scope || left.Reduction != right.Reduction) return false;
                var leftDefault = left.DefaultValue;
                var rightDefault = right.DefaultValue;
                if (leftDefault == null || rightDefault == null) return leftDefault == rightDefault;
                if (leftDefault.ValueType != rightDefault.ValueType
                    || !string.Equals(leftDefault.EnumContract, rightDefault.EnumContract, StringComparison.Ordinal)) return false;
                var leftHasRuntime = leftDefault.TryGetRuntimeValue(out var leftValue);
                var rightHasRuntime = rightDefault.TryGetRuntimeValue(out var rightValue);
                if (leftHasRuntime || rightHasRuntime) return leftHasRuntime && rightHasRuntime && leftValue == rightValue;
                var leftBytes = leftDefault.GetRegisteredBytesCopy();
                var rightBytes = rightDefault.GetRegisteredBytesCopy();
                if (leftBytes == null || rightBytes == null || leftBytes.Length != rightBytes.Length) return false;
                for (var index = 0; index < leftBytes.Length; index++)
                    if (leftBytes[index] != rightBytes[index]) return false;
                return true;
            }

            private static string Describe(BlackboardTypeReference type)
            {
                var text = type.CanonicalTypeId ?? "<invalid>";
                if (!string.IsNullOrEmpty(type.EnumContract)) text += "<" + type.EnumContract + ">";
                if (type.RuntimeDescriptor.Version != 0) text += " v" + type.RuntimeDescriptor.Version;
                return text;
            }

            private static DiagnosticLocation At(DiagnosticLocation location, string pointer)
                => new DiagnosticLocation(location.DocumentId, pointer, treeId: location.TreeId, nodeId: location.NodeId);

            private static DiagnosticLocation Related(string documentId, string pointer, TreeId treeId)
                => new DiagnosticLocation(documentId, pointer, treeId: treeId);

            private static Diagnostic WithRelated(Diagnostic diagnostic, DiagnosticLocation reference, List<DiagnosticLocation> outer)
            {
                var related = new List<DiagnosticLocation>(diagnostic.RelatedLocations);
                related.AddRange(outer);
                related.Add(reference);
                return new Diagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Location, related);
            }

            private void Add(
                DiagnosticCode code,
                string message,
                DiagnosticLocation location,
                List<DiagnosticLocation> outer,
                DiagnosticLocation? extra = null)
            {
                var related = new List<DiagnosticLocation>(outer);
                if (extra.HasValue) related.Add(extra.Value);
                Diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    code, message, location.DocumentId, location.JsonPointer, location.TreeId, location.NodeId, related));
            }
        }
    }
}
