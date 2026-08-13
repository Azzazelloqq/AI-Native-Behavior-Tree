using System;
using System.Collections.Generic;

namespace AIBT
{
    internal sealed class ReferenceCommandBuffer
    {
        private readonly TreeInstanceId _treeInstanceId;
        private readonly List<CommandRecord> _records = new List<CommandRecord>();
        private readonly List<byte> _payload = new List<byte>();
        private ulong _nextSequence;
        private bool _sequenceExhausted;

        internal ReferenceCommandBuffer(TreeInstanceId treeInstanceId, ulong firstSequence = 1)
        {
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            if (firstSequence == 0) throw new ArgumentOutOfRangeException(nameof(firstSequence));
            _treeInstanceId = treeInstanceId;
            _nextSequence = firstSequence;
        }

        internal bool CanAppend => !_sequenceExhausted;
        internal TreeInstanceId TreeInstanceId => _treeInstanceId;

        internal bool TryAppend(
            CommandType commandType,
            OperationId operationId,
            CommandPhase phase,
            ReadOnlySpan<byte> payload,
            out CommandRecord record,
            out Diagnostic diagnostic)
        {
            record = default;
            diagnostic = null;
            if (!commandType.IsValid || !Enum.IsDefined(typeof(CommandPhase), phase))
            {
                diagnostic = CommandAsyncDiagnostics.Create(
                    CommandAsyncDiagnosticCodes.InvalidCommand,
                    "A command requires a known phase and nonzero type ID and version.",
                    _treeInstanceId);
                return false;
            }

            if (operationId.IsValid && operationId.TreeInstanceId != _treeInstanceId)
            {
                diagnostic = CommandAsyncDiagnostics.Create(
                    CommandAsyncDiagnosticCodes.InvalidCommand,
                    "A command operation ID belongs to another tree instance.",
                    _treeInstanceId);
                return false;
            }

            if (_sequenceExhausted)
            {
                diagnostic = CommandAsyncDiagnostics.Create(
                    CommandAsyncDiagnosticCodes.CommandSequenceOverflow,
                    "The per-instance command sequence is exhausted and cannot advance without wrapping.",
                    _treeInstanceId);
                return false;
            }

            if ((ulong)_payload.Count + (ulong)payload.Length > uint.MaxValue)
            {
                diagnostic = CommandAsyncDiagnostics.Create(
                    CommandAsyncDiagnosticCodes.InvalidCommand,
                    "The command payload buffer exceeds its 32-bit range.",
                    _treeInstanceId);
                return false;
            }

            var payloadOffset = payload.Length == 0 ? 0u : checked((uint)_payload.Count);
            for (var index = 0; index < payload.Length; index++) _payload.Add(payload[index]);
            record = new CommandRecord(
                commandType,
                operationId,
                payloadOffset,
                checked((uint)payload.Length),
                phase,
                _treeInstanceId,
                _nextSequence);
            _records.Add(record);
            if (_nextSequence == ulong.MaxValue)
            {
                _sequenceExhausted = true;
            }
            else
            {
                _nextSequence++;
            }

            return true;
        }

        internal CommandBatch TakeBatch()
        {
            if (_records.Count == 0) return CommandBatch.Empty;
            var batch = new CommandBatch(_records, _payload);
            _records.Clear();
            _payload.Clear();
            return batch;
        }
    }

    public static class CommandBatchMerger
    {
        public static CommandBatch Merge(IEnumerable<CommandBatch> batches)
        {
            if (batches == null) throw new ArgumentNullException(nameof(batches));
            var entries = new List<Entry>();
            foreach (var batch in batches)
            {
                if (batch == null) throw new ArgumentException("Command batches cannot contain null.", nameof(batches));
                for (var index = 0; index < batch.Records.Count; index++)
                {
                    var record = batch.Records[index];
                    entries.Add(new Entry(record, Copy(batch.GetPayload(record))));
                }
            }

            entries.Sort(EntryComparer.Instance);
            var sequences = new HashSet<InstanceSequence>();
            for (var index = 0; index < entries.Count; index++)
            {
                var record = entries[index].Record;
                if (!sequences.Add(new InstanceSequence(record.TreeInstanceId, record.Sequence)))
                {
                    throw new ArgumentException("A tree instance cannot publish two commands with the same sequence.", nameof(batches));
                }
            }

            var records = new List<CommandRecord>(entries.Count);
            var payload = new List<byte>();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var offset = entry.Payload.Length == 0 ? 0u : checked((uint)payload.Count);
                payload.AddRange(entry.Payload);
                records.Add(new CommandRecord(
                    entry.Record.CommandType,
                    entry.Record.OperationId,
                    offset,
                    checked((uint)entry.Payload.Length),
                    entry.Record.Phase,
                    entry.Record.TreeInstanceId,
                    entry.Record.Sequence));
            }

            return records.Count == 0 ? CommandBatch.Empty : new CommandBatch(records, payload);
        }

        private static byte[] Copy(ReadOnlySpan<byte> source)
        {
            var result = new byte[source.Length];
            source.CopyTo(result);
            return result;
        }

        private readonly struct Entry
        {
            internal Entry(CommandRecord record, byte[] payload)
            {
                Record = record;
                Payload = payload;
            }

            internal CommandRecord Record { get; }
            internal byte[] Payload { get; }
        }

        private readonly struct InstanceSequence : IEquatable<InstanceSequence>
        {
            internal InstanceSequence(TreeInstanceId treeInstanceId, ulong sequence)
            {
                TreeInstanceId = treeInstanceId;
                Sequence = sequence;
            }

            private TreeInstanceId TreeInstanceId { get; }
            private ulong Sequence { get; }
            public bool Equals(InstanceSequence other) => TreeInstanceId == other.TreeInstanceId && Sequence == other.Sequence;
            public override bool Equals(object obj) => obj is InstanceSequence other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (TreeInstanceId.GetHashCode() * 397) ^ Sequence.GetHashCode(); }
            }
        }

        private sealed class EntryComparer : IComparer<Entry>
        {
            internal static readonly EntryComparer Instance = new EntryComparer();
            public int Compare(Entry left, Entry right)
            {
                var result = left.Record.Phase.CompareTo(right.Record.Phase);
                if (result != 0) return result;
                result = left.Record.TreeInstanceId.CompareTo(right.Record.TreeInstanceId);
                return result != 0 ? result : left.Record.Sequence.CompareTo(right.Record.Sequence);
            }
        }
    }
}
