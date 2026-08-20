using System;

namespace AIBT
{
    /// <summary>
    /// Implements <c>Documentation~/execution-and-scheduling.md</c>'s batching formula
    /// (<c>batch size = target batch work / estimated work per agent</c>), clamped by policy and
    /// memory limits, plus the spec's "enough batches for worker load balancing without flooding
    /// the job queue with tiny tasks" rule. Pure math over caller-supplied inputs -- no runtime
    /// adaptation, no selection logic (that is <c>P4-005</c>'s `Auto` policy, not this card's job).
    /// </summary>
    internal static class NativeBatchSizeCalibrationV1
    {
        /// <summary>
        /// Computes a batch size from the ratio of target batch work to estimated per-agent work,
        /// clamped to <c>[policyMinBatchSize, policyMaxBatchSize]</c> and further to
        /// <paramref name="memoryLimitBatchSize"/> (whichever bound is stricter wins -- memory
        /// always wins over policy if the two conflict, since a batch the memory budget cannot
        /// hold can never be scheduled regardless of policy preference), then adjusted so the
        /// resulting batch count uses at least <paramref name="workerCount"/> workers when the
        /// population supports it, without shrinking batch size below any of the above floors.
        /// </summary>
        internal static bool TryComputeBatchSize(
            double targetBatchWorkNanoseconds,
            double estimatedWorkPerAgentNanoseconds,
            uint policyMinBatchSize,
            uint policyMaxBatchSize,
            uint memoryLimitBatchSize,
            uint runnableAgents,
            uint workerCount,
            out uint batchSize,
            out NativeRuntimeFailureV1 failure)
        {
            batchSize = 0;
            if (targetBatchWorkNanoseconds <= 0 || estimatedWorkPerAgentNanoseconds <= 0
                || policyMinBatchSize == 0 || policyMaxBatchSize < policyMinBatchSize
                || memoryLimitBatchSize == 0 || runnableAgents == 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }

            var effectiveMax = Math.Min(policyMaxBatchSize, memoryLimitBatchSize);
            var effectiveMin = Math.Min(policyMinBatchSize, effectiveMax);

            var raw = targetBatchWorkNanoseconds / estimatedWorkPerAgentNanoseconds;
            var clamped = raw < effectiveMin ? effectiveMin : raw > effectiveMax ? effectiveMax : raw;
            var result = (uint)Math.Round(clamped, MidpointRounding.AwayFromZero);
            if (result < effectiveMin) result = effectiveMin;
            if (result > effectiveMax) result = effectiveMax;

            if (workerCount > 1 && runnableAgents >= workerCount)
            {
                var batchesAtResult = (runnableAgents + result - 1) / result;
                if (batchesAtResult < workerCount)
                {
                    var target = (runnableAgents + workerCount - 1) / workerCount;
                    if (target < effectiveMin) target = effectiveMin;
                    if (target < result) result = target;
                }
            }

            batchSize = result;
            failure = default;
            return true;
        }
    }
}
