using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.Tests.CodeGen.Generation;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    [AibtCatalogSet("aibt.tests.p7037.lifecycle-proof", 1u, typeof(GenerationShard))]
    public static partial class GeneratedDispatchLifecycleProofCatalog
    {
        internal static BurstCatalogHandshake HandshakeForTests()
            => new BurstCatalogHandshake(
                2u,
                Fingerprint,
                NodeRegistryFingerprint,
                1u,
                1u,
                ConfigurationLayoutFingerprint,
                MemoryLayoutFingerprint,
                AccessLayoutFingerprint);
    }

    /// <summary>
    /// Disposable P7-037 proof. It deliberately keeps the generated catalog calls test-local until
    /// the production bootstrap/dispatcher surface is fixed by the implementation proposal.
    /// Every layout, node offset and binding ordinal comes from generated metadata or the normal
    /// v2 compiler; no dispatch shape is authored in this test.
    /// </summary>
    public sealed class GeneratedDispatchLifecycleProofTests
    {
        [Test]
        public void NormallyCompiledTree_ImmediateAndScheduledGeneratedDispatch_AreLifecycleEquivalent()
        {
            var artifact = MaterializeGenerationShard();
            var plan = GeneratedBurstDispatchPrebindingV2.CatalogPlan(
                "aibt.tests.p7037.lifecycle-proof",
                1u,
                new[] { artifact });
            var compiled = CompileFixture(artifact);
            var handshake = plan.Handshake;
            var validation = GeneratedDispatchLifecycleProofCatalog.Validate(in handshake);
            Assert.That(validation.Success, Is.True,
                validation.Code + " generated=" + Hash(GeneratedDispatchLifecycleProofCatalog.HandshakeForTests().NodeRegistry)
                + " plan=" + Hash(handshake.NodeRegistry)
                + " compiled=" + compiled.SemanticProgram.Header.NodeRegistryHash.HexadecimalValue
                + " shard=" + GenerationShard.AibtGeneratedMetadata.NodeRegistryHash);

            var immediate = Run(compiled, plan, scheduled: false);
            var scheduled = Run(compiled, plan, scheduled: true);

            Assert.That(immediate.RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(scheduled.RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(immediate.Phases, Is.EqualTo(new[]
            {
                BurstCallbackPhase.Enter,
                BurstCallbackPhase.Tick,
                BurstCallbackPhase.Exit,
            }));
            Assert.That(scheduled.Phases, Is.EqualTo(immediate.Phases));
            Assert.That(scheduled.Memory, Is.EqualTo(immediate.Memory));
            Assert.That(immediate.TickCount, Is.EqualTo(38u),
                "The generated Tick must read score=37, decode enabled=true and commit Count=38.");
            Assert.That(scheduled.TickCount, Is.EqualTo(immediate.TickCount));
        }

        private static ProofResult Run(
            GeneratedCompiledProgramV2 compiled,
            GeneratedBurstDispatchCatalogPlanV2 plan,
            bool scheduled)
        {
            var program = compiled.SemanticProgram;
            var leafIndex = FindGeneratedLeaf(program);
            var leaf = program.Nodes[(int)leafIndex];
            var dispatchCase = FindCase(plan, leaf.NodeTypeId, leaf.NodeTypeVersion);
            var countField = plan.MemoryFields[(int)dispatchCase.FirstMemoryField];
            using (var native = new NativeInputs(program))
            {
                Assert.That(native.Machine.TryBeginUpdate(1uL, 123_456L, out var failure), Is.True, failure.Code.ToString());
                var phases = new List<BurstCallbackPhase>();
                NodeStatus rootStatus = default;
                uint tickCount = 0u;
                var completed = false;

                while (!completed)
                {
                    Assert.That(native.Machine.TryAdvance(out var step, out failure), Is.True, failure.Code.ToString());
                    if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                    {
                        phases.Add(step.Phase);
                        var callback = Dispatch(native, compiled, plan, step, scheduled);
                        if (step.NodeIndex == leafIndex && step.Phase == BurstCallbackPhase.Tick)
                        {
                            tickCount = ReadUInt32(
                                native.Memory.ToArray(),
                                leaf.InstanceMemoryOffset + countField.ByteOffset);
                        }
                        Assert.That(native.Machine.TryCompleteDispatch(
                            step.DispatchToken,
                            callback.Failure,
                            callback.Status,
                            out failure), Is.True, failure.Code.ToString());
                    }
                    else if (step.Kind == NativeLifecycleStepKindV1.Completed)
                    {
                        Assert.That(step.HasRootStatus, Is.True);
                        rootStatus = step.RootStatus;
                        completed = true;
                    }
                    else if (step.Kind == NativeLifecycleStepKindV1.Waiting)
                    {
                        Assert.Fail("The fixture is terminal and must complete in one logical update.");
                    }
                }

                return new ProofResult(rootStatus, phases.ToArray(), native.Memory.ToArray(), tickCount);
            }
        }

        private static CallbackResult Dispatch(
            NativeInputs native,
            GeneratedCompiledProgramV2 compiled,
            GeneratedBurstDispatchCatalogPlanV2 plan,
            NativeLifecycleStepResultV1 step,
            bool scheduled)
        {
            var node = compiled.SemanticProgram.Nodes[(int)step.NodeIndex];
            var dispatchCase = FindCase(plan, node.NodeTypeId, node.NodeTypeVersion);
            var access = compiled.Accesses.Single(value => value.NodeIndex == step.NodeIndex);

            using (var input = new DispatchInput(
                plan,
                native.Configuration,
                native.Memory,
                new NativeBurstDispatchRequestV2(
                    0u,
                    step.NodeIndex,
                    node.NodeTypeId,
                    node.NodeTypeVersion,
                    dispatchCase.CatalogCaseIndex,
                    step.Phase,
                    node.ConfigOffset,
                    node.InstanceMemoryOffset,
                    0u,
                    123_456L,
                    new TreeInstanceId(1uL),
                    1u,
                    0u,
                    dispatchCase.BindingCount,
                    step.AbortReason,
                    step.ExitReason),
                access.SlotIndex))
            {
                var createInput = input.CreateInput(plan.Handshake);
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in createInput,
                    Allocator.Persistent,
                    out var owner,
                    out var contextFailure), Is.True, contextFailure.ToString());
                try
                {
                    Assert.That(owner.TryAcquireImmediateBatch(out var batch), Is.True);
                    if (scheduled)
                    {
                        var dependency = GeneratedDispatchLifecycleProofCatalog.Schedule(ref batch, default);
                        Assert.That(owner.TryRegisterDependency(in batch, dependency), Is.True);
                        dependency.Complete();
                        Assert.That(owner.TryAcquireCompletedBatch(out _), Is.True);
                    }
                    else
                    {
                        var execution = GeneratedDispatchLifecycleProofCatalog.ExecuteImmediate(ref batch);
                        Assert.That(execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                    }

                    Assert.That(owner.TryGetRequestStatus(0u, out var status), Is.True);
                    for (uint offset = 0; offset < dispatchCase.MemorySize; offset++)
                    {
                        Assert.That(owner.TryReadCommittedMemoryByte(0u, offset, out var value), Is.True);
                        native.Memory[(int)(node.InstanceMemoryOffset + offset)] = value;
                    }

                    return new CallbackResult(BurstContextResult.Success, status);
                }
                finally
                {
                    Assert.That(owner.TryDispose(out var disposeFailure), Is.True, disposeFailure.ToString());
                }
            }
        }

        private static GeneratedCompiledProgramV2 CompileFixture(GeneratedShardMetadataArtifact artifact)
        {
            const string sourceId = "Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json";
            var path = Path.Combine(
                Application.dataPath,
                "AIBT/Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json");
            var parsed = CanonicalTreeJson.Parse(File.ReadAllText(path), sourceId);
            Assert.That(parsed.Success, Is.True, Diagnostics(parsed.Diagnostics));
            var options = new ReferenceCompilerOptions(
                sourceId,
                ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 5));
            var result = GeneratedCompiledProgramV2Compiler.Compile(
                parsed.Document,
                artifact.Nodes,
                artifact.RegisteredTypes,
                options);
            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            return result.Program;
        }

        private static GeneratedShardMetadataArtifact MaterializeGenerationShard()
            => GeneratedShardMetadataMaterializer.MaterializeArtifact(
                GenerationShard.AibtGeneratedMetadata.ShardId,
                GenerationShard.AibtGeneratedMetadata.ShardVersion,
                GenerationShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                GenerationShard.AibtGeneratedMetadata.DescriptorHash,
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash);

        private static NativeBurstDispatchCaseV2 FindCase(
            GeneratedBurstDispatchCatalogPlanV2 plan,
            ulong typeId,
            uint version)
            => plan.Cases.Single(value => value.TypeNumericId == typeId && value.TypeVersion == version);

        private static uint FindGeneratedLeaf(CompiledProgram program)
        {
            for (var index = 0; index < program.Nodes.Count; index++)
            {
                if (NativeHotReloadInstance.ClassifyKind(program.Nodes[index].NodeTypeId)
                    == NativeLifecycleNodeKindV1.GeneratedLeaf)
                {
                    return (uint)index;
                }
            }
            throw new InvalidOperationException("Compiled fixture lacks a generated leaf.");
        }

        private static string Hash(BurstHash256 value)
            => value.Word0.ToString("x8") + value.Word1.ToString("x8")
                + value.Word2.ToString("x8") + value.Word3.ToString("x8")
                + value.Word4.ToString("x8") + value.Word5.ToString("x8")
                + value.Word6.ToString("x8") + value.Word7.ToString("x8");

        private static string Diagnostics(DiagnosticCollection diagnostics)
            => string.Join("\n", diagnostics.Select(value => value.Code + ": " + value.Message));

        private static uint ReadUInt32(byte[] source, uint offset)
            => source[(int)offset]
                | (uint)source[(int)(offset + 1u)] << 8
                | (uint)source[(int)(offset + 2u)] << 16
                | (uint)source[(int)(offset + 3u)] << 24;

        private readonly struct CallbackResult
        {
            internal CallbackResult(BurstContextResult failure, NodeStatus status)
            {
                Failure = failure;
                Status = status;
            }
            internal BurstContextResult Failure { get; }
            internal NodeStatus Status { get; }
        }

        private readonly struct ProofResult
        {
            internal ProofResult(NodeStatus rootStatus, BurstCallbackPhase[] phases, byte[] memory, uint tickCount)
            {
                RootStatus = rootStatus;
                Phases = phases;
                Memory = memory;
                TickCount = tickCount;
            }
            internal NodeStatus RootStatus { get; }
            internal BurstCallbackPhase[] Phases { get; }
            internal byte[] Memory { get; }
            internal uint TickCount { get; }
        }

        private sealed class NativeInputs : IDisposable
        {
            internal NativeInputs(CompiledProgram program)
            {
                Nodes = new NativeArray<NativeCompiledNodeRecordV1>(program.Nodes.Count, Allocator.Persistent);
                Children = new NativeArray<uint>(program.ChildIndices.Count, Allocator.Persistent);
                Bindings = new NativeArray<NativeLifecycleNodeBindingV1>(program.Nodes.Count, Allocator.Persistent);
                Memory = new NativeArray<byte>((int)program.Header.InstanceNodeMemorySize, Allocator.Persistent);
                Configuration = new NativeArray<byte>(program.ConfigBlob.Count, Allocator.Persistent);
                Frames = new NativeArray<NativeFrameStateV1>(program.Nodes.Count, Allocator.Persistent);
                Generations = new NativeArray<uint>(program.Nodes.Count, Allocator.Persistent);
                Control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                for (var index = 0; index < program.Nodes.Count; index++)
                {
                    Nodes[index] = new NativeCompiledNodeRecordV1(program.Nodes[index]);
                    Bindings[index] = new NativeLifecycleNodeBindingV1(
                        (uint)index,
                        NativeHotReloadInstance.ClassifyKind(program.Nodes[index].NodeTypeId));
                }
                for (var index = 0; index < program.ChildIndices.Count; index++) Children[index] = program.ChildIndices[index];
                for (var index = 0; index < program.ConfigBlob.Count; index++) Configuration[index] = program.ConfigBlob[index];
                Cooldown = new NativeArray<byte>(0, Allocator.Persistent);
                Parallel = new NativeArray<NativeParallelBranchStateV1>(0, Allocator.Persistent);
                Assert.That(NativeLifecycleMachineV1.TryCreate(
                    Nodes,
                    Children,
                    Bindings,
                    Memory,
                    Frames,
                    Generations,
                    Control,
                    Configuration,
                    Cooldown,
                    Parallel,
                    out var machine,
                    out var failure), Is.True, failure.Code.ToString());
                Machine = machine;
            }

            internal NativeArray<NativeCompiledNodeRecordV1> Nodes;
            internal NativeArray<uint> Children;
            internal NativeArray<NativeLifecycleNodeBindingV1> Bindings;
            internal NativeArray<byte> Memory;
            internal NativeArray<byte> Configuration;
            internal NativeArray<NativeFrameStateV1> Frames;
            internal NativeArray<uint> Generations;
            internal NativeArray<NativeLifecycleControlV1> Control;
            internal NativeArray<byte> Cooldown;
            internal NativeArray<NativeParallelBranchStateV1> Parallel;
            internal NativeLifecycleMachineV1 Machine;

            public void Dispose()
            {
                Parallel.Dispose();
                Cooldown.Dispose();
                Control.Dispose();
                Generations.Dispose();
                Frames.Dispose();
                Configuration.Dispose();
                Memory.Dispose();
                Bindings.Dispose();
                Children.Dispose();
                Nodes.Dispose();
            }
        }

        private sealed class DispatchInput : IDisposable
        {
            internal DispatchInput(
                GeneratedBurstDispatchCatalogPlanV2 plan,
                NativeArray<byte> configuration,
                NativeArray<byte> memory,
                NativeBurstDispatchRequestV2 request,
                uint targetOrdinal)
            {
                Cases = Copy(plan.Cases);
                Requests = Copy(new[] { request });
                ConfigurationFields = Copy(plan.ConfigurationFields);
                MemoryFields = Copy(plan.MemoryFields);
                Configuration = new NativeArray<byte>(configuration, Allocator.Temp);
                Memory = new NativeArray<byte>(memory, Allocator.Temp);
                RandomStates = new NativeArray<ulong>(0, Allocator.Temp);
                RandomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                Bindings = Copy(plan.Bindings);
                ResolvedBindings = Copy(new[] { new NativeBurstDispatchResolvedBindingV2(0u, targetOrdinal, 0u) });
                ValueFields = Copy(plan.ValueFields);
                LiveValueBytes = new NativeArray<byte>(4, Allocator.Temp);
                WriteUInt32(LiveValueBytes, 0u, 37u);
                Completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Temp);
                CompletionPayload = new NativeArray<byte>(0, Allocator.Temp);
                CaseRanges = Copy(plan.CaseRanges);
                BindingRanges = Copy(plan.BindingRanges);
                Rules = Copy(plan.CanonicalRules);
            }

            internal NativeBurstDispatchCreateInputV2 CreateInput(BurstCatalogHandshake handshake)
            {
                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    CaseRanges.AsReadOnly(),
                    BindingRanges.AsReadOnly(),
                    Rules.AsReadOnly());
                var bindingInput = new NativeBurstDispatchBindingInputV2(
                    Bindings.AsReadOnly(),
                    ResolvedBindings.AsReadOnly(),
                    ValueFields.AsReadOnly(),
                    LiveValueBytes.AsReadOnly(),
                    Completions.AsReadOnly(),
                    CompletionPayload.AsReadOnly(),
                    new NativeBurstDispatchBindingCapacityV2(8u, 64u, 8u, 64u, 8u),
                    canonical);
                return new NativeBurstDispatchCreateInputV2(
                    handshake,
                    Cases.AsReadOnly(),
                    Requests.AsReadOnly(),
                    ConfigurationFields.AsReadOnly(),
                    MemoryFields.AsReadOnly(),
                    Configuration.AsReadOnly(),
                    Memory.AsReadOnly(),
                    RandomStates.AsReadOnly(),
                    RandomIncrements.AsReadOnly(),
                    bindingInput,
                    canonical);
            }

            internal NativeArray<NativeBurstDispatchCaseV2> Cases { get; }
            internal NativeArray<NativeBurstDispatchRequestV2> Requests { get; }
            internal NativeArray<NativeBurstDispatchFieldV2> ConfigurationFields { get; }
            internal NativeArray<NativeBurstDispatchFieldV2> MemoryFields { get; }
            internal NativeArray<byte> Configuration { get; }
            internal NativeArray<byte> Memory { get; }
            internal NativeArray<ulong> RandomStates { get; }
            internal NativeArray<ulong> RandomIncrements { get; }
            internal NativeArray<NativeBurstDispatchBindingV2> Bindings { get; }
            internal NativeArray<NativeBurstDispatchResolvedBindingV2> ResolvedBindings { get; }
            internal NativeArray<NativeBurstDispatchFieldV2> ValueFields { get; }
            internal NativeArray<byte> LiveValueBytes { get; }
            internal NativeArray<NativeBurstDispatchCompletionV2> Completions { get; }
            internal NativeArray<byte> CompletionPayload { get; }
            internal NativeArray<NativeBurstDispatchCanonicalRangeV2> CaseRanges { get; }
            internal NativeArray<NativeBurstDispatchCanonicalRangeV2> BindingRanges { get; }
            internal NativeArray<NativeBurstDispatchCanonicalRuleV2> Rules { get; }

            public void Dispose()
            {
                Rules.Dispose();
                BindingRanges.Dispose();
                CaseRanges.Dispose();
                CompletionPayload.Dispose();
                Completions.Dispose();
                LiveValueBytes.Dispose();
                ValueFields.Dispose();
                ResolvedBindings.Dispose();
                Bindings.Dispose();
                RandomIncrements.Dispose();
                RandomStates.Dispose();
                Memory.Dispose();
                Configuration.Dispose();
                MemoryFields.Dispose();
                ConfigurationFields.Dispose();
                Requests.Dispose();
                Cases.Dispose();
            }

            private static NativeArray<T> Copy<T>(IReadOnlyList<T> source) where T : struct
            {
                var result = new NativeArray<T>(source.Count, Allocator.Temp);
                for (var index = 0; index < source.Count; index++) result[index] = source[index];
                return result;
            }

            private static void WriteUInt32(NativeArray<byte> destination, uint offset, uint value)
            {
                destination[(int)offset] = (byte)value;
                destination[(int)(offset + 1u)] = (byte)(value >> 8);
                destination[(int)(offset + 2u)] = (byte)(value >> 16);
                destination[(int)(offset + 3u)] = (byte)(value >> 24);
            }
        }
    }
}
