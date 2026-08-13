using System;
using System.Globalization;

namespace AIBT
{
    public readonly struct OperationId : IEquatable<OperationId>
    {
        public OperationId(
            TreeInstanceId treeInstanceId,
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            ulong sequence)
        {
            TreeInstanceId = treeInstanceId;
            NodeIndex = nodeIndex;
            ActivationGeneration = activationGeneration;
            Sequence = sequence;
        }

        public TreeInstanceId TreeInstanceId { get; }

        public RuntimeNodeIndex NodeIndex { get; }

        public uint ActivationGeneration { get; }

        public ulong Sequence { get; }

        public bool IsValid => TreeInstanceId.IsValid && NodeIndex.IsValid;

        public static OperationId Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException(
                    "Operation IDs must contain four colon-separated unsigned decimal fields.");
            }

            return result;
        }

        public static bool TryParse(string value, out OperationId result)
        {
            result = default;
            if (value == null)
            {
                return false;
            }

            var parts = value.Split(':');
            if (parts.Length != 4
                || !TreeInstanceId.TryParse(parts[0], out var treeInstanceId)
                || !RuntimeNodeIndex.TryParse(parts[1], out var nodeIndex)
                || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
                || !ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            {
                return false;
            }

            result = new OperationId(treeInstanceId, nodeIndex, generation, sequence);
            return true;
        }

        public bool Equals(OperationId other)
        {
            return TreeInstanceId == other.TreeInstanceId
                && NodeIndex == other.NodeIndex
                && ActivationGeneration == other.ActivationGeneration
                && Sequence == other.Sequence;
        }

        public override bool Equals(object obj) => obj is OperationId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TreeInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ NodeIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)ActivationGeneration;
                hashCode = (hashCode * 397) ^ Sequence.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Concat(
                TreeInstanceId.ToString(), ":",
                NodeIndex.ToString(), ":",
                ActivationGeneration.ToString(CultureInfo.InvariantCulture), ":",
                Sequence.ToString(CultureInfo.InvariantCulture));
        }

        public static bool operator ==(OperationId left, OperationId right) => left.Equals(right);

        public static bool operator !=(OperationId left, OperationId right) => !left.Equals(right);
    }
}
