using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    public enum CompletionOutcome : byte
    {
        Succeeded = 0,
        Failed = 1,
        Cancelled = 2,
    }

    public readonly struct CompletionPayloadType : IEquatable<CompletionPayloadType>
    {
        public CompletionPayloadType(ulong typeId, uint version)
        {
            if (typeId == 0) throw new ArgumentOutOfRangeException(nameof(typeId));
            if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
            TypeId = typeId;
            Version = version;
        }

        public ulong TypeId { get; }
        public uint Version { get; }
        public bool IsValid => TypeId != 0 && Version != 0;
        public bool Equals(CompletionPayloadType other) => TypeId == other.TypeId && Version == other.Version;
        public override bool Equals(object obj) => obj is CompletionPayloadType other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (TypeId.GetHashCode() * 397) ^ (int)Version; }
        }

        public static bool operator ==(CompletionPayloadType left, CompletionPayloadType right) => left.Equals(right);
        public static bool operator !=(CompletionPayloadType left, CompletionPayloadType right) => !left.Equals(right);
    }

    public readonly struct CompletionRecord : IEquatable<CompletionRecord>
    {
        public CompletionRecord(
            OperationId operationId,
            CompletionOutcome outcome,
            CompletionPayloadType payloadType,
            uint payloadOffset,
            uint payloadSize,
            ulong sourceId,
            ulong sourceSequence,
            Revision snapshotRevision)
        {
            if (!operationId.IsValid) throw new ArgumentException("A valid operation ID is required.", nameof(operationId));
            if (!Enum.IsDefined(typeof(CompletionOutcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
            if (sourceId == 0) throw new ArgumentOutOfRangeException(nameof(sourceId));
            if (payloadType.IsValid)
            {
                if (payloadSize == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(payloadSize), "A typed completion payload cannot be empty.");
                }
            }
            else if (payloadOffset != 0 || payloadSize != 0)
            {
                throw new ArgumentException("An absent completion payload type cannot declare payload bytes.", nameof(payloadType));
            }

            if ((ulong)payloadOffset + payloadSize > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadSize), "The completion payload range exceeds 32 bits.");
            }

            OperationId = operationId;
            Outcome = outcome;
            PayloadType = payloadType;
            PayloadOffset = payloadOffset;
            PayloadSize = payloadSize;
            SourceId = sourceId;
            SourceSequence = sourceSequence;
            SnapshotRevision = snapshotRevision;
        }

        public OperationId OperationId { get; }
        public CompletionOutcome Outcome { get; }
        public CompletionPayloadType PayloadType { get; }
        public uint PayloadOffset { get; }
        public uint PayloadSize { get; }
        public ulong SourceId { get; }
        public ulong SourceSequence { get; }
        public Revision SnapshotRevision { get; }

        public bool Equals(CompletionRecord other)
        {
            return OperationId == other.OperationId
                && Outcome == other.Outcome
                && PayloadType == other.PayloadType
                && PayloadOffset == other.PayloadOffset
                && PayloadSize == other.PayloadSize
                && SourceId == other.SourceId
                && SourceSequence == other.SourceSequence
                && SnapshotRevision == other.SnapshotRevision;
        }

        public override bool Equals(object obj) => obj is CompletionRecord other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = OperationId.GetHashCode();
                hash = (hash * 397) ^ (int)Outcome;
                hash = (hash * 397) ^ PayloadType.GetHashCode();
                hash = (hash * 397) ^ (int)PayloadOffset;
                hash = (hash * 397) ^ (int)PayloadSize;
                hash = (hash * 397) ^ SourceId.GetHashCode();
                hash = (hash * 397) ^ SourceSequence.GetHashCode();
                return (hash * 397) ^ SnapshotRevision.GetHashCode();
            }
        }

        public static bool operator ==(CompletionRecord left, CompletionRecord right) => left.Equals(right);
        public static bool operator !=(CompletionRecord left, CompletionRecord right) => !left.Equals(right);
    }

    public sealed class CompletionBatch
    {
        private static readonly CompletionBatch EmptyInstance = new CompletionBatch(Array.Empty<CompletionRecord>(), Array.Empty<byte>());
        private readonly ReadOnlyCollection<CompletionRecord> _records;
        private readonly byte[] _payload;

        public CompletionBatch(IEnumerable<CompletionRecord> records, IEnumerable<byte> payload)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var recordArray = Copy(records);
            _payload = Copy(payload);
            for (var index = 0; index < recordArray.Length; index++)
            {
                var record = recordArray[index];
                if ((ulong)record.PayloadOffset + record.PayloadSize > (ulong)_payload.Length)
                {
                    throw new ArgumentException("A completion payload range is outside the batch payload.", nameof(records));
                }
            }

            _records = Array.AsReadOnly(recordArray);
        }

        public static CompletionBatch Empty => EmptyInstance;
        public IReadOnlyList<CompletionRecord> Records => _records;
        public int PayloadSize => _payload.Length;

        public byte GetPayloadByte(int index)
        {
            if ((uint)index >= (uint)_payload.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return _payload[index];
        }

        public ReadOnlySpan<byte> GetPayload(in CompletionRecord record)
        {
            return new ReadOnlySpan<byte>(_payload, checked((int)record.PayloadOffset), checked((int)record.PayloadSize));
        }

        private static T[] Copy<T>(IEnumerable<T> source)
        {
            if (source is ICollection<T> collection)
            {
                var result = new T[collection.Count];
                collection.CopyTo(result, 0);
                return result;
            }

            return new List<T>(source).ToArray();
        }
    }
}
