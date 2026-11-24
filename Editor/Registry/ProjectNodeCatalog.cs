using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Burst;
using UnityEditor;
using UnityEditor.Compilation;

namespace AIBT.Editor.Registry
{
    internal sealed class ProjectNodeCatalog
    {
        private ProjectNodeCatalog(NodeRegistryBuildResult result, IReadOnlyList<string> problems, IReadOnlyDictionary<string, GeneratedNodeDescriptor> generated = null, RegisteredBlackboardTypeCatalog registeredTypes = null)
        {
            BuildResult = result;
            Problems = problems;
            Generated = generated ?? new Dictionary<string, GeneratedNodeDescriptor>();
            RegisteredTypes = registeredTypes ?? new RegisteredBlackboardTypeCatalog(Array.Empty<RegisteredBlackboardTypeCatalogEntry>());
        }

        internal NodeRegistryBuildResult BuildResult { get; }
        internal NodeRegistry Registry => BuildResult.Registry;
        internal IReadOnlyList<string> Problems { get; }
        internal IReadOnlyDictionary<string, GeneratedNodeDescriptor> Generated { get; }
        internal RegisteredBlackboardTypeCatalog RegisteredTypes { get; }

        /// <summary>
        /// True when the tree uses a managed (main-thread) node, so it runs in a BehaviorTreeAgent
        /// and is validated with the managed layer's policy instead of the native one.
        /// </summary>
        internal bool UsesManagedNodes(TreeDocument document) => UsesManagedNodes(document, Registry);

        internal static bool UsesManagedNodes(TreeDocument document, NodeRegistry registry)
            => document.Nodes.Any(node => registry.TryGet(node.TypeId, out var entry)
                && entry.Manifest.ExecutionDomain != NodeExecutionDomain.Burst);

        /// <summary>
        /// ADR-P9-001 (amendment 2026-11-24, P11-015): the policy a tree is checked with when the project
        /// has no <c>.aibt/policy.json</c>. A tree with a managed node runs in a <c>BehaviorTreeAgent</c> and
        /// gets the managed layer's policy (<see cref="ManagedTreeProgram.Policy"/>); any other tree runs in a
        /// native host and gets <see cref="NativeDefaultPolicy"/>. The tree list and MCP <c>validate</c> and
        /// <c>compile</c> take it from here; <c>prepare_runtime</c> only through its <c>validate</c> gate: the bake
        /// itself compiles every native tree with <see cref="ReferenceCompilationPolicy.Phase1"/>
        /// (<c>ProductionTreeAssetBaker</c>), whose scope flags the native compiler does not apply. Give the
        /// expanded tree when the tree uses subtrees (<see cref="ExpandForObserverRule"/>), so a subtree's nodes
        /// count as the parent's.
        /// </summary>
        internal static ReferenceCompilationPolicy DefaultPolicy(TreeDocument document, NodeRegistry registry)
            => UsesManagedNodes(document, registry) ? ManagedTreeProgram.Policy : NativeDefaultPolicy;

        /// <summary>
        /// <see cref="DefaultPolicy(TreeDocument, NodeRegistry)"/> for a tree that may reference subtrees:
        /// decided on the expanded tree when the expansion succeeds. P11-015 review follow-up (item 5): when
        /// it fails (a missing, ambiguous or mismatched subtree), on the parent and every subtree that does
        /// resolve, so a parent whose managed nodes all sit in a subtree still gets the managed policy and the
        /// result names the expansion's cause, not the native default refusing the subtree's managed nodes
        /// (AIBT2039/AIBT2040).
        /// </summary>
        internal ReferenceCompilationPolicy DefaultPolicy(TreeDocument document, ISubtreeResolver resolver, string sourceId)
        {
            var expanded = ExpandForObserverRule(document, resolver, sourceId);
            if (expanded != null) return DefaultPolicy(expanded, Registry);
            return UsesManagedNodesInResolvedTrees(document, resolver) ? ManagedTreeProgram.Policy : NativeDefaultPolicy;
        }

