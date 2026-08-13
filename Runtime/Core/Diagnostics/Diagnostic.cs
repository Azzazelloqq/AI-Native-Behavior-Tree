using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    public sealed class Diagnostic : IEquatable<Diagnostic>, IComparable<Diagnostic>
    {
        private readonly ReadOnlyCollection<DiagnosticLocation> _relatedLocations;

        public Diagnostic(
            DiagnosticCode code,
            DiagnosticSeverity severity,
            string message,
            DiagnosticLocation location = default,
            IEnumerable<DiagnosticLocation> relatedLocations = null)
        {
            if (!code.IsValid)
            {
                throw new ArgumentException("A valid diagnostic code is required.", nameof(code));
            }

            if (!Enum.IsDefined(typeof(DiagnosticSeverity), severity))
            {
                throw new ArgumentOutOfRangeException(nameof(severity));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A diagnostic message is required.", nameof(message));
            }

            Code = code;
            Severity = severity;
            Message = message;
            Location = location;

            var locations = relatedLocations == null
                ? Array.Empty<DiagnosticLocation>()
                : new List<DiagnosticLocation>(relatedLocations).ToArray();
            Array.Sort(locations);
            _relatedLocations = Array.AsReadOnly(locations);
        }

        public DiagnosticCode Code { get; }

        public DiagnosticSeverity Severity { get; }

        public string Message { get; }

        public DiagnosticLocation Location { get; }

        public IReadOnlyList<DiagnosticLocation> RelatedLocations => _relatedLocations;

        public int CompareTo(Diagnostic other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            var result = Severity.CompareTo(other.Severity);
            if (result != 0)
            {
                return result;
            }

            result = Code.CompareTo(other.Code);
            if (result != 0)
            {
                return result;
            }

            result = Location.CompareTo(other.Location);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(Message, other.Message, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            var sharedLength = Math.Min(_relatedLocations.Count, other._relatedLocations.Count);
            for (var index = 0; index < sharedLength; index++)
            {
                result = _relatedLocations[index].CompareTo(other._relatedLocations[index]);
                if (result != 0)
                {
                    return result;
                }
            }

            return _relatedLocations.Count.CompareTo(other._relatedLocations.Count);
        }

        public bool Equals(Diagnostic other)
        {
            if (ReferenceEquals(other, null) || Code != other.Code || Severity != other.Severity
                || !string.Equals(Message, other.Message, StringComparison.Ordinal)
                || Location != other.Location || _relatedLocations.Count != other._relatedLocations.Count)
            {
                return false;
            }

            for (var index = 0; index < _relatedLocations.Count; index++)
            {
                if (_relatedLocations[index] != other._relatedLocations[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Diagnostic);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Code.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Severity;
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Message);
                hashCode = (hashCode * 397) ^ Location.GetHashCode();
                for (var index = 0; index < _relatedLocations.Count; index++)
                {
                    hashCode = (hashCode * 397) ^ _relatedLocations[index].GetHashCode();
                }

                return hashCode;
            }
        }
    }
}
