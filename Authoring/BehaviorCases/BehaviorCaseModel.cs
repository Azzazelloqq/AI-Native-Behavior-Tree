using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring.BehaviorCases
{
    internal enum BehaviorCaseOperation : byte { Update, Resume, Restart, Abort }
    internal enum BehaviorCaseProgress : byte { Completed, Waiting, Suspended, Rejected, Faulted }
    internal enum BehaviorCaseCommandMatch : byte { Exact, OrderedSubset }
    internal enum BehaviorCaseInvariant : byte
    {
        NoErrorDiagnostics,
        NoDuplicateCommandSequences,
        NoActiveOperationLeaks,
        TerminalRootHasNoActiveNodes,
    }

    internal enum BehaviorCaseTraceEvent : byte
    {
        UpdateStarted,
        UpdateCompleted,
        NodeEntered,
        NodeTicked,
        NodeAbortStarted,
        NodeExited,
        BlackboardChanged,
        ObserverQueued,
        ObserverEvaluated,
        CommandEmitted,
        CompletionConsumed,
        CompletionDiscarded,
        DiagnosticRaised,
        BudgetYielded,
        ExecutionResumed,
    }

    internal sealed class BehaviorCaseDocument
    {
        internal const string CurrentFormat = "aibt.case";
        internal const int CurrentFormatVersion = 1;

        internal BehaviorCaseDocument(
            string name,
            string description,
            string tree,
            TreeInstanceId treeInstanceId,
            ulong rootSeed,
            IReadOnlyDictionary<string, BehaviorCaseValue> initialBlackboard,
            IEnumerable<BehaviorCaseStep> steps,
            IEnumerable<string> tags)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A case name is required.", nameof(name));
            if (string.IsNullOrEmpty(tree)) throw new ArgumentException("A semantic tree path is required.", nameof(tree));
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            Name = name;
            Description = description;
            Tree = tree;
            TreeInstanceId = treeInstanceId;
            RootSeed = rootSeed;
            InitialBlackboard = CopyMap(initialBlackboard);
            Steps = Copy(steps);
            Tags = CopyTags(tags);
            if (Steps.Count == 0) throw new ArgumentException("At least one case step is required.", nameof(steps));
            for (var index = 0; index < Steps.Count; index++)
                if (Steps[index] == null) throw new ArgumentException("Case steps cannot contain null.", nameof(steps));
        }

        internal string Name { get; }
        internal string Description { get; }
        internal string Tree { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal ulong RootSeed { get; }
        internal IReadOnlyDictionary<string, BehaviorCaseValue> InitialBlackboard { get; }
        internal IReadOnlyList<BehaviorCaseStep> Steps { get; }
        internal IReadOnlyList<string> Tags { get; }

        private static IReadOnlyDictionary<string, BehaviorCaseValue> CopyMap(
            IReadOnlyDictionary<string, BehaviorCaseValue> values)
        {
            var copy = new SortedDictionary<string, BehaviorCaseValue>(AIBT.Authoring.Utf8OrdinalComparer.Instance);
            if (values != null)
            {
                foreach (var pair in values)
                {
                    if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                        throw new ArgumentException("Initial blackboard entries require nonempty keys and values.", nameof(values));
                    copy.Add(pair.Key, pair.Value);
                }
            }

            return new ReadOnlyDictionary<string, BehaviorCaseValue>(copy);
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
            => Array.AsReadOnly(values == null ? Array.Empty<T>() : new List<T>(values).ToArray());

        private static IReadOnlyList<string> CopyTags(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (var value in values)
                {
                    if (string.IsNullOrEmpty(value) || !seen.Add(value))
                        throw new ArgumentException("Case tags must be nonempty and unique.", nameof(values));
                    result.Add(value);
                }
            }
            result.Sort(AIBT.Authoring.Utf8OrdinalComparer.Instance);
            return result.AsReadOnly();
        }
    }

    internal sealed class BehaviorCaseValue
    {
        private readonly byte[] _registeredBytes;

        private BehaviorCaseValue(BlackboardValue builtInValue, string enumContract = null)
        {
            if (!builtInValue.IsValid) throw new ArgumentException("A valid built-in value is required.", nameof(builtInValue));
            if (builtInValue.Type == BlackboardValueType.Enum32 && string.IsNullOrEmpty(enumContract))
                throw new ArgumentException("Enum32 values require their canonical contract string.", nameof(enumContract));
            if (builtInValue.Type != BlackboardValueType.Enum32 && enumContract != null)
                throw new ArgumentException("Only Enum32 values can carry an enum contract.", nameof(enumContract));
            BuiltInValue = builtInValue;
            EnumContract = enumContract;
        }

        private BehaviorCaseValue(ulong registeredTypeId, uint registeredTypeVersion, byte[] bytes)
        {
            if (registeredTypeId == 0) throw new ArgumentOutOfRangeException(nameof(registeredTypeId));
            if (registeredTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(registeredTypeVersion));
            RegisteredTypeId = registeredTypeId;
            RegisteredTypeVersion = registeredTypeVersion;
            _registeredBytes = bytes == null ? throw new ArgumentNullException(nameof(bytes)) : (byte[])bytes.Clone();
        }

        internal bool IsRegistered => RegisteredTypeId != 0;
        internal BlackboardValue BuiltInValue { get; }
        internal string EnumContract { get; }
        internal ulong RegisteredTypeId { get; }
        internal uint RegisteredTypeVersion { get; }
        internal byte[] CopyRegisteredBytes() => _registeredBytes == null ? null : (byte[])_registeredBytes.Clone();
        internal static BehaviorCaseValue BuiltIn(BlackboardValue value)
        {
            if (value.Type == BlackboardValueType.Enum32)
                throw new ArgumentException("Use Enum32 so the canonical contract string is preserved.", nameof(value));
            return new BehaviorCaseValue(value);
        }
        internal static BehaviorCaseValue Enum32(string contract, int value)
        {
            if (string.IsNullOrEmpty(contract)) throw new ArgumentException("An enum contract is required.", nameof(contract));
            return new BehaviorCaseValue(
                BlackboardValue.FromEnum32(new Enum32Value(StableHash.Fnv1A64(contract), value)),
                contract);
        }
        internal static BehaviorCaseValue Registered(ulong typeId, uint version, byte[] bytes)
            => new BehaviorCaseValue(typeId, version, bytes);
    }

    internal abstract class BehaviorCaseStep
    {
        protected BehaviorCaseStep(BehaviorCaseOperation operation, BehaviorCaseExpectation expectation)
        {
            Operation = operation;
            Expectation = expectation ?? BehaviorCaseExpectation.Empty;
        }

        internal BehaviorCaseOperation Operation { get; }
        internal BehaviorCaseExpectation Expectation { get; }
    }

    internal sealed class BehaviorCaseUpdateStep : BehaviorCaseStep
    {
        internal BehaviorCaseUpdateStep(
            ulong updateId,
            Revision snapshotRevision,
            long timeMicroseconds,
            ulong? stepBudget,
            IEnumerable<BehaviorCaseEvent> events,
            IEnumerable<BehaviorCaseCompletion> completions,
            BehaviorCaseExpectation expectation)
            : base(BehaviorCaseOperation.Update, expectation)
        {
            if (updateId == 0) throw new ArgumentOutOfRangeException(nameof(updateId));
            if (!snapshotRevision.IsValid) throw new ArgumentException("A snapshot revision is required.", nameof(snapshotRevision));
            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            TimeMicroseconds = timeMicroseconds;
            StepBudget = stepBudget;
            Events = Copy(events);
            Completions = Copy(completions);
        }

        internal ulong UpdateId { get; }
        internal Revision SnapshotRevision { get; }
        internal long TimeMicroseconds { get; }
        internal ulong? StepBudget { get; }
        internal IReadOnlyList<BehaviorCaseEvent> Events { get; }
        internal IReadOnlyList<BehaviorCaseCompletion> Completions { get; }
        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
            => Array.AsReadOnly(values == null ? Array.Empty<T>() : new List<T>(values).ToArray());
    }

    internal sealed class BehaviorCaseResumeStep : BehaviorCaseStep
    {
        internal BehaviorCaseResumeStep(ulong? stepBudget, BehaviorCaseExpectation expectation)
            : base(BehaviorCaseOperation.Resume, expectation) { StepBudget = stepBudget; }
        internal ulong? StepBudget { get; }
    }

    internal sealed class BehaviorCaseControlStep : BehaviorCaseStep
    {
        internal BehaviorCaseControlStep(BehaviorCaseExpectation expectation)
            : base(BehaviorCaseOperation.Restart, expectation) { }
    }

    internal sealed class BehaviorCaseAbortStep : BehaviorCaseStep
    {
        internal BehaviorCaseAbortStep(
            ulong updateId,
            Revision snapshotRevision,
            long timeMicroseconds,
            ulong? stepBudget,
            IEnumerable<BehaviorCaseCompletion> completions,
            BehaviorCaseExpectation expectation)
            : base(BehaviorCaseOperation.Abort, expectation)
        {
            if (updateId == 0) throw new ArgumentOutOfRangeException(nameof(updateId));
            if (!snapshotRevision.IsValid) throw new ArgumentException("A snapshot revision is required.", nameof(snapshotRevision));
            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            TimeMicroseconds = timeMicroseconds;
            StepBudget = stepBudget;
            Completions = Array.AsReadOnly(completions == null
                ? Array.Empty<BehaviorCaseCompletion>()
                : new List<BehaviorCaseCompletion>(completions).ToArray());
        }

        internal ulong UpdateId { get; }
        internal Revision SnapshotRevision { get; }
        internal long TimeMicroseconds { get; }
        internal ulong? StepBudget { get; }
        internal IReadOnlyList<BehaviorCaseCompletion> Completions { get; }
    }

    internal readonly struct BehaviorCaseEvent
    {
        internal BehaviorCaseEvent(ulong sourceId, ulong sourceSequence, ulong typeId, uint typeVersion, BehaviorCaseValue payload)
        {
            if (sourceId == 0 || typeId == 0 || typeVersion == 0) throw new ArgumentOutOfRangeException(nameof(sourceId));
            SourceId = sourceId;
            SourceSequence = sourceSequence;
            TypeId = typeId;
            TypeVersion = typeVersion;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
        internal ulong SourceId { get; }
        internal ulong SourceSequence { get; }
        internal ulong TypeId { get; }
        internal uint TypeVersion { get; }
        internal BehaviorCaseValue Payload { get; }
    }

    internal readonly struct BehaviorCaseCompletion
    {
        internal BehaviorCaseCompletion(
            ulong sourceId,
            ulong sourceSequence,
            OperationId operationId,
            CompletionOutcome outcome,
            Revision snapshotRevision,
            BehaviorCaseValue payload)
        {
            if (sourceId == 0) throw new ArgumentOutOfRangeException(nameof(sourceId));
            if (!operationId.IsValid) throw new ArgumentException("A valid operation ID is required.", nameof(operationId));
            if (!Enum.IsDefined(typeof(CompletionOutcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
            if (!snapshotRevision.IsValid) throw new ArgumentException("A snapshot revision is required.", nameof(snapshotRevision));
            if (payload != null && !payload.IsRegistered)
                throw new ArgumentException("Completion payloads require a registered opaque contract.", nameof(payload));
            SourceId = sourceId;
            SourceSequence = sourceSequence;
            OperationId = operationId;
            Outcome = outcome;
            SnapshotRevision = snapshotRevision;
            Payload = payload;
        }
        internal ulong SourceId { get; }
        internal ulong SourceSequence { get; }
        internal OperationId OperationId { get; }
        internal CompletionOutcome Outcome { get; }
        internal Revision SnapshotRevision { get; }
        internal BehaviorCaseValue Payload { get; }
    }

    internal sealed class BehaviorCaseExpectation
    {
        internal static BehaviorCaseExpectation Empty { get; } = new BehaviorCaseExpectation();

        internal BehaviorCaseExpectation(
            BehaviorCaseProgress? progress = null,
            NodeStatus? rootStatus = null,
            ulong? executedSteps = null,
            IEnumerable<BehaviorCaseBlackboardExpectation> blackboard = null,
            BehaviorCaseCommandExpectationSet commands = null,
            IEnumerable<BehaviorCaseTraceExpectation> trace = null,
            IEnumerable<BehaviorCaseDiagnosticExpectation> diagnostics = null,
            IEnumerable<BehaviorCaseInvariant> invariants = null,
            bool hasBlackboardExpectation = false,
            bool hasTraceExpectation = false,
            bool hasDiagnosticExpectation = false,
            bool hasInvariantExpectation = false)
        {
            if (progress.HasValue && !Enum.IsDefined(typeof(BehaviorCaseProgress), progress.Value))
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (rootStatus.HasValue && rootStatus != NodeStatus.Success && rootStatus != NodeStatus.Failure)
                throw new ArgumentOutOfRangeException(nameof(rootStatus), "A public root result must be terminal.");
            Progress = progress;
            RootStatus = rootStatus;
            ExecutedSteps = executedSteps;
            Blackboard = Copy(blackboard);
            Commands = commands;
            Trace = Copy(trace);
            Diagnostics = Copy(diagnostics);
            Invariants = Copy(invariants);
            for (var index = 0; index < Blackboard.Count; index++)
                if (Blackboard[index].Value == null) throw new ArgumentException("Blackboard expectations must be valid.", nameof(blackboard));
            for (var index = 0; index < Diagnostics.Count; index++)
                if (!Diagnostics[index].Code.IsValid) throw new ArgumentException("Diagnostic expectations must be valid.", nameof(diagnostics));
            for (var index = 0; index < Invariants.Count; index++)
                if (!Enum.IsDefined(typeof(BehaviorCaseInvariant), Invariants[index]))
                    throw new ArgumentOutOfRangeException(nameof(invariants));
            HasBlackboardExpectation = hasBlackboardExpectation || Blackboard.Count != 0;
            HasTraceExpectation = hasTraceExpectation || Trace.Count != 0;
            HasDiagnosticExpectation = hasDiagnosticExpectation || Diagnostics.Count != 0;
            HasInvariantExpectation = hasInvariantExpectation || Invariants.Count != 0;
        }
        internal BehaviorCaseProgress? Progress { get; }
        internal NodeStatus? RootStatus { get; }
        internal ulong? ExecutedSteps { get; }
        internal IReadOnlyList<BehaviorCaseBlackboardExpectation> Blackboard { get; }
        internal BehaviorCaseCommandExpectationSet Commands { get; }
        internal IReadOnlyList<BehaviorCaseTraceExpectation> Trace { get; }
        internal IReadOnlyList<BehaviorCaseDiagnosticExpectation> Diagnostics { get; }
        internal IReadOnlyList<BehaviorCaseInvariant> Invariants { get; }
        internal bool HasBlackboardExpectation { get; }
        internal bool HasTraceExpectation { get; }
        internal bool HasDiagnosticExpectation { get; }
        internal bool HasInvariantExpectation { get; }
        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
            => Array.AsReadOnly(values == null ? Array.Empty<T>() : new List<T>(values).ToArray());
    }

    internal readonly struct BehaviorCaseBlackboardExpectation
    {
        internal BehaviorCaseBlackboardExpectation(string key, BehaviorCaseValue value, ulong? version, double? absoluteTolerance, double? relativeTolerance)
        {
            Key = string.IsNullOrEmpty(key) ? throw new ArgumentException("A key is required.", nameof(key)) : key;
            Value = value ?? throw new ArgumentNullException(nameof(value));
            if (absoluteTolerance.HasValue && (double.IsNaN(absoluteTolerance.Value)
                || double.IsInfinity(absoluteTolerance.Value) || absoluteTolerance.Value < 0))
                throw new ArgumentOutOfRangeException(nameof(absoluteTolerance));
            if (relativeTolerance.HasValue && (double.IsNaN(relativeTolerance.Value)
                || double.IsInfinity(relativeTolerance.Value) || relativeTolerance.Value < 0))
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
            Version = version;
            AbsoluteTolerance = absoluteTolerance;
            RelativeTolerance = relativeTolerance;
        }
        internal string Key { get; }
        internal BehaviorCaseValue Value { get; }
        internal ulong? Version { get; }
        internal double? AbsoluteTolerance { get; }
        internal double? RelativeTolerance { get; }
    }

    internal sealed class BehaviorCaseCommandExpectationSet
    {
        internal BehaviorCaseCommandExpectationSet(BehaviorCaseCommandMatch match, IEnumerable<BehaviorCaseCommandExpectation> records)
        {
            if (!Enum.IsDefined(typeof(BehaviorCaseCommandMatch), match)) throw new ArgumentOutOfRangeException(nameof(match));
            var copy = records == null ? Array.Empty<BehaviorCaseCommandExpectation>() : new List<BehaviorCaseCommandExpectation>(records).ToArray();
            for (var index = 0; index < copy.Length; index++)
            {
                if (!copy[index].Type.IsValid || !copy[index].TreeInstanceId.IsValid
                    || copy[index].Sequence == 0 || copy[index].Payload == null)
                    throw new ArgumentException("Command expectation records must be valid.", nameof(records));
            }
            Match = match;
            Records = Array.AsReadOnly(copy);
        }
        internal BehaviorCaseCommandMatch Match { get; }
        internal IReadOnlyList<BehaviorCaseCommandExpectation> Records { get; }
    }

    internal readonly struct BehaviorCaseCommandExpectation
    {
        internal BehaviorCaseCommandExpectation(CommandPhase phase, CommandType type, TreeInstanceId treeInstanceId, ulong sequence, OperationId? operationId, BehaviorCaseValue payload)
        {
            if (!type.IsValid || !treeInstanceId.IsValid || sequence == 0) throw new ArgumentException("A valid command identity is required.");
            if (!Enum.IsDefined(typeof(CommandPhase), phase)) throw new ArgumentOutOfRangeException(nameof(phase));
            if (operationId.HasValue && !operationId.Value.IsValid) throw new ArgumentException("A valid operation ID is required.", nameof(operationId));
            if (payload == null || !payload.IsRegistered)
                throw new ArgumentException("Command payloads require a registered opaque contract.", nameof(payload));
            Phase = phase; Type = type; TreeInstanceId = treeInstanceId; Sequence = sequence; OperationId = operationId;
            Payload = payload;
        }
        internal CommandPhase Phase { get; }
        internal CommandType Type { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal ulong Sequence { get; }
        internal OperationId? OperationId { get; }
        internal BehaviorCaseValue Payload { get; }
    }

    internal sealed class BehaviorCaseTraceExpectation
    {
        internal BehaviorCaseTraceExpectation(
            BehaviorCaseTraceEvent eventKind,
            uint? traceFormatVersion = null,
            CompiledHash? treeSemanticHash = null,
            TreeInstanceId? treeInstanceId = null,
            ulong? sequence = null,
            ulong? updateId = null,
            Revision? snapshotRevision = null,
            RuntimeNodeIndex? nodeIndex = null,
            NodeStatus? status = null,
            NodeExitReason? exitReason = null,
            NodeAbortReason? abortReason = null,
            RuntimeNodeIndex? sourceNodeIndex = null,
            DiagnosticCode? diagnosticCode = null,
            ulong? stableBlackboardKeyId = null,
            ulong? oldBlackboardVersion = null,
            ulong? newBlackboardVersion = null)
        {
            if (!Enum.IsDefined(typeof(BehaviorCaseTraceEvent), eventKind)) throw new ArgumentOutOfRangeException(nameof(eventKind));
            if (traceFormatVersion == 0) throw new ArgumentOutOfRangeException(nameof(traceFormatVersion));
            if (treeSemanticHash.HasValue && !treeSemanticHash.Value.IsValid) throw new ArgumentException("Tree semantic hash must be valid.", nameof(treeSemanticHash));
            if (treeInstanceId.HasValue && !treeInstanceId.Value.IsValid) throw new ArgumentException("Tree instance ID must be valid.", nameof(treeInstanceId));
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (status.HasValue && !Enum.IsDefined(typeof(NodeStatus), status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
            if (exitReason.HasValue && !Enum.IsDefined(typeof(NodeExitReason), exitReason.Value)) throw new ArgumentOutOfRangeException(nameof(exitReason));
            if (abortReason.HasValue && !Enum.IsDefined(typeof(NodeAbortReason), abortReason.Value)) throw new ArgumentOutOfRangeException(nameof(abortReason));
            if (updateId == 0) throw new ArgumentOutOfRangeException(nameof(updateId));
            if (snapshotRevision.HasValue && !snapshotRevision.Value.IsValid) throw new ArgumentException("Snapshot revision must be valid.", nameof(snapshotRevision));
            if (nodeIndex.HasValue && !nodeIndex.Value.IsValid) throw new ArgumentException("Node index must be valid.", nameof(nodeIndex));
            if (sourceNodeIndex.HasValue && !sourceNodeIndex.Value.IsValid) throw new ArgumentException("Source node index must be valid.", nameof(sourceNodeIndex));
            if (diagnosticCode.HasValue && !diagnosticCode.Value.IsValid) throw new ArgumentException("Diagnostic code must be valid.", nameof(diagnosticCode));
            if (stableBlackboardKeyId == 0) throw new ArgumentOutOfRangeException(nameof(stableBlackboardKeyId));
            Event = eventKind;
            TraceFormatVersion = traceFormatVersion;
            TreeSemanticHash = treeSemanticHash;
            TreeInstanceId = treeInstanceId;
            Sequence = sequence;
            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            NodeIndex = nodeIndex;
            Status = status;
            ExitReason = exitReason;
            AbortReason = abortReason;
            SourceNodeIndex = sourceNodeIndex;
            DiagnosticCode = diagnosticCode;
            StableBlackboardKeyId = stableBlackboardKeyId;
            OldBlackboardVersion = oldBlackboardVersion;
            NewBlackboardVersion = newBlackboardVersion;
        }

        internal BehaviorCaseTraceEvent Event { get; }
        internal uint? TraceFormatVersion { get; }
        internal CompiledHash? TreeSemanticHash { get; }
        internal TreeInstanceId? TreeInstanceId { get; }
        internal ulong? Sequence { get; }
        internal ulong? UpdateId { get; }
        internal Revision? SnapshotRevision { get; }
        internal RuntimeNodeIndex? NodeIndex { get; }
        internal NodeStatus? Status { get; }
        internal NodeExitReason? ExitReason { get; }
        internal NodeAbortReason? AbortReason { get; }
        internal RuntimeNodeIndex? SourceNodeIndex { get; }
        internal DiagnosticCode? DiagnosticCode { get; }
        internal ulong? StableBlackboardKeyId { get; }
        internal ulong? OldBlackboardVersion { get; }
        internal ulong? NewBlackboardVersion { get; }
    }

    internal readonly struct BehaviorCaseDiagnosticExpectation
    {
        internal BehaviorCaseDiagnosticExpectation(DiagnosticCode code, DiagnosticSeverity severity, DiagnosticLocation location)
        {
            if (!code.IsValid) throw new ArgumentException("A diagnostic code is required.", nameof(code));
            if (!Enum.IsDefined(typeof(DiagnosticSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));
            Code = code; Severity = severity; Location = location;
        }
        internal DiagnosticCode Code { get; }
        internal DiagnosticSeverity Severity { get; }
        internal DiagnosticLocation Location { get; }
    }
}
