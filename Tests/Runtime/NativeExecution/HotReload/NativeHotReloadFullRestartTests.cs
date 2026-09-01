using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Runtime.NativeExecution.HotReload.NativeHotReloadTestDriver;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    public sealed class NativeHotReloadFullRestartTests
    {
        [Test]
        public void Restart_AbortsActiveOldInstance_ConstructsFreshOwnersFromNewProgram()
        {
            var oldProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);
            var newProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var buildFailure), Is.True, buildFailure.Code.ToString());
            Assert.That(old.Machine.TryBeginUpdate(1, out var beginFailure), Is.True, beginFailure.Code.ToString());
            RunOneTickToWaiting(ref old.Machine);

            Assert.That(
                NativeHotReloadFullRestart.TryRestart(old, newProgram, 2, Allocator.Persistent, out var fresh, out var report, out var restartFailure),
                Is.True, restartFailure.Code.ToString());
            // TryRestart disposes `old` on success -- do not dispose it again.
            try
            {
                Assert.That(report.OldInstanceWasAborted, Is.True);
                Assert.That(fresh.Machine.TryBeginUpdate(1, out var freshBeginFailure), Is.True, freshBeginFailure.Code.ToString());
                var freshStep = AdvanceToDispatch(ref fresh.Machine);
                Assert.That(freshStep.Kind, Is.EqualTo(NativeLifecycleStepKindV1.DispatchRequired),
                    "the fresh instance must start clean, re-entering the first node from scratch, unaffected by the old instance's abort.");
            }
            finally
            {
                fresh.Dispose();
            }
        }

        [Test]
        public void Restart_OldInstanceNeverBegunAnUpdate_StillRestartsCleanly()
        {
            // Investigated, not assumed: TryBeginUpdate succeeds unconditionally for a genuinely
            // fresh instance too (Depth==0 just means "initialize frame 0 on this call" -- it is not
            // a distinct failure mode), so OldInstanceWasAborted is true here as well; the report's
            // own meaning is "a fresh update could be opened and an abort requested," not "there was
            // live state" -- this test proves restart still completes cleanly on a never-touched
            // instance, not that the report can distinguish the two.
            var oldProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);
            var newProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var buildFailure), Is.True, buildFailure.Code.ToString());

            Assert.That(
                NativeHotReloadFullRestart.TryRestart(old, newProgram, 1, Allocator.Persistent, out var fresh, out var report, out var restartFailure),
                Is.True, restartFailure.Code.ToString());
            try
            {
                Assert.That(report.OldInstanceWasAborted, Is.True);
                Assert.That(fresh.Machine.TryBeginUpdate(1, out var freshBeginFailure), Is.True, freshBeginFailure.Code.ToString());
                var freshStep = AdvanceToDispatch(ref fresh.Machine);
                Assert.That(freshStep.Kind, Is.EqualTo(NativeLifecycleStepKindV1.DispatchRequired));
            }
            finally
            {
                fresh.Dispose();
            }
        }

        [Test]
        public void Restart_DrivenToCompletionAfterRestart_ProducesTheSameResultAsAFreshInstance()
        {
            var oldProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);
            var newProgram = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var buildFailure), Is.True, buildFailure.Code.ToString());
            Assert.That(old.Machine.TryBeginUpdate(1, out var beginFailure), Is.True, beginFailure.Code.ToString());
            RunOneTickToWaiting(ref old.Machine);

            Assert.That(
                NativeHotReloadFullRestart.TryRestart(old, newProgram, 2, Allocator.Persistent, out var fresh, out var report, out var restartFailure),
                Is.True, restartFailure.Code.ToString());
            try
            {
                Assert.That(fresh.Machine.TryBeginUpdate(1, out var freshBeginFailure), Is.True, freshBeginFailure.Code.ToString());
                var completed = DrainToCompleted(ref fresh.Machine);
                Assert.That(completed.HasRootStatus, Is.True);
                Assert.That(completed.RootStatus, Is.EqualTo(NodeStatus.Success),
                    "a reloaded instance, driven to completion, must behave exactly like a freshly constructed one.");
            }
            finally
            {
                fresh.Dispose();
            }
        }
    }
}
