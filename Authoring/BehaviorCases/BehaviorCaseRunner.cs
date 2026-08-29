using System;
using System.Collections.Generic;

namespace AIBT.Authoring.BehaviorCases
{
    internal static class BehaviorCaseRunner
    {
        internal static BehaviorCaseRunResult Run(
            byte[] utf8,
            string documentId,
            IBehaviorCaseExecutorFactory factory,
            BehaviorCaseRegisteredValueRegistry registeredValues)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            return RunParsed(BehaviorCaseJson.Parse(utf8, documentId), documentId, factory, registeredValues);
        }

        internal static BehaviorCaseRunResult Run(
            string json,
            string documentId,
            IBehaviorCaseExecutorFactory factory,
            BehaviorCaseRegisteredValueRegistry registeredValues)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return RunParsed(BehaviorCaseJson.Parse(json, documentId), documentId, factory, registeredValues);
        }

        private static BehaviorCaseRunResult RunParsed(
            BehaviorCaseJsonReadResult read,
            string documentId,
            IBehaviorCaseExecutorFactory factory,
            BehaviorCaseRegisteredValueRegistry registeredValues)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (registeredValues == null) throw new ArgumentNullException(nameof(registeredValues));
            if (!read.Success)
                return new BehaviorCaseRunResult(null, read.Diagnostics, null, 0);

            var semanticDiagnostics = BehaviorCaseSemanticValidator.Validate(
                read.Document,
                documentId,
                registeredValues: registeredValues);
            if (semanticDiagnostics.Count != 0)
                return new BehaviorCaseRunResult(null, semanticDiagnostics, null, 0);

            var failures = new List<BehaviorCaseAssertionFailure>();
            IBehaviorCaseExecutor executor;
            try
            {
                executor = factory.Create(new BehaviorCaseExecutorConfiguration(read.Document));
                if (executor == null) throw new InvalidOperationException("The behavior-case executor factory returned null.");
            }
            catch (Exception exception)
            {
                failures.Add(Failure(-1, BehaviorCaseFailureKind.FactoryFault, string.Empty,
                    "Executor factory failed: " + exception.GetType().Name + "."));
                return new BehaviorCaseRunResult(read.Document, DiagnosticCollection.Empty, failures, 0);
            }

            var executed = 0;
            CompiledHash observedTreeHash = default;
            ulong observedTraceSequence = 0;
            ulong activeUpdateId = 0;
            Revision activeRevision = default;
            using (executor)
            {
                for (var index = 0; index < read.Document.Steps.Count; index++)
                {
                    BehaviorCaseExecutorStepResult actual;
                    try
                    {
                        actual = executor.Execute(read.Document.Steps[index]);
                        if (actual == null) throw new InvalidOperationException("The behavior-case executor returned null.");
                        executed++;
                    }
                    catch (Exception exception)
                    {
                        failures.Add(Failure(index, BehaviorCaseFailureKind.ExecutorFault, "/steps/" + index,
                            "Executor failed: " + exception.GetType().Name + "."));
                        break;
                    }

                    if (!ValidateObservedTrace(
                        index,
                        read.Document.Steps[index],
                        read.Document.TreeInstanceId,
                        actual.Trace,
                        ref observedTreeHash,
                        ref observedTraceSequence,
                        ref activeUpdateId,
                        ref activeRevision,
                        failures))
                        break;

                    AssertStep(index, read.Document.Steps[index].Expectation, actual, failures);
                }
            }

            return new BehaviorCaseRunResult(read.Document, DiagnosticCollection.Empty, failures, executed);
        }

        private static bool ValidateObservedTrace(
            int stepIndex,
            BehaviorCaseStep step,
            TreeInstanceId caseTreeInstanceId,
            IReadOnlyList<BehaviorCaseObservedTrace> trace,
            ref CompiledHash observedTreeHash,
            ref ulong observedTraceSequence,
            ref ulong activeUpdateId,
            ref Revision activeRevision,
            List<BehaviorCaseAssertionFailure> failures)
        {
            if (step is BehaviorCaseUpdateStep update)
            {
                activeUpdateId = update.UpdateId;
                activeRevision = update.SnapshotRevision;
            }
            else if (step is BehaviorCaseAbortStep abort)
            {
                activeUpdateId = abort.UpdateId;
                activeRevision = abort.SnapshotRevision;
            }
            else if (step.Operation == BehaviorCaseOperation.Restart)
            {
                activeUpdateId = 0;
                activeRevision = default;
                if (trace.Count != 0)
                    return ContractFailure(stepIndex, failures, "Restart cannot emit update-scoped trace records.");
                return true;
            }

            if (activeUpdateId == 0 || !activeRevision.IsValid)
                return trace.Count == 0 || ContractFailure(stepIndex, failures, "Trace records have no active update context.");

            for (var index = 0; index < trace.Count; index++)
            {
                var record = trace[index];
                if (record.TraceFormatVersion != BehaviorCaseObservedTrace.SupportedFormatVersion)
                    return ContractFailure(stepIndex, failures, "Observed trace format version is unsupported.");
                if (record.TreeInstanceId != caseTreeInstanceId)
                    return ContractFailure(stepIndex, failures, "Observed trace tree instance differs from the case.");
                if (!observedTreeHash.IsValid) observedTreeHash = record.TreeSemanticHash;
                else if (record.TreeSemanticHash != observedTreeHash)
                    return ContractFailure(stepIndex, failures, "Observed trace tree semantic hash changed.");
                if (record.Sequence <= observedTraceSequence)
                    return ContractFailure(stepIndex, failures, "Observed trace sequence is not strictly increasing.");
                if (record.UpdateId != activeUpdateId || record.SnapshotRevision != activeRevision)
                    return ContractFailure(stepIndex, failures, "Observed trace update context differs from the executed step.");
                observedTraceSequence = record.Sequence;
            }
            return true;
        }

        private static bool ContractFailure(
            int stepIndex,
            List<BehaviorCaseAssertionFailure> failures,
            string message)
        {
            failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.ExecutorContract,
                "/steps/" + stepIndex, message));
            return false;
        }

        private static void AssertStep(
            int stepIndex,
            BehaviorCaseExpectation expected,
            BehaviorCaseExecutorStepResult actual,
            List<BehaviorCaseAssertionFailure> failures)
        {
            var pointer = "/steps/" + stepIndex + "/expect";
            if (expected.Progress.HasValue && expected.Progress.Value != actual.Progress)
                failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.ProgressMismatch, pointer + "/progress", "Executor progress differs."));
            if (expected.RootStatus.HasValue && expected.RootStatus != actual.RootStatus)
                failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.RootStatusMismatch, pointer + "/rootStatus", "Root status differs."));
            if (expected.ExecutedSteps.HasValue && expected.ExecutedSteps.Value != actual.ExecutedSteps)
                failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.ExecutedStepsMismatch, pointer + "/executedSteps", "Executed step count differs."));

            AssertBlackboard(stepIndex, pointer + "/blackboard", expected.Blackboard, actual.Blackboard, failures);
            AssertCommands(stepIndex, pointer + "/commands", expected.Commands, actual.Commands, failures);
            AssertTrace(stepIndex, pointer + "/trace", expected.Trace, actual.Trace, expected.HasTraceExpectation, failures);
            AssertDiagnostics(stepIndex, pointer + "/diagnostics", expected.Diagnostics, actual.Diagnostics, expected.HasDiagnosticExpectation, failures);
            AssertInvariants(stepIndex, pointer + "/invariants", expected.Invariants, actual, failures);
        }

        private static void AssertBlackboard(
            int stepIndex,
            string pointer,
            IReadOnlyList<BehaviorCaseBlackboardExpectation> expected,
            IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> actual,
            List<BehaviorCaseAssertionFailure> failures)
        {
            for (var index = 0; index < expected.Count; index++)
            {
                var item = expected[index];
                var itemPointer = pointer + "/" + index;
                if (!actual.TryGetValue(item.Key, out var observed))
                {
                    failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.MissingBlackboardKey, itemPointer + "/key", "Expected blackboard key is absent."));
                    continue;
                }

                if (!ValuesEqual(item.Value, observed.Value, item.AbsoluteTolerance, item.RelativeTolerance))
                    failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.BlackboardValueMismatch, itemPointer + "/value", "Blackboard value differs."));
                if (item.Version.HasValue && item.Version.Value != observed.Version)
                    failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.BlackboardVersionMismatch, itemPointer + "/version", "Blackboard version differs."));
            }
        }

        private static void AssertCommands(
            int stepIndex,
            string pointer,
            BehaviorCaseCommandExpectationSet expected,
            IReadOnlyList<BehaviorCaseCommandExpectation> actual,
            List<BehaviorCaseAssertionFailure> failures)
        {
            if (expected == null) return;
            var matches = expected.Match == BehaviorCaseCommandMatch.Exact
                ? ExactSequence(expected.Records, actual, CommandEqual)
                : OrderedSubset(expected.Records, actual, CommandEqual);
            if (!matches)
                failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.CommandMismatch, pointer, "Command sequence differs."));
        }

        private static void AssertTrace(
            int stepIndex,
            string pointer,
            IReadOnlyList<BehaviorCaseTraceExpectation> expected,
            IReadOnlyList<BehaviorCaseObservedTrace> actual,
            bool asserted,
            List<BehaviorCaseAssertionFailure> failures)
        {
            if (asserted && !ExactSequence(expected, actual, TraceMatches))
                failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.TraceMismatch, pointer, "Trace sequence differs."));
        }

        private static void AssertDiagnostics(
            int stepIndex,
            string pointer,
            IReadOnlyList<BehaviorCaseDiagnosticExpectation> expected,
            DiagnosticCollection actual,
            bool asserted,
            List<BehaviorCaseAssertionFailure> failures)
        {
            if (!asserted) return;
            if (expected.Count != actual.Count)
            {
                failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.DiagnosticMismatch, pointer, "Diagnostic count differs."));
                return;
            }

            for (var index = 0; index < expected.Count; index++)
            {
                if (!DiagnosticMatches(expected[index], actual[index]))
                {
                    failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.DiagnosticMismatch, pointer + "/" + index, "Diagnostic differs."));
                    return;
                }
            }
        }

        private static void AssertInvariants(
            int stepIndex,
            string pointer,
            IReadOnlyList<BehaviorCaseInvariant> expected,
            BehaviorCaseExecutorStepResult actual,
            List<BehaviorCaseAssertionFailure> failures)
        {
            for (var index = 0; index < expected.Count; index++)
            {
                var valid = true;
                switch (expected[index])
                {
                    case BehaviorCaseInvariant.NoErrorDiagnostics:
                        for (var diagnosticIndex = 0; diagnosticIndex < actual.Diagnostics.Count; diagnosticIndex++)
                            if (actual.Diagnostics[diagnosticIndex].Severity == DiagnosticSeverity.Error) valid = false;
                        break;
                    case BehaviorCaseInvariant.NoDuplicateCommandSequences:
                        var identities = new HashSet<string>(StringComparer.Ordinal);
                        for (var commandIndex = 0; commandIndex < actual.Commands.Count; commandIndex++)
                            valid &= identities.Add(actual.Commands[commandIndex].TreeInstanceId.Value + ":" + actual.Commands[commandIndex].Sequence);
                        break;
                    case BehaviorCaseInvariant.NoActiveOperationLeaks:
                        valid = actual.ActiveOperationCount == 0;
                        break;
                    case BehaviorCaseInvariant.TerminalRootHasNoActiveNodes:
                        valid = !actual.RootStatus.HasValue || actual.ActiveNodeCount == 0;
                        break;
                    default:
                        valid = false;
                        break;
                }

                if (!valid)
                    failures.Add(Failure(stepIndex, BehaviorCaseFailureKind.InvariantViolation, pointer + "/" + index, "Typed invariant failed."));
            }
        }

        private static bool ValuesEqual(
            BehaviorCaseValue expected,
            BehaviorCaseValue actual,
            double? absoluteTolerance,
            double? relativeTolerance)
        {
            if (actual == null || expected.IsRegistered != actual.IsRegistered) return false;
            if (expected.IsRegistered)
            {
                return expected.RegisteredTypeId == actual.RegisteredTypeId
                    && expected.RegisteredTypeVersion == actual.RegisteredTypeVersion
                    && BytesEqual(expected.CopyRegisteredBytes(), actual.CopyRegisteredBytes());
            }

            if (expected.BuiltInValue.Type != actual.BuiltInValue.Type) return false;
            if (expected.BuiltInValue.Type == BlackboardValueType.Enum32
                && !string.Equals(expected.EnumContract, actual.EnumContract, StringComparison.Ordinal)) return false;
            if (!absoluteTolerance.HasValue && !relativeTolerance.HasValue)
                return expected.BuiltInValue.Equals(actual.BuiltInValue);

            var absolute = absoluteTolerance ?? 0d;
            var relative = relativeTolerance ?? 0d;
            switch (expected.BuiltInValue.Type)
            {
                case BlackboardValueType.Float32:
                    expected.BuiltInValue.TryGetFloat32(out var ef32); actual.BuiltInValue.TryGetFloat32(out var af32);
                    return Near(ef32, af32, absolute, relative);
                case BlackboardValueType.Float64:
                    expected.BuiltInValue.TryGetFloat64(out var ef64); actual.BuiltInValue.TryGetFloat64(out var af64);
                    return Near(ef64, af64, absolute, relative);
                case BlackboardValueType.Float2:
                    expected.BuiltInValue.TryGetFloat2(out var ef2); actual.BuiltInValue.TryGetFloat2(out var af2);
                    return Near(ef2.X, af2.X, absolute, relative) && Near(ef2.Y, af2.Y, absolute, relative);
                case BlackboardValueType.Float3:
                    expected.BuiltInValue.TryGetFloat3(out var ef3); actual.BuiltInValue.TryGetFloat3(out var af3);
                    return Near(ef3.X, af3.X, absolute, relative) && Near(ef3.Y, af3.Y, absolute, relative) && Near(ef3.Z, af3.Z, absolute, relative);
                case BlackboardValueType.Quaternion:
                    expected.BuiltInValue.TryGetQuaternion(out var eq); actual.BuiltInValue.TryGetQuaternion(out var aq);
                    return Near(eq.X, aq.X, absolute, relative) && Near(eq.Y, aq.Y, absolute, relative)
                        && Near(eq.Z, aq.Z, absolute, relative) && Near(eq.W, aq.W, absolute, relative);
                default:
                    return false;
            }
        }

        private static bool CommandEqual(BehaviorCaseCommandExpectation expected, BehaviorCaseCommandExpectation actual)
        {
            return expected.Phase == actual.Phase && expected.Type == actual.Type
                && expected.TreeInstanceId == actual.TreeInstanceId && expected.Sequence == actual.Sequence
                && (!expected.OperationId.HasValue || expected.OperationId == actual.OperationId)
                && ValuesEqual(expected.Payload, actual.Payload, null, null);
        }

        private static bool TraceMatches(BehaviorCaseTraceExpectation expected, BehaviorCaseObservedTrace actual)
        {
            return expected.Event == actual.Event
                && OptionalMatches(expected.TraceFormatVersion, actual.TraceFormatVersion)
                && OptionalMatches(expected.TreeSemanticHash, actual.TreeSemanticHash)
                && OptionalMatches(expected.TreeInstanceId, actual.TreeInstanceId)
                && OptionalMatches(expected.Sequence, actual.Sequence)
                && OptionalMatches(expected.UpdateId, actual.UpdateId)
                && OptionalMatches(expected.SnapshotRevision, actual.SnapshotRevision)
                && OptionalMatches(expected.NodeIndex, actual.NodeIndex)
                && OptionalMatches(expected.Status, actual.Status)
                && OptionalMatches(expected.ExitReason, actual.ExitReason)
                && OptionalMatches(expected.AbortReason, actual.AbortReason)
                && OptionalMatches(expected.SourceNodeIndex, actual.SourceNodeIndex)
                && OptionalMatches(expected.DiagnosticCode, actual.DiagnosticCode)
                && OptionalMatches(expected.StableBlackboardKeyId, actual.StableBlackboardKeyId)
                && OptionalMatches(expected.OldBlackboardVersion, actual.OldBlackboardVersion)
                && OptionalMatches(expected.NewBlackboardVersion, actual.NewBlackboardVersion);
        }

        private static bool DiagnosticMatches(BehaviorCaseDiagnosticExpectation expected, Diagnostic actual)
        {
            var location = expected.Location;
            return expected.Code == actual.Code && expected.Severity == actual.Severity
                && (!location.HasDocumentId || location.DocumentId == actual.Location.DocumentId)
                && (!location.HasJsonPointer || location.JsonPointer == actual.Location.JsonPointer)
                && (!location.TreeInstanceId.IsValid || location.TreeInstanceId == actual.Location.TreeInstanceId)
                && (!location.NodeId.IsValid || location.NodeId == actual.Location.NodeId);
        }

        private static bool OptionalMatches<T>(T? expected, T? actual) where T : struct
            => !expected.HasValue || expected.Equals(actual);

        private static bool OptionalMatches<T>(T? expected, T actual) where T : struct
            => !expected.HasValue || expected.Value.Equals(actual);

        private static bool ExactSequence<TExpected, TActual>(
            IReadOnlyList<TExpected> expected,
            IReadOnlyList<TActual> actual,
            Func<TExpected, TActual, bool> equals)
        {
            if (expected.Count != actual.Count) return false;
            for (var index = 0; index < expected.Count; index++)
                if (!equals(expected[index], actual[index])) return false;
            return true;
        }

        private static bool OrderedSubset<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, Func<T, T, bool> equals)
        {
            var expectedIndex = 0;
            for (var actualIndex = 0; actualIndex < actual.Count && expectedIndex < expected.Count; actualIndex++)
                if (equals(expected[expectedIndex], actual[actualIndex])) expectedIndex++;
            return expectedIndex == expected.Count;
        }

        private static bool Near(double expected, double actual, double absolute, double relative)
            => Math.Abs(expected - actual) <= absolute + relative * Math.Max(Math.Abs(expected), Math.Abs(actual));

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        private static BehaviorCaseAssertionFailure Failure(int stepIndex, BehaviorCaseFailureKind kind, string pointer, string message)
            => new BehaviorCaseAssertionFailure(stepIndex, kind, pointer, message);
    }
}
