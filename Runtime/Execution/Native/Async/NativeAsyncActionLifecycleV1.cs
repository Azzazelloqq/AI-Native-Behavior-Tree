using AIBT.Burst;

namespace AIBT
{
    internal struct NativeAsyncActionStateV1
    {
        internal OperationId OperationId;
        internal uint ActivationGeneration;
        internal byte Started;
        internal byte Terminal;
        internal byte Cancelled;
    }

    internal static class NativeAsyncActionLifecycleV1
    {
        internal static BurstContextResult TryBeginActivation(
            ref NativeAsyncActionStateV1 state,
            uint activationGeneration)
        {
            if (activationGeneration == 0 || state.Started != 0 && state.Terminal == 0)
                return BurstContextResult.PhaseViolation;
            state = new NativeAsyncActionStateV1 { ActivationGeneration = activationGeneration };
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryStartOnce(
            ref NativeAsyncActionStateV1 state,
            ref NativeCommandAsyncViewV1 view,
            RuntimeNodeIndex nodeIndex,
            CommandType startCommand,
            CommandType cancelCommand,
            NativePayloadSliceV1 startPayload,
            NativePayloadSliceV1 faultCancelPayload,
            out OperationId operationId)
        {
            operationId = default;
            if (state.ActivationGeneration == 0 || state.Terminal != 0) return BurstContextResult.PhaseViolation;
            if (state.Started != 0)
            {
                operationId = state.OperationId;
                return BurstContextResult.Success;
            }
            var result = view.TryStart(
                nodeIndex, state.ActivationGeneration, startCommand, cancelCommand,
                startPayload, faultCancelPayload, out operationId);
            if (result != BurstContextResult.Success) return result;
            state.OperationId = operationId;
            state.Started = 1;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryPoll(
            ref NativeAsyncActionStateV1 state,
            ref NativeCommandAsyncViewV1 view,
            RuntimeNodeIndex nodeIndex,
            NativeCompletionExpectationV1 expectation,
            out bool terminal,
            out NodeStatus status,
            out NativeConsumedCompletionV1 completion)
        {
            terminal = false;
            status = NodeStatus.Running;
            completion = default;
            if (state.Started == 0 || state.Terminal != 0 || state.Cancelled != 0)
                return BurstContextResult.PhaseViolation;
            var result = view.TryConsume(
                nodeIndex, state.ActivationGeneration, state.OperationId, expectation, out completion);
            if (result == BurstContextResult.IncompleteValue) return BurstContextResult.Success;
            if (result != BurstContextResult.Success) return result;
            terminal = true;
            status = completion.Outcome == CompletionOutcome.Succeeded ? NodeStatus.Success : NodeStatus.Failure;
            state.Terminal = 1;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryAbortOnce(
            ref NativeAsyncActionStateV1 state,
            ref NativeCommandAsyncViewV1 view,
            CommandType cancelCommand,
            NativePayloadSliceV1 payload,
            out bool emitted)
        {
            emitted = false;
            if (state.Started == 0 || state.Terminal != 0) return BurstContextResult.Success;
            if (state.Cancelled != 0) return BurstContextResult.Success;
            var result = view.TryCancel(state.OperationId, cancelCommand, payload, out emitted);
            if (result == BurstContextResult.Success || result == BurstContextResult.CapacityExceeded
                || result == BurstContextResult.Overflow)
                state.Cancelled = 1;
            return result;
        }
    }
}
