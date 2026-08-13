using System;

namespace AIBT
{
    public readonly struct DiagnosticLocation : IEquatable<DiagnosticLocation>, IComparable<DiagnosticLocation>
    {
        public DiagnosticLocation(
            string documentId = null,
            string jsonPointer = null,
            int? line = null,
            int? column = null,
            TreeId treeId = default,
            NodeId nodeId = default,
            TreeInstanceId treeInstanceId = default)
        {
            if (documentId != null && documentId.Length == 0)
            {
                throw new ArgumentException("Document IDs cannot be empty when present.", nameof(documentId));
            }

            if (!IsValidJsonPointer(jsonPointer))
            {
                throw new ArgumentException("JSON Pointer must use RFC 6901 syntax.", nameof(jsonPointer));
            }

            if (line.HasValue && line.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "Line numbers are one-based.");
            }

            if (column.HasValue && column.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(column), "Column numbers are one-based.");
            }

            DocumentId = documentId;
            JsonPointer = jsonPointer;
            Line = line;
            Column = column;
            TreeId = treeId;
            NodeId = nodeId;
            TreeInstanceId = treeInstanceId;
        }

        public string DocumentId { get; }

        public string JsonPointer { get; }

        public int? Line { get; }

        public int? Column { get; }

        public TreeId TreeId { get; }

        public NodeId NodeId { get; }

        public TreeInstanceId TreeInstanceId { get; }

        public bool HasDocumentId => DocumentId != null;

        public bool HasJsonPointer => JsonPointer != null;

        public bool IsKnown => HasDocumentId || HasJsonPointer || Line.HasValue || Column.HasValue
            || TreeId.IsValid || NodeId.IsValid || TreeInstanceId.IsValid;

        public int CompareTo(DiagnosticLocation other)
        {
            var result = CompareOptionalOrdinal(DocumentId, other.DocumentId);
            if (result != 0)
            {
                return result;
            }

            result = CompareOptionalOrdinal(JsonPointer, other.JsonPointer);
            if (result != 0)
            {
                return result;
            }

            result = CompareOptional(Line, other.Line);
            if (result != 0)
            {
                return result;
            }

            result = CompareOptional(Column, other.Column);
            if (result != 0)
            {
                return result;
            }

            result = CompareOptionalOrdinal(NodeId.IsValid ? NodeId.Value : null, other.NodeId.IsValid ? other.NodeId.Value : null);
            if (result != 0)
            {
                return result;
            }

            result = CompareOptionalOrdinal(TreeId.IsValid ? TreeId.Value : null, other.TreeId.IsValid ? other.TreeId.Value : null);
            if (result != 0)
            {
                return result;
            }

            return CompareOptional(
                TreeInstanceId.IsValid ? TreeInstanceId.Value : (ulong?)null,
                other.TreeInstanceId.IsValid ? other.TreeInstanceId.Value : (ulong?)null);
        }

        public bool Equals(DiagnosticLocation other)
        {
            return string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal)
                && string.Equals(JsonPointer, other.JsonPointer, StringComparison.Ordinal)
                && Line == other.Line
                && Column == other.Column
                && TreeId == other.TreeId
                && NodeId == other.NodeId
                && TreeInstanceId == other.TreeInstanceId;
        }

        public override bool Equals(object obj)
        {
            return obj is DiagnosticLocation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = DocumentId == null ? 0 : StringComparer.Ordinal.GetHashCode(DocumentId);
                hashCode = (hashCode * 397) ^ (JsonPointer == null ? 0 : StringComparer.Ordinal.GetHashCode(JsonPointer));
                hashCode = (hashCode * 397) ^ Line.GetHashCode();
                hashCode = (hashCode * 397) ^ Column.GetHashCode();
                hashCode = (hashCode * 397) ^ TreeId.GetHashCode();
                hashCode = (hashCode * 397) ^ NodeId.GetHashCode();
                hashCode = (hashCode * 397) ^ TreeInstanceId.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(DiagnosticLocation left, DiagnosticLocation right) => left.Equals(right);

        public static bool operator !=(DiagnosticLocation left, DiagnosticLocation right) => !left.Equals(right);

        private static int CompareOptionalOrdinal(string left, string right)
        {
            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            return right == null ? 1 : string.Compare(left, right, StringComparison.Ordinal);
        }

        private static int CompareOptional<T>(T? left, T? right)
            where T : struct, IComparable<T>
        {
            if (!left.HasValue)
            {
                return right.HasValue ? -1 : 0;
            }

            return !right.HasValue ? 1 : left.Value.CompareTo(right.Value);
        }

        private static bool IsValidJsonPointer(string value)
        {
            if (value == null || value.Length == 0)
            {
                return true;
            }

            if (value[0] != '/')
            {
                return false;
            }

            for (var index = 1; index < value.Length; index++)
            {
                if (value[index] != '~')
                {
                    continue;
                }

                if (++index >= value.Length || (value[index] != '0' && value[index] != '1'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