        private bool UsesManagedNodesInResolvedTrees(TreeDocument document, ISubtreeResolver resolver)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { document.TreeId.Value };
            var pending = new Queue<TreeDocument>();
            pending.Enqueue(document);
            while (pending.Count != 0)
            {
                var tree = pending.Dequeue();
                if (UsesManagedNodes(tree)) return true;
                foreach (var node in tree.Nodes)
                {
                    if (!node.IsSubtreeReference || !seen.Add(node.Subtree.TreeId.Value)) continue;
                    var resolution = resolver.Resolve(node.Subtree.TreeId);
                    if (resolution != null && resolution.Status == SubtreeResolutionStatus.Found && resolution.Document != null)
                        pending.Enqueue(resolution.Document);
                }
            }
            return false;
        }

        /// <summary>
        /// The native default without a policy file: the built-in defaults (no managed or main-thread
        /// nodes, deterministic nodes required) with Agent and Shared scope allowed, because native hosts
        /// bind both.
        /// </summary>
        internal static readonly ReferenceCompilationPolicy NativeDefaultPolicy = new ReferenceCompilationPolicy(
            supportsAgentScope: true, supportsSharedScope: true);

        internal DiagnosticCollection ValidateAuthoring(TreeDocument document, ReferenceCompilerOptions options)
        {
            var managed = UsesManagedNodes(document);
            if (managed) options = ManagedTreeProgram.CreateOptions(options.SourceId);
            var validation = new DiagnosticCollection(TreeValidator.Validate(document, Registry,
                options.Policy.CreateValidationOptions(options.SourceId), RegisteredTypes));
            if (validation.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return WithManagedRefusals(validation, managed, document, options);
            var nativeOnly = document.Nodes.Any(node => Registry.TryGet(node.TypeId, out var entry) && !entry.HasReferenceHandlerBinding);
            return WithManagedRefusals(nativeOnly
                ? GeneratedCompiledProgramV2Compiler.Compile(document, Generated.Values, RegisteredTypes, options).Diagnostics
                : ReferenceCompiler.Compile(document, Registry, options).Diagnostics, managed, document, options);
        }

        // P11-008 (review F10): a tree that runs in a BehaviorTreeAgent is refused there when it has an
        // observer block (AIBT2051); the Editor shows the same error. P11-009 (review F10, native half):
        // any other tree runs in a native host, which does not re-evaluate observers; the Editor warns
        // (AIBT2054) and the tree compiles as before. P11-014: one rule, ObserverDiagnostics.
        private DiagnosticCollection WithManagedRefusals(DiagnosticCollection diagnostics, bool managed, TreeDocument document,
            ReferenceCompilerOptions options)
        {
            var added = ObserverDiagnostics(document, Registry, options.SourceId);
            return added.Count == 0 ? diagnostics : new DiagnosticCollection(diagnostics.Concat(added));
        }

        /// <summary>
        /// P11-014 (items 1-3): what the hosts do with the <c>observer</c> blocks of a tree, given as the
        /// expanded tree (a subtree's nodes count as the parent's). A tree with a managed node runs only in a
        /// <c>BehaviorTreeAgent</c>, which refuses it: one error AIBT2051 per block
        /// (<see cref="ManagedTreeProgram.ObserverRefusals"/>, the agent's own rule). A tree of built-in nodes
        /// only runs in either host: one warning AIBT2054 per block that says what each does. Any other tree
        /// runs only in a native host: the warning AIBT2054 of <see cref="NativeObserverWarnings"/>. Used by
        /// the graph and MCP edits, the tree list, MCP <c>validate</c> and <c>compile</c>, and the bake.
        /// </summary>
        internal static List<Diagnostic> ObserverDiagnostics(TreeDocument document, NodeRegistry registry, string sourceId)
        {
            if (document == null || registry == null || document.Nodes.All(node => node.Observer == null)) return new List<Diagnostic>();
            if (UsesManagedNodes(document, registry)) return ManagedTreeProgram.ObserverRefusals(document, sourceId);
            return RunsInEitherHost(document, registry) ? ObserverWarnings(document, sourceId, true) : NativeObserverWarnings(document, sourceId);
        }

        /// <summary>
        /// Every node is a built-in (an <c>aibt.core.*</c> composite or decorator, or a built-in leaf such
        /// as <c>aibt.stdlib.wait</c>): a <c>BehaviorTreeAgent</c> binds these too, so either host loads the tree.
        /// </summary>
        private static bool RunsInEitherHost(TreeDocument document, NodeRegistry registry)
            => document.Nodes.All(node => !node.IsSubtreeReference && registry.TryGet(node.TypeId, out var entry)
                && entry.Source == NodeManifestSource.BuiltIn);

        /// <summary>
        /// P11-014 (item 2): the tree as the observer rule sees it: the document itself, or its structural
        /// expansion when it references subtrees and <paramref name="resolver"/> is given (as
        /// <see cref="ValidateAuthoring(TreeDocument, ReferenceCompilerOptions, ISubtreeResolver)"/> does).
        /// Null when the expansion fails; its errors are the compiler's to report.
        /// </summary>
        internal TreeDocument ExpandForObserverRule(TreeDocument document, ISubtreeResolver resolver, string sourceId)
        {
            if (document == null || resolver == null || !SubtreeExpander.RequiresExpansion(document)) return document;
            var probe = SubtreeExpander.Expand(document, resolver, Registry, StructuralExpansionPolicy.CreateValidationOptions(sourceId), RegisteredTypes);
            return probe.Success ? probe.Document : null;
        }

        /// <summary>
        /// P11-009 (review F10, native half): one warning (AIBT2054) per node with an <c>observer</c>
        /// block in a tree that runs in a native host. Native hosts (<c>NativeTreeAgent</c>,
        /// <c>ProductionTreeHost</c>, <c>ProductionTreeScheduler</c>, <c>TreeSimulation</c>) do not
        /// re-evaluate observers, so the block compiles and changes no outcome. Used by the graph and
        /// MCP edits (<see cref="ValidateAuthoring(TreeDocument, ReferenceCompilerOptions)"/>), the tree
        /// list, MCP <c>validate</c> and the native bake.
        /// </summary>
        internal static List<Diagnostic> NativeObserverWarnings(TreeDocument document, string sourceId)
            => ObserverWarnings(document, sourceId, false);

        private static List<Diagnostic> ObserverWarnings(TreeDocument document, string sourceId, bool eitherHost)
        {
            var result = new List<Diagnostic>();
            if (document == null) return result;
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                var node = document.Nodes[index];
                if (node.Observer == null) continue;
                var what = eitherHost
                    ? "Node '" + node.Id.Value + "' has an 'observer' block, and no host runs it. This tree has only built-in nodes, so it "
                      + "can run in either host: a BehaviorTreeAgent refuses it when it loads (AIBT2051), and native hosts (NativeTreeAgent, "
                      + "ProductionTreeHost, ProductionTreeScheduler, TreeSimulation) run it without re-evaluating observers: nothing "
                      + "re-checks the condition when a watched key changes, so the block changes no outcome."
                    : "Node '" + node.Id.Value + "' has an 'observer' block, but native hosts (NativeTreeAgent, ProductionTreeHost, "
                      + "ProductionTreeScheduler, TreeSimulation) do not re-evaluate observers: nothing re-checks the condition when a "
                      + "watched key changes, so the block changes no outcome.";
                result.Add(new Diagnostic(TreeValidationDiagnosticCodes.ObserverNotEvaluatedByNativeHost, DiagnosticSeverity.Warning,
                    what + " To re-check the condition on every update, make it an "
                    + "earlier child of '" + BuiltInNodeManifests.ReactiveSequenceTypeId + "' or '" + BuiltInNodeManifests.ReactiveSelectorTypeId
                    + "' (or their -retaining variants above a wait or a multi-step branch) and remove the block.",
                    new DiagnosticLocation(sourceId, "/nodes/" + node.Id.Value.Replace("~", "~0").Replace("/", "~1") + "/observer",
                        treeId: document.TreeId, nodeId: node.Id)));
            }
            return result;
        }

