using System;

namespace AIBT
{
    /// <summary>
    /// Implements the reference-executor side of <c>ADR-P5-001</c>'s shared reload mechanism at
    /// its simplest, always-available exclusion set: the whole tree. Tears down a live
    /// <see cref="ReferenceExecutionMachine"/> (cancelling any active operations via
    /// <see cref="NodeAbortReason.HotReload"/>, the abort reason reserved for exactly this) and
    /// constructs a fresh instance bound to the new <see cref="CompiledProgram"/>. No state is
    /// copied -- <c>P5-005</c>/<c>P5-006</c> extend this same shape with a narrower exclusion set,
    /// they do not reimplement teardown or construction.
    /// <para>
    /// Scope: this card covers the managed reference-executor backend only. The native backend's
    /// own program-generation binding (<c>native-runtime-v1.md</c>'s <c>AIBT4311</c> invariant)
    /// already forces an unconditional dispose-and-recreate sequence by construction -- there is no
    /// restart "decision" to make there, only the existing <c>NativeInstanceArenaOwner.TryDispose</c>
    /// / <c>NativeProgramImageOwnerV1.TryCreate</c> lifecycle already proven in
    /// <c>Tests/Runtime/NativeExecution/ProgramAndState/NativeProgramAndStateTests.cs</c>. Wiring a
    /// hot-reload-specific wrapper around that lifecycle (including its capacity-plan/lease
    /// preflight machinery) is real, disclosed follow-up work this card does not build -- see
    /// <c>Planning~/Evidence/P5-004/README.md</c>.
    /// </para>
    /// </summary>
    internal static class HotReloadFullRestart
    {
        /// <param name="abortUpdateContext">
        /// The update context to abort the old instance with, if it needs aborting at all. Its
        /// <c>UpdateId</c> must strictly exceed every update ID the caller has already used on
        /// <paramref name="oldMachine"/> (<see cref="ReferenceExecutionMachine"/>'s own monotonic
        /// update-ID contract) -- this method has no way to know that sequence itself, so the
        /// caller, which already tracks it for ordinary <c>Update</c> calls, supplies it.
        /// </param>
        internal static ReferenceExecutionMachine Restart(
            ReferenceExecutionMachine oldMachine,
            CompiledProgram newProgram,
            ReferenceUpdateContext abortUpdateContext,
            TreeInstanceId treeInstanceId,
            ReferenceLeafRegistry leafRegistry,
            IReferenceTraceSink traceSink,
            ReferenceMemoryCompositeRegistry memoryCompositeRegistry,
            ReferenceReactiveCompositeRegistry reactiveCompositeRegistry,
            ReferenceDecoratorRegistry decoratorRegistry,
            ReferenceParallelRegistry parallelRegistry,
            RegisteredBlackboardRegistry registeredBlackboardRegistry,
            ReferenceObserverConditionRegistry observerRegistry,
            out HotReloadFullRestartReport report)
        {
            if (oldMachine == null) throw new ArgumentNullException(nameof(oldMachine));
            if (newProgram == null) throw new ArgumentNullException(nameof(newProgram));

            var activeNodeCount = 0u;
            var activeOperationCount = 0u;
            try
            {
                var inspection = oldMachine.CaptureInspection();
                activeNodeCount = inspection.ActiveNodeCount;
                activeOperationCount = inspection.ActiveOperationCount;
            }
            catch (InvalidOperationException)
            {
                // Mid-execution, or a machine that never finished constructing a blackboard.
                // Abort's own reject guard below still governs safety; a precise "before" count
                // simply is not available for this edge case, and is reported as zero rather than
                // guessed.
            }

            var abortResult = oldMachine.Abort(
                abortUpdateContext,
                NodeAbortReason.HotReload,
                new RuntimeNodeIndex(0));
            var wasAborted = abortResult.Progress != ReferenceExecutionProgress.Rejected;

            var freshMachine = new ReferenceExecutionMachine(
                newProgram,
                treeInstanceId,
                leafRegistry,
                traceSink,
                memoryCompositeRegistry,
                reactiveCompositeRegistry,
                decoratorRegistry,
                parallelRegistry,
                registeredBlackboardRegistry,
                observerRegistry);

            report = new HotReloadFullRestartReport(wasAborted, activeNodeCount, activeOperationCount);
            return freshMachine;
        }
    }
}
