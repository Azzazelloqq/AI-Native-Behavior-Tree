using System;
using System.Collections.Generic;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P7-041 behavior proof. The fixture kernel reads a P7-039 Tree-scope fact and emits a real
    /// generated command; these tests assert only the public host/world receiver contract.
    /// </summary>
    public sealed class ProductionTreeWorldEffectTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.p7041.production-world-effect"",
  ""name"": ""P7-041 Production World Effect Fixture"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": {
      ""type"": ""aibt.tests.external-tree-effect"",
      ""typeVersion"": 1,
      ""parameters"": { ""value"": 17 },
      ""bindings"": { ""live-target"": ""live-target"" }
    }
  }
}";

        [Test]
        public void ImmediateGeneratedEffect_ReachesPublicReceiverExactlyOnceAfterCommit()
        {
            using (var scenario = Scenario.Create(effectCapacity: 2))
            {
                Assert.That(scenario.TryWriteTarget(1f, out var writeFailure), Is.True, writeFailure.Code.ToString());

                var receiver = new RecordingReceiver();
                DriveUntilEffect(scenario.Host, receiver);
                Assert.That(receiver.Values, Is.EqualTo(new[] { 17 }),
                    "A generated EffectCommand must be delivered once, only after its Tick transaction commits.");

                Assert.That(scenario.Host.TryDrainEffects(receiver, out var drainFailure), Is.True, drainFailure.Code.ToString());
                Assert.That(receiver.Values, Is.EqualTo(new[] { 17 }),
                    "Draining again must not replay an already delivered command.");
                Assert.That(scenario.Host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            }
        }

        [Test]
        public void GeneratedCatalog_DescribesEffectByStableBindingId_AndRejectsUnknownOrdinal()
        {
            using (var scenario = Scenario.Create(effectCapacity: 1))
            {
                Assert.That(scenario.Catalog.TryFindCase(
                    StableHash.Fnv1A64("aibt.tests.external-tree-effect"), 1u, out var caseIndex), Is.True);
                var bindingOrdinal = FindEffectBindingOrdinal(scenario.Catalog, caseIndex);

                Assert.That(scenario.Catalog.Executor.TryGetEffectDescriptor(
                    caseIndex, bindingOrdinal, out var descriptor), Is.True);
                Assert.That(descriptor.StableId, Is.EqualTo(StableHash.Fnv1A64("hit")));
                Assert.That(descriptor.ValueKind, Is.EqualTo(ProductionTreeEffectValueKind.Int32));
                Assert.That(descriptor.IsSupported, Is.True);
                Assert.That(scenario.Catalog.Executor.TryGetEffectDescriptor(
                    caseIndex, bindingOrdinal + 1000u, out _), Is.False);
            }
        }

        [Test]
        public void GroupedGeneratedEffects_AreAttributedToTheirOwnPublicHostQueues()
        {
            var artifact = MaterializeShard();
            var compiled = CompileFixture(artifact);
            Assert.That(ExternalTreeWriteFixtureCatalog.TryCreateRuntimeCatalog(
                Allocator.Persistent, out var catalog, out var catalogFailure), Is.True, catalogFailure.ToString());
            GeneratedDispatchGroupWorkspaceV2 workspace = null;
            GameObject objectA = null;
            GameObject objectB = null;
            try
            {
                Assert.That(compiled.TryCreateRuntimeDefinitionV2(
                    catalog, artifact.RegisteredTypes, out var definition, out var definitionFailure), Is.True, definitionFailure.ToString());
                Assert.That(GeneratedDispatchGroupWorkspaceV2.TryCreate(
                    catalog, 2, out workspace, out var workspaceFailure), Is.True, workspaceFailure.ToString());

                objectA = new GameObject("AIBT.Tests.P7041.GroupHostA");
                objectB = new GameObject("AIBT.Tests.P7041.GroupHostB");
                var hostA = BootstrapHost(objectA, definition, catalog, effectCapacity: 2);
                var hostB = BootstrapHost(objectB, definition, catalog, effectCapacity: 2);
                Assert.That(TryWriteTarget(hostA, 1f, out var writeFailureA), Is.True, writeFailureA.Code.ToString());
                Assert.That(TryWriteTarget(hostB, 1f, out var writeFailureB), Is.True, writeFailureB.Code.ToString());

                var receiverA = new RecordingReceiver();
                var receiverB = new RecordingReceiver();
                var hosts = new[] { hostA, hostB };
                for (var wave = 0; wave < 8 && (receiverA.Values.Count == 0 || receiverB.Values.Count == 0); wave++)
                {
                    var members = new List<GeneratedDispatchGroupExecutorV2.Member>();
                    for (var index = 0; index < hosts.Length; index++)
                    {
                        if (hosts[index].TryAdvanceToNextDispatch(out var request))
                            members.Add(new GeneratedDispatchGroupExecutorV2.Member(hosts[index], request.NodeIndex, request));
                    }
                    if (members.Count != 0)
                    {
                        Assert.That(GeneratedDispatchGroupExecutorV2.TryExecuteGroup(
                            catalog, workspace, members, scheduled: false, out var groupFailure), Is.True,
                            "group wave " + wave + ": " + groupFailure);
                    }
                    Assert.That(hostA.TryDrainEffects(receiverA, out var drainFailureA), Is.True, drainFailureA.Code.ToString());
                    Assert.That(hostB.TryDrainEffects(receiverB, out var drainFailureB), Is.True, drainFailureB.Code.ToString());
                }

                Assert.That(receiverA.Values, Is.EqualTo(new[] { 17 }));
                Assert.That(receiverB.Values, Is.EqualTo(new[] { 17 }),
                    "The remapped group target ordinal must never send one agent's effect to another host.");
            }
            finally
            {
                if (objectA != null) DestroyHost(objectA);
                if (objectB != null) DestroyHost(objectB);
                if (workspace != null)
                    Assert.That(workspace.TryDispose(out var disposeWorkspaceFailure), Is.True, disposeWorkspaceFailure.ToString());
                Assert.That(catalog.TryDispose(out var disposeCatalogFailure), Is.True, disposeCatalogFailure.ToString());
            }
        }

        [Test]
        public void WorldBridge_ImmediateFrameFlushesFactThenDeliversCommittedEffect()
        {
            using (var scenario = Scenario.Create(effectCapacity: 2))
            {
                var schedulerObject = new GameObject("AIBT.Tests.P7041.ImmediateScheduler");
                try
                {
                    var scheduler = schedulerObject.AddComponent<ProductionTreeScheduler>();
                    var world = schedulerObject.AddComponent<ProductionTreeWorld>();
                    InvokePrivate(world, "Awake");
                    InvokePrivate(world, "OnEnable");
                    var participant = new StagedParticipant(scenario.Host) { NextTarget = 1f };

                    Assert.That(world.TryRegister(participant, SchedulingProfile.Normal, out var registrationError), Is.True,
                        registrationError.ToString());
                    for (var frame = 0; frame < 8 && participant.Effects.Count == 0; frame++)
                        InvokePrivate(scheduler, "Update");

                    Assert.That(participant.SampleCount, Is.GreaterThanOrEqualTo(1));
                    Assert.That(participant.FlushedTargets, Has.All.EqualTo(1f),
                        "The current frame must write the sampled fact before admitting immediate work.");
                    Assert.That(participant.Effects, Is.EqualTo(new[] { 17 }),
                        "The public receiver must see the effect after the native callback transaction commits.");
                    Assert.That(world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(schedulerObject);
                }
            }
        }

        [Test]
        public void WorldBridge_PipelinedWaveKeepsEarlierFactUntilItsNextIdleBarrier()
        {
            var artifact = MaterializeShard();
            var compiled = CompileFixture(artifact);
            Assert.That(ExternalTreeWriteFixtureCatalog.TryCreateRuntimeCatalog(
                Allocator.Persistent, out var catalog, out var catalogFailure), Is.True, catalogFailure.ToString());
            GameObject schedulerObject = null;
            GameObject objectA = null;
            GameObject objectB = null;
            try
            {
                Assert.That(compiled.TryCreateRuntimeDefinitionV2(
                    catalog, artifact.RegisteredTypes, out var definition, out var definitionFailure), Is.True, definitionFailure.ToString());
                Assert.That(SchedulerJobsCapabilities.TryCreate(
                    1.0, 1.0, 1u, 8u, 8u, true, out var capabilities, out var capabilitiesError), Is.True,
                    capabilitiesError.ToString());
                Assert.That(SchedulingProfile.TryCreate(
                    "aibt.tests.p7041.pipelined", 0, 1u, null, null, true,
                    SchedulingPolicy.PipelinedJobs, out var profile, out var profileError), Is.True, profileError.ToString());

                schedulerObject = new GameObject("AIBT.Tests.P7041.PipelinedScheduler");
                var scheduler = schedulerObject.AddComponent<ProductionTreeScheduler>();
                scheduler.SetJobsCapabilities(capabilities);
                scheduler.SetFixedBudget(1_000_000.0);
                var world = schedulerObject.AddComponent<ProductionTreeWorld>();
                InvokePrivate(world, "Awake");
                InvokePrivate(world, "OnEnable");
                objectA = new GameObject("AIBT.Tests.P7041.PipelinedHostA");
                objectB = new GameObject("AIBT.Tests.P7041.PipelinedHostB");
                var hostA = BootstrapHost(objectA, definition, catalog, effectCapacity: 2);
                var hostB = BootstrapHost(objectB, definition, catalog, effectCapacity: 2);
                var participantA = new StagedParticipant(hostA) { NextTarget = 0f };
                var participantB = new StagedParticipant(hostB) { NextTarget = 0f };
                Assert.That(world.TryRegister(participantA, profile, out var registrationErrorA), Is.True, registrationErrorA.ToString());
                Assert.That(world.TryRegister(participantB, profile, out var registrationErrorB), Is.True, registrationErrorB.ToString());

                InvokePrivate(scheduler, "Update");
                Assert.That(participantA.FlushedTargets, Is.EqualTo(new[] { 0f }));
                Assert.That(participantB.FlushedTargets, Is.EqualTo(new[] { 0f }));

                participantA.NextTarget = 1f;
                participantB.NextTarget = 1f;
                for (var frame = 0; frame < 32 && (participantA.Effects.Count == 0 || participantB.Effects.Count == 0); frame++)
                    InvokePrivate(scheduler, "Update");

                Assert.That(participantA.Effects, Is.EqualTo(new[] { 17 }));
                Assert.That(participantB.Effects, Is.EqualTo(new[] { 17 }));
                Assert.That(participantA.FlushedTargets, Has.Some.EqualTo(1f));
                Assert.That(participantB.FlushedTargets, Has.Some.EqualTo(1f));
                Assert.That(participantA.FirstEffectSawFlushedTarget, Is.EqualTo(1f),
                    "The original pipelined wave used 0; its later wave may see 1 only after an idle flush barrier.");
                Assert.That(participantB.FirstEffectSawFlushedTarget, Is.EqualTo(1f));
                Assert.That(world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            }
            finally
            {
                if (schedulerObject != null) UnityEngine.Object.DestroyImmediate(schedulerObject);
                if (objectA != null) DestroyHost(objectA);
                if (objectB != null) DestroyHost(objectB);
                Assert.That(catalog.TryDispose(out var disposeCatalogFailure), Is.True, disposeCatalogFailure.ToString());
            }
        }

        [Test]
        public void WarmedHostEffectDelivery_AllocatesNoManagedMemory()
        {
            using (var scenario = Scenario.Create(effectCapacity: 2))
            {
                Assert.That(scenario.TryWriteTarget(1f, out var writeFailure), Is.True, writeFailure.Code.ToString());
                var receiver = new CountingReceiver();
                DriveUntilEffect(scenario.Host, receiver);
                scenario.Host.DriveOneUpdate();

                Assert.That(() =>
                {
                    scenario.Host.TryDrainEffects(receiver, out _);
                }, GcAllocIs.Not.AllocatingGCMemory());
                Assert.That(receiver.Count, Is.EqualTo(2));
            }
        }

        private static uint FindEffectBindingOrdinal(GeneratedBurstCatalogV2 catalog, uint caseIndex)
        {
            var dispatchCase = catalog.Cases[(int)caseIndex];
            for (uint local = 0; local < dispatchCase.BindingCount; local++)
            {
                var binding = catalog.Bindings[(int)(dispatchCase.FirstBinding + local)];
                if (binding.Kind == NativeBurstDispatchBindingKindV2.EffectCommand) return binding.BindingOrdinal;
            }
            throw new AssertionException("The P7-041 fixture has no generated EffectCommand binding.");
        }

        private static void DriveUntilEffect(ProductionTreeHost host, IProductionTreeEffectReceiver receiver)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                host.DriveOneUpdate();
                Assert.That(host.TryDrainEffects(receiver, out var failure), Is.True, failure.Code.ToString());
                if (receiver is RecordingReceiver recording && recording.Values.Count != 0) return;
                if (receiver is CountingReceiver counting && counting.Count != 0) return;
            }
            Assert.Fail(
                "The fixture did not publish an effect after eight updates. updates=" + host.TotalUpdates
                + ", root=" + (host.LastRootResult.HasValue ? host.LastRootResult.Value.ToString() : "none")
                + ", failure=" + host.LastFailure.Code + ".");
        }

        private static ProductionTreeHost BootstrapHost(
            GameObject gameObject,
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            int effectCapacity)
        {
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(host.TryConfigureEffectDelivery(effectCapacity, out var configureFailure), Is.True, configureFailure.Code.ToString());
            Assert.That(host.TryBootstrap(
                definition, catalog,
                new NativeTraceChannelCapacityV1(65, 0, 0, 256),
                () => 123_456L, out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());
            return host;
        }

        private static bool TryWriteTarget(ProductionTreeHost host, float value, out NativeRuntimeFailureV1 failure)
        {
            var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
            if (!host.TryResolveExternalTreeWrite<float>("live-target", expectedType, out var handle, out failure)) return false;
            return host.TryWriteExternalTreeValue(handle, value, out _, out failure);
        }

        private static GeneratedCompiledProgramV2 CompileFixture(GeneratedShardMetadataArtifact artifact)
        {
            var parsed = CanonicalTreeJson.Parse(TreeJson, "Tests/P7041/production-world-effect.aibt.json");
            Assert.That(parsed.Success, Is.True, Diagnostics(parsed.Diagnostics));
            var result = GeneratedCompiledProgramV2Compiler.Compile(
                parsed.Document, artifact.Nodes, artifact.RegisteredTypes,
                new ReferenceCompilerOptions(
                    "Tests/P7041/production-world-effect.aibt.json",
                    ReferenceCompilationPolicy.Phase1,
                    new CompiledCompilerVersion(1, 0, 0, 5)));
            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            return result.Program;
        }

        private static GeneratedShardMetadataArtifact MaterializeShard()
            => GeneratedShardMetadataMaterializer.MaterializeArtifact(
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardId,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardVersion,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.DescriptorHash,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ManifestRegistryJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.NodeRegistryHash);

        private static string Diagnostics(DiagnosticCollection diagnostics)
            => string.Join("\n", System.Linq.Enumerable.Select(diagnostics, value => value.Code + ": " + value.Message));

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static void DestroyHost(GameObject gameObject)
        {
            var host = gameObject.GetComponent<ProductionTreeHost>();
            if (host != null) InvokePrivate(host, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        private sealed class RecordingReceiver : IProductionTreeEffectReceiver
        {
            internal readonly List<int> Values = new List<int>();
            public void Receive(in ProductionTreeEffect effect)
            {
                Assert.That(effect.ValueKind, Is.EqualTo(ProductionTreeEffectValueKind.Int32));
                Assert.That(effect.TryGetInt32(out var value), Is.True);
                Values.Add(value);
            }
        }

        private sealed class CountingReceiver : IProductionTreeEffectReceiver
        {
            internal int Count;
            public void Receive(in ProductionTreeEffect effect)
            {
                effect.TryGetInt32(out _);
                Count++;
            }
        }

        private sealed class StagedParticipant : IProductionTreeWorldParticipant
        {
            private readonly ProductionTreeHost.ExternalTreeWriteHandle<float> _target;
            private float _stagedTarget;

            internal StagedParticipant(ProductionTreeHost host)
            {
                Host = host;
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(host.TryResolveExternalTreeWrite<float>(
                    "live-target", expectedType, out _target, out var failure), Is.True, failure.Code.ToString());
            }

            public ProductionTreeHost Host { get; }
            internal float NextTarget;
            internal int SampleCount;
            internal readonly List<float> FlushedTargets = new List<float>();
            internal readonly List<int> Effects = new List<int>();
            internal float FirstEffectSawFlushedTarget = float.NaN;

            public void SampleInputs()
            {
                SampleCount++;
                _stagedTarget = NextTarget;
            }

            public bool TryFlushInputs(out NativeRuntimeFailureV1 failure)
            {
                var result = Host.TryWriteExternalTreeValue(_target, _stagedTarget, out _, out failure);
                if (result) FlushedTargets.Add(_stagedTarget);
                return result;
            }

            public void Receive(in ProductionTreeEffect effect)
            {
                Assert.That(effect.TryGetInt32(out var value), Is.True);
                if (Effects.Count == 0) FirstEffectSawFlushedTarget = FlushedTargets[FlushedTargets.Count - 1];
                Effects.Add(value);
            }
        }

        private sealed class Scenario : IDisposable
        {
            private readonly GameObject _gameObject;
            internal ProductionTreeHost Host { get; }
            internal GeneratedBurstCatalogV2 Catalog { get; }

            private Scenario(GameObject gameObject, ProductionTreeHost host, GeneratedBurstCatalogV2 catalog)
            {
                _gameObject = gameObject;
                Host = host;
                Catalog = catalog;
            }

            internal static Scenario Create(int effectCapacity)
            {
                var artifact = MaterializeShard();
                var compiled = CompileFixture(artifact);
                Assert.That(ExternalTreeWriteFixtureCatalog.TryCreateRuntimeCatalog(
                    Allocator.Persistent, out var catalog, out var catalogFailure), Is.True, catalogFailure.ToString());
                try
                {
                    Assert.That(compiled.TryCreateRuntimeDefinitionV2(
                        catalog, artifact.RegisteredTypes, out var definition, out var definitionFailure), Is.True,
                        definitionFailure.ToString());
                    var gameObject = new GameObject("AIBT.Tests.P7041.Host");
                    return new Scenario(gameObject, BootstrapHost(gameObject, definition, catalog, effectCapacity), catalog);
                }
                catch
                {
                    catalog.TryDispose(out _);
                    throw;
                }
            }

            internal bool TryWriteTarget(float value, out NativeRuntimeFailureV1 failure)
                => ProductionTreeWorldEffectTests.TryWriteTarget(Host, value, out failure);

            public void Dispose()
            {
                DestroyHost(_gameObject);
                Assert.That(Catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }
    }
}
