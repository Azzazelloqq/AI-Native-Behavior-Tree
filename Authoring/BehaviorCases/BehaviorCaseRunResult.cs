using System;
using System.Collections.Generic;

namespace AIBT.Authoring.BehaviorCases
{
    internal enum BehaviorCaseFailureKind : byte
    {
        FactoryFault,
        ExecutorFault,
        ExecutorContract,
        ProgressMismatch,
        RootStatusMismatch,
        ExecutedStepsMismatch,
        MissingBlackboardKey,
        BlackboardValueMismatch,
        BlackboardVersionMismatch,
        CommandMismatch,
        TraceMismatch,
        DiagnosticMismatch,
        InvariantViolation,
    }

    internal readonly struct BehaviorCaseAssertionFailure
    {
        internal BehaviorCaseAssertionFailure(int stepIndex, BehaviorCaseFailureKind kind, string pointer, string message)
        {
            StepIndex = stepIndex;
            Kind = kind;
            Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        internal int StepIndex { get; }
        internal BehaviorCaseFailureKind Kind { get; }
        internal string Pointer { get; }
        internal string Message { get; }
    }

    internal sealed class BehaviorCaseRunResult
    {
        internal BehaviorCaseRunResult(
            BehaviorCaseDocument document,
            DiagnosticCollection inputDiagnostics,
            IEnumerable<BehaviorCaseAssertionFailure> failures,
            int executedStepCount)
        {
            Document = document;
            InputDiagnostics = inputDiagnostics ?? throw new ArgumentNullException(nameof(inputDiagnostics));
            Failures = Array.AsReadOnly(failures == null
                ? Array.Empty<BehaviorCaseAssertionFailure>()
                : new List<BehaviorCaseAssertionFailure>(failures).ToArray());
            ExecutedStepCount = executedStepCount;
        }

        internal BehaviorCaseDocument Document { get; }
        internal DiagnosticCollection InputDiagnostics { get; }
        internal IReadOnlyList<BehaviorCaseAssertionFailure> Failures { get; }
        internal int ExecutedStepCount { get; }
        internal bool Success => Document != null && InputDiagnostics.Count == 0 && Failures.Count == 0;
    }
}
