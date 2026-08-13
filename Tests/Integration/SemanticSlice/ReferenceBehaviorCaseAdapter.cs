using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Tests.BehaviorCases;

namespace AIBT.Tests.Integration.SemanticSlice
{
    internal sealed class ReferenceBehaviorCaseExecutorFactory : IBehaviorCaseExecutorFactory
    {
        private static readonly CompiledCompilerVersion CompilerVersion
            = new CompiledCompilerVersion(1, 0, 0, 1);
        private readonly string _treeRoot;
        private readonly NodeRegistry _nodeRegistry;

        internal ReferenceBehaviorCaseExecutorFactory(string treeRoot, NodeRegistry nodeRegistry)
        {
            if (string.IsNullOrEmpty(treeRoot)) throw new ArgumentException("A tree fixture root is required.", nameof(treeRoot));
            _treeRoot = Path.GetFullPath(treeRoot);
            _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
        }

        public IBehaviorCaseExecutor Create(BehaviorCaseExecutorConfiguration configuration)
        {
            if (configuration.RootSeed != 0)
                throw new NotSupportedException("Phase 1 reference execution has no random service; rootSeed must be zero.");
            var treePath = ResolveTreePath(configuration.Tree);
            var read = CanonicalTreeJson.Parse(File.ReadAllBytes(treePath), configuration.Tree);
            if (!read.Success) throw new InvalidOperationException(Diagnostics(read.Diagnostics));
            var validation = TreeValidator.Validate(read.Document, _nodeRegistry);
            if (validation.Any(item => item.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException(Diagnostics(validation));
            var compilation = ReferenceCompiler.Compile(
                read.Document,
                _nodeRegistry,
                new ReferenceCompilerOptions(
                    NormalizeSourceId(configuration.Tree),
                    ReferenceCompilationPolicy.Phase1,
                    CompilerVersion));
            if (!compilation.Success) throw new InvalidOperationException(Diagnostics(compilation.Diagnostics));

            var keyMetadata = read.Document.Blackboard.ToDictionary(
                key => StableHash.Fnv1A64(key.Id),
                key => new SourceBlackboardKey(key.Id, key.Type.EnumContract));
            var initialValues = CreateInitialValues(configuration.InitialBlackboard);
            var trace = new CollectingTraceSink();
            var machine = new ReferenceExecutionMachine(
                compilation.Program,
                configuration.TreeInstanceId,
                SemanticSliceNodeContracts.CreateLeafRegistry(),
                trace,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                SemanticSliceNodeContracts.CreateObserverRegistry(),
                initialBlackboard: initialValues);
            return new ReferenceBehaviorCaseExecutor(machine, trace, keyMetadata, configuration.TreeInstanceId);
        }

        private string ResolveTreePath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath)) throw new InvalidOperationException("Behavior-case tree paths must be relative.");
            var fullPath = Path.GetFullPath(Path.Combine(_treeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = _treeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Behavior-case tree path escapes its fixture root.");
            return fullPath;
        }

        private static IReadOnlyList<ReferenceBlackboardInitialValue> CreateInitialValues(
            IReadOnlyDictionary<string, BehaviorCaseValue> values)
        {
            var result = new List<ReferenceBlackboardInitialValue>(values.Count);
            foreach (var pair in values)
            {
                var stableKeyId = StableHash.Fnv1A64(pair.Key);
                result.Add(pair.Value.IsRegistered
                    ? ReferenceBlackboardInitialValue.Registered(
                        stableKeyId,
                        pair.Value.RegisteredTypeId,
                        pair.Value.RegisteredTypeVersion,
                        pair.Value.CopyRegisteredBytes())
                    : ReferenceBlackboardInitialValue.BuiltIn(stableKeyId, pair.Value.BuiltInValue));
            }
            return result.AsReadOnly();
        }

        private static string NormalizeSourceId(string path) => path.Replace('\\', '/');

        private static string Diagnostics(DiagnosticCollection diagnostics)
            => string.Join(" | ", diagnostics.Select(item => item.Code + " "
                + item.Location.JsonPointer + ": " + item.Message));
    }

    internal readonly struct SourceBlackboardKey
    {
        internal SourceBlackboardKey(string name, string enumContract)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            EnumContract = enumContract;
        }

        internal string Name { get; }
        internal string EnumContract { get; }
    }

    internal sealed class ReferenceBehaviorCaseExecutor : IBehaviorCaseExecutor
    {
        private readonly ReferenceExecutionMachine _machine;
        private readonly CollectingTraceSink _trace;
        private readonly IReadOnlyDictionary<ulong, SourceBlackboardKey> _keyMetadata;
        private readonly TreeInstanceId _treeInstanceId;

        internal ReferenceBehaviorCaseExecutor(
            ReferenceExecutionMachine machine,
            CollectingTraceSink trace,
            IReadOnlyDictionary<ulong, SourceBlackboardKey> keyMetadata,
            TreeInstanceId treeInstanceId)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _trace = trace ?? throw new ArgumentNullException(nameof(trace));
            _keyMetadata = keyMetadata ?? throw new ArgumentNullException(nameof(keyMetadata));
            _treeInstanceId = treeInstanceId;
        }

