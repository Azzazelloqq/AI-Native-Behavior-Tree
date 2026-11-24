using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-016 item 3: the agent-frame job's two results for a dispatch it has no slot for stay as they
    /// were before the slot table became leaf-only: a node beyond the table, and a node the table has no
    /// slot for. Each is reached by running the job core on a real host's agent with a table that lacks
    /// the first leaf, once without an effect queue (the machine rejects the completion) and once with
    /// one (the reservation's CapacityExceeded). Tree and catalog as in <see cref="BorrowedCatalogMetadataTests"/>.
    /// </summary>
    public sealed class AgentFrameSlotTableTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.p11016.slot-table"",
  ""name"": ""P11-016 agent-frame slot table"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""armed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""project-node"", ""armed-check"", ""wait"", ""set-score"", ""score-check""] },
    ""project-node"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""armed-check"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""armed"" } },
    ""wait"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 500000 } },
    ""set-score"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } },
    ""score-check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        /// <summary>The first leaf the memory sequence dispatches ("project-node").</summary>
        private const uint FirstLeaf = 1u;

        private enum Missing { BeyondTable, WithoutSlot }

        private struct NoBody : AIBT.AgentFrame.IAgentFrameBodyV1
        {
            public BurstExecutionResult Execute(ref BurstExecutionBatch batch)
            {
                Assert.Fail("No node body may run for a dispatch without a slot.");
                return default;
            }
        }

        private GameObject _gameObject;
        private GeneratedBurstCatalogV2 _catalog;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                var host = _gameObject.GetComponent<ProductionTreeHost>();
                if (host != null)
                    typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
            if (_catalog != null && _catalog.IsCreated) Assert.That(_catalog.TryDispose(out var failure), Is.True, failure.ToString());
            _catalog = null;
        }

        [Test]
        public void ANodeBeyondTheTable_WithoutEffects_IsRejectedAsInvalidHandle()
            => AssertRejected(RunWithout(Missing.BeyondTable, effects: false), BurstContextResult.InvalidHandle);

        [Test]
        public void ANodeWithoutASlot_WithoutEffects_IsRejectedAsTypeMismatch()
            => AssertRejected(RunWithout(Missing.WithoutSlot, effects: false), BurstContextResult.TypeMismatch);

        [Test]
        public void ANodeBeyondTheTable_WithEffects_IsRejectedAsCapacityExceeded()
            => AssertRejected(RunWithout(Missing.BeyondTable, effects: true), BurstContextResult.CapacityExceeded);

        [Test]
        public void ANodeWithoutASlot_WithEffects_IsRejectedAsCapacityExceeded()
            => AssertRejected(RunWithout(Missing.WithoutSlot, effects: true), BurstContextResult.CapacityExceeded);

        /// <summary>
        /// The results measured on the layout before P11-016 (one slot per node, commit a02d2f4): the
        /// machine refuses the completion, the job stops with CallbackRejected at the callback site, and
        /// no body runs.
        /// </summary>
        private static void AssertRejected(AgentFrameAgentV1 agent, BurstContextResult expected)
        {
            Assert.That(agent.Outcome, Is.EqualTo(AgentFrameOutcomeV1.CallbackRejected));
            Assert.That(agent.RejectedResult, Is.EqualTo(expected));
            Assert.That(agent.RejectedNodeIndex, Is.EqualTo(FirstLeaf));
            Assert.That(agent.RejectedSite, Is.EqualTo((byte)AgentFrameRejectionSiteV1.Callback));
            Assert.That(agent.Dispatches, Is.Zero);
        }

        /// <summary>
        /// Begins one agent frame of a real host, runs the job core on it with a slot table that lacks
        /// the first leaf, and finishes the frame. The host's own table is not changed.
        /// </summary>
        private AgentFrameAgentV1 RunWithout(Missing missing, bool effects)
        {
            var host = CreateHost(effects);
            var agent = default(AgentFrameAgentV1);
            host.BeginAgentFrame(out var included, ref agent);
            Assert.That(included, Is.True, "The host's first update runs.");
            Assert.That(agent.EffectFree >= 0, Is.EqualTo(effects));
            var table = AgentFrameTableWithout(ref agent, missing);
            try
            {
                var body = default(NoBody);
                AgentFrameCoreV1.RunAgent(ref agent, ref body);
                var result = agent;
                LogAssert.Expect(LogType.Error, new Regex("Callback rejected: " + agent.RejectedResult));
                host.FinishAgentFrame(ref agent);
                return result;
            }
            finally
            {
                table.Dispose();
            }
        }

        /// <summary>
        /// Replaces the agent's node-to-slot map with a copy that lacks <see cref="FirstLeaf"/> (shorter, or
        /// -1 for it); returns the copy to dispose. The leaf-only slot table itself is not changed.
        /// </summary>
        private static UnsafeList<int> AgentFrameTableWithout(ref AgentFrameAgentV1 agent, Missing missing)
        {
            var original = agent.SlotOfNode;
            Assert.That(original.Length, Is.GreaterThan((int)FirstLeaf));
            Assert.That(original[(int)FirstLeaf], Is.GreaterThanOrEqualTo(0));
            var length = missing == Missing.BeyondTable ? (int)FirstLeaf : original.Length;
            var copy = new UnsafeList<int>(System.Math.Max(1, length), Allocator.Persistent);
            for (var index = 0; index < length; index++) copy.Add(original[index]);
            if (missing == Missing.WithoutSlot) copy.ElementAt((int)FirstLeaf) = -1;
            agent.SlotOfNode = copy;
            return copy;
        }

        [Test]
        public void TheTableHasOneSlotPerLeaf_AndEveryLeafMapsToItsOwnSlot()
        {
            var host = CreateHost(effects: false);
            host.GeneratedDispatchAdapter.GetAgentFrameTable(out var slots, out var slotOfNode, out var catalog);
            Assert.That(slotOfNode.Length, Is.EqualTo(6), "The root and five leaves.");
            Assert.That(slots.Length, Is.EqualTo(5), "Five generated leaves; the memory sequence has no slot.");
            Assert.That(slotOfNode[0], Is.EqualTo(-1), "The root composite.");
            for (var node = 1; node < slotOfNode.Length; node++)
            {
                var slot = slotOfNode[node];
                Assert.That(slot, Is.InRange(0, slots.Length - 1));
                Assert.That(slots[slot].NodeIndex, Is.EqualTo((uint)node));
            }
            Assert.That(_catalog.TryLendWorkspaceMetadata(out var metadata), Is.True);
            Assert.That(catalog.Cases.Equals(metadata.Cases), Is.True, "The agent's catalog fields are the catalog's own arrays.");
            Assert.That(catalog.Bindings.Equals(metadata.Bindings), Is.True);
            Assert.That(catalog.CanonicalRules.Equals(metadata.CanonicalRules), Is.True);
        }

        private ProductionTreeHost CreateHost(bool effects)
        {
            Assert.That(StandardNativeProbeCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out _catalog, out var catalogFailure),
                Is.True, catalogFailure.ToString());
            var standard = Materialize(typeof(StandardNativeShard));
            var project = Materialize(typeof(ExternalTreeWriteFixtureShard));
            var nodes = standard.Nodes.Concat(project.Nodes).ToArray();
            const string sourceId = "Tests/P11016/slot-table.aibt.json";
            var parsed = CanonicalTreeJson.Parse(TreeJson, sourceId);
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes, options);
            Assert.That(compiled.Success, Is.True, string.Join("\n", compiled.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            Assert.That(compiled.Program.TryCreateRuntimeDefinitionV2(_catalog, standard.RegisteredTypes, out var definition, out var definitionFailure),
                Is.True, definitionFailure.ToString());
            var programNodes = definition.Binding.SemanticProgram.Nodes;
            Assert.That(programNodes[(int)FirstLeaf].NodeTypeId, Is.Not.EqualTo(programNodes[0].NodeTypeId),
                "Node 1 is the first leaf, not the root.");

            _gameObject = new GameObject("AIBT.Tests.P11016.SlotTableHost");
            var host = _gameObject.AddComponent<ProductionTreeHost>();
            if (effects) Assert.That(host.TryConfigureEffectDelivery(16, out var effectFailure), Is.True, effectFailure.Code.ToString());
            Assert.That(host.TryBootstrap(definition, _catalog, NativeTraceChannelCapacityV1.None, () => 0L, out var bootstrapFailure),
                Is.True, bootstrapFailure.Code.ToString());
            return host;
        }

        private static GeneratedShardMetadataArtifact Materialize(System.Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }
    }
}
