using System;
using System.Collections.Generic;

namespace AIBT.Authoring.BehaviorCases
{
    internal static class BehaviorCaseSemanticValidator
    {
        internal static DiagnosticCollection Validate(
            BehaviorCaseDocument document,
            string documentId = null,
            BehaviorCaseRegisteredValueRegistry registeredValues = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var diagnostics = new List<Diagnostic>();
            ulong lastUpdateId = 0;
            var mayResume = false;
            var sourceSequences = new Dictionary<ulong, ulong>();
            var seenSources = new HashSet<ulong>();

            if (registeredValues != null)
            {
                foreach (var pair in document.InitialBlackboard)
                    ValidateRegisteredValue(pair.Value, registeredValues, documentId,
                        "/initialBlackboard/" + Escape(pair.Key), diagnostics);
            }

            for (var index = 0; index < document.Steps.Count; index++)
            {
                var pointer = "/steps/" + index;
                var step = document.Steps[index];
                if (step is BehaviorCaseUpdateStep update)
                {
                    if (update.UpdateId <= lastUpdateId)
                    {
                        diagnostics.Add(Error("Update IDs must strictly increase.", documentId, pointer + "/updateId"));
                    }
                    else
                    {
                        lastUpdateId = update.UpdateId;
                    }

                    mayResume = update.StepBudget.HasValue;
                    ValidateSourceOrdering(update.Events, sourceSequences, seenSources, documentId, pointer + "/events", diagnostics);
                    ValidateSourceOrdering(update.Completions, sourceSequences, seenSources, documentId, pointer + "/completions", diagnostics);
                    if (registeredValues != null)
                    {
                        for (var eventIndex = 0; eventIndex < update.Events.Count; eventIndex++)
                            ValidateRegisteredValue(update.Events[eventIndex].Payload, registeredValues, documentId,
                                pointer + "/events/" + eventIndex + "/payload", diagnostics);
                        ValidateCompletionValues(update.Completions, registeredValues, documentId,
                            pointer + "/completions", diagnostics);
                    }
                }
                else if (step is BehaviorCaseResumeStep resume)
                {
                    if (!mayResume)
                    {
                        diagnostics.Add(Error("Resume must follow a step that supplies a deterministic step budget.", documentId, pointer));
                    }

                    mayResume = resume.StepBudget.HasValue;
                }
                else if (step is BehaviorCaseAbortStep abort)
                {
                    if (abort.UpdateId <= lastUpdateId)
                        diagnostics.Add(Error("Update IDs must strictly increase.", documentId, pointer + "/updateId"));
                    else
                        lastUpdateId = abort.UpdateId;
                    ValidateSourceOrdering(abort.Completions, sourceSequences, seenSources, documentId,
                        pointer + "/completions", diagnostics);
                    if (registeredValues != null)
                        ValidateCompletionValues(abort.Completions, registeredValues, documentId,
                            pointer + "/completions", diagnostics);
                    mayResume = abort.StepBudget.HasValue;
                }
                else mayResume = false;

                ValidateExpectation(step.Expectation, documentId, pointer + "/expect", diagnostics);
                if (registeredValues != null)
                    ValidateExpectationValues(step.Expectation, registeredValues, documentId,
                        pointer + "/expect", diagnostics);
            }

            return diagnostics.Count == 0 ? DiagnosticCollection.Empty : new DiagnosticCollection(diagnostics);
        }

        private static void ValidateSourceOrdering<T>(
            IReadOnlyList<T> records,
            Dictionary<ulong, ulong> highWater,
            HashSet<ulong> seen,
            string documentId,
            string pointer,
            List<Diagnostic> diagnostics)
        {
            for (var index = 0; index < records.Count; index++)
            {
                ulong sourceId;
                ulong sequence;
                if (records[index] is BehaviorCaseEvent eventRecord)
                {
                    sourceId = eventRecord.SourceId;
                    sequence = eventRecord.SourceSequence;
                }
                else
                {
                    var completion = (BehaviorCaseCompletion)(object)records[index];
                    sourceId = completion.SourceId;
                    sequence = completion.SourceSequence;
                }

                if (seen.Contains(sourceId) && sequence <= highWater[sourceId])
                {
                    diagnostics.Add(Error(
                        "Source sequences must strictly increase per source; gaps are allowed.",
                        documentId,
                        pointer + "/" + index + "/sourceSequence"));
                }
                else
                {
                    seen.Add(sourceId);
                    highWater[sourceId] = sequence;
                }
            }
        }

        private static void ValidateExpectation(
            BehaviorCaseExpectation expectation,
            string documentId,
            string pointer,
            List<Diagnostic> diagnostics)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < expectation.Blackboard.Count; index++)
            {
                var item = expectation.Blackboard[index];
                if (!keys.Add(item.Key))
                {
                    diagnostics.Add(Error("Blackboard expectations must have unique keys.", documentId, pointer + "/blackboard/" + index + "/key"));
                }

                var tolerance = item.AbsoluteTolerance.HasValue || item.RelativeTolerance.HasValue;
                var floatValue = !item.Value.IsRegistered
                    && (item.Value.BuiltInValue.Type == BlackboardValueType.Float32
                        || item.Value.BuiltInValue.Type == BlackboardValueType.Float64
                        || item.Value.BuiltInValue.Type == BlackboardValueType.Float2
                        || item.Value.BuiltInValue.Type == BlackboardValueType.Float3
                        || item.Value.BuiltInValue.Type == BlackboardValueType.Quaternion);
                if (tolerance && !floatValue)
                {
                    diagnostics.Add(Error("Tolerance is permitted only for floating-point expectations.", documentId, pointer + "/blackboard/" + index));
                }
            }
        }

        private static void ValidateCompletionValues(
            IReadOnlyList<BehaviorCaseCompletion> completions,
            BehaviorCaseRegisteredValueRegistry registry,
            string documentId,
            string pointer,
            List<Diagnostic> diagnostics)
        {
            for (var index = 0; index < completions.Count; index++)
                if (completions[index].Payload != null)
                    ValidateRegisteredValue(completions[index].Payload, registry, documentId,
                        pointer + "/" + index + "/payload", diagnostics);
        }

        private static void ValidateExpectationValues(
            BehaviorCaseExpectation expectation,
            BehaviorCaseRegisteredValueRegistry registry,
            string documentId,
            string pointer,
            List<Diagnostic> diagnostics)
        {
            for (var index = 0; index < expectation.Blackboard.Count; index++)
                ValidateRegisteredValue(expectation.Blackboard[index].Value, registry, documentId,
                    pointer + "/blackboard/" + index + "/value", diagnostics);
            if (expectation.Commands == null) return;
            for (var index = 0; index < expectation.Commands.Records.Count; index++)
                ValidateRegisteredValue(expectation.Commands.Records[index].Payload, registry, documentId,
                    pointer + "/commands/records/" + index + "/payload", diagnostics);
        }

        private static void ValidateRegisteredValue(
            BehaviorCaseValue value,
            BehaviorCaseRegisteredValueRegistry registry,
            string documentId,
            string pointer,
            List<Diagnostic> diagnostics)
        {
            if (value != null && value.IsRegistered && !registry.TryValidate(value, out var message))
                diagnostics.Add(Error(message, documentId, pointer));
        }

        private static string Escape(string value)
            => value.Replace("~", "~0").Replace("/", "~1");

        private static Diagnostic Error(string message, string documentId, string pointer)
            => BehaviorCaseJsonDiagnostics.Create(
                BehaviorCaseJsonDiagnosticCodes.SemanticViolation,
                message,
                documentId,
                pointer);
    }
}