        /// <summary>
        /// <see cref="ValidateAuthoring(TreeDocument, ReferenceCompilerOptions)"/> for a tree that may
        /// use subtrees (ADR-P11-001): references are resolved through <paramref name="resolver"/>, and
        /// the managed/native choice is made on the expanded tree, so a subtree's nodes count as the
        /// parent's. A document with no reference node and no interface, or a null resolver, takes the
        /// overload without a resolver unchanged.
        /// </summary>
        internal DiagnosticCollection ValidateAuthoring(TreeDocument document, ReferenceCompilerOptions options, ISubtreeResolver resolver)
        {
            if (resolver == null || !SubtreeExpander.RequiresExpansion(document)) return ValidateAuthoring(document, options);
            // Structural expansion first (every node domain admitted, as at import), only to see which
            // nodes the expanded tree has; the compilers below expand again under the real policy.
            var probe = SubtreeExpander.Expand(document, resolver, Registry, StructuralExpansionPolicy.CreateValidationOptions(options.SourceId), RegisteredTypes);
            if (!probe.Success) return probe.Diagnostics;
            var managed = UsesManagedNodes(probe.Document);
            if (managed) options = ManagedTreeProgram.CreateOptions(options.SourceId);
            var nativeOnly = probe.Document.Nodes.Any(node => Registry.TryGet(node.TypeId, out var entry) && !entry.HasReferenceHandlerBinding);
            return WithManagedRefusals(nativeOnly
                ? GeneratedCompiledProgramV2Compiler.Compile(document, Generated.Values, RegisteredTypes, options, resolver).Diagnostics
                : ReferenceCompiler.Compile(document, Registry, options, resolver).Diagnostics, managed, probe.Document, options);
        }

