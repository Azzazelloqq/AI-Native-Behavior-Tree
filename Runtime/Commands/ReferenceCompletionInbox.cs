using System;
using System.Collections.Generic;

namespace AIBT
{
    internal readonly struct ReferenceCompletionExpectation
    {
        private ReferenceCompletionExpectation(byte mode, CompletionPayloadType payloadType, uint payloadSize)
        {
            Mode = mode;
            PayloadType = payloadType;
            PayloadSize = payloadSize;
        }

        private byte Mode { get; }
        private CompletionPayloadType PayloadType { get; }
        private uint PayloadSize { get; }

        internal static ReferenceCompletionExpectation Any => default;
        internal static ReferenceCompletionExpectation NoPayload => new ReferenceCompletionExpectation(1, default, 0);

        internal static ReferenceCompletionExpectation Typed(CompletionPayloadType payloadType, uint payloadSize)
        {
            if (!payloadType.IsValid) throw new ArgumentException("A completion payload type is required.", nameof(payloadType));
            if (payloadSize == 0) throw new ArgumentOutOfRangeException(nameof(payloadSize));
            return new ReferenceCompletionExpectation(2, payloadType, payloadSize);
        }

        internal bool Matches(in CompletionRecord record)
        {
            if (Mode == 0) return true;
            if (Mode == 1) return !record.PayloadType.IsValid && record.PayloadSize == 0;
            return record.PayloadType == PayloadType && record.PayloadSize == PayloadSize;
        }
    }

    internal readonly ref struct ReferenceCompletionView
    {
        private readonly byte[] _payload;

        internal ReferenceCompletionView(CompletionRecord record, byte[] payload)
        {
            Record = record;
            _payload = payload ?? Array.Empty<byte>();
        }

        internal CompletionRecord Record { get; }
        internal ReadOnlySpan<byte> Payload => _payload;
    }

    internal sealed class ReferenceCompletionInbox
    {
        private readonly ReferenceOperationLedger _ledger;
        private readonly Dictionary<ulong, ulong> _sourceHighWater = new Dictionary<ulong, ulong>();
        private readonly List<PendingCompletion> _pending = new List<PendingCompletion>();

        internal ReferenceCompletionInbox(ReferenceOperationLedger ledger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        internal int PendingCount => _pending.Count;

        internal DiagnosticCollection Normalize(CompletionBatch batch, IReadOnlyList<uint> activationGenerations)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (activationGenerations == null) throw new ArgumentNullException(nameof(activationGenerations));
            var diagnostics = new List<Diagnostic>();
            var records = new List<CompletionRecord>(batch.Records.Count);
            for (var index = 0; index < batch.Records.Count; index++) records.Add(batch.Records[index]);
            records.Sort(CompletionOrderingComparer.Instance);

            var cursor = 0;
            while (cursor < records.Count)
            {
                var groupEnd = cursor + 1;
                while (groupEnd < records.Count && HasSameOrderingKey(records[cursor], records[groupEnd])) groupEnd++;
                if (groupEnd - cursor > 1)
                {
                    diagnostics.Add(Create(
                        CommandAsyncDiagnosticCodes.DuplicateCompletionOrderingKey,
                        "Conflicting completion records use the same source ID and source sequence; the whole group was discarded."));
                    cursor = groupEnd;
                    continue;
                }

                var record = records[cursor++];
                if (_sourceHighWater.TryGetValue(record.SourceId, out var highWater)
                    && record.SourceSequence <= highWater)
                {
                    diagnostics.Add(Create(
                        CommandAsyncDiagnosticCodes.NonIncreasingSourceSequence,
                        "A completion source sequence did not increase across normalized inputs."));
                    continue;
                }

                _sourceHighWater[record.SourceId] = record.SourceSequence;
                if (!_ledger.TryGetState(record.OperationId, out var state))
                {
                    diagnostics.Add(Create(
                        CommandAsyncDiagnosticCodes.UnknownOperation,
                        "A completion references an operation that was never issued by this tree instance."));
                    continue;
                }

                if (state == ReferenceOperationState.Cancelled)
                {
                    diagnostics.Add(Create(
                        CommandAsyncDiagnosticCodes.CancelledOperation,
                        "A completion for a cancelled operation was discarded."));
                    continue;
                }

                if (state == ReferenceOperationState.Consumed)
                {
                    diagnostics.Add(Create(
                        CommandAsyncDiagnosticCodes.AlreadyConsumedOperation,
                        "A completion for an already-consumed operation was discarded."));
                    continue;
                }

                var nodeValue = record.OperationId.NodeIndex.Value;
                if (nodeValue >= activationGenerations.Count
                    || activationGenerations[(int)nodeValue] != record.OperationId.ActivationGeneration)
                {
                    _ledger.MarkCancelled(record.OperationId);
                    diagnostics.Add(Create(
                        CommandAsyncDiagnosticCodes.StaleOperationGeneration,
                        "A completion for an inactive activation generation was discarded."));
                    continue;
                }

                _pending.Add(new PendingCompletion(record, Copy(batch.GetPayload(record))));
            }

            _pending.Sort(PendingCompletionComparer.Instance);
            return diagnostics.Count == 0 ? DiagnosticCollection.Empty : new DiagnosticCollection(diagnostics);
        }

