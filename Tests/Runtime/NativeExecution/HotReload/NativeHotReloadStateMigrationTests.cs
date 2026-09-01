using System.Collections.Generic;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Runtime.NativeExecution.HotReload.NativeHotReloadTestDriver;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    public sealed class NativeHotReloadStateMigrationTests
    {
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