        private static readonly ReferenceCompilationPolicy StructuralExpansionPolicy = new ReferenceCompilationPolicy(
            allowManagedNodes: true, allowMainThreadNodes: true, requireDeterministicNodes: false,
            supportsAgentScope: true, supportsSharedScope: true);

        internal static ProjectNodeCatalog Initial()
            => new ProjectNodeCatalog(NodeRegistryBuilder.CreateWithBuiltIns().Build(), Array.Empty<string>());

        internal static ProjectNodeCatalog Discover()
        {
            var problems = new List<string>();
            var playerAssemblies = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(assembly => assembly.name), StringComparer.Ordinal);
            var shards = TypeCache.GetTypesWithAttribute<AibtCatalogShardAttribute>()
                .Where(type => playerAssemblies.Contains(type.Assembly.GetName().Name));
            return Build(shards, ProjectLeafExtensionDiscovery.DiscoverViaTypeCache(problems), problems, ManagedNodeCatalog.Default);
        }

        internal static IReadOnlyList<Type> RuntimeCatalogTypes()
        {
            var playerAssemblies = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(assembly => assembly.name), StringComparer.Ordinal);
            return TypeCache.GetTypesWithAttribute<AibtCatalogSetAttribute>()
                .Where(type => playerAssemblies.Contains(type.Assembly.GetName().Name))
                .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();
        }

        internal static ProjectNodeCatalog ForCatalog(Type type)
        {
            var declaration = type.GetCustomAttributesData().Single(attribute => attribute.AttributeType == typeof(AibtCatalogSetAttribute));
            var shards = ((IEnumerable<System.Reflection.CustomAttributeTypedArgument>)declaration.ConstructorArguments[2].Value)
                .Select(argument => (Type)argument.Value);
            return Build(shards, Array.Empty<IReferenceLeafBehaviorProvider>());
        }

        internal static ProjectNodeCatalog Build(IEnumerable<Type> shards,
            IEnumerable<IReferenceLeafBehaviorProvider> providers, List<string> problems = null, ManagedNodeSet managed = null)
        {
            problems = problems ?? new List<string>();
            var builder = ProjectLeafExtensionDiscovery.AddDiscovered(NodeRegistryBuilder.CreateWithBuiltIns(), providers, problems);
            if (managed != null)
            {
                // Managed nodes (BtAction/BtCondition classes) run in a BehaviorTreeAgent; they are
                // listed here so the graph, validation and node search see them like any other node.
                managed.AddTo(builder);
                problems.AddRange(managed.Problems);
            }
            var references = builder.Build();
            if (!references.Success) return Fallback(references, problems);
            var generated = new Dictionary<string, GeneratedNodeDescriptor>(StringComparer.Ordinal);
            var registered = new Dictionary<string, RegisteredBlackboardTypeCatalogEntry>(StringComparer.Ordinal);
            foreach (var shard in shards.OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal))
            {
                try
                {
                    if (!GeneratedNodeReflectionHarness.TryReflectMetadata(shard, out var metadata, out var failure) ||
                        !GeneratedNodeReflectionHarness.TryMaterializeArtifact(metadata, out var artifact, out failure))
                    {
                        problems.Add("Cannot read node shard " + shard.FullName + ": " + failure);
                        continue;
                    }
                    // This generated shard is already represented by CreateWithBuiltIns().
                    if (shard.Assembly.GetName().Name == "AIBT.Authoring.BuiltInLeaves" &&
                        artifact.ShardId == "aibt.stdlib.built-in-leaves")
                    {
                        foreach (var node in artifact.Nodes) generated.Add(node.Manifest.TypeId, node);
                        continue;
                    }
                    foreach (var entry in artifact.RegisteredTypes.Entries)
                    {
                        var identity = entry.CanonicalTypeId + "\0" + entry.Version;
                        if (registered.TryGetValue(identity, out var previous))
                        {
                            if (previous.SchemaHash != entry.SchemaHash || !previous.Descriptor.Equals(entry.Descriptor))
                            {
                                problems.Add("Conflicting registered type " + entry.CanonicalTypeId + " version " + entry.Version + ".");
                                return Fallback(references, problems);
                            }
                        }
                        else registered.Add(identity, entry);
                    }
                    foreach (var node in artifact.Nodes)
                    {
                        if (!generated.ContainsKey(node.Manifest.TypeId)) generated.Add(node.Manifest.TypeId, node);
                        // A project may provide a reference behavior for the same generated node.
                        // Keep its executable binding when both contracts are exactly identical.
                        if (references.Registry.TryGet(node.Manifest.TypeId, out var existing) &&
                            existing.Source == NodeManifestSource.UserExtension && existing.HasReferenceHandlerBinding &&
                            ManifestHash(existing.Manifest) == ManifestHash(node.Manifest)) continue;
                        builder.AddUserExtension(node.Manifest);
                    }
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
                                                  exception is System.Reflection.TargetInvocationException || exception is InvalidCastException)
                {
                    problems.Add("Cannot read node shard " + shard.FullName + ": " + exception.Message);
                }
            }
            var result = builder.Build();
            if (!result.Success) return Fallback(result, problems);
            return new ProjectNodeCatalog(result, problems.AsReadOnly(), generated, new RegisteredBlackboardTypeCatalog(registered.Values));
        }

        private static string ManifestHash(NodeManifest manifest)
        {
            var result = new NodeRegistryBuilder().AddUserExtension(manifest).Build();
            return result.Success ? result.Registry.Hash : null;
        }

        private static ProjectNodeCatalog Fallback(NodeRegistryBuildResult failed, List<string> problems)
        {
            problems.AddRange(failed.Diagnostics.Select(diagnostic => diagnostic.Code.Value + ": " + diagnostic.Message));
            problems.Add("Project node registry is ambiguous; showing built-in nodes only.");
            return new ProjectNodeCatalog(NodeRegistryBuilder.CreateWithBuiltIns().Build(), problems.AsReadOnly());
        }
    }

    [InitializeOnLoad]
    internal static class ProjectNodeCatalogCache
    {
        private static volatile ProjectNodeCatalog _current = ProjectNodeCatalog.Initial();
        internal static event Action Changed;

        static ProjectNodeCatalogCache()
        {
            EditorApplication.delayCall += Refresh;
            EditorApplication.projectChanged += Refresh;
        }

        internal static ProjectNodeCatalog Current => _current;

        // Unity's compilation and type-discovery APIs are called only on the Editor main thread.
        internal static void Refresh()
        {
            _current = ProjectNodeCatalog.Discover();
            Changed?.Invoke();
        }
    }
}