        internal bool TryConsume(
            OperationId operationId,
            out ReferenceCompletionView completion,
            out DiagnosticCollection diagnostics)
        {
            return TryConsume(operationId, ReferenceCompletionExpectation.Any, out completion, out diagnostics);
        }

        internal bool TryConsume(
            OperationId operationId,
            ReferenceCompletionExpectation expectation,
            out ReferenceCompletionView completion,
            out DiagnosticCollection diagnostics)
        {
            completion = default;
            var discarded = new List<Diagnostic>();
            if (!_ledger.TryGetState(operationId, out var state))
            {
                diagnostics = One(
                    CommandAsyncDiagnosticCodes.UnknownOperation,
                    "A node attempted to consume an operation that was never issued by this tree instance.");
                return false;
            }

            if (state == ReferenceOperationState.Cancelled)
            {
                diagnostics = One(
                    CommandAsyncDiagnosticCodes.CancelledOperation,
                    "A node attempted to consume a cancelled operation.");
                return false;
            }

            if (state == ReferenceOperationState.Consumed)
            {
                diagnostics = One(
                    CommandAsyncDiagnosticCodes.AlreadyConsumedOperation,
                    "A node attempted to consume an already-consumed operation.");
                return false;
            }

            var match = -1;
            for (var index = 0; index < _pending.Count; index++)
            {
                if (_pending[index].Record.OperationId != operationId) continue;
                if (expectation.Matches(_pending[index].Record))
                {
                    match = index;
                    break;
                }

                _pending.RemoveAt(index--);
                discarded.Add(Create(
                    CommandAsyncDiagnosticCodes.CompletionPayloadMismatch,
                    "A matching completion used a payload type, version, or size outside the node contract."));
            }

            if (match < 0)
            {
                diagnostics = discarded.Count == 0 ? DiagnosticCollection.Empty : new DiagnosticCollection(discarded);
                return false;
            }

            var selected = _pending[match];
            _pending.RemoveAt(match);
            if (_ledger.MarkConsumed(operationId) != ReferenceOperationTransition.Applied)
            {
                throw new InvalidOperationException("The operation ledger changed while consuming a completion.");
            }

            for (var index = _pending.Count - 1; index >= 0; index--)
            {
                if (_pending[index].Record.OperationId != operationId) continue;
                _pending.RemoveAt(index);
                discarded.Add(Create(
                    CommandAsyncDiagnosticCodes.AlreadyConsumedOperation,
                    "A later completion for an operation consumed in the same normalized stream was discarded."));
            }

            completion = new ReferenceCompletionView(selected.Record, selected.Payload);
            diagnostics = discarded.Count == 0 ? DiagnosticCollection.Empty : new DiagnosticCollection(discarded);
            return true;
        }

        internal DiagnosticCollection DiscardCancelled(OperationId operationId)
        {
            var diagnostics = new List<Diagnostic>();
            for (var index = _pending.Count - 1; index >= 0; index--)
            {
                if (_pending[index].Record.OperationId != operationId) continue;
                _pending.RemoveAt(index);
                diagnostics.Add(Create(
                    CommandAsyncDiagnosticCodes.CancelledOperation,
                    "A pending completion was discarded because its operation was cancelled."));
            }

            return diagnostics.Count == 0 ? DiagnosticCollection.Empty : new DiagnosticCollection(diagnostics);
        }

        private DiagnosticCollection One(DiagnosticCode code, string message)
        {
            return new DiagnosticCollection(new[] { Create(code, message) });
        }

        private Diagnostic Create(DiagnosticCode code, string message)
        {
            return CommandAsyncDiagnostics.Create(code, message, _ledger.TreeInstanceId);
        }

        private static bool HasSameOrderingKey(CompletionRecord left, CompletionRecord right)
        {
            return left.SourceId == right.SourceId && left.SourceSequence == right.SourceSequence;
        }

        private static byte[] Copy(ReadOnlySpan<byte> source)
        {
            var result = new byte[source.Length];
            source.CopyTo(result);
            return result;
        }

        private sealed class PendingCompletion
        {
            internal PendingCompletion(CompletionRecord source, byte[] payload)
            {
                Payload = payload;
                Record = new CompletionRecord(
                    source.OperationId,
                    source.Outcome,
                    source.PayloadType,
                    0,
                    checked((uint)payload.Length),
                    source.SourceId,
                    source.SourceSequence,
                    source.SnapshotRevision);
            }

            internal CompletionRecord Record { get; }
            internal byte[] Payload { get; }
        }

        private sealed class CompletionOrderingComparer : IComparer<CompletionRecord>
        {
            internal static readonly CompletionOrderingComparer Instance = new CompletionOrderingComparer();
            public int Compare(CompletionRecord left, CompletionRecord right)
            {
                var result = left.SourceId.CompareTo(right.SourceId);
                return result != 0 ? result : left.SourceSequence.CompareTo(right.SourceSequence);
            }
        }

        private sealed class PendingCompletionComparer : IComparer<PendingCompletion>
        {
            internal static readonly PendingCompletionComparer Instance = new PendingCompletionComparer();
            public int Compare(PendingCompletion left, PendingCompletion right)
            {
                return CompletionOrderingComparer.Instance.Compare(left.Record, right.Record);
            }
        }
    }
}
