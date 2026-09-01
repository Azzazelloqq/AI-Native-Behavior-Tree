using System;
using System.Collections.Generic;
using System.Text;
using AIBT.Authoring;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Editor.Tests.Spikes
{
    /// <summary>
    /// P7-011 disposable spike: proves ADR-P5-001's construct-fresh-and-selectively-copy hot-reload
    /// model applied to the native backend, against real NativeProgramImageOwnerV1/
    /// NativeInstanceArenaOwnerV1/NativeLifecycleMachineV1 instances -- no production file touched.
    /// Archived to Spikes~/NativeHotReloadModel/ and removed from Tests/Editor/ once verified.
    /// </summary>
    [TestFixture]
    public sealed class SpikeNativeHotReloadModel
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 1);
        private static readonly ulong MemorySequenceTypeId = StableHash.Fnv1A64("aibt.core.memory-sequence");

        [Test]
        public void FullRestart_AbortsActiveOldInstance_ConstructsFreshOwnersFromNewProgram()
        {
            var oldProgram = Compile(TwoLeafTreeJson(reversed: false), "spike/p7-011/full-restart-old.aibt.json");
            var newProgram = Compile(TwoLeafTreeJson(reversed: false), "spike/p7-011/full-restart-new.aibt.json");

            var old = BuildInstance(oldProgram);
            try
            {
                Assert.IsTrue(old.Machine.TryBeginUpdate(1, out var beginFailure), beginFailure.Code.ToString());
                // Run the first tick to its natural Waiting boundary (the "running" leaf never
                // completes) -- the old instance is now genuinely active (live frames), the same
                // "stable point between frames" precondition hot reload requires.
                RunOneTickToWaiting(ref old.Machine);

                // Real finding: unlike the reference executor's own Abort (used by
                // HotReloadFullRestart), which specifically requires NO open update (works BETWEEN
                // ticks), native's TryRequestAbort requires the OPPOSITE -- an OPEN update
                // (control.UpdateOpen != 0). Waiting itself closes the update (UpdateOpen=0), so a
                // caller must open a fresh update first (resuming the already-active instance, not
                // re-entering it -- TryBeginUpdate only (re)initializes frame 0 when Depth==0) before
                // it can request the abort within that update.
                Assert.IsTrue(old.Machine.TryBeginUpdate(2, out var resumeFailure), resumeFailure.Code.ToString());
                Assert.IsTrue(old.Machine.TryRequestAbort(BurstNodeAbortReason.HotReload, out var abortFailure), abortFailure.Code.ToString());
                DrainToBoundary(ref old.Machine);

                var fresh = BuildInstance(newProgram);
                try
                {
                    Assert.IsTrue(fresh.Machine.TryBeginUpdate(1, out var freshBeginFailure), freshBeginFailure.Code.ToString());
                    var freshStep = AdvanceToDispatch(ref fresh.Machine);
                    // The fresh instance starts clean -- it re-enters the first node from scratch, unaffected by the old instance's abort.
                    Assert.AreEqual(NativeLifecycleStepKindV1.DispatchRequired, freshStep.Kind);
                }
                finally
                {
                    fresh.Dispose();
                }
            }
            finally
            {
                old.Dispose();
            }
        }

        [Test]
        public void Migration_ReorderedChildren_CopiesActivationByStableNodeIdAcrossShiftedCompiledIndices_UsingOnlyPublicOwnerApi()
        {
            // Old: sequence(a-running, b-success). New: sequence(b-success, a-running) -- a pure
            // reorder of the children array; each node's own NodeId keeps its own fixed type
            // ("a" is always "running", "b" is always "success"), so both classify Migrate under
            // ADR-P5-001 even though the compiled index of the active node ("a") shifts, mirroring
            // P5-001's own load-bearing reorder proof.
            var oldProgram = Compile(TwoLeafTreeJson(reversed: false), "spike/p7-011/reorder-old.aibt.json");
            var newProgram = Compile(TwoLeafTreeJson(reversed: true), "spike/p7-011/reorder-new.aibt.json");

            var oldIdByIndex = InvertDebugMap(oldProgram);
            var newIndexById = new Dictionary<NodeId, uint>();
            foreach (var entry in newProgram.DebugMap) newIndexById[entry.AuthoringNodeId] = entry.RuntimeNodeIndex;

            var old = BuildInstance(oldProgram);
            try
            {
                Assert.IsTrue(old.Machine.TryBeginUpdate(1, out var beginFailure), beginFailure.Code.ToString());
                var oldActiveIndex = RunOneTickToWaiting(ref old.Machine);
                var oldActiveNodeId = oldIdByIndex[oldActiveIndex];
                var newActiveIndex = newIndexById[oldActiveNodeId];
                Assert.AreNotEqual(oldActiveIndex, newActiveIndex, "the reorder must actually shift the compiled index, or this proves nothing.");

                // Capture: read the old instance's live Frame/Generation for every node that classifies
                // Migrate (same NodeTypeId/NodeTypeVersion in both programs, by stable NodeId), using
                // only the already-public NativeInstanceArenaOwnerV1 execution-lease/View API -- no
                // internal engine method, mirroring this ADR's own central claim.
                Assert.IsTrue(old.ProgramOwner.TryAcquireReadLease(out var oldProgramLease, out var oldReadFailure), oldReadFailure.Code.ToString());
                Assert.IsTrue(old.ArenaOwner.TryAcquireExecutionLease(oldProgramLease, out var oldExecLease, out var oldExecFailure), oldExecFailure.Code.ToString());
                var oldFrames = oldExecLease.View.Frames;
                var oldGenerations = oldExecLease.View.Generations;
                var captured = new Dictionary<NodeId, (NativeFrameStateV1 Frame, uint Generation)>();
                foreach (var entry in oldProgram.DebugMap)
                {
                    captured[entry.AuthoringNodeId] = (oldFrames[(int)entry.RuntimeNodeIndex], oldGenerations[(int)entry.RuntimeNodeIndex]);
                }
                Assert.IsTrue(old.ArenaOwner.TryReleaseExecutionLease(oldExecLease, out var oldReleaseFailure), oldReleaseFailure.Code.ToString());
                Assert.IsTrue(old.ProgramOwner.TryReleaseReadLease(oldProgramLease, out var oldProgramReleaseFailure), oldProgramReleaseFailure.Code.ToString());

                // Seed: construct a fresh instance bound to the new (reordered) program, then write the
                // captured state at each node's NEW compiled index, resolved by the same stable NodeId --
                // never by assuming the old index still applies.
                var fresh = BuildInstance(newProgram);
                try
                {
                    Assert.IsTrue(fresh.ProgramOwner.TryAcquireReadLease(out var newProgramLease, out var newReadFailure), newReadFailure.Code.ToString());
                    Assert.IsTrue(fresh.ArenaOwner.TryAcquireExecutionLease(newProgramLease, out var newExecLease, out var newExecFailure), newExecFailure.Code.ToString());
                    var newFrames = newExecLease.View.Frames;
                    var newGenerations = newExecLease.View.Generations;
                    foreach (var entry in newProgram.DebugMap)
                    {
                        if (!captured.TryGetValue(entry.AuthoringNodeId, out var state)) continue;
                        var frame = state.Frame;
                        frame.NodeIndex = entry.RuntimeNodeIndex; // NodeIndex itself is position-dependent, remapped to the new slot.
                        newFrames[(int)entry.RuntimeNodeIndex] = frame;
                        newGenerations[(int)entry.RuntimeNodeIndex] = state.Generation;
                    }
                    Assert.IsTrue(fresh.ArenaOwner.TryReleaseExecutionLease(newExecLease, out var newReleaseFailure), newReleaseFailure.Code.ToString());
                    Assert.IsTrue(fresh.ProgramOwner.TryReleaseReadLease(newProgramLease, out var newProgramReleaseFailure), newProgramReleaseFailure.Code.ToString());

                    // Prove it landed on the NEW compiled index, not the old one -- pure copy, never
                    // in-place mutation of a shared structure.
                    Assert.IsTrue(fresh.ProgramOwner.TryAcquireReadLease(out var verifyProgramLease, out _));
                    Assert.IsTrue(fresh.ArenaOwner.TryAcquireExecutionLease(verifyProgramLease, out var verifyExecLease, out _));
                    Assert.AreEqual(NativeFrameLifecycleStateV1.Running, verifyExecLease.View.Frames[(int)newActiveIndex].LifecycleState,
                        "the migrated node's own live lifecycle state must have moved to its new compiled index.");
                    Assert.IsTrue(fresh.ArenaOwner.TryReleaseExecutionLease(verifyExecLease, out _));
                    Assert.IsTrue(fresh.ProgramOwner.TryReleaseReadLease(verifyProgramLease, out _));
                }
                finally
                {
                    fresh.Dispose();
                }

                // The old instance itself was never mutated by any of the above -- its own live state,
                // still at the OLD compiled index, is unchanged (pure copy, never in-place mutation).
                Assert.IsTrue(old.ProgramOwner.TryAcquireReadLease(out var oldVerifyProgramLease, out _));
                Assert.IsTrue(old.ArenaOwner.TryAcquireExecutionLease(oldVerifyProgramLease, out var oldVerifyExecLease, out _));
                Assert.AreEqual(NativeFrameLifecycleStateV1.Running, oldVerifyExecLease.View.Frames[(int)oldActiveIndex].LifecycleState);
                Assert.IsTrue(old.ArenaOwner.TryReleaseExecutionLease(oldVerifyExecLease, out _));
                Assert.IsTrue(old.ProgramOwner.TryReleaseReadLease(oldVerifyProgramLease, out _));
            }
            finally
            {
                old.Dispose();
            }
        }

        private struct SpikeInstance
        {
            internal NativeLifecycleMachineV1 Machine;
            internal NativeProgramImageOwnerV1 ProgramOwner;
            internal NativeInstanceArenaOwnerV1 ArenaOwner;
            internal NativeArray<NativeCompiledNodeRecordV1> Nodes;
            internal NativeArray<uint> Children;
            internal NativeArray<byte> Configuration;
            internal NativeArray<NativeLifecycleNodeBindingV1> Bindings;
            internal NativeArray<NativeLifecycleControlV1> Control;

            internal void Dispose()
            {
                ArenaOwner.TryDispose(out _);
                ProgramOwner.TryDispose(out _);
                Nodes.Dispose();
                Children.Dispose();
                Configuration.Dispose();
                Bindings.Dispose();
                Control.Dispose();
            }
        }

        private static SpikeInstance BuildInstance(CompiledProgram program)
        {
            var capacity = NativeProgramImageCapacityV1.Exact(program);
            Assert.IsTrue(NativeProgramImageOwnerV1.TryCreate(program, capacity, Allocator.Persistent, out var programOwner, out var createFailure), createFailure.Code.ToString());
            Assert.IsTrue(programOwner.TryAcquireReadLease(out var programLease, out var leaseFailure), leaseFailure.Code.ToString());
            Assert.IsTrue(NativeInstanceArenaCapacityV1.TryDerive(programLease.View, out var arenaCapacity, out var deriveFailure), deriveFailure.Code.ToString());
            Assert.IsTrue(NativeInstanceArenaOwnerV1.TryCreate(programLease, arenaCapacity, Allocator.Persistent, out var arenaOwner, out var arenaFailure), arenaFailure.Code.ToString());
            Assert.IsTrue(programOwner.TryReleaseReadLease(programLease, out var releaseFailure), releaseFailure.Code.ToString());

            // NativeLifecycleMachineV1.TryCreate needs its own writable arrays -- NativeProgramImageOwnerV1's
            // own View exposes program data as NativeArray<T>.ReadOnly (by design: shared, leased, never
            // mutated during execution), which cannot be handed to TryCreate's mutable-array-typed
            // parameters. A real, disclosed seam between the two owner-based capacity-planned
            // abstractions and NativeLifecycleMachineV1 itself: a caller still separately owns writable
            // copies of nodes/children/configuration, mirroring SchedulingPolicyDriver's own existing
            // pattern -- NativeProgramImageOwnerV1's role is the safety-checked, leased, generation-bound
            // READ access (proven above), not literally supplying the machine's constructor arrays.
            var nodes = new NativeArray<NativeCompiledNodeRecordV1>(program.Nodes.Count, Allocator.Persistent);
            for (var index = 0; index < nodes.Length; index++) nodes[index] = new NativeCompiledNodeRecordV1(program.Nodes[index]);
            var children = new NativeArray<uint>(program.ChildIndices.Count, Allocator.Persistent);
            for (var index = 0; index < children.Length; index++) children[index] = program.ChildIndices[index];
            var configuration = new NativeArray<byte>(program.ConfigBlob.Count, Allocator.Persistent);
            for (var index = 0; index < configuration.Length; index++) configuration[index] = program.ConfigBlob[index];

            var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(nodes.Length, Allocator.Persistent);
            for (var index = 0; index < nodes.Length; index++)
            {
                var kind = program.Nodes[index].NodeTypeId == MemorySequenceTypeId
                    ? NativeLifecycleNodeKindV1.MemorySequence
                    : NativeLifecycleNodeKindV1.GeneratedLeaf;
                bindings[index] = new NativeLifecycleNodeBindingV1((uint)index, kind);
            }
            var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.IsTrue(programOwner.TryAcquireReadLease(out var execProgramLease, out var execProgramFailure), execProgramFailure.Code.ToString());
            Assert.IsTrue(arenaOwner.TryAcquireExecutionLease(execProgramLease, out var execLease, out var execFailure), execFailure.Code.ToString());
            Assert.IsTrue(NativeLifecycleMachineV1.TryCreate(
                nodes,
                children,
                bindings,
                execLease.View.NodeMemory,
                execLease.View.Frames,
                execLease.View.Generations,
                control,
                configuration,
                out var machine,
                out var machineFailure), machineFailure.Code.ToString());
            Assert.IsTrue(arenaOwner.TryReleaseExecutionLease(execLease, out var execReleaseFailure), execReleaseFailure.Code.ToString());
            Assert.IsTrue(programOwner.TryReleaseReadLease(execProgramLease, out var execReleaseProgramFailure), execReleaseProgramFailure.Code.ToString());

            return new SpikeInstance
            {
                Machine = machine,
                ProgramOwner = programOwner,
                ArenaOwner = arenaOwner,
                Nodes = nodes,
                Children = children,
                Configuration = configuration,
                Bindings = bindings,
                Control = control,
            };
        }

        /// <summary>
        /// Advances past internal bookkeeping steps (e.g. the root's own CompositeEntered) to the
        /// first real DispatchRequired/Completed/Waiting boundary -- TryAdvance is a single atomic
        /// machine step, not "run to the next leaf," mirroring ReferencePreviewDriver.StepAtomic's
        /// own documented granularity on the reference-executor side.
        /// </summary>
        private static NativeLifecycleStepResultV1 AdvanceToDispatch(ref NativeLifecycleMachineV1 machine)
        {
            for (var guard = 0; guard < 64; guard++)
            {
                if (!machine.TryAdvance(out var step, out var failure)) Assert.Fail(failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired
                    || step.Kind == NativeLifecycleStepKindV1.Completed
                    || step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    return step;
                }
            }
            Assert.Fail("Did not reach a dispatch/boundary step within the guard step count.");
            return default;
        }

        /// <summary>
        /// Drives the current tick to its natural Waiting boundary, feeding every DispatchRequired
        /// leaf a Running status so the tick never completes -- leaving the instance genuinely
        /// active (live frames, no open dispatch) rather than idle, the same precondition
        /// TryRequestAbort itself enforces (ActiveDispatchId must be 0). Returns the compiled index
        /// of the first (and, for this spike's two-leaf trees, only) dispatched leaf.
        /// </summary>
        private static uint RunOneTickToWaiting(ref NativeLifecycleMachineV1 machine)
        {
            uint? firstDispatchedIndex = null;
            for (var guard = 0; guard < 64; guard++)
            {
                if (!machine.TryAdvance(out var step, out var failure)) Assert.Fail(failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    firstDispatchedIndex ??= step.NodeIndex;
                    Assert.IsTrue(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out var completeFailure), completeFailure.Code.ToString());
                    continue;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    Assert.IsTrue(firstDispatchedIndex.HasValue, "expected at least one leaf dispatch before Waiting.");
                    return firstDispatchedIndex.Value;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Completed)
                {
                    Assert.Fail("expected the tick to end Waiting (a live Running leaf), not Completed.");
                }
            }
            Assert.Fail("Did not reach Waiting within the guard step count.");
            return default;
        }

        private static void DrainToBoundary(ref NativeLifecycleMachineV1 machine)
        {
            for (var guard = 0; guard < 64; guard++)
            {
                if (!machine.TryAdvance(out var step, out var failure)) Assert.Fail(failure.Code.ToString());
                switch (step.Kind)
                {
                    case NativeLifecycleStepKindV1.DispatchRequired:
                        Assert.IsTrue(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out var completeFailure), completeFailure.Code.ToString());
                        continue;
                    case NativeLifecycleStepKindV1.Completed:
                    case NativeLifecycleStepKindV1.Waiting:
                        return;
                    default:
                        continue;
                }
            }
            Assert.Fail("Did not reach a boundary within the guard step count.");
        }

        private static Dictionary<uint, NodeId> InvertDebugMap(CompiledProgram program)
        {
            var result = new Dictionary<uint, NodeId>();
            foreach (var entry in program.DebugMap) result[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            return result;
        }

        /// <summary>
        /// Builds sequence("a", "b") or, reversed, sequence("b", "a") -- "a" is always the
        /// "running" leaf type, "b" is always the "success" leaf type, so a stable-NodeId-keyed
        /// classifier still sees both as Migrate (same NodeTypeId/NodeTypeVersion) even though
        /// reversing the children array genuinely shifts each node's own compiled index.
        /// </summary>
        private static string TwoLeafTreeJson(bool reversed)
        {
            var children = reversed ? "[\"b\",\"a\"]" : "[\"a\",\"b\"]";
            var json = new StringBuilder();
            json.Append("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"tree.spike.p7011\",\"name\":\"P7-011 Spike\",\"root\":\"n0\",\"nodes\":{");
            json.Append("\"n0\":{\"type\":\"aibt.core.memory-sequence\",\"typeVersion\":1,\"children\":").Append(children).Append("},");
            json.Append("\"a\":{\"type\":\"aibt.test.running\",\"typeVersion\":1},");
            json.Append("\"b\":{\"type\":\"aibt.test.success\",\"typeVersion\":1}");
            json.Append("}}");
            return json.ToString();
        }

        private static CompiledProgram Compile(string json, string sourceId)
        {
            var parseResult = CanonicalTreeJson.Parse(json, sourceId);
            Assert.IsNotNull(parseResult.Document, "tree JSON failed to parse: " + Describe(parseResult.Diagnostics));
            var registry = ReferencePreviewDriver.CreatePreviewNodeRegistry();
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, CompilerVersion);
            var compilation = ReferenceCompiler.Compile(parseResult.Document, registry, options);
            Assert.IsTrue(compilation.Success, "tree failed to compile: " + Describe(compilation.Diagnostics));
            return compilation.Program;
        }

        private static string Describe(DiagnosticCollection diagnostics)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (index > 0) builder.Append("; ");
                builder.Append(diagnostics[index].Code.Value).Append(": ").Append(diagnostics[index].Message);
            }
            return builder.ToString();
        }
    }
}
