using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    public enum CommandPhase : byte
    {
        Execute = 0,
        Cancel = 1,
    }

    public readonly struct CommandType : IEquatable<CommandType>
    {
        public CommandType(ulong typeId, uint version)
        {
            if (typeId == 0) throw new ArgumentOutOfRangeException(nameof(typeId));
            if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
            TypeId = typeId;
            Version = version;
        }

        public ulong TypeId { get; }
        public uint Version { get; }
        public bool IsValid => TypeId != 0 && Version != 0;
        public bool Equals(CommandType other) => TypeId == other.TypeId && Version == other.Version;
        public override bool Equals(object obj) => obj is CommandType other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (TypeId.GetHashCode() * 397) ^ (int)Version; }
        }

        public static bool operator ==(CommandType left, CommandType right) => left.Equals(right);
        public static bool operator !=(CommandType left, CommandType right) => !left.Equals(right);
    }

    public readonly struct CommandRecord : IEquatable<CommandRecord>
    {
        public CommandRecord(
            CommandType commandType,
            OperationId operationId,
            uint payloadOffset,
            uint payloadSize,
            CommandPhase phase,
            TreeInstanceId treeInstanceId,
            ulong sequence)
        {
            if (!commandType.IsValid) throw new ArgumentException("A command type is required.", nameof(commandType));
            if (!Enum.IsDefined(typeof(CommandPhase), phase)) throw new ArgumentOutOfRangeException(nameof(phase));
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (payloadSize == 0 && payloadOffset != 0)
            {
                throw new ArgumentException("An empty command payload must use offset zero.", nameof(payloadOffset));
            }

            if ((ulong)payloadOffset + payloadSize > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadSize), "The command payload range exceeds 32 bits.");
            }

            CommandType = commandType;
            OperationId = operationId;
            PayloadOffset = payloadOffset;
            PayloadSize = payloadSize;
            Phase = phase;
            TreeInstanceId = treeInstanceId;
            Sequence = sequence;
        }

        public CommandType CommandType { get; }
        public OperationId OperationId { get; }
        public uint PayloadOffset { get; }
        public uint PayloadSize { get; }
        public CommandPhase Phase { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public ulong Sequence { get; }

        public bool Equals(CommandRecord other)
        {
            return CommandType == other.CommandType
                && OperationId == other.OperationId
                && PayloadOffset == other.PayloadOffset
                && PayloadSize == other.PayloadSize
                && Phase == other.Phase
                && TreeInstanceId == other.TreeInstanceId
                && Sequence == other.Sequence;
        }

        public override bool Equals(object obj) => obj is CommandRecord other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = CommandType.GetHashCode();
                hash = (hash * 397) ^ OperationId.GetHashCode();
                hash = (hash * 397) ^ (int)PayloadOffset;
                hash = (hash * 397) ^ (int)PayloadSize;
                hash = (hash * 397) ^ (int)Phase;
                hash = (hash * 397) ^ TreeInstanceId.GetHashCode();
                return (hash * 397) ^ Sequence.GetHashCode();
            }
        }

        public static bool operator ==(CommandRecord left, CommandRecord right) => left.Equals(right);
        public static bool operator !=(CommandRecord left, CommandRecord right) => !left.Equals(right);
    }

    public sealed class CommandBatch
    {
        private static readonly CommandBatch EmptyInstance = new CommandBatch(Array.Empty<CommandRecord>(), Array.Empty<byte>());
        private readonly ReadOnlyCollection<CommandRecord> _records;
        private readonly byte[] _payload;

        public CommandBatch(IEnumerable<CommandRecord> records, IEnumerable<byte> payload)
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
                    throw new ArgumentException("A command payload range is outside the batch payload.", nameof(records));
                }
            }

            _records = Array.AsReadOnly(recordArray);
        }

        public static CommandBatch Empty => EmptyInstance;
        public IReadOnlyList<CommandRecord> Records => _records;
        public int PayloadSize => _payload.Length;

        public byte GetPayloadByte(int index)
        {
            if ((uint)index >= (uint)_payload.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return _payload[index];
        }

        public ReadOnlySpan<byte> GetPayload(in CommandRecord record)
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
