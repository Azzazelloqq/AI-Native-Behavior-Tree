using System;

namespace AIBT.Authoring
{
    public sealed class SuggestedDiagnosticOperation : IEquatable<SuggestedDiagnosticOperation>, IComparable<SuggestedDiagnosticOperation>
    {
        private readonly byte[] _canonicalBytes;

        internal SuggestedDiagnosticOperation(DiagnosticOperationDescriptor descriptor, DiagnosticOperationPayload payload)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            descriptor.PayloadContract.Validate(payload);
            OperationId = descriptor.OperationId;
            PayloadType = descriptor.PayloadType;
            _canonicalBytes = CanonicalDiagnosticJsonWriter.SerializeOperationUtf8(this);
        }

        public string OperationId { get; }

        public string PayloadType { get; }

        public DiagnosticOperationPayload Payload { get; }

        public int CompareTo(SuggestedDiagnosticOperation other)
        {
            return other == null ? 1 : CanonicalBytes.Compare(_canonicalBytes, other._canonicalBytes);
        }

        public bool Equals(SuggestedDiagnosticOperation other)
        {
            return other != null && CanonicalBytes.Equals(_canonicalBytes, other._canonicalBytes);
        }

        public override bool Equals(object obj) => Equals(obj as SuggestedDiagnosticOperation);

        public override int GetHashCode() => CanonicalBytes.GetDeterministicHashCode(_canonicalBytes);
    }
}
