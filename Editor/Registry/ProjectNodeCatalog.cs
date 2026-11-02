using System;
using System.Collections.Generic;
using System.Linq;
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

        internal static ProjectNodeCatalog Initial()
            => new ProjectNodeCatalog(NodeRegistryBuilder.CreateWithBuiltIns().Build(), Array.Empty<string>());

        internal static ProjectNodeCatalog Discover()
        {
            var problems = new List<string>();
            var playerAssemblies = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(assembly => assembly.name), StringComparer.Ordinal);
            var shards = TypeCache.GetTypesWithAttribute<AibtCatalogShardAttribute>()
                .Where(type => playerAssemblies.Contains(type.Assembly.GetName().Name));
            return Build(shards, ProjectLeafExtensionDiscovery.DiscoverViaTypeCache(problems), problems);
        }

        internal static ProjectNodeCatalog Build(IEnumerable<Type> shards,
            IEnumerable<IReferenceLeafBehaviorProvider> providers, List<string> problems = null)
        {
            problems = problems ?? new List<string>();
            var builder = ProjectLeafExtensionDiscovery.AddDiscovered(NodeRegistryBuilder.CreateWithBuiltIns(), providers, problems);
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
                        artifact.ShardId == "aibt.stdlib.built-in-leaves") continue;
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
