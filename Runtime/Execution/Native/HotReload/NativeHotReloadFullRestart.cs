using System;
using AIBT.Burst;
using Unity.Collections;

namespace AIBT
{
    // Applies ADR-P7-011 (P7-012) to production. Mirrors HotReloadFullRestart's own shape for the
    // native backend -- the "whole tree" exclusion-set special case of the one shared reload
    // mechanism. Structurally different from the reference executor's own Restart in exactly the way
    // the ADR discloses: NativeLifecycleMachineV1.TryRequestAbort requires an OPEN update
    // (control.UpdateOpen != 0), the opposite precondition from ReferenceExecutionMachine.Abort
    // (which requires NO open update) -- so this reopens a fresh update (safe on an already-active
    // instance; TryBeginUpdate only (re)initializes frame 0 when Depth == 0) before requesting the
    // abort, then drains via TryAdvance to Completed rather than aborting synchronously in one call.
    internal static class NativeHotReloadFullRestart
    {
        /// <summary>
        /// On success, <paramref name="oldInstance"/> has already been aborted (if it needed
        /// aborting) and disposed -- the caller must not use or dispose it again.
        /// On failure, <paramref name="oldInstance"/> is left untouched (or, if the abort sequence
        /// itself is what failed, left mid-sequence) and still owned by the caller, who must dispose
        /// it themselves; <paramref name="freshInstance"/> is never partially constructed.
        /// </summary>
        internal static bool TryRestart(
            NativeHotReloadInstance oldInstance,
            CompiledProgram newProgram,
            ulong resumeUpdateId,
            Allocator allocator,
            out NativeHotReloadInstance freshInstance,
            out NativeHotReloadFullRestartReport report,
            out NativeRuntimeFailureV1 failure)
        {
            if (newProgram == null) throw new ArgumentNullException(nameof(newProgram));

            var wasAborted = false;
            if (oldInstance.Machine.TryBeginUpdate(resumeUpdateId, out _))
            {
                if (!oldInstance.Machine.TryRequestAbort(BurstNodeAbortReason.HotReload, out failure))
                {
                    freshInstance = default;
                    report = default;
                    return false;
                }

                wasAborted = true;
                if (!DrainToCompleted(ref oldInstance.Machine, out failure))
                {
                    freshInstance = default;
                    report = default;
                    return false;
                }
            }

            // Build the fresh instance before disposing the old one -- a fresh-build failure must
            // never leave the caller with neither a valid old nor a valid new instance.
            if (!NativeHotReloadInstance.TryBuild(newProgram, allocator, out freshInstance, out failure))
            {
                report = default;
                return false;
            }

            oldInstance.Dispose();
            report = new NativeHotReloadFullRestartReport(wasAborted);
            failure = default;
            return true;
        }

        // Unlike the spike's own simplified DrainToBoundary (which stopped at Completed OR Waiting),
        // production drains all the way to Completed -- an abort that leaves the instance at Waiting
        // has not actually finished unwinding, and restarting on top of that would discard a still-
        // live frame silently rather than tearing it down.
        private static bool DrainToCompleted(ref NativeLifecycleMachineV1 machine, out NativeRuntimeFailureV1 failure)
        {
            for (var guard = 0; guard < 4096; guard++)
            {
                if (!machine.TryAdvance(out var step, out failure))
                {
                    return false;
                }

                switch (step.Kind)
                {
                    case NativeLifecycleStepKindV1.DispatchRequired:
                        if (!machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure))
                        {
                            return false;
                        }

                        continue;
                    case NativeLifecycleStepKindV1.Completed:
                        failure = default;
                        return true;
                    default:
                        continue;
                }
            }

            failure = default;
            return false;
        }
    }
}
