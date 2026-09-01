using System;
using Unity.Collections;

namespace AIBT
{
    // Applies ADR-P7-011 (P7-012): bundles one native-backend tree instance's full ownership set --
    // NativeLifecycleMachineV1 itself owns nothing (confirmed: no TryDispose), so a caller must hold
    // its own NativeProgramImageOwnerV1/NativeInstanceArenaOwnerV1 plus every caller-allocated array
    // TryCreate needs (nodes/children/configuration/bindings/control/cooldownInitialized/
    // parallelBranches -- NativeInstanceArenaOwnerV1's own View supplies only NodeMemory/Frames/
    // Generations, confirmed by reading NativeInstanceArenaViewV1's own field list directly: no
    // cooldown or exactly-sized parallel-branch array exists there, so those two remain
    // caller-owned exactly like SchedulingPolicyDriver's/NativeBehaviorCaseAdapter's own established
    // pattern). Mirrors Spikes~/NativeHotReloadModel's own disposable SpikeInstance, generalized to
    // every node kind (the spike's own two-leaf trees never exercised Cooldown/Parallel).
    internal struct NativeHotReloadInstance : IDisposable
    {
        internal NativeLifecycleMachineV1 Machine;
        internal NativeProgramImageOwnerV1 ProgramOwner;
        internal NativeInstanceArenaOwnerV1 ArenaOwner;
        internal NativeArray<NativeCompiledNodeRecordV1> Nodes;
        internal NativeArray<uint> Children;
        internal NativeArray<byte> Configuration;
        internal NativeArray<NativeLifecycleNodeBindingV1> Bindings;
        internal NativeArray<NativeLifecycleControlV1> Control;
        internal NativeArray<byte> CooldownInitialized;
        internal NativeArray<NativeParallelBranchStateV1> ParallelBranches;

        public void Dispose()
        {
            ArenaOwner?.TryDispose(out _);
            ProgramOwner?.TryDispose(out _);
            if (Nodes.IsCreated) Nodes.Dispose();
            if (Children.IsCreated) Children.Dispose();
            if (Configuration.IsCreated) Configuration.Dispose();
            if (Bindings.IsCreated) Bindings.Dispose();
            if (Control.IsCreated) Control.Dispose();
            if (CooldownInitialized.IsCreated) CooldownInitialized.Dispose();
            if (ParallelBranches.IsCreated) ParallelBranches.Dispose();
        }

        // Same classification every node-kind-aware native test/driver in this codebase already
        // uses (Tests/Integration/NativeRuntime/NativeBehaviorCaseAdapter.cs's own private Kind
        // method) -- duplicated here rather than shared across a Runtime/Tests boundary, since nothing
        // production-facing exposed it before this card.
        // Internal (not private): NativeHotReloadStateMigration also needs this classification, to
        // recognize which migrated nodes are composites for the composite-cursor-reset rule.
        internal static NativeLifecycleNodeKindV1 ClassifyKind(ulong typeId)
        {
            if (typeId == StableHash.Fnv1A64("aibt.core.memory-sequence")) return NativeLifecycleNodeKindV1.MemorySequence;
            if (typeId == StableHash.Fnv1A64("aibt.core.memory-selector")) return NativeLifecycleNodeKindV1.MemorySelector;
            if (typeId == StableHash.Fnv1A64("aibt.core.reactive-sequence")) return NativeLifecycleNodeKindV1.ReactiveSequence;
            if (typeId == StableHash.Fnv1A64("aibt.core.reactive-selector")) return NativeLifecycleNodeKindV1.ReactiveSelector;
            if (typeId == StableHash.Fnv1A64("aibt.core.inverter")) return NativeLifecycleNodeKindV1.Inverter;
            if (typeId == StableHash.Fnv1A64("aibt.core.succeeder")) return NativeLifecycleNodeKindV1.Succeeder;
            if (typeId == StableHash.Fnv1A64("aibt.core.failer")) return NativeLifecycleNodeKindV1.Failer;
            if (typeId == StableHash.Fnv1A64("aibt.core.repeater")) return NativeLifecycleNodeKindV1.Repeater;
            if (typeId == StableHash.Fnv1A64("aibt.core.timeout")) return NativeLifecycleNodeKindV1.Timeout;
            if (typeId == StableHash.Fnv1A64("aibt.core.cooldown")) return NativeLifecycleNodeKindV1.Cooldown;
            if (typeId == StableHash.Fnv1A64("aibt.core.parallel")) return NativeLifecycleNodeKindV1.Parallel;
            return NativeLifecycleNodeKindV1.GeneratedLeaf;
        }

