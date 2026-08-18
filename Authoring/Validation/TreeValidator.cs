using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class TreeValidator
    {
        public static DiagnosticCollection Validate(
            TreeDocument document,
            NodeRegistry registry,
            ValidationOptions options = null)
            => Validate(document, registry, options, null);

        public static DiagnosticCollection Validate(
            TreeDocument document,
            NodeRegistry registry,
            ValidationOptions options,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            options = options ?? ValidationOptions.Phase1;
            var diagnostics = new List<Diagnostic>();
            if (document == null)
            {
                diagnostics.Add(Create(
                    options,
                    TreeValidationDiagnosticCodes.InvalidDocument,
                    "A tree document is required.",
                    string.Empty));
                return new DiagnosticCollection(diagnostics);
            }

            ValidateHeader(document, options, diagnostics);
            ValidatePolicyContract(options, diagnostics);

            var blackboard = ValidateBlackboard(document, options, registeredTypes, diagnostics);
            var graph = BuildGraph(document, options, diagnostics);
            ValidateGraph(document, graph, options, diagnostics);

            if (registry == null)
            {
                diagnostics.Add(Create(
                    options,
                    TreeValidationDiagnosticCodes.InvalidDocument,
                    "A node registry is required for semantic validation.",
                    "/nodes",
                    document.TreeId));
            }
            else
            {
                ValidateNodes(document, graph, blackboard, registry, options, diagnostics);
            }

            ValidateProjectPolicy(document, graph, registry, options, diagnostics);
            return new DiagnosticCollection(PromoteWarnings(diagnostics, options.Policy.WarningsAsErrors));
        }

        private static void ValidateHeader(
            TreeDocument document,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            if (!string.Equals(document.Format, TreeDocument.CurrentFormat, StringComparison.Ordinal))
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidFormat,
                    "Tree format must be 'aibt.tree'.", "/format", document.TreeId));
            }

            if (document.FormatVersion != TreeDocument.CurrentFormatVersion
                && document.FormatVersion != TreeDocument.LatestFormatVersion)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedFormatVersion,
                    "Tree format version is unsupported.", "/formatVersion", document.TreeId));
            }

            if (!document.TreeId.IsValid)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidTreeIdentity,
                    "Tree identity is invalid.", "/treeId"));
            }

            if (string.IsNullOrWhiteSpace(document.Name))
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.MissingTreeName,
                    "Tree name is required.", "/name", document.TreeId));
            }
        }

        private static Dictionary<string, BlackboardKeyDefinition> ValidateBlackboard(
            TreeDocument document,
            ValidationOptions options,
            RegisteredBlackboardTypeCatalog registeredTypes,
            ICollection<Diagnostic> diagnostics)
        {
            var result = new Dictionary<string, BlackboardKeyDefinition>(StringComparer.Ordinal);
            var names = new Dictionary<ScopedName, DiagnosticLocation>();
            var sorted = new List<IndexedBlackboardKey>();
            for (var index = 0; index < document.Blackboard.Count; index++)
            {
                sorted.Add(new IndexedBlackboardKey(document.Blackboard[index], index));
            }

            sorted.Sort(IndexedBlackboardKey.Compare);
            for (var index = 0; index < sorted.Count; index++)
            {
                var key = sorted[index].Key;
                if (key == null)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidDocument,
                        "Blackboard declarations cannot contain null entries.", "/blackboard", document.TreeId));
                    continue;
                }

                var pointer = BlackboardPointer(key.Id);
                var location = Location(options, pointer, document.TreeId);
                if (!IsAuthoringId(key.Id))
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.InvalidKeyId,
                        "Blackboard key ID is invalid.",
                        location));
                }
                else if (result.TryGetValue(key.Id, out var existing))
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.InvalidKeyId,
                        "Blackboard key IDs must be unique.",
                        location,
                        new[] { Location(options, BlackboardPointer(existing.Id), document.TreeId) }));
                }
                else
                {
                    result.Add(key.Id, key);
                }

                if (string.IsNullOrWhiteSpace(key.Name))
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.InvalidKeyName,
                        "Blackboard key name is required.", location));
                }
                else if (Enum.IsDefined(typeof(BlackboardScope), key.Scope))
                {
                    var scopedName = new ScopedName(key.Scope, key.Name);
                    if (names.TryGetValue(scopedName, out var original))
                    {
                        diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                            BlackboardDiagnosticCodes.InvalidKeyName,
                            "Blackboard key names must be unique within their scope.", location, new[] { original }));
                    }
                    else
                    {
                        names.Add(scopedName, location);
                    }
                }

                ValidateBlackboardTypeAndDefault(key, location, registeredTypes, diagnostics);
                ValidateBlackboardScope(document, key, options, diagnostics);
            }

            return result;
        }

        private static void ValidateBlackboardTypeAndDefault(
            BlackboardKeyDefinition key,
            DiagnosticLocation location,
            RegisteredBlackboardTypeCatalog registeredTypes,
            ICollection<Diagnostic> diagnostics)
        {
            if (!key.Type.IsValid)
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidType, "Blackboard key type is invalid.", location));
            }

            if (key.Scope == BlackboardScope.NodeLocal)
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.NodeLocalTreeDeclaration,
                    "NodeLocal memory cannot be declared as a tree blackboard key.", location));
            }
            else if (!Enum.IsDefined(typeof(BlackboardScope), key.Scope))
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidScope, "Blackboard key scope is invalid.", location));
            }

            var value = key.DefaultValue;
            if (key.Type.IsRegistered)
            {
                if (registeredTypes == null
                    || !registeredTypes.TryGet(key.Type.CanonicalTypeId, key.Type.RegisteredDescriptor.Version, out var registered)
                    || registered.Descriptor != key.Type.RegisteredDescriptor)
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.MissingCanonicalSchema,
                        "Registered blackboard type is absent from the exact accepted canonical schema/equality catalog.", location));
            }

            if (value == null)
            {
                return;
            }

            if (key.Type.IsValid && value.ValueType != key.Type.ValueType)
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.DefaultTypeMismatch,
                    "Blackboard default type does not match the declared key type.", location));
            }

            if (!value.IsCanonical)
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidDefaultValue,
                    "Blackboard default is not a valid canonical value.", location));
            }

            if (key.Type.IsRegistered
                && (value.RegisteredTypeVersion != key.Type.RegisteredDescriptor.Version
                    || !string.Equals(value.RegisteredTypeId, key.Type.CanonicalTypeId, StringComparison.Ordinal)))
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.DefaultTypeMismatch,
                    "Registered blackboard default does not match the declared type ID and version.", location));
            }

            if (key.Type.IsRegistered && registeredTypes != null
                && registeredTypes.TryGet(key.Type.CanonicalTypeId, key.Type.RegisteredDescriptor.Version, out var catalogEntry)
                && value.TryGetRegisteredValue(out var registeredValue))
            {
                try
                {
                    var expected = RegisteredBlackboardDefaultCodec.Encode(catalogEntry, registeredValue, registeredTypes);
                    if (!BytesEqual(expected, value.GetRegisteredBytesCopy()))
                        throw new ArgumentException("Stored registered bytes differ from the closed codec.");
                }
                catch (ArgumentException)
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.InvalidDefaultValue,
                        "Registered blackboard default differs from the exact accepted catalog codec.", location));
                }
            }

        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        private static void ValidateBlackboardScope(
            TreeDocument document,
            BlackboardKeyDefinition key,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var unsupported = key.Scope == BlackboardScope.Agent && !options.SupportsAgentScope
                || key.Scope == BlackboardScope.Shared && !options.SupportsSharedScope;
            if (unsupported)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedBlackboardScope,
                    "The declared blackboard scope is recognized but unsupported by the selected execution capabilities.",
                    BlackboardPointer(key.Id) + "/scope", document.TreeId));
            }
        }

        private static Graph BuildGraph(
            TreeDocument document,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var graph = new Graph();
            var sorted = new List<IndexedNode>();
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                sorted.Add(new IndexedNode(document.Nodes[index], index));
            }

            sorted.Sort(IndexedNode.Compare);
            for (var index = 0; index < sorted.Count; index++)
            {
                var node = sorted[index].Node;
                if (node == null)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidDocument,
                        "Node declarations cannot contain null entries.", "/nodes", document.TreeId));
                    continue;
                }

                if (!node.Id.IsValid)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidNodeIdentity,
                        "Node identity is invalid.", "/nodes", document.TreeId));
                    continue;
                }

                if (graph.Nodes.TryGetValue(node.Id, out var existing))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.DuplicateNodeIdentity,
                        "Node identities must be unique.", NodePointer(node.Id), document.TreeId, node.Id,
                        new[] { Location(options, NodePointer(existing.Id), document.TreeId, existing.Id) }));
                    continue;
                }

                graph.Nodes.Add(node.Id, node);
                graph.OrderedNodes.Add(node);
            }

            graph.OrderedNodes.Sort((left, right) =>
                string.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal));
            return graph;
        }

        private static void ValidateGraph(
            TreeDocument document,
            Graph graph,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            if (!document.Root.IsValid || !graph.Nodes.ContainsKey(document.Root))
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidRoot,
                    "Root must reference a declared node.", "/root", document.TreeId));
            }

            var parentCounts = new Dictionary<NodeId, int>();
            var parentLocations = new Dictionary<NodeId, DiagnosticLocation>();
            for (var nodeIndex = 0; nodeIndex < graph.OrderedNodes.Count; nodeIndex++)
            {
                var node = graph.OrderedNodes[nodeIndex];
                var children = new HashSet<NodeId>();
                for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
                {
                    var child = node.Children[childIndex];
                    var pointer = NodePointer(node.Id) + "/children/" + childIndex;
                    if (!child.IsValid || !graph.Nodes.ContainsKey(child))
                    {
                        diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.MissingChild,
                            "Child reference must identify a declared node.", pointer, document.TreeId, node.Id));
                        continue;
                    }

                    if (!children.Add(child))
                    {
                        diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.DuplicateChild,
                            "A child may appear only once in a node's child list.", pointer, document.TreeId, node.Id));
                        continue;
                    }

                    parentCounts.TryGetValue(child, out var count);
                    parentCounts[child] = count + 1;
                    if (!parentLocations.ContainsKey(child))
                    {
                        parentLocations.Add(child, Location(options, pointer, document.TreeId, node.Id));
                    }

                    if (!graph.Parents.TryGetValue(child, out var parents))
                    {
                        parents = new List<NodeDocument>();
                        graph.Parents.Add(child, parents);
                    }

                    parents.Add(node);
                }
            }

            foreach (var pair in parentCounts)
            {
                if (pair.Value > 1)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.MultipleParents,
                        "A behavior-tree node cannot have multiple parents.", NodePointer(pair.Key),
                        document.TreeId, pair.Key, new[] { parentLocations[pair.Key] }));
                }
            }

            var colors = new Dictionary<NodeId, byte>();
            for (var index = 0; index < graph.OrderedNodes.Count; index++)
            {
                DetectCycles(document, graph.OrderedNodes[index], graph, colors, options, diagnostics);
            }

            var reachable = new HashSet<NodeId>();
            if (document.Root.IsValid && graph.Nodes.ContainsKey(document.Root))
            {
                MarkReachable(document.Root, graph, reachable);
            }

            if (options.UnreachableNodes != UnreachableNodePolicy.Allow)
            {
                var severity = options.UnreachableNodes == UnreachableNodePolicy.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error;
                for (var index = 0; index < graph.OrderedNodes.Count; index++)
                {
                    var node = graph.OrderedNodes[index];
                    if (!reachable.Contains(node.Id))
                    {
                        diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnreachableNode,
                            "Node is unreachable from the tree root.", NodePointer(node.Id), document.TreeId, node.Id,
                            severity: severity));
                    }
                }
            }
        }

        private static void DetectCycles(
            TreeDocument document,
            NodeDocument node,
            Graph graph,
            IDictionary<NodeId, byte> colors,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            colors.TryGetValue(node.Id, out var color);
            if (color != 0) return;

            var stack = new Stack<TraversalFrame>();
            colors[node.Id] = 1;
            stack.Push(new TraversalFrame(node));
            while (stack.Count > 0)
            {
                var frame = stack.Peek();
                if (frame.ChildIndex >= frame.Node.Children.Count)
                {
                    colors[frame.Node.Id] = 2;
                    stack.Pop();
                    continue;
                }

                var childIndex = frame.ChildIndex++;
                var childId = frame.Node.Children[childIndex];
                if (!graph.Nodes.TryGetValue(childId, out var child)) continue;

                colors.TryGetValue(childId, out var childColor);
                if (childColor == 1)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.Cycle,
                        "Child reference creates a cycle.", NodePointer(frame.Node.Id) + "/children/" + childIndex,
                        document.TreeId, frame.Node.Id,
                        new[] { Location(options, NodePointer(childId), document.TreeId, childId) }));
                }
                else if (childColor == 0)
                {
                    colors[childId] = 1;
                    stack.Push(new TraversalFrame(child));
                }
            }
        }

        private static void MarkReachable(NodeId id, Graph graph, ISet<NodeId> reachable)
        {
            var pending = new Stack<NodeId>();
            pending.Push(id);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!reachable.Add(current) || !graph.Nodes.TryGetValue(current, out var node)) continue;
                for (var index = node.Children.Count - 1; index >= 0; index--)
                    pending.Push(node.Children[index]);
            }
        }

        private static void ValidateNodes(
            TreeDocument document,
            Graph graph,
            IReadOnlyDictionary<string, BlackboardKeyDefinition> blackboard,
            NodeRegistry registry,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            for (var index = 0; index < graph.OrderedNodes.Count; index++)
            {
                var node = graph.OrderedNodes[index];
                if (!registry.TryGet(node.TypeId, out var entry))
                {
                    ValidateObserver(document, node, graph, blackboard, null, options, diagnostics);
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnknownNodeType,
                        "Node type is not present in the selected registry.", NodePointer(node.Id) + "/type",
                        document.TreeId, node.Id));
                    continue;
                }

                var manifest = entry.Manifest;
                if (node.TypeVersion <= 0 || (uint)node.TypeVersion != manifest.Version)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedNodeVersion,
                        "Node type version is not supported by the selected registry.",
                        NodePointer(node.Id) + "/typeVersion", document.TreeId, node.Id));
                }

                ValidateChildPolicy(document, node, manifest, options, diagnostics);
                ValidateParameters(document, node, manifest, options, diagnostics);
                ValidateAccess(document, node, manifest, blackboard, options, diagnostics);
                ValidateObserver(document, node, graph, blackboard, manifest, options, diagnostics);
                ValidateNodeCapabilities(document, node, manifest, options, diagnostics);
            }
        }

        private static void ValidateChildPolicy(
            TreeDocument document,
            NodeDocument node,
            NodeManifest manifest,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var count = (uint)node.Children.Count;
            if (count < manifest.ChildPolicy.Minimum
                || manifest.ChildPolicy.Maximum.HasValue && count > manifest.ChildPolicy.Maximum.Value)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.ChildPolicy,
                    "Node child count violates its manifest child policy.", NodePointer(node.Id) + "/children",
                    document.TreeId, node.Id));
            }
        }

        private static void ValidateParameters(
            TreeDocument document,
            NodeDocument node,
            NodeManifest manifest,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var values = new Dictionary<string, SemanticValue>(StringComparer.Ordinal);
            var properties = new List<SemanticProperty>(node.Parameters?.Properties ?? Array.Empty<SemanticProperty>());
            properties.Sort((left, right) => string.Compare(left?.Name, right?.Name, StringComparison.Ordinal));
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties[index];
                if (property == null || string.IsNullOrEmpty(property.Name))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnknownParameter,
                        "Parameter entries require a non-empty name.", NodePointer(node.Id) + "/parameters",
                        document.TreeId, node.Id));
                    continue;
                }

                var pointer = ParameterPointer(node.Id, property.Name);
                if (values.ContainsKey(property.Name))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.DuplicateParameter,
                        "Parameter names must be unique.", pointer, document.TreeId, node.Id));
                    continue;
                }

                values.Add(property.Name, property.Value);
                if (!TryFindParameter(manifest.Parameters, property.Name, out _))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnknownParameter,
                        "Parameter is not declared by the node manifest.", pointer, document.TreeId, node.Id));
                }
            }

            for (var index = 0; index < manifest.Parameters.Count; index++)
            {
                var contract = manifest.Parameters[index];
                var hasValue = values.TryGetValue(contract.Name, out var value);
                var conditionApplies = ConditionMatches(contract.RequiredWhen, values);
                if (!hasValue && (contract.Required || conditionApplies))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.MissingParameter,
                        "A required node parameter is missing.", ParameterPointer(node.Id, contract.Name),
                        document.TreeId, node.Id));
                    continue;
                }

                if (!hasValue)
                {
                    continue;
                }

                if (contract.ForbiddenUnless != null && !ConditionMatches(contract.ForbiddenUnless, values))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.ParameterConstraint,
                        "Parameter is forbidden unless its manifest condition is satisfied.",
                        ParameterPointer(node.Id, contract.Name), document.TreeId, node.Id));
                }

                if (!ValidateParameterType(value, contract, out var constraintMessage))
                {
                    diagnostics.Add(Create(options,
                        constraintMessage == null
                            ? TreeValidationDiagnosticCodes.ParameterType
                            : TreeValidationDiagnosticCodes.ParameterConstraint,
                        constraintMessage ?? "Parameter has the wrong exact semantic type.",
                        ParameterPointer(node.Id, contract.Name), document.TreeId, node.Id));
                }
            }

            if (string.Equals(node.TypeId, BuiltInNodeManifests.ParallelTypeId, StringComparison.Ordinal))
            {
                ValidateParallel(document, node, values, options, diagnostics);
            }
        }

        private static bool ValidateParameterType(
            SemanticValue value,
            NodeParameterContract contract,
            out string constraintMessage)
        {
            constraintMessage = null;
            if (value == null)
            {
                return false;
            }

            switch (contract.Type)
            {
                case NodeParameterType.Boolean:
                    return value.Kind == SemanticValueKind.Boolean;
                case NodeParameterType.UInt32:
                    if (!TryGetUnsigned(value, uint.MaxValue, out var uint32)) return false;
                    if (contract.Minimum.HasValue && uint32 < contract.Minimum.Value)
                    {
                        constraintMessage = "Unsigned parameter is below its declared minimum.";
                        return false;
                    }
                    return true;
                case NodeParameterType.UInt64:
                    if (!TryGetUnsigned(value, ulong.MaxValue, out var uint64)) return false;
                    if (contract.Minimum.HasValue && uint64 < contract.Minimum.Value)
                    {
                        constraintMessage = "Unsigned parameter is below its declared minimum.";
                        return false;
                    }
                    return true;
                case NodeParameterType.StringEnum:
                    if (!value.TryGetString(out var text)) return false;
                    for (var index = 0; index < contract.AllowedValues.Count; index++)
                    {
                        if (string.Equals(text, contract.AllowedValues[index], StringComparison.Ordinal)) return true;
                    }
                    constraintMessage = "String parameter is not one of the manifest's allowed values.";
                    return false;
                default:
                    return false;
            }
        }

        private static void ValidateParallel(
            TreeDocument document,
            NodeDocument node,
            IReadOnlyDictionary<string, SemanticValue> parameters,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            if (!parameters.TryGetValue("policy", out var policyValue)
                || !policyValue.TryGetString(out var policy)
                || !string.Equals(policy, "threshold", StringComparison.Ordinal))
            {
                return;
            }

            if (!parameters.TryGetValue("successThreshold", out var successValue)
                || !TryGetUnsigned(successValue, uint.MaxValue, out var success)
                || !parameters.TryGetValue("failureThreshold", out var failureValue)
                || !TryGetUnsigned(failureValue, uint.MaxValue, out var failure))
            {
                return;
            }

            var count = (ulong)node.Children.Count;
            if (success == 0 || failure == 0 || success > count || failure > count || success + failure > count + 1)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.ParallelThreshold,
                    "Parallel thresholds must be positive, not exceed child count, and have a sum no greater than child count plus one.",
                    NodePointer(node.Id) + "/parameters", document.TreeId, node.Id));
            }
        }

        private static void ValidateAccess(
            TreeDocument document,
            NodeDocument node,
            NodeManifest manifest,
            IReadOnlyDictionary<string, BlackboardKeyDefinition> blackboard,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            for (var index = 0; index < manifest.Accesses.Count; index++)
            {
                var access = manifest.Accesses[index];
                if (!blackboard.TryGetValue(access.Key, out var key))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.MissingBlackboardAccess,
                        "Node manifest access references a blackboard key not declared by the tree.",
                        NodePointer(node.Id) + "/parameters", document.TreeId, node.Id));
                }
                else if (access.Mode == NodeAccessMode.Write && key.Scope == BlackboardScope.Shared)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedBlackboardWrite,
                        "Shared-scope writes require a deterministic reduction policy not available in Phase 1.",
                        BlackboardPointer(key.Id), document.TreeId, node.Id));
                }
            }
        }

        private static void ValidateObserver(
            TreeDocument document,
            NodeDocument node,
            Graph graph,
            IReadOnlyDictionary<string, BlackboardKeyDefinition> blackboard,
            NodeManifest manifest,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var observer = node.Observer;
            if (observer == null)
            {
                return;
            }

            var pointer = NodePointer(node.Id) + "/observer";
            var self = string.Equals(observer.Mode, "self", StringComparison.Ordinal);
            var lower = string.Equals(observer.Mode, "lower-priority", StringComparison.Ordinal);
            var both = string.Equals(observer.Mode, "both", StringComparison.Ordinal);
            if (!self && !lower && !both)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidObserverMode,
                    "Observer mode must be 'self', 'lower-priority', or 'both'.", pointer + "/mode",
                    document.TreeId, node.Id));
            }

            graph.Parents.TryGetValue(node.Id, out var parents);
            var oneParent = parents != null && parents.Count == 1;
            var reactive = oneParent && (parents[0].TypeId == BuiltInNodeManifests.ReactiveSequenceTypeId
                || parents[0].TypeId == BuiltInNodeManifests.ReactiveSelectorTypeId);
            var reactiveSelector = oneParent && parents[0].TypeId == BuiltInNodeManifests.ReactiveSelectorTypeId;
            if (!reactive || (lower || both) && !reactiveSelector)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidObserverContext,
                    "Observer must be a condition child of one reactive composite; lower-priority modes require a reactive selector.",
                    pointer, document.TreeId, node.Id));
            }

            if (manifest != null && manifest.Kind != NodeBehaviorKind.Condition)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidObserverContext,
                    "Only a node manifest declared as a Condition may own an observer.",
                    pointer, document.TreeId, node.Id));
            }

            var watched = new HashSet<string>(StringComparer.Ordinal);
            if (observer.WatchedKeys.Count == 0)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidWatchedKey,
                    document.FormatVersion == TreeDocument.CurrentFormatVersion
                        ? "Observer must watch at least one Tree-scope blackboard key."
                        : "Observer must watch at least one declared blackboard key.", pointer + "/watchedKeys",
                    document.TreeId, node.Id));
            }

            for (var index = 0; index < observer.WatchedKeys.Count; index++)
            {
                var keyId = observer.WatchedKeys[index];
                var keyPointer = pointer + "/watchedKeys/" + index;
                if (keyId == null || !watched.Add(keyId))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.DuplicateWatchedKey,
                        "Observer watched-key IDs must be non-null and unique.", keyPointer,
                        document.TreeId, node.Id));
                    continue;
                }

                if (!blackboard.TryGetValue(keyId, out var key) || !IsSupportedObserverScope(document, key.Scope))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.InvalidWatchedKey,
                        document.FormatVersion == TreeDocument.CurrentFormatVersion
                            ? "Observer watched key must identify a declared Tree-scope blackboard key."
                            : "Observer watched key must identify a declared key in a contracted persisted scope.", keyPointer,
                        document.TreeId, node.Id));
                }
            }
        }

        private static bool IsSupportedObserverScope(TreeDocument document, BlackboardScope scope)
        {
            if (scope == BlackboardScope.Tree)
            {
                return true;
            }

            if (document.FormatVersion != TreeDocument.LatestFormatVersion)
            {
                return false;
            }

            return scope == BlackboardScope.Agent && document.AgentContract != null
                || scope == BlackboardScope.Shared && document.SharedContract != null;
        }

        private static void ValidateNodeCapabilities(
            TreeDocument document,
            NodeDocument node,
            NodeManifest manifest,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var policy = options.Policy;
            if (manifest.ExecutionDomain == NodeExecutionDomain.Managed && !policy.AllowManagedNodes
                || manifest.ExecutionDomain == NodeExecutionDomain.MainThread && !policy.AllowMainThreadNodes)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedExecutionDomain,
                    "Node execution domain is disabled by the selected project policy.",
                    NodePointer(node.Id) + "/type", document.TreeId, node.Id));
            }

            if (policy.RequireDeterministicNodes && !manifest.Deterministic)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedNodeCapability,
                    "Nondeterministic nodes are disabled by the selected project policy.",
                    NodePointer(node.Id) + "/type", document.TreeId, node.Id));
            }

            if (!policy.AllowSideEffects && manifest.SideEffects.Count > 0)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.UnsupportedNodeCapability,
                    "Side-effecting nodes are disabled by the selected project policy.",
                    NodePointer(node.Id) + "/type", document.TreeId, node.Id));
            }
        }

        private static void ValidatePolicyContract(ValidationOptions options, ICollection<Diagnostic> diagnostics)
        {
            var policy = options.Policy;
            if (policy.MaxTreeDepth.HasValue && policy.MaxTreeDepth.Value < 1)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Policy maxTreeDepth must be positive.", "/policy/maxTreeDepth"));
            }

            if (policy.MaxNodesPerTree.HasValue && policy.MaxNodesPerTree.Value < 1)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Policy maxNodesPerTree must be positive.", "/policy/maxNodesPerTree"));
            }

            if (!Enum.IsDefined(typeof(BlackboardNamingPolicy), policy.BlackboardNaming))
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Policy blackboardNaming value is invalid.", "/policy/blackboardNaming"));
            }

            if (policy.MaxEstimatedCost.HasValue
                && (double.IsNaN(policy.MaxEstimatedCost.Value)
                    || double.IsInfinity(policy.MaxEstimatedCost.Value)
                    || policy.MaxEstimatedCost.Value < 0d))
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Policy maxEstimatedCost must be a finite non-negative number.",
                    "/policy/performance/maxEstimatedCost"));
            }

            for (var index = 0; index < policy.WarningsAsErrors.Count; index++)
            {
                if (!policy.WarningsAsErrors[index].IsValid)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Policy warningsAsErrors entries must be valid diagnostic codes.",
                        "/policy/warningsAsErrors/" + index));
                }
                else if (index > 0 && policy.WarningsAsErrors[index] == policy.WarningsAsErrors[index - 1])
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Policy warningsAsErrors entries must be unique.",
                        "/policy/warningsAsErrors/" + index));
                }
            }

            for (var index = 1; index < policy.ForbiddenNodeTypes.Count; index++)
            {
                if (string.Equals(policy.ForbiddenNodeTypes[index], policy.ForbiddenNodeTypes[index - 1], StringComparison.Ordinal))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Policy forbiddenNodeTypes entries must be unique.",
                        "/policy/forbiddenNodeTypes/" + index));
                }
            }
        }

        private static void ValidateProjectPolicy(
            TreeDocument document,
            Graph graph,
            NodeRegistry registry,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            var policy = options.Policy;
            if (policy.MaxNodesPerTree.HasValue && policy.MaxNodesPerTree.Value > 0
                && graph.OrderedNodes.Count > policy.MaxNodesPerTree.Value)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Tree exceeds the project policy node-count limit.", "/nodes", document.TreeId));
            }

            if (policy.RequireTreeDescription && string.IsNullOrWhiteSpace(document.Description))
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Project policy requires a tree description.", "/description", document.TreeId));
            }

            var forbidden = new HashSet<string>(policy.ForbiddenNodeTypes, StringComparer.Ordinal);
            double estimatedCost = 0d;
            for (var index = 0; index < graph.OrderedNodes.Count; index++)
            {
                var node = graph.OrderedNodes[index];
                if (policy.RequireNodeDescriptions && string.IsNullOrWhiteSpace(node.Description))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Project policy requires a node description.", NodePointer(node.Id) + "/description",
                        document.TreeId, node.Id));
                }

                if (forbidden.Contains(node.TypeId))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Node type is forbidden by project policy.", NodePointer(node.Id) + "/type",
                        document.TreeId, node.Id));
                }

                if (registry != null && registry.TryGet(node.TypeId, out var entry))
                {
                    var manifest = entry.Manifest;
                    estimatedCost += EstimatedCost(manifest.CostHint);
                    if (policy.RequireEventDrivenServices
                        && string.Equals(manifest.Category, "Service", StringComparison.Ordinal))
                    {
                        diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                            "Service nodes are rejected because Phase 1 has no event-driven service contract.",
                            NodePointer(node.Id) + "/type", document.TreeId, node.Id));
                    }

                    if (policy.ForbidUnboundedRepeaters
                        && string.Equals(node.TypeId, BuiltInNodeManifests.RepeaterTypeId, StringComparison.Ordinal)
                        && (node.Parameters == null
                            || !node.Parameters.TryGetValue("count", out var count)
                            || !TryGetUnsigned(count, uint.MaxValue, out var repeatCount)
                            || repeatCount == 0))
                    {
                        diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                            "Project policy forbids an unbounded or invalid repeater.",
                            ParameterPointer(node.Id, "count"), document.TreeId, node.Id));
                    }
                }
            }

            if (policy.MaxEstimatedCost.HasValue && policy.MaxEstimatedCost.Value >= 0d
                && !double.IsInfinity(policy.MaxEstimatedCost.Value)
                && !double.IsNaN(policy.MaxEstimatedCost.Value)
                && estimatedCost > policy.MaxEstimatedCost.Value)
            {
                diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                    "Tree exceeds the project policy estimated-cost limit.", "/nodes", document.TreeId));
            }

            ValidateNames(document, policy, options, diagnostics);
            if (policy.MaxTreeDepth.HasValue && policy.MaxTreeDepth.Value > 0
                && document.Root.IsValid && graph.Nodes.ContainsKey(document.Root))
            {
                var depth = MaximumDepth(document.Root, graph, new HashSet<NodeId>());
                if (depth > policy.MaxTreeDepth.Value)
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Tree exceeds the project policy depth limit.", "/root", document.TreeId));
                }
            }
        }

        private static void ValidateNames(
            TreeDocument document,
            TreeValidationPolicy policy,
            ValidationOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            if (policy.BlackboardNaming == BlackboardNamingPolicy.Any
                || !Enum.IsDefined(typeof(BlackboardNamingPolicy), policy.BlackboardNaming))
            {
                return;
            }

            for (var index = 0; index < document.Blackboard.Count; index++)
            {
                var key = document.Blackboard[index];
                if (key != null && !MatchesNaming(key.Name, policy.BlackboardNaming))
                {
                    diagnostics.Add(Create(options, TreeValidationDiagnosticCodes.PolicyViolation,
                        "Blackboard key name violates the project naming policy.",
                        BlackboardPointer(key.Id) + "/name", document.TreeId));
                }
            }
        }

        private static int MaximumDepth(NodeId id, Graph graph, ISet<NodeId> stack)
        {
            var maximum = 0;
            var pending = new Stack<DepthFrame>();
            pending.Push(new DepthFrame(id, 1, false));
            while (pending.Count > 0)
            {
                var frame = pending.Pop();
                if (frame.Exiting)
                {
                    stack.Remove(frame.Id);
                    continue;
                }

                if (!stack.Add(frame.Id) || !graph.Nodes.TryGetValue(frame.Id, out var node)) continue;
                maximum = Math.Max(maximum, frame.Depth);
                pending.Push(new DepthFrame(frame.Id, frame.Depth, true));
                for (var index = node.Children.Count - 1; index >= 0; index--)
                    pending.Push(new DepthFrame(node.Children[index], frame.Depth + 1, false));
            }

            return maximum;
        }

        private static bool ConditionMatches(
            NodeParameterCondition condition,
            IReadOnlyDictionary<string, SemanticValue> values)
        {
            return condition != null
                && values.TryGetValue(condition.ParameterName, out var value)
                && value != null
                && value.TryGetString(out var text)
                && string.Equals(text, condition.RequiredValue, StringComparison.Ordinal);
        }

        private static bool TryFindParameter(
            IReadOnlyList<NodeParameterContract> contracts,
            string name,
            out NodeParameterContract contract)
        {
            for (var index = 0; index < contracts.Count; index++)
            {
                if (string.Equals(contracts[index].Name, name, StringComparison.Ordinal))
                {
                    contract = contracts[index];
                    return true;
                }
            }

            contract = null;
            return false;
        }

        private static bool TryGetUnsigned(SemanticValue value, ulong maximum, out ulong result)
        {
            result = 0;
            if (value == null)
            {
                return false;
            }

            if (value.TryGetUInt64(out result))
            {
                return result <= maximum;
            }

            if (value.TryGetInt64(out var signed) && signed >= 0)
            {
                result = (ulong)signed;
                return result <= maximum;
            }

            return false;
        }

        private static bool MatchesNaming(string value, BlackboardNamingPolicy policy)
        {
            if (string.IsNullOrEmpty(value)) return false;
            switch (policy)
            {
                case BlackboardNamingPolicy.SnakeCase:
                    if (value[0] == '_' || value[value.Length - 1] == '_') return false;
                    for (var index = 0; index < value.Length; index++)
                    {
                        var character = value[index];
                        if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9') && character != '_') return false;
                        if (character == '_' && index > 0 && value[index - 1] == '_') return false;
                    }
                    return true;
                case BlackboardNamingPolicy.CamelCase:
                    return value[0] >= 'a' && value[0] <= 'z' && IsAlphaNumericWord(value);
                case BlackboardNamingPolicy.PascalCase:
                    return value[0] >= 'A' && value[0] <= 'Z' && IsAlphaNumericWord(value);
                default:
                    return true;
            }
        }

        private static bool IsAlphaNumericWord(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')) return false;
            }
            return true;
        }

        private static bool IsAuthoringId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsAlphaNumeric(value[0])) return false;
            for (var index = 1; index < value.Length; index++)
            {
                var character = value[index];
                if (!IsAlphaNumeric(character) && character != '.' && character != '_' && character != ':' && character != '-') return false;
            }
            return true;
        }

        private static bool IsAlphaNumeric(char value)
        {
            return value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z' || value >= '0' && value <= '9';
        }

        private static IEnumerable<Diagnostic> PromoteWarnings(
            IEnumerable<Diagnostic> diagnostics,
            IReadOnlyList<DiagnosticCode> warningsAsErrors)
        {
            var promoted = new HashSet<DiagnosticCode>(warningsAsErrors);
            foreach (var diagnostic in diagnostics)
            {
                yield return diagnostic.Severity == DiagnosticSeverity.Warning && promoted.Contains(diagnostic.Code)
                    ? new Diagnostic(diagnostic.Code, DiagnosticSeverity.Error, diagnostic.Message,
                        diagnostic.Location, diagnostic.RelatedLocations)
                    : diagnostic;
            }
        }

        private static double EstimatedCost(NodeCostHint hint)
        {
            switch (hint)
            {
                case NodeCostHint.Trivial: return 1d;
                case NodeCostHint.Low: return 2d;
                case NodeCostHint.Medium: return 4d;
                case NodeCostHint.High: return 8d;
                case NodeCostHint.Variable: return 16d;
                default: return 0d;
            }
        }

        private static Diagnostic Create(
            ValidationOptions options,
            DiagnosticCode code,
            string message,
            string pointer,
            TreeId treeId = default,
            NodeId nodeId = default,
            IEnumerable<DiagnosticLocation> related = null,
            DiagnosticSeverity? severity = null)
        {
            return TreeValidationDiagnosticCatalog.Create(
                code, message, Location(options, pointer, treeId, nodeId), related, severity);
        }

        private static DiagnosticLocation Location(
            ValidationOptions options,
            string pointer,
            TreeId treeId = default,
            NodeId nodeId = default)
        {
            return new DiagnosticLocation(options.DocumentId, pointer, treeId: treeId, nodeId: nodeId);
        }

        private static string NodePointer(NodeId id) => "/nodes/" + Escape(id.Value);

        private static string ParameterPointer(NodeId id, string name)
            => NodePointer(id) + "/parameters/" + Escape(name);

        private static string BlackboardPointer(string id)
            => string.IsNullOrEmpty(id) ? "/blackboard" : "/blackboard/" + Escape(id);

        private static string Escape(string value)
            => (value ?? string.Empty).Replace("~", "~0").Replace("/", "~1");

        private sealed class Graph
        {
            public Dictionary<NodeId, NodeDocument> Nodes { get; } = new Dictionary<NodeId, NodeDocument>();
            public List<NodeDocument> OrderedNodes { get; } = new List<NodeDocument>();
            public Dictionary<NodeId, List<NodeDocument>> Parents { get; } = new Dictionary<NodeId, List<NodeDocument>>();
        }

        private sealed class TraversalFrame
        {
            public TraversalFrame(NodeDocument node) { Node = node; }
            public NodeDocument Node { get; }
            public int ChildIndex { get; set; }
        }

        private readonly struct DepthFrame
        {
            public DepthFrame(NodeId id, int depth, bool exiting)
            {
                Id = id;
                Depth = depth;
                Exiting = exiting;
            }

            public NodeId Id { get; }
            public int Depth { get; }
            public bool Exiting { get; }
        }

        private readonly struct IndexedNode
        {
            public IndexedNode(NodeDocument node, int index) { Node = node; Index = index; }
            public NodeDocument Node { get; }
            public int Index { get; }
            public static int Compare(IndexedNode left, IndexedNode right)
            {
                if (left.Node == null) return right.Node == null ? left.Index.CompareTo(right.Index) : 1;
                if (right.Node == null) return -1;
                var result = string.Compare(left.Node.Id.Value, right.Node.Id.Value, StringComparison.Ordinal);
                return result != 0 ? result : left.Index.CompareTo(right.Index);
            }
        }

        private readonly struct IndexedBlackboardKey
        {
            public IndexedBlackboardKey(BlackboardKeyDefinition key, int index) { Key = key; Index = index; }
            public BlackboardKeyDefinition Key { get; }
            public int Index { get; }
            public static int Compare(IndexedBlackboardKey left, IndexedBlackboardKey right)
            {
                if (left.Key == null) return right.Key == null ? left.Index.CompareTo(right.Index) : 1;
                if (right.Key == null) return -1;
                var result = string.Compare(left.Key.Id, right.Key.Id, StringComparison.Ordinal);
                return result != 0 ? result : left.Index.CompareTo(right.Index);
            }
        }

        private readonly struct ScopedName : IEquatable<ScopedName>
        {
            public ScopedName(BlackboardScope scope, string name) { Scope = scope; Name = name; }
            public BlackboardScope Scope { get; }
            public string Name { get; }
            public bool Equals(ScopedName other) => Scope == other.Scope && string.Equals(Name, other.Name, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is ScopedName other && Equals(other);
            public override int GetHashCode() => ((int)Scope * 397) ^ StringComparer.Ordinal.GetHashCode(Name);
        }
    }
}
