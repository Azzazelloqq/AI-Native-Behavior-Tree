using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring.BehaviorCases
{
    internal interface IBehaviorCaseExecutorFactory
    {
        IBehaviorCaseExecutor Create(BehaviorCaseExecutorConfiguration configuration);
    }

    internal interface IBehaviorCaseExecutor : IDisposable
    {
        BehaviorCaseExecutorStepResult Execute(BehaviorCaseStep step);
    }

    internal sealed class BehaviorCaseExecutorConfiguration
    {
        internal BehaviorCaseExecutorConfiguration(BehaviorCaseDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            Tree = document.Tree;
            TreeInstanceId = document.TreeInstanceId;
            RootSeed = document.RootSeed;
            InitialBlackboard = CopyMap(document.InitialBlackboard);
        }

        internal string Tree { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal ulong RootSeed { get; }
        internal IReadOnlyDictionary<string, BehaviorCaseValue> InitialBlackboard { get; }

        private static IReadOnlyDictionary<string, BehaviorCaseValue> CopyMap(
            IReadOnlyDictionary<string, BehaviorCaseValue> source)
        {
            var copy = new SortedDictionary<string, BehaviorCaseValue>(AIBT.Authoring.Utf8OrdinalComparer.Instance);
            foreach (var pair in source) copy.Add(pair.Key, pair.Value);
            return new ReadOnlyDictionary<string, BehaviorCaseValue>(copy);
        }
    }

    internal readonly struct BehaviorCaseObservedBlackboardValue
    {
        internal BehaviorCaseObservedBlackboardValue(BehaviorCaseValue value, ulong version)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Version = version;
        }

        internal BehaviorCaseValue Value { get; }
        internal ulong Version { get; }
    }

    internal sealed class BehaviorCaseObservedTrace
    {
        internal const uint SupportedFormatVersion = 1;

        internal BehaviorCaseObservedTrace(
            BehaviorCaseTraceEvent eventKind,
            uint traceFormatVersion,
            CompiledHash treeSemanticHash,
            TreeInstanceId treeInstanceId,
            ulong sequence,
            ulong updateId,
            Revision snapshotRevision,
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
            if (!treeSemanticHash.IsValid) throw new ArgumentException("A tree semantic hash is required.", nameof(treeSemanticHash));
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (updateId == 0) throw new ArgumentOutOfRangeException(nameof(updateId));
            if (!snapshotRevision.IsValid) throw new ArgumentException("A snapshot revision is required.", nameof(snapshotRevision));
            if (nodeIndex.HasValue && !nodeIndex.Value.IsValid) throw new ArgumentException("Node index must be valid.", nameof(nodeIndex));
            if (status.HasValue && !Enum.IsDefined(typeof(NodeStatus), status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
            if (exitReason.HasValue && !Enum.IsDefined(typeof(NodeExitReason), exitReason.Value)) throw new ArgumentOutOfRangeException(nameof(exitReason));
            if (abortReason.HasValue && !Enum.IsDefined(typeof(NodeAbortReason), abortReason.Value)) throw new ArgumentOutOfRangeException(nameof(abortReason));
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
        internal uint TraceFormatVersion { get; }
        internal CompiledHash TreeSemanticHash { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal ulong Sequence { get; }
        internal ulong UpdateId { get; }
        internal Revision SnapshotRevision { get; }
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

    internal sealed class BehaviorCaseExecutorStepResult
    {
        internal BehaviorCaseExecutorStepResult(
            BehaviorCaseProgress progress,
            NodeStatus? rootStatus,
            ulong executedSteps,
            IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> blackboard = null,
            IEnumerable<BehaviorCaseCommandExpectation> commands = null,
            IEnumerable<BehaviorCaseObservedTrace> trace = null,
            DiagnosticCollection diagnostics = null,
            uint activeOperationCount = 0,
            uint activeNodeCount = 0)
        {
            if (!Enum.IsDefined(typeof(BehaviorCaseProgress), progress))
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (rootStatus.HasValue && rootStatus != NodeStatus.Success && rootStatus != NodeStatus.Failure)
                throw new ArgumentOutOfRangeException(nameof(rootStatus), "A public root result must be terminal.");
            Progress = progress;
            RootStatus = rootStatus;
            ExecutedSteps = executedSteps;
            Blackboard = CopyMap(blackboard);
            Commands = Copy(commands);
            Trace = Copy(trace);
            Diagnostics = diagnostics ?? DiagnosticCollection.Empty;
            ActiveOperationCount = activeOperationCount;
            ActiveNodeCount = activeNodeCount;
        }

        internal BehaviorCaseProgress Progress { get; }
        internal NodeStatus? RootStatus { get; }
        internal ulong ExecutedSteps { get; }
        internal IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> Blackboard { get; }
        internal IReadOnlyList<BehaviorCaseCommandExpectation> Commands { get; }
        internal IReadOnlyList<BehaviorCaseObservedTrace> Trace { get; }
        internal DiagnosticCollection Diagnostics { get; }
        internal uint ActiveOperationCount { get; }
        internal uint ActiveNodeCount { get; }

        private static IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> CopyMap(
            IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> source)
        {
            var copy = new SortedDictionary<string, BehaviorCaseObservedBlackboardValue>(AIBT.Authoring.Utf8OrdinalComparer.Instance);
            if (source != null) foreach (var pair in source) copy.Add(pair.Key, pair.Value);
            return new ReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue>(copy);
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source)
            => Array.AsReadOnly(source == null ? Array.Empty<T>() : new List<T>(source).ToArray());
    }
}
