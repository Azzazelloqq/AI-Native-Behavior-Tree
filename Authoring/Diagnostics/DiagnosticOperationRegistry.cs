using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class DiagnosticOperationDescriptor
    {
        public DiagnosticOperationDescriptor(
            string operationId,
            string payloadType,
            DiagnosticPayloadContract payloadContract)
        {
            DiagnosticContractId.Validate(operationId, nameof(operationId));
            DiagnosticContractId.Validate(payloadType, nameof(payloadType));
            OperationId = operationId;
            PayloadType = payloadType;
            PayloadContract = payloadContract ?? throw new ArgumentNullException(nameof(payloadContract));
        }

        public string OperationId { get; }

        public string PayloadType { get; }

        public DiagnosticPayloadContract PayloadContract { get; }
    }

    public sealed class DiagnosticOperationRegistry
    {
        private readonly ReadOnlyDictionary<string, DiagnosticOperationDescriptor> _descriptors;

        public DiagnosticOperationRegistry(IEnumerable<DiagnosticOperationDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            var entries = new Dictionary<string, DiagnosticOperationDescriptor>(StringComparer.Ordinal);
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    throw new ArgumentException("Operation registries cannot contain null descriptors.", nameof(descriptors));
                }

                if (entries.ContainsKey(descriptor.OperationId))
                {
                    throw new ArgumentException("Operation IDs must be unique within a registry.", nameof(descriptors));
                }

                entries.Add(descriptor.OperationId, descriptor);
            }

            _descriptors = new ReadOnlyDictionary<string, DiagnosticOperationDescriptor>(entries);
        }

        public int Count => _descriptors.Count;

        public bool TryGet(string operationId, out DiagnosticOperationDescriptor descriptor)
        {
            if (operationId == null)
            {
                descriptor = null;
                return false;
            }

            return _descriptors.TryGetValue(operationId, out descriptor);
        }

        public SuggestedDiagnosticOperation Create(string operationId, string payloadType, DiagnosticOperationPayload payload)
        {
            if (!TryGet(operationId, out var descriptor))
            {
                throw new ArgumentException("The diagnostic operation ID is not registered.", nameof(operationId));
            }

            if (!string.Equals(descriptor.PayloadType, payloadType, StringComparison.Ordinal))
            {
                throw new ArgumentException("The payload type does not match the registered operation contract.", nameof(payloadType));
            }

            return new SuggestedDiagnosticOperation(descriptor, payload);
        }
    }

    internal static class DiagnosticContractId
    {
        public static void Validate(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || value[0] < 'a' || value[0] > 'z')
            {
                throw new ArgumentException("Contract IDs must be stable dot-separated ASCII identifiers.", parameterName);
            }

            var previousWasSeparator = false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var isAlphaNumeric = character >= 'a' && character <= 'z'
                    || character >= 'A' && character <= 'Z'
                    || character >= '0' && character <= '9';
                var isSeparator = character == '.' || character == '-' || character == '_';
                if (!isAlphaNumeric && !isSeparator || isSeparator && (index == 0 || previousWasSeparator))
                {
                    throw new ArgumentException("Contract IDs must be stable dot-separated ASCII identifiers.", parameterName);
                }

                previousWasSeparator = isSeparator;
            }

            if (previousWasSeparator || value.IndexOf('.') < 1)
            {
                throw new ArgumentException("Contract IDs must be stable dot-separated ASCII identifiers.", parameterName);
            }
        }
    }
}
