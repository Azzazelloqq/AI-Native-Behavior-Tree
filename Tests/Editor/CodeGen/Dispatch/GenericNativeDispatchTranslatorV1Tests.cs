using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.CodeGen.Dispatch
{
    // Three real nodes, one real compiled shard (via the packaged analyzer, exactly like
    // Tests/Editor/CodeGen/Generation/GeneratedArtifactContractTests.cs's own GenerationShard) --
    // proves GenericNativeDispatchTranslatorV1's 0..targetIndex prefix path (P7-009, ADR-P6-022)
    // against real generated metadata, not a hand-authored workspace. TypeId alphabetical order
    // (GeneratedMetadataEmitter's own emission order) fixes AaaAsyncBlocker at dispatch index 0,
    // AlphaCondition at index 1, BetaCondition at index 2.
    [AibtCatalogShard("aibt.tests.p7009.dispatch-prefix", 1u)]
    public partial struct PrefixDispatchShard { }

    public partial struct AaaAsyncBlockerConfig
    {
        [AibtConfigField("source", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("source", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Source;

        [AibtConfigField("operation", "GeneratedHandle", 1u)]
        [AibtAsyncOperationBinding("operation", "Int32", 1u, "Int32", 1u)]
        public AsyncOperationHandle<int, int> Operation;

        [AibtConfigField("completion", "GeneratedHandle", 1u)]
        [AibtCompletionBinding("completion", "Int32", 1u)]
        public CompletionHandle<int> Completion;
    }

    public partial struct AaaAsyncBlockerMemory
    {
        [AibtMemoryField("operation-id", "OperationId", 1u)]
        public OperationId OperationId;

        [AibtMemoryField("started", "Bool", 1u)]
        public bool Started;
    }

    [AibtNodeDocumentation("Deliberately out-of-scope (AsyncOperation binding).", "Tests", "Prefix-blocking proof.", "Never in production.", "aaa-async-blocker")]
    [AibtBurstNode(
        "aibt.tests.p7009.aaa-async-blocker", 1u, BurstNodeKind.Action,
        typeof(AaaAsyncBlockerConfig), typeof(AaaAsyncBlockerMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.Command, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct AaaAsyncBlockerNode
    {
        public static void Enter(in AaaAsyncBlockerConfig config, ref AaaAsyncBlockerMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in AaaAsyncBlockerConfig config, ref AaaAsyncBlockerMemory memory, ref BurstTickContext context) => NodeStatus.Running;
        public static void Abort(in AaaAsyncBlockerConfig config, ref AaaAsyncBlockerMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in AaaAsyncBlockerConfig config, ref AaaAsyncBlockerMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct AlphaConditionConfig
    {
        [AibtConfigField("current", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("current", BurstBlackboardAccess.Read, BlackboardScope.Tree, "UInt32", 1u)]
        public BlackboardReadHandle<uint> Current;

        [AibtConfigField("threshold", "UInt32", 1u)]
        public uint Minimum;
    }

    public partial struct AlphaConditionConfigMemory { }

    [AibtNodeDocumentation("In-scope condition, dispatch index 1.", "Tests", "Prefix translation proof.", "Never in production.", "alpha")]
    [AibtBurstNode(
        "aibt.tests.p7009.alpha-condition", 1u, BurstNodeKind.Condition,
        typeof(AlphaConditionConfig), typeof(AlphaConditionConfigMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct AlphaConditionNode
    {
        public static void Enter(in AlphaConditionConfig config, ref AlphaConditionConfigMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in AlphaConditionConfig config, ref AlphaConditionConfigMemory memory, ref BurstTickContext context)
        {
            var result = PrefixDispatchShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum ? NodeStatus.Success : NodeStatus.Failure;
        }
        public static void Abort(in AlphaConditionConfig config, ref AlphaConditionConfigMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in AlphaConditionConfig config, ref AlphaConditionConfigMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct BetaConditionConfig
    {
        [AibtConfigField("current", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("current", BurstBlackboardAccess.Read, BlackboardScope.Tree, "UInt32", 1u)]
        public BlackboardReadHandle<uint> Current;

        [AibtConfigField("threshold", "UInt32", 1u)]
        public uint Minimum;
    }

    public partial struct BetaConditionConfigMemory { }

    [AibtNodeDocumentation("In-scope condition, dispatch index 2.", "Tests", "Prefix translation proof.", "Never in production.", "beta")]
    [AibtBurstNode(
        "aibt.tests.p7009.beta-condition", 1u, BurstNodeKind.Condition,
        typeof(BetaConditionConfig), typeof(BetaConditionConfigMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct BetaConditionNode
    {
        public static void Enter(in BetaConditionConfig config, ref BetaConditionConfigMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in BetaConditionConfig config, ref BetaConditionConfigMemory memory, ref BurstTickContext context)
        {
            var result = PrefixDispatchShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum ? NodeStatus.Success : NodeStatus.Failure;
        }
        public static void Abort(in BetaConditionConfig config, ref BetaConditionConfigMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in BetaConditionConfig config, ref BetaConditionConfigMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public sealed class GenericNativeDispatchTranslatorV1Tests
    {
        [Test]
        public void ThreeNodeCatalog_MatchesExpectedAlphabeticalDispatchOrder()
        {
            var artifact = Materialize();
            Assert.That(artifact.Nodes.Count, Is.EqualTo(3));
            Assert.That(artifact.Nodes[0].Manifest.TypeId, Is.EqualTo("aibt.tests.p7009.aaa-async-blocker"));
            Assert.That(artifact.Nodes[1].Manifest.TypeId, Is.EqualTo("aibt.tests.p7009.alpha-condition"));
            Assert.That(artifact.Nodes[2].Manifest.TypeId, Is.EqualTo("aibt.tests.p7009.beta-condition"));
        }

        [Test]
        public void PrefixTranslation_TargetNotAtIndexZero_BuildsAStructurallyValidWorkspace()
        {
            var artifact = Materialize();
            var handshake = default(BurstCatalogHandshake);

            // Targets aibt.tests.p7009.alpha-condition (real dispatch index 1) -- proves the
            // 0..targetIndex prefix path against a real, non-index-0 node, the exact scenario
            // ADR-P6-022's own spike addendum could not reach (test-node itself never can either --
            // see Planning~/Evidence/P7-009/). aaa-async-blocker sorts before alpha but is itself
            // in scope for THIS index (its own AsyncOperation binding is only checked once alpha's
            // own prefix requires translating it -- proven separately below), so building the
            // prefix through index 0 alone must fail here.
            Assert.Throws<System.NotSupportedException>(() =>
                GenericNativeDispatchTranslatorV1.Build(artifact, "aibt.tests.p7009.alpha-condition", handshake, Allocator.Temp));
        }

        [Test]
        public void PrefixTranslation_OutOfScopeCaseBlocksALaterInScopeTarget_NamingTheBlockingCase()
        {
            var artifact = Materialize();
            var handshake = default(BurstCatalogHandshake);

            // beta-condition (index 2) is itself fully in scope, but reaching it requires
            // translating the full 0..2 prefix, which includes aaa-async-blocker (index 0) -- an
            // AsyncOperation/Completion-bound node, explicitly out of the translator's proven scope.
            var ex = Assert.Throws<System.NotSupportedException>(() =>
                GenericNativeDispatchTranslatorV1.Build(artifact, "aibt.tests.p7009.beta-condition", handshake, Allocator.Temp));
            Assert.That(ex.Message, Does.Contain("aibt.tests.p7009.aaa-async-blocker"),
                "The failure must name the actual blocking case, not just fail opaquely.");
        }

        [Test]
        public void PrefixTranslation_TwoInScopeCasesOnly_BuildsAStructurallyValidWorkspace()
        {
            // Isolate to just the two in-scope nodes by materializing a fresh artifact view that
            // skips the blocker -- proves the flattened-array cursor bookkeeping (FirstConfigurationField/
            // FirstBinding running offsets across cases) is correct via the real TryCreate validation
            // path, not simply "did not throw".
            var fullArtifact = Materialize();
            var inScopeNodes = new System.Collections.Generic.List<GeneratedNodeDescriptor>();
            foreach (var node in fullArtifact.Nodes)
            {
                if (node.Manifest.TypeId != "aibt.tests.p7009.aaa-async-blocker")
                {
                    inScopeNodes.Add(node);
                }
            }

            var artifact = new GeneratedShardMetadataArtifact(
                fullArtifact.ShardId, fullArtifact.ShardVersion, inScopeNodes, fullArtifact.RegisteredTypes);
            var handshake = default(BurstCatalogHandshake);

            using (var built = GenericNativeDispatchTranslatorV1.Build(artifact, "aibt.tests.p7009.beta-condition", handshake, Allocator.Temp))
            {
                Assert.That(built.TargetCaseIndex, Is.EqualTo(1u));
                Assert.That(built.Shape.Cases.Length, Is.EqualTo(2));
                Assert.That(built.Shape.Cases[0].FirstConfigurationField, Is.EqualTo(0u));
                Assert.That(built.Shape.Cases[1].FirstConfigurationField, Is.EqualTo(built.Shape.Cases[0].ConfigurationFieldCount),
                    "The second case's own fields must start immediately after the first case's own fields in the shared flattened array.");
                Assert.That(built.Shape.Cases[1].FirstBinding, Is.EqualTo(built.Shape.Cases[0].BindingCount));

                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(64u, new NativeBurstDispatchBindingCapacityV2(8u, 64u, 8u, 64u, 4u, 1UL));
                var shape = built.Shape;
                Assert.That(
                    NativeBurstDispatchWorkspaceOwnerV2.TryCreate(in shape, in capacity, Allocator.Persistent, out var owner, out var failure),
                    Is.True, failure.ToString(),
                    "The real production ValidateShape must accept the flattened two-case workspace this translator built.");
                Assert.That(owner.TryDispose(out var disposeFailure), Is.True, disposeFailure.ToString());
            }
        }

        private static GeneratedShardMetadataArtifact Materialize()
            => GeneratedShardMetadataMaterializer.MaterializeArtifact(
                PrefixDispatchShard.AibtGeneratedMetadata.ShardId,
                PrefixDispatchShard.AibtGeneratedMetadata.ShardVersion,
                PrefixDispatchShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                PrefixDispatchShard.AibtGeneratedMetadata.DescriptorHash,
                PrefixDispatchShard.AibtGeneratedMetadata.ManifestRegistryJson,
                PrefixDispatchShard.AibtGeneratedMetadata.NodeRegistryHash);
    }
}
