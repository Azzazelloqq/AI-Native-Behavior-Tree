using AIBT.Burst;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    /// <summary>Shared step-driving helpers for P7-012's own hot-reload tests, mirroring
    /// Spikes~/NativeHotReloadModel's own AdvanceToDispatch/RunOneTickToWaiting exactly.</summary>
    internal static class NativeHotReloadTestDriver
    {
        internal static NativeLifecycleStepResultV1 AdvanceToDispatch(ref NativeLifecycleMachineV1 machine)
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

        /// <summary>Drives the current tick to its natural Waiting boundary, feeding every
        /// DispatchRequired leaf a Running status so the tick never completes. Returns the compiled
        /// index of the first dispatched leaf.</summary>
        internal static uint RunOneTickToWaiting(ref NativeLifecycleMachineV1 machine)
        {
            uint? firstDispatchedIndex = null;
            for (var guard = 0; guard < 64; guard++)
            {
                if (!machine.TryAdvance(out var step, out var failure)) Assert.Fail(failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    firstDispatchedIndex ??= step.NodeIndex;
                    Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out var completeFailure), Is.True, completeFailure.Code.ToString());
                    continue;
                }

                if (step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    Assert.That(firstDispatchedIndex.HasValue, Is.True, "expected at least one leaf dispatch before Waiting.");
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

        /// <summary>Drives every DispatchRequired step to completion with Success, until Completed.</summary>
        internal static NativeLifecycleStepResultV1 DrainToCompleted(ref NativeLifecycleMachineV1 machine)
        {
            for (var guard = 0; guard < 64; guard++)
            {
                if (!machine.TryAdvance(out var step, out var failure)) Assert.Fail(failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out var completeFailure), Is.True, completeFailure.Code.ToString());
                    continue;
                }

                if (step.Kind == NativeLifecycleStepKindV1.Completed)
                {
                    return step;
                }
            }

            Assert.Fail("Did not reach Completed within the guard step count.");
            return default;
        }
    }
}
