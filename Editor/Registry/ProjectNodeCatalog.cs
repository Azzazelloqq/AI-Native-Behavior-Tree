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
        internal bool UsesManagedNodes(TreeDocument document)
            => document.Nodes.Any(node => Registry.TryGet(node.TypeId, out var entry)
                && entry.Manifest.ExecutionDomain != NodeExecutionDomain.Burst);

        internal DiagnosticCollection ValidateAuthoring(TreeDocument document, ReferenceCompilerOptions options)
        {
            if (UsesManagedNodes(document)) options = ManagedTreeProgram.CreateOptions(options.SourceId);
            var validation = new DiagnosticCollection(TreeValidator.Validate(document, Registry,
                options.Policy.CreateValidationOptions(options.SourceId), RegisteredTypes));
            if (validation.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return validation;
            var nativeOnly = document.Nodes.Any(node => Registry.TryGet(node.TypeId, out var entry) && !entry.HasReferenceHandlerBinding);
            return nativeOnly
                ? GeneratedCompiledProgramV2Compiler.Compile(document, Generated.Values, RegisteredTypes, options).Diagnostics
                : ReferenceCompiler.Compile(document, Registry, options).Diagnostics;
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
            if (UsesManagedNodes(probe.Document)) options = ManagedTreeProgram.CreateOptions(options.SourceId);
            var nativeOnly = probe.Document.Nodes.Any(node => Registry.TryGet(node.TypeId, out var entry) && !entry.HasReferenceHandlerBinding);
            return nativeOnly
                ? GeneratedCompiledProgramV2Compiler.Compile(document, Generated.Values, RegisteredTypes, options, resolver).Diagnostics
                : ReferenceCompiler.Compile(document, Registry, options, resolver).Diagnostics;
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