        public BehaviorCaseExecutorStepResult Execute(BehaviorCaseStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            ReferenceExecutionEnvelope envelope;
            switch (step)
            {
                case BehaviorCaseUpdateStep update:
                    if (update.Events.Count != 0)
                        throw new NotSupportedException("Phase 1 reference execution does not consume external events.");
                    envelope = _machine.Update(
                        Context(update.UpdateId, update.SnapshotRevision, update.TimeMicroseconds, update.Completions),
                        Budget(update.StepBudget));
                    break;
                case BehaviorCaseResumeStep resume:
                    envelope = _machine.Resume(Budget(resume.StepBudget));
                    break;
                case BehaviorCaseAbortStep abort:
                    envelope = ExecuteAbort(abort);
                    break;
                case BehaviorCaseControlStep _:
                    envelope = _machine.Restart();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }

            var inspection = _machine.CaptureInspection();
            return new BehaviorCaseExecutorStepResult(
                Map(envelope.Progress),
                envelope.RootResult,
                envelope.SegmentSteps,
                Snapshot(inspection.Blackboard),
                Commands(envelope.Commands),
                _trace.Take(),
                envelope.Diagnostics,
                inspection.ActiveOperationCount,
                inspection.ActiveNodeCount);
        }

        public void Dispose() { }

