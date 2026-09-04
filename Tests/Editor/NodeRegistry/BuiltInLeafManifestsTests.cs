using System;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.NodeRegistry
{
    /// <summary>
    /// P7-028: proves the two aibt.stdlib.* built-in leaves (aibt.stdlib.wait,
    /// aibt.stdlib.random-condition) are correctly registered as real built-ins (not project
    /// extensions), that their reference-side <see cref="NodeManifest"/> stays byte-identical to
    /// the canonical JSON the native [AibtBurstNode] shard derives from its own attributes (the
    /// compile-time AIBT5012 catalog-handshake check this card discovered enforces this for any
    /// type actually reachable through a live [AibtCatalogSet] -- this test protects the same
    /// invariant at the unit-test level so a future edit to either side fails fast, without a full
    /// Unity domain reload), and that both tick correctly through the real, unmodified
    /// <see cref="ReferenceExecutionMachine"/>.
    /// </summary>
    public sealed class BuiltInLeafManifestsTests
    {
        [Test]
        public void BuiltInLeaves_AreRegisteredAsBuiltInWithAReferenceBindingAndAnExecutableBehavior()
        {
            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            var result = builder.Build();

            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));

            foreach (var typeId in new[] { BuiltInLeafManifests.WaitTypeId, BuiltInLeafManifests.RandomConditionTypeId })
            {
                Assert.That(result.Registry.TryGet(typeId, out var entry), Is.True, typeId + " must be in the built-in registry.");
                Assert.That(entry.Source, Is.EqualTo(NodeManifestSource.BuiltIn));
                Assert.That(entry.HasReferenceHandlerBinding, Is.True);
                Assert.That(builder.TryGetProjectLeafBehavior(typeId, out var behavior), Is.True);
                Assert.That(behavior, Is.Not.Null);
            }
        }

        [Test]
        public void ReferenceManifests_MatchTheNativeShardsOwnGeneratedCanonicalJsonExactly()
        {
            // AIBT.Authoring.BuiltInLeaves.BuiltInLeafShard's compiler-generated
            // AibtGeneratedMetadata.ManifestRegistryJson is the ground truth the AIBT5012
            // catalog-handshake analyzer checks these two manifests against. Reading it back via
            // reflection here (instead of hand-copying it into this test) means a future edit to
            // Authoring/Registry/Generated/BuiltInLeaves/Runtime/BuiltInLeafNodes.cs that drifts
            // from Authoring/Model/Nodes/BuiltInLeafManifests.cs fails this test, not just a full
            // Editor compile.
            var shardType = Type.GetType(
                "AIBT.Authoring.BuiltInLeaves.BuiltInLeafShard, AIBT.Authoring.BuiltInLeaves", throwOnError: true);
            var metadataType = shardType.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(metadataType, Is.Not.Null, "BuiltInLeafShard must carry generator-emitted AibtGeneratedMetadata.");
            var jsonField = metadataType.GetField(
                "ManifestRegistryJson", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var shardJson = (string)jsonField.GetRawConstantValue();

            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var entries = new[]
            {
                registry.Single(e => e.Manifest.TypeId == BuiltInLeafManifests.WaitTypeId),
                registry.Single(e => e.Manifest.TypeId == BuiltInLeafManifests.RandomConditionTypeId),
            };
            var referenceJson = NodeManifestCanonicalJson.SerializeRegistry(entries);

            foreach (var typeId in new[] { BuiltInLeafManifests.WaitTypeId, BuiltInLeafManifests.RandomConditionTypeId })
            {
                Assert.That(ExtractManifestBlock(shardJson, typeId), Is.EqualTo(ExtractManifestBlock(referenceJson, typeId)),
                    "The native shard's own generated manifest for " + typeId +
                    " no longer matches the hand-authored reference manifest -- these must stay byte-identical.");
            }
        }

        [Test]
        public void Wait_RunsForTheConfiguredTickCountThenSucceeds_ThroughTheRealUnmodifiedMachine()
        {
            var (registry, behavior) = BuildRegistryAndBehavior(BuiltInLeafManifests.WaitTypeId);
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("ticks", SemanticValue.FromUInt64(3)),
            });
            var machine = BuildMachine(registry, behavior, BuiltInLeafManifests.WaitTypeId, parameters);

            var first = machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            var second = machine.Update(new ReferenceUpdateContext(2, new Revision(1), 0));
            Assert.That(second.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            var third = machine.Update(new ReferenceUpdateContext(3, new Revision(1), 0));

            Assert.That(third.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(third.RootResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void RandomCondition_ZeroPercentAlwaysFails_HundredPercentAlwaysSucceeds()
        {
            var (registry, behavior) = BuildRegistryAndBehavior(BuiltInLeafManifests.RandomConditionTypeId);

            var nonDeterministicPolicy = new ReferenceCompilationPolicy(requireDeterministicNodes: false);

            var zeroPercent = new SemanticObject(new[]
            {
                new SemanticProperty("success-chance-percent", SemanticValue.FromUInt64(0)),
            });
            var neverMachine = BuildMachine(registry, behavior, BuiltInLeafManifests.RandomConditionTypeId, zeroPercent, nonDeterministicPolicy);
            var neverResult = neverMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(neverResult.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(neverResult.RootResult, Is.EqualTo(NodeStatus.Failure));

            var (registry2, behavior2) = BuildRegistryAndBehavior(BuiltInLeafManifests.RandomConditionTypeId);
            var hundredPercent = new SemanticObject(new[]
            {
                new SemanticProperty("success-chance-percent", SemanticValue.FromUInt64(100)),
            });
            var alwaysMachine = BuildMachine(registry2, behavior2, BuiltInLeafManifests.RandomConditionTypeId, hundredPercent, nonDeterministicPolicy);
            var alwaysResult = alwaysMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(alwaysResult.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(alwaysResult.RootResult, Is.EqualTo(NodeStatus.Success));
        }

        private static (Authoring.NodeRegistry Registry, IReferenceLeafBehavior Behavior) BuildRegistryAndBehavior(string typeId)
        {
            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            var registry = builder.Build().Registry;
            Assert.That(builder.TryGetProjectLeafBehavior(typeId, out var behavior), Is.True);
            return (registry, behavior);
        }

        private static ReferenceExecutionMachine BuildMachine(
            Authoring.NodeRegistry registry, IReferenceLeafBehavior behavior, string typeId, SemanticObject parameters,
            ReferenceCompilationPolicy policy = null)
        {
            var leaf = new NodeDocument(
                new NodeId("root"), typeId, 1, Array.Empty<NodeId>(), parameters, tags: TagSet.Empty);
            var document = new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p7-028-built-in-leaf"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);

            var options = new ReferenceCompilerOptions(
                "trees/p7-028-built-in-leaf.aibt.json", policy ?? ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var compilation = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(compilation.Success, Is.True,
                string.Join(" | ", compilation.Diagnostics.Select(d => d.Code + ": " + d.Message)));

            var leafRegistry = new ReferenceLeafRegistry(new[]
            {
                new ReferenceLeafBinding(StableHash.Fnv1A64(typeId), 1, new ProjectReferenceLeafHandlerAdapter(behavior)),
            });

            return new ReferenceExecutionMachine(
                compilation.Program, new TreeInstanceId(1), leafRegistry, null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);
        }

        private static string ExtractManifestBlock(string registryJson, string typeId)
        {
            var marker = "\"typeId\": \"" + typeId + "\"";
            var markerIndex = registryJson.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), typeId + " not found in: " + registryJson);
            var start = registryJson.LastIndexOf('{', markerIndex);
            var depth = 0;
            for (var index = start; index < registryJson.Length; index++)
            {
                var character = registryJson[index];
                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return registryJson.Substring(start, index - start + 1);
                    }
                }
            }

            throw new InvalidOperationException("Unbalanced braces while extracting " + typeId + ".");
        }
    }
}
