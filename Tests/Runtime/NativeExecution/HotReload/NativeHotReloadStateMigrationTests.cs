using System.Collections.Generic;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Runtime.NativeExecution.HotReload.NativeHotReloadTestDriver;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    public sealed class NativeHotReloadStateMigrationTests
    {
        [TestCase(20, false, false, NodeStatus.Failure)]
        [TestCase(110, false, false, NodeStatus.Success)]
        [TestCase(20, true, false, NodeStatus.Success)]
        [TestCase(20, false, true, NodeStatus.Success)]
        public void Cooldown_PreservesDeadlineOnlyForMigratingNodes(long now, bool excluded, bool incompatible, NodeStatus expected)
        {
            var program = NativeHotReloadTestProgram.Cooldown();
            var next = NativeHotReloadTestProgram.Cooldown(replaceWithTimeout: incompatible);
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                DriveCallbacks(ref old.Machine, program, _ => NodeStatus.Success);
                Assert.That(old.CooldownInitialized[0], Is.EqualTo(1));
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next,
                    HotReloadCompatibilityClassifier.Classify(program, next),
                    excluded ? new[] { NativeHotReloadTestProgram.RootNodeId } : NoExclusions, Allocator.Persistent,
                    out fresh, out _, out failure), Is.True, failure.Code.ToString());
                Assert.That(fresh.Machine.TryBeginUpdate(2, now, out _), Is.True);
                var calls = DriveCallbacks(ref fresh.Machine, next, _ => NodeStatus.Success);
                Assert.That(fresh.Control[0].RootStatus, Is.EqualTo(expected));
                Assert.That(calls.Count, Is.EqualTo(expected == NodeStatus.Failure ? 0 : 3));
                Assert.That(old.CooldownInitialized[0], Is.EqualTo(1));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public void Migration_ResumesTheAgreedCallbackOrder(bool secondChildActive, bool reordered, bool nested)
        {
            var oldProgram = nested ? NativeHotReloadTestProgram.NestedSequence(false) : NativeHotReloadTestProgram.TwoLeafSequence(false);
            var newProgram = nested ? NativeHotReloadTestProgram.NestedSequence(reordered) : NativeHotReloadTestProgram.TwoLeafSequence(reordered);
            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out _), Is.True);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                DriveCallbacks(ref old.Machine, oldProgram, i => i == (secondChildActive ? 2u : 1u) + (nested ? 1u : 0u) ? NodeStatus.Running : NodeStatus.Success);
                var oldDepth = old.Control[0].Depth;
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, oldProgram, newProgram,
                    HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram), NoExclusions, Allocator.Persistent,
                    out fresh, out _, out var failure), Is.True, failure.Code.ToString());
                Assert.That(old.Control[0].Depth, Is.EqualTo(oldDepth));
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                var calls = DriveCallbacks(ref fresh.Machine, newProgram, _ => NodeStatus.Success);
                var active = secondChildActive ? "b" : "a";
                var expected = new System.Collections.Generic.List<string>();
                if (reordered)
                {
                    expected.Add(active + ":Abort:HotReload"); expected.Add(active + ":Exit:Aborted");
                    expected.AddRange(new[] { "b:Enter", "b:Tick", "b:Exit:Success", "a:Enter", "a:Tick", "a:Exit:Success" });
                }
                else
                {
                    expected.Add(active + ":Tick"); expected.Add(active + ":Exit:Success");
                    if (!secondChildActive) expected.AddRange(new[] { "b:Enter", "b:Tick", "b:Exit:Success" });
                }
                if (nested) expected.AddRange(new[] { "c:Enter", "c:Tick", "c:Exit:Success" });
                Assert.That(calls, Is.EqualTo(expected));
                Assert.That(fresh.Control[0].RootStatus, Is.EqualTo(NodeStatus.Success));
                Assert.That(fresh.Control[0].HasRootStatus, Is.EqualTo(1));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void NestedStructuralChanges_CancelTheActivePathOnceAtTheOutermostOwner()
        {
            var program = NativeHotReloadTestProgram.NestedSequence(false);
            var next = NativeHotReloadTestProgram.NestedSequence(true, reverseOuter: true);
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out _), Is.True);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                DriveCallbacks(ref old.Machine, program, _ => NodeStatus.Running);
                var controlBefore = old.Control[0];
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next,
                    HotReloadCompatibilityClassifier.Classify(program, next), NoExclusions, Allocator.Persistent,
                    out fresh, out var report, out var failure), Is.True, failure.Code.ToString());
                Assert.That(report.CursorResetNodeCount, Is.EqualTo(2));
                Assert.That(old.Control[0], Is.EqualTo(controlBefore));
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                Assert.That(DriveCallbacks(ref fresh.Machine, next, _ => NodeStatus.Success), Is.EqualTo(new[] {
                    "a:Abort:HotReload", "a:Exit:Aborted", "c:Enter", "c:Tick", "c:Exit:Success",
                    "b:Enter", "b:Tick", "b:Exit:Success", "a:Enter", "a:Tick", "a:Exit:Success" }));
                Assert.That(fresh.Control[0].HasRootStatus, Is.EqualTo(1));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [TestCase(NodeStatus.Success)]
        [TestCase(NodeStatus.Failure)]
        public void Reorder_AfterTerminalTick_PreservesPendingExitReason(NodeStatus terminal)
        {
            var program = NativeHotReloadTestProgram.TwoLeafSequence(false);
            var next = NativeHotReloadTestProgram.TwoLeafSequence(true);
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out _), Is.True);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                var tickReached = false;
                for (var guard = 0; guard < 32 && !tickReached; guard++)
                {
                    Assert.That(old.Machine.TryAdvance(out var step, out _), Is.True);
                    if (step.Kind != NativeLifecycleStepKindV1.DispatchRequired) continue;
                    Assert.That(old.Machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, terminal, out _), Is.True);
                    tickReached = step.Phase == BurstCallbackPhase.Tick;
                }
                Assert.That(tickReached, Is.True);
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next,
                    HotReloadCompatibilityClassifier.Classify(program, next), NoExclusions, Allocator.Persistent,
                    out fresh, out _, out var failure), Is.True, failure.Code.ToString());
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                Assert.That(DriveCallbacks(ref fresh.Machine, next, _ => NodeStatus.Success), Is.EqualTo(new[] {
                    "a:Exit:" + terminal, "b:Enter", "b:Tick", "b:Exit:Success", "a:Enter", "a:Tick", "a:Exit:Success" }));
                Assert.That(fresh.Control[0].HasRootStatus, Is.EqualTo(1));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        private static System.Collections.Generic.List<string> DriveCallbacks(ref NativeLifecycleMachineV1 machine,
            CompiledProgram program, System.Func<uint, NodeStatus> tick)
        {
            var calls = new System.Collections.Generic.List<string>();
            for (var guard = 0; guard < 128; guard++)
            {
                Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    var id = program.DebugMap[(int)step.NodeIndex].AuthoringNodeId.ToString();
                    var suffix = step.Phase == BurstCallbackPhase.Abort ? ":" + step.AbortReason
                        : step.Phase == BurstCallbackPhase.Exit ? ":" + step.ExitReason : "";
                    calls.Add(id + ":" + step.Phase + suffix);
                    Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success,
                        step.Phase == BurstCallbackPhase.Tick ? tick(step.NodeIndex) : NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
                }
                else if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting) return calls;
            }
            Assert.Fail("No execution boundary reached."); return calls;
        }
        private static readonly IReadOnlyCollection<NodeId> NoExclusions = new List<NodeId>();

        [Test]
        public void Migrate_NoStructuralChange_CopiesFrameGenerationAndCompositeCursorVerbatim()
        {
            var oldProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);
            var newProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.StructuralChildChangeNodeIds, Is.Empty,
                "identical child order in both programs must never trigger the cursor-reset path.");

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var buildFailure), Is.True, buildFailure.Code.ToString());
            Assert.That(old.Machine.TryBeginUpdate(1, out var beginFailure), Is.True, beginFailure.Code.ToString());
            var activeIndex = RunOneTickToWaiting(ref old.Machine); // leaf "a", compiled index 1, Running.
            Assert.That(activeIndex, Is.EqualTo(1u));

            Assert.That(
                NativeHotReloadStateMigration.TryMigrate(
                    old, oldProgram, newProgram, classification, NoExclusions, Allocator.Persistent,
                    out var fresh, out var report, out var migrateFailure),
                Is.True, migrateFailure.Code.ToString());
            try
            {
                Assert.That(report.MigratedNodeCount, Is.EqualTo(3u), "root + both leaves must all migrate -- nothing changed shape.");
                Assert.That(report.CursorResetNodeCount, Is.Zero);

                Assert.That(fresh.ProgramOwner.TryAcquireReadLease(out var lease, out _), Is.True);
                Assert.That(fresh.ArenaOwner.TryAcquireExecutionLease(lease, out var exec, out _), Is.True);
                Assert.That(exec.View.Frames[1].LifecycleState, Is.EqualTo(NativeFrameLifecycleStateV1.Running),
                    "leaf \"a\"'s own live state must have migrated to its own (unchanged) compiled index.");
                var rootMemory = exec.View.NodeMemory;
                Assert.That(rootMemory[0], Is.Zero.And.EqualTo(rootMemory[0]),
                    "root's own cursor bytes, unchanged from the old instance's own 0, prove a real byte-for-byte NodeMemory copy happened (not just Frame/Generation).");
                Assert.That(fresh.ArenaOwner.TryReleaseExecutionLease(exec, out _), Is.True);
                Assert.That(fresh.ProgramOwner.TryReleaseReadLease(lease, out _), Is.True);
            }
            finally
            {
                fresh.Dispose();
                old.Dispose();
            }
        }

        [Test]
        public void Migrate_ReorderedChildrenMidFlight_ResetsCompositeCursorInsteadOfCopyingAStaleValue()
        {
            var oldProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false); // sequence(a, b)
            var newProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: true); // sequence(b, a)
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.StructuralChildChangeNodeIds, Does.Contain(NativeHotReloadTestProgram.RootNodeId),
                "the reorder must actually register as a structural child change, or this test proves nothing.");

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var buildFailure), Is.True, buildFailure.Code.ToString());
            Assert.That(old.Machine.TryBeginUpdate(1, out var beginFailure), Is.True, beginFailure.Code.ToString());

            // Complete leaf "a" (old compiled index 1) with Success first, advancing the sequence's
            // own cursor to 1 (child "b"), THEN drive "b" (old compiled index 2) to Waiting -- a
            // non-zero old cursor value, so a verbatim copy into the reordered tree would be
            // observably wrong (cursor=1 in the new, reversed tree points at "a", not the actually
            // -live "b") rather than coincidentally matching what a reset would produce anyway.
            var activeIndex = AdvanceSequencePastFirstLeafToSecondWaiting(ref old.Machine);
            Assert.That(activeIndex, Is.EqualTo(2u), "leaf \"b\" (old compiled index 2) must be the live one.");

            Assert.That(old.ProgramOwner.TryAcquireReadLease(out var oldLease, out _), Is.True);
            Assert.That(old.ArenaOwner.TryAcquireExecutionLease(oldLease, out var oldExec, out _), Is.True);
            var oldCursor = oldExec.View.NodeMemory[0];
            Assert.That(old.ArenaOwner.TryReleaseExecutionLease(oldExec, out _), Is.True);
            Assert.That(old.ProgramOwner.TryReleaseReadLease(oldLease, out _), Is.True);
            Assert.That(oldCursor, Is.EqualTo(1), "sequencing past leaf \"a\" must have advanced the composite's own cursor to 1.");

            Assert.That(
                NativeHotReloadStateMigration.TryMigrate(
                    old, oldProgram, newProgram, classification, NoExclusions, Allocator.Persistent,
                    out var fresh, out var report, out var migrateFailure),
                Is.True, migrateFailure.Code.ToString());
            try
            {
                Assert.That(report.CursorResetNodeCount, Is.EqualTo(1u), "exactly the root composite must have had its cursor reset.");

                Assert.That(fresh.ProgramOwner.TryAcquireReadLease(out var newLease, out _), Is.True);
                Assert.That(fresh.ArenaOwner.TryAcquireExecutionLease(newLease, out var newExec, out _), Is.True);
                Assert.That(newExec.View.NodeMemory[0], Is.Zero,
                    "the reset rule must have zeroed the cursor -- a verbatim copy would have left it at 1, silently pointing at the wrong (reordered) child.");
                // Leaf "b" is now at new compiled index 1 and depth 1 (root's only active child);
                // its own live Frame must still have migrated correctly by stable NodeId.
                Assert.That(newExec.View.Frames[1].LifecycleState, Is.EqualTo(NativeFrameLifecycleStateV1.Running),
                    "leaf \"b\"'s own live state must have migrated to its new compiled index, at the same active-stack depth.");
                Assert.That(newExec.View.Frames[1].NodeIndex, Is.EqualTo(1u), "the migrated frame's own NodeIndex must be remapped to leaf \"b\"'s new compiled index.");
                Assert.That(fresh.ArenaOwner.TryReleaseExecutionLease(newExec, out _), Is.True);
                Assert.That(fresh.ProgramOwner.TryReleaseReadLease(newLease, out _), Is.True);

                // Driving the migrated instance forward from its reset cursor must not crash or
                // produce an out-of-range access -- the ADR's own explicitly-unverified callout this
                // test closes.
                Assert.That(fresh.Machine.TryBeginUpdate(2, out var resumeFailure), Is.True, resumeFailure.Code.ToString());
                var step = AdvanceToDispatch(ref fresh.Machine);
                Assert.That(step.Kind, Is.EqualTo(NativeLifecycleStepKindV1.DispatchRequired),
                    "resuming from a reset cursor must produce a well-defined next dispatch, not a stale/wrong one.");
            }
            finally
            {
                fresh.Dispose();
                old.Dispose();
            }
        }

        // Every dispatch belonging to the FIRST leaf encountered (Enter/Tick/Exit alike -- Enter and
        // Exit are void and ignore the NodeStatus argument entirely, only Tick's own returned status
        // matters) completes with Success, walking it fully through its own lifecycle and advancing
        // the sequence's own cursor; every dispatch for whichever DIFFERENT node index appears next
        // completes with Running, leaving it live at the Waiting boundary.
        private static uint AdvanceSequencePastFirstLeafToSecondWaiting(ref NativeLifecycleMachineV1 machine)
        {
            uint? firstIndex = null;
            for (var guard = 0; guard < 64; guard++)
            {
                Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    firstIndex ??= step.NodeIndex;
                    var status = step.NodeIndex == firstIndex.Value ? NodeStatus.Success : NodeStatus.Running;
                    Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out var completeFailure), Is.True, completeFailure.Code.ToString());
                    continue;
                }

                if (step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    Assert.That(step.NodeIndex, Is.Not.EqualTo(firstIndex), "expected the SECOND leaf to be the one left Waiting.");
                    return step.NodeIndex;
                }
            }

            Assert.Fail("Did not reach Waiting within the guard step count.");
            return default;
        }
    }
}
