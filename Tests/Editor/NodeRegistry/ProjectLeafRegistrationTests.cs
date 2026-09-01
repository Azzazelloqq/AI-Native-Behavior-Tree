using System;
using System.Linq;
using AIBT.Authoring;
using AIBT.Mcp.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.NodeRegistry
{
    /// <summary>
    /// P7-008, applying ADR-P6-017: proves the new public per-project leaf-registration surface
    /// (<see cref="IReferenceLeafBehavior"/>/<see cref="ReferenceLeafContext"/>/
    /// <see cref="IReferenceLeafBehaviorProvider"/>/<see cref="NodeRegistryBuilder.AddProjectExtension"/>)
    /// end to end: a project-style leaf, defined below using only the new public contract (no
    /// internal AIBT.Runtime/AIBT.Authoring type), registers, is discoverable through the same
    /// registry the MCP discovery tools read from, and executes correctly through a real,
    /// unmodified <see cref="ReferenceExecutionMachine"/> -- closing both the ADR's own deferred
    /// design question and the P6-012 gate's live-reproduced discoverability gap.
    /// </summary>
    public sealed class ProjectLeafRegistrationTests
    {
        private const string CustomLeafTypeId = "example.project.doubling-counter";

        [Test]
        public void ProjectExtension_AttachesABindingAndIsDiscoverableThroughTheSameRegistry()
        {
            var provider = new DoublingCounterLeafProvider();

            var result = new NodeRegistryBuilder()
                .AddProjectExtension(provider.Manifest, provider.CreateBehavior())
                .Build();

            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(result.Registry[0].Source, Is.EqualTo(NodeManifestSource.UserExtension));
            Assert.That(result.Registry[0].HasReferenceHandlerBinding, Is.True);
            Assert.That((result.Registry.Capabilities & NodeRegistryCapabilityFlags.UserExtensions) != 0, Is.True);
            Assert.That((result.Registry.Capabilities & NodeRegistryCapabilityFlags.ReferenceHandlerBindings) != 0, Is.True);

            // The exact registry-shape used by aibt_search_nodes/aibt_get_node_contract
            // (MCP/McpToolDispatcher.cs) -- proves the discovery-layer query surfaces a project
            // registration the same way it surfaces a built-in.
            var query = new NodeCatalogQuery(result.Registry);
            Assert.That(query.TryGetContract(CustomLeafTypeId, out _), Is.True);
            Assert.That(query.Search("doubling-counter").Select(e => e.Manifest.TypeId), Does.Contain(CustomLeafTypeId));
        }

        [Test]
        public void AddUserExtension_StillNeverAttachesABinding_UnchangedFromBeforeThisCard()
        {
            // The ADR's own "unchanged negative test": the pre-existing, unbound public path must
            // keep behaving exactly as before -- also directly covered, unmodified, by
            // NodeRegistryBuilderTests.UserExtension_IsUnboundAndAdvertisedAsCapability.
            var result = new NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.stillunbound"))
                .Build();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Registry[0].HasReferenceHandlerBinding, Is.False);
        }

        [Test]
        public void ProjectRegisteredLeaf_TicksCorrectlyThroughTheRealUnmodifiedMachine()
        {
            var provider = new DoublingCounterLeafProvider();
            var behavior = provider.CreateBehavior();

            var nodeRegistry = new NodeRegistryBuilder()
                .AddProjectExtension(provider.Manifest, behavior)
                .Build().Registry;

            var document = SingleLeafTree();
            var options = new ReferenceCompilerOptions(
                "trees/p7-008-project-leaf.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var compilation = ReferenceCompiler.Compile(document, nodeRegistry, options);
            Assert.That(compilation.Success, Is.True,
                string.Join(" | ", compilation.Diagnostics.Select(d => d.Code + ": " + d.Message)));

            // Only the reference-executor test harness itself (not the project's own leaf class
            // above) reaches into internal machinery -- exactly like the disposable P6-017 spike
            // this proof supersedes, and unavoidable here since ReferenceLeafRegistry/
            // ReferenceExecutionMachine are internal-by-design engine plumbing, never something a
            // project constructs itself.
            var leafRegistry = new ReferenceLeafRegistry(new[]
            {
                new ReferenceLeafBinding(
                    StableHash.Fnv1A64(CustomLeafTypeId), 1, new ProjectReferenceLeafHandlerAdapter(behavior)),
            });

            var machine = new ReferenceExecutionMachine(
                compilation.Program, new TreeInstanceId(1), leafRegistry, null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);

            var first = machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            var second = machine.Update(new ReferenceUpdateContext(2, new Revision(1), 0));
            Assert.That(second.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            var third = machine.Update(new ReferenceUpdateContext(3, new Revision(1), 0));

            Assert.That(third.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(third.RootResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void McpDiscoveryCombination_FoldsAProjectProviderIntoTheBuiltInRegistry()
        {
            // Exercises the exact pure combination logic MCP/McpToolDispatcher.cs's SearchNodes/
            // GetNodeContract/GetProjectManifest now call (via
            // ProjectLeafExtensionDiscovery.BuildWithBuiltInsAndProjectExtensions), without needing
            // a live UnityEditor.TypeCache scan in a unit test.
            var builder = ProjectLeafExtensionDiscovery.AddDiscovered(
                NodeRegistryBuilder.CreateWithBuiltIns(), new IReferenceLeafBehaviorProvider[] { new DoublingCounterLeafProvider() });

            var result = builder.Build();
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));

            var query = new NodeCatalogQuery(result.Registry);
            Assert.That(query.TryGetContract(CustomLeafTypeId, out _), Is.True);
            Assert.That(result.Registry.Count, Is.EqualTo(BuiltInNodeManifests.All.Count + 1));
        }

        private static TreeDocument SingleLeafTree()
        {
            var leaf = new NodeDocument(
                new NodeId("leaf"), CustomLeafTypeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p7-008-project-leaf"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        // Project-style: implemented using only the new public contract, no internal AIBT type.
        private sealed class DoublingCounterLeafProvider : IReferenceLeafBehaviorProvider
        {
            // Deliberately no public parameterless constructor: UnityEditor.TypeCache scans every
            // loaded assembly, test assemblies included, so a discoverable fixture here would leak
            // into every *live* aibt_search_nodes/aibt_get_node_contract call made anywhere in this
            // Editor session -- exactly what broke McpToolDispatcherTests.
            // ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog the first time this was tried.
            // This fixture is only ever constructed directly, in this file; TypeCache-based
            // discovery itself is proven separately by McpDiscoveryCombination_..., which supplies
            // its own provider list explicitly rather than relying on a live scan.
            internal DoublingCounterLeafProvider()
            {
            }

            public NodeManifest Manifest { get; } = CreateManifest();

            public IReferenceLeafBehavior CreateBehavior() => new DoublingCounterLeafBehavior();

            private static NodeManifest CreateManifest()
            {
                var childPolicy = new NodeChildPolicy(0, 0, true);
                return new NodeManifest(
                    CustomLeafTypeId, 1, "Ticks 3 times then succeeds.", "Project",
                    NodeBehaviorKind.Action, "Proves the public per-project leaf registration surface.", "Never in production.",
                    NodeExecutionDomain.Burst, true,
                    Array.Empty<NodeParameterContract>(), childPolicy,
                    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                    new[] { NodeStatus.Success, NodeStatus.Running },
                    new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                    new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                    NodeCancellationMode.AbortOnly, NodeCostHint.Trivial,
                    new[] { new NodeManifestExample("Third tick succeeds", "{}", "Succeeds on the third tick.") });
            }
        }

        // Project-style: implemented using only the new public contract (IReferenceLeafBehavior,
        // ReferenceLeafContext, NodeStatus, NodeAbortReason, NodeExitReason) -- no internal type.
        private sealed class DoublingCounterLeafBehavior : IReferenceLeafBehavior
        {
            private int _tickCount;

            public void Enter(ref ReferenceLeafContext context)
            {
                _tickCount = 0;
            }

            public NodeStatus Tick(ref ReferenceLeafContext context)
            {
                _tickCount++;
                return _tickCount >= 3 ? NodeStatus.Success : NodeStatus.Running;
            }

            public void Abort(ref ReferenceLeafContext context, NodeAbortReason reason)
            {
            }

            public void Exit(ref ReferenceLeafContext context, NodeExitReason reason)
            {
            }
        }
    }
}