        private ReferenceExecutionEnvelope ExecuteAbort(BehaviorCaseAbortStep abort)
        {
            var context = Context(
                abort.UpdateId,
                abort.SnapshotRevision,
                abort.TimeMicroseconds,
                abort.Completions);
            return _machine.Abort(
                context,
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0),
                Budget(abort.StepBudget));
        }

        private static ReferenceUpdateContext Context(
            ulong updateId,
            Revision revision,
            long timeMicroseconds,
            IReadOnlyList<BehaviorCaseCompletion> completions)
            => new ReferenceUpdateContext(
                updateId,
                revision,
                timeMicroseconds,
                CompletionBatch(completions));

        private static CompletionBatch CompletionBatch(IReadOnlyList<BehaviorCaseCompletion> source)
        {
            if (source.Count == 0) return AIBT.CompletionBatch.Empty;
            var records = new List<CompletionRecord>(source.Count);
            var payload = new List<byte>();
            for (var index = 0; index < source.Count; index++)
            {
                var item = source[index];
                var bytes = item.Payload?.CopyRegisteredBytes() ?? Array.Empty<byte>();
                var offset = bytes.Length == 0 ? 0u : checked((uint)payload.Count);
                payload.AddRange(bytes);
                records.Add(new CompletionRecord(
                    item.OperationId,
                    item.Outcome,
                    item.Payload == null
                        ? default
                        : new CompletionPayloadType(item.Payload.RegisteredTypeId, item.Payload.RegisteredTypeVersion),
                    offset,
                    checked((uint)bytes.Length),
                    item.SourceId,
                    item.SourceSequence,
                    item.SnapshotRevision));
            }
            return new CompletionBatch(records, payload);
        }

        private static ReferenceStepBudget Budget(ulong? value)
            => value.HasValue ? ReferenceStepBudget.Limited(value.Value) : ReferenceStepBudget.Unlimited;

        private IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> Snapshot(
            ReferenceBlackboardSnapshot snapshot)
        {
            var values = new SortedDictionary<string, BehaviorCaseObservedBlackboardValue>(
                AIBT.Authoring.Utf8OrdinalComparer.Instance);
            for (var index = 0; index < snapshot.Entries.Count; index++)
            {
                var entry = snapshot.Entries[index];
                if (!_keyMetadata.TryGetValue(entry.StableKeyId, out var key))
                    throw new InvalidOperationException("Compiled blackboard key has no source identity.");
                BehaviorCaseValue value;
                if (entry.IsRegistered)
                {
                    value = BehaviorCaseValue.Registered(
                        entry.Type.TypeId,
                        entry.Type.Version,
                        entry.CopyRegisteredBytes());
                }
                else if (entry.BuiltInValue.Type == BlackboardValueType.Enum32)
                {
                    if (string.IsNullOrEmpty(key.EnumContract)
                        || !entry.BuiltInValue.TryGetEnum32(out var enumValue)
                        || enumValue.ContractTypeId != StableHash.Fnv1A64(key.EnumContract))
                        throw new InvalidOperationException("Compiled Enum32 value does not match its source contract.");
                    value = BehaviorCaseValue.Enum32(key.EnumContract, enumValue.Value);
                }
                else
                {
                    value = BehaviorCaseValue.BuiltIn(entry.BuiltInValue);
                }
                values.Add(key.Name, new BehaviorCaseObservedBlackboardValue(value, entry.Version));
            }
            return new ReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue>(values);
        }

        private static IReadOnlyList<BehaviorCaseCommandExpectation> Commands(CommandBatch batch)
        {
            var result = new BehaviorCaseCommandExpectation[batch.Records.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var record = batch.Records[index];
                result[index] = new BehaviorCaseCommandExpectation(
                    record.Phase,
                    record.CommandType,
                    record.TreeInstanceId,
                    record.Sequence,
                    record.OperationId.IsValid ? (OperationId?)record.OperationId : null,
                    BehaviorCaseValue.Registered(
                        record.CommandType.TypeId,
                        record.CommandType.Version,
                        Copy(batch.GetPayload(record))));
            }
            return Array.AsReadOnly(result);
        }

        private static byte[] Copy(ReadOnlySpan<byte> value)
        {
            var result = new byte[value.Length];
            value.CopyTo(result);
            return result;
        }

        private static BehaviorCaseProgress Map(ReferenceExecutionProgress value)
        {
            switch (value)
            {
                case ReferenceExecutionProgress.Completed: return BehaviorCaseProgress.Completed;
                case ReferenceExecutionProgress.Waiting: return BehaviorCaseProgress.Waiting;
                case ReferenceExecutionProgress.Suspended: return BehaviorCaseProgress.Suspended;
                case ReferenceExecutionProgress.Rejected: return BehaviorCaseProgress.Rejected;
                case ReferenceExecutionProgress.Faulted: return BehaviorCaseProgress.Faulted;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }

    internal sealed class CollectingTraceSink : IReferenceTraceSink
    {
        private readonly List<ReferenceTraceRecord> _records = new List<ReferenceTraceRecord>();

        public void Record(in ReferenceTraceRecord record) => _records.Add(record);

        internal IReadOnlyList<BehaviorCaseObservedTrace> Take()
        {
            var result = new BehaviorCaseObservedTrace[_records.Count];
            for (var index = 0; index < result.Length; index++) result[index] = Convert(_records[index]);
            _records.Clear();
            return Array.AsReadOnly(result);
        }

        private static BehaviorCaseObservedTrace Convert(ReferenceTraceRecord record)
            => new BehaviorCaseObservedTrace(
                Map(record.Kind),
                record.TraceFormatVersion,
                record.TreeSemanticHash,
                record.TreeInstanceId,
                record.Sequence,
                record.UpdateId,
                record.SnapshotRevision,
                record.NodeIndex.IsValid ? (RuntimeNodeIndex?)record.NodeIndex : null,
                record.Status,
                record.ExitReason,
                record.AbortReason,
                record.SourceNodeIndex.IsValid ? (RuntimeNodeIndex?)record.SourceNodeIndex : null,
                record.DiagnosticCode.IsValid ? (DiagnosticCode?)record.DiagnosticCode : null,
                record.StableBlackboardKeyId == 0 ? null : (ulong?)record.StableBlackboardKeyId,
                record.OldBlackboardVersion,
                record.NewBlackboardVersion);

        private static BehaviorCaseTraceEvent Map(ReferenceTraceEventKind value)
        {
            switch (value)
            {
                case ReferenceTraceEventKind.UpdateStarted: return BehaviorCaseTraceEvent.UpdateStarted;
                case ReferenceTraceEventKind.UpdateCompleted: return BehaviorCaseTraceEvent.UpdateCompleted;
                case ReferenceTraceEventKind.NodeEntered: return BehaviorCaseTraceEvent.NodeEntered;
                case ReferenceTraceEventKind.NodeTicked: return BehaviorCaseTraceEvent.NodeTicked;
                case ReferenceTraceEventKind.NodeAbortStarted: return BehaviorCaseTraceEvent.NodeAbortStarted;
                case ReferenceTraceEventKind.NodeExited: return BehaviorCaseTraceEvent.NodeExited;
                case ReferenceTraceEventKind.BlackboardChanged: return BehaviorCaseTraceEvent.BlackboardChanged;
                case ReferenceTraceEventKind.ObserverQueued: return BehaviorCaseTraceEvent.ObserverQueued;
                case ReferenceTraceEventKind.ObserverEvaluated: return BehaviorCaseTraceEvent.ObserverEvaluated;
                case ReferenceTraceEventKind.CommandEmitted: return BehaviorCaseTraceEvent.CommandEmitted;
                case ReferenceTraceEventKind.CompletionConsumed: return BehaviorCaseTraceEvent.CompletionConsumed;
                case ReferenceTraceEventKind.CompletionDiscarded: return BehaviorCaseTraceEvent.CompletionDiscarded;
                case ReferenceTraceEventKind.DiagnosticRaised: return BehaviorCaseTraceEvent.DiagnosticRaised;
                case ReferenceTraceEventKind.BudgetYielded: return BehaviorCaseTraceEvent.BudgetYielded;
                case ReferenceTraceEventKind.ExecutionResumed: return BehaviorCaseTraceEvent.ExecutionResumed;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}
