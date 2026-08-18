using Unity.Collections;

namespace AIBT
{
    internal enum NativeParallelPolicyV1 : byte { RequireAllSuccess, RequireAnySuccess, Threshold }
    internal enum NativeParallelTieBreakV1 : byte { FailureFirst, SuccessFirst }
    internal enum NativeParallelChildStateV1 : byte { NotStarted, Running, Success, Failure }

    internal readonly struct NativeParallelConfigurationV1
    {
        internal NativeParallelConfigurationV1(
            NativeParallelPolicyV1 policy,
            uint successThreshold,
            uint failureThreshold,
            NativeParallelTieBreakV1 tieBreak)
        {
            Policy = policy;
            SuccessThreshold = successThreshold;
            FailureThreshold = failureThreshold;
            TieBreak = tieBreak;
        }
        internal NativeParallelPolicyV1 Policy { get; }
        internal uint SuccessThreshold { get; }
        internal uint FailureThreshold { get; }
        internal NativeParallelTieBreakV1 TieBreak { get; }
    }

    internal readonly struct NativeParallelDecisionV1
    {
        internal NativeParallelDecisionV1(bool terminal, NodeStatus status)
        {
            IsTerminal = terminal;
            Status = status;
        }
        internal bool IsTerminal { get; }
        internal NodeStatus Status { get; }
    }

    internal static class NativeParallelPolicyEvaluatorV1
    {
        internal static bool TryDecode(
            NativeArray<byte>.ReadOnly bytes,
            uint offset,
            uint size,
            uint childCount,
            out NativeParallelConfigurationV1 configuration)
        {
            configuration = default;
            if (size != 16 || childCount == 0 || offset > bytes.Length || size > bytes.Length - offset) return false;
            var start = (int)offset;
            if (bytes[start] > 2 || bytes[start + 12] > 1
                || bytes[start + 1] != 0 || bytes[start + 2] != 0 || bytes[start + 3] != 0
                || bytes[start + 13] != 0 || bytes[start + 14] != 0 || bytes[start + 15] != 0) return false;
            var policy = (NativeParallelPolicyV1)bytes[start];
            var success = ReadU32(bytes, start + 4);
            var failure = ReadU32(bytes, start + 8);
            var tieBreak = (NativeParallelTieBreakV1)bytes[start + 12];
            if (policy == NativeParallelPolicyV1.Threshold)
            {
                if (success == 0 || failure == 0 || success > childCount || failure > childCount
                    || (ulong)success + failure > (ulong)childCount + 1) return false;
            }
            else if (success != 0 || failure != 0 || tieBreak != NativeParallelTieBreakV1.FailureFirst) return false;
            configuration = new NativeParallelConfigurationV1(policy, success, failure, tieBreak);
            return true;
        }

        internal static bool TryEvaluate(
            NativeParallelConfigurationV1 configuration,
            NativeArray<NativeParallelBranchStateV1>.ReadOnly branches,
            uint first,
            uint count,
            out NativeParallelDecisionV1 decision)
        {
            decision = default;
            if (!branches.IsCreated || count == 0 || first > branches.Length || count > branches.Length - first) return false;
            uint successes = 0, failures = 0;
            for (uint index = 0; index < count; index++)
            {
                var state = (NativeParallelChildStateV1)branches[(int)(first + index)].State;
                if (state > NativeParallelChildStateV1.Failure) return false;
                if (state == NativeParallelChildStateV1.Success) successes++;
                else if (state == NativeParallelChildStateV1.Failure) failures++;
            }
            switch (configuration.Policy)
            {
                case NativeParallelPolicyV1.RequireAllSuccess:
                    decision = failures != 0
                        ? new NativeParallelDecisionV1(true, NodeStatus.Failure)
                        : new NativeParallelDecisionV1(successes == count, successes == count ? NodeStatus.Success : NodeStatus.Running);
                    return true;
                case NativeParallelPolicyV1.RequireAnySuccess:
                    decision = successes != 0
                        ? new NativeParallelDecisionV1(true, NodeStatus.Success)
                        : new NativeParallelDecisionV1(failures == count, failures == count ? NodeStatus.Failure : NodeStatus.Running);
                    return true;
                case NativeParallelPolicyV1.Threshold:
                    var successReached = successes >= configuration.SuccessThreshold;
                    var failureReached = failures >= configuration.FailureThreshold;
                    if (successReached && failureReached)
                        decision = new NativeParallelDecisionV1(true,
                            configuration.TieBreak == NativeParallelTieBreakV1.SuccessFirst ? NodeStatus.Success : NodeStatus.Failure);
                    else if (successReached) decision = new NativeParallelDecisionV1(true, NodeStatus.Success);
                    else if (failureReached) decision = new NativeParallelDecisionV1(true, NodeStatus.Failure);
                    else decision = new NativeParallelDecisionV1(false, NodeStatus.Running);
                    return true;
                default:
                    return false;
            }
        }

        private static uint ReadU32(NativeArray<byte>.ReadOnly bytes, int offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
    }
}
