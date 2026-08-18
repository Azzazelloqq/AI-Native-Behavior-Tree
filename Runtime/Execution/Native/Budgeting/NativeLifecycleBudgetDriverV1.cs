namespace AIBT
{
    internal enum NativeBudgetAdvanceKindV1 : byte
    {
        Step = 0,
        Suspended = 1,
        Waiting = 2,
        Completed = 3,
    }

    internal static class NativeLifecycleBudgetDriverV1
    {
        internal static bool TryBeginSegment(uint stepLimit, ref NativeBudgetStateV1 state)
        {
            if (state.Exhausted == 0 && state.StepLimit != 0 && state.StepsConsumed < state.StepLimit) return false;
            state.StepLimit = stepLimit;
            state.StepsConsumed = 0;
            state.Exhausted = 0;
            return true;
        }

        internal static bool TryAdvance(
            ref NativeLifecycleMachineV1 machine,
            ref NativeBudgetStateV1 state,
            out NativeBudgetAdvanceKindV1 kind,
            out NativeLifecycleStepResultV1 step,
            out NativeRuntimeFailureV1 failure)
        {
            step = default;
            if (state.Exhausted != 0 || state.StepsConsumed >= state.StepLimit)
            {
                state.Exhausted = 1;
                kind = NativeBudgetAdvanceKindV1.Suspended;
                failure = default;
                return true;
            }
            if (state.StepsConsumed == uint.MaxValue || state.ResumeCursor == uint.MaxValue)
            {
                kind = default;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }
            if (!machine.TryAdvance(out step, out failure))
            {
                kind = default;
                return false;
            }
            state.StepsConsumed++;
            state.ResumeCursor++;
            if (step.Kind == NativeLifecycleStepKindV1.Waiting) kind = NativeBudgetAdvanceKindV1.Waiting;
            else if (step.Kind == NativeLifecycleStepKindV1.Completed) kind = NativeBudgetAdvanceKindV1.Completed;
            else kind = NativeBudgetAdvanceKindV1.Step;
            return true;
        }
    }
}