        // Fresh-instance construction reuses NativeProgramImageOwnerV1.TryCreate/
        // NativeInstanceArenaOwnerV1.TryCreate unchanged (ADR-P7-011 decision 1) -- no new
        // capacity-planning or lease-management code. Cleans up every partial allocation on any
        // failing step rather than leaking.
        internal static bool TryBuild(
            CompiledProgram program, Allocator allocator, out NativeHotReloadInstance instance, out NativeRuntimeFailureV1 failure)
        {
            instance = default;

            var programCapacity = NativeProgramImageCapacityV1.Exact(program);
            if (!NativeProgramImageOwnerV1.TryCreate(program, programCapacity, allocator, out var programOwner, out failure))
            {
                return false;
            }

            if (!programOwner.TryAcquireReadLease(out var buildLease, out failure))
            {
                programOwner.TryDispose(out _);
                return false;
            }

            if (!NativeInstanceArenaCapacityV1.TryDerive(buildLease.View, out var arenaCapacity, out failure))
            {
                programOwner.TryReleaseReadLease(buildLease, out _);
                programOwner.TryDispose(out _);
                return false;
            }

            if (!NativeInstanceArenaOwnerV1.TryCreate(buildLease, arenaCapacity, allocator, out var arenaOwner, out failure))
            {
                programOwner.TryReleaseReadLease(buildLease, out _);
                programOwner.TryDispose(out _);
                return false;
            }

            programOwner.TryReleaseReadLease(buildLease, out _);

            var nodes = new NativeArray<NativeCompiledNodeRecordV1>(program.Nodes.Count, allocator);
            var children = new NativeArray<uint>(program.ChildIndices.Count, allocator);
            var configuration = new NativeArray<byte>(program.ConfigBlob.Count, allocator);
            var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(program.Nodes.Count, allocator);
            var control = new NativeArray<NativeLifecycleControlV1>(1, allocator);

            var parallelBranchCount = 0u;
            var hasCooldown = false;
            for (var index = 0; index < program.Nodes.Count; index++)
            {
                var record = program.Nodes[index];
                nodes[index] = new NativeCompiledNodeRecordV1(record);
                var kind = ClassifyKind(record.NodeTypeId);
                bindings[index] = new NativeLifecycleNodeBindingV1((uint)index, kind);
                if (kind == NativeLifecycleNodeKindV1.Parallel) parallelBranchCount += (uint)record.Children.Count;
                if (kind == NativeLifecycleNodeKindV1.Cooldown) hasCooldown = true;
            }
            for (var index = 0; index < program.ChildIndices.Count; index++) children[index] = program.ChildIndices[index];
            for (var index = 0; index < program.ConfigBlob.Count; index++) configuration[index] = program.ConfigBlob[index];

            var cooldownInitialized = new NativeArray<byte>(hasCooldown ? program.Nodes.Count : 0, allocator);
            var parallelBranches = new NativeArray<NativeParallelBranchStateV1>((int)parallelBranchCount, allocator);

            if (!programOwner.TryAcquireReadLease(out var execProgramLease, out failure))
            {
                DisposeCallerArrays(nodes, children, configuration, bindings, control, cooldownInitialized, parallelBranches);
                arenaOwner.TryDispose(out _);
                programOwner.TryDispose(out _);
                return false;
            }

            if (!arenaOwner.TryAcquireExecutionLease(execProgramLease, out var execLease, out failure))
            {
                programOwner.TryReleaseReadLease(execProgramLease, out _);
                DisposeCallerArrays(nodes, children, configuration, bindings, control, cooldownInitialized, parallelBranches);
                arenaOwner.TryDispose(out _);
                programOwner.TryDispose(out _);
                return false;
            }

            var created = NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings,
                execLease.View.NodeMemory, execLease.View.Frames, execLease.View.Generations,
                control, configuration, cooldownInitialized, parallelBranches,
                out var machine, out failure);

            arenaOwner.TryReleaseExecutionLease(execLease, out _);
            programOwner.TryReleaseReadLease(execProgramLease, out _);

            if (!created)
            {
                DisposeCallerArrays(nodes, children, configuration, bindings, control, cooldownInitialized, parallelBranches);
                arenaOwner.TryDispose(out _);
                programOwner.TryDispose(out _);
                return false;
            }

            instance = new NativeHotReloadInstance
            {
                Machine = machine,
                ProgramOwner = programOwner,
                ArenaOwner = arenaOwner,
                Nodes = nodes,
                Children = children,
                Configuration = configuration,
                Bindings = bindings,
                Control = control,
                CooldownInitialized = cooldownInitialized,
                ParallelBranches = parallelBranches,
            };
            failure = default;
            return true;
        }

        private static void DisposeCallerArrays(
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> children,
            NativeArray<byte> configuration,
            NativeArray<NativeLifecycleNodeBindingV1> bindings,
            NativeArray<NativeLifecycleControlV1> control,
            NativeArray<byte> cooldownInitialized,
            NativeArray<NativeParallelBranchStateV1> parallelBranches)
        {
            nodes.Dispose();
            children.Dispose();
            configuration.Dispose();
            bindings.Dispose();
            control.Dispose();
            cooldownInitialized.Dispose();
            parallelBranches.Dispose();
        }
    }
}
