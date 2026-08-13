using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public readonly struct DiagnosticPayloadPropertyContract
    {
        public DiagnosticPayloadPropertyContract(string name, DiagnosticPayloadContract contract, bool isRequired = true)
        {
            CanonicalJsonText.ValidateUnicode(name, nameof(name));
            if (name.Length == 0)
            {
                throw new ArgumentException("Payload property names cannot be empty.", nameof(name));
            }

            Name = name;
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            IsRequired = isRequired;
        }

        public string Name { get; }

        public DiagnosticPayloadContract Contract { get; }

        public bool IsRequired { get; }
    }

    public sealed class DiagnosticPayloadContract
    {
        private readonly ReadOnlyCollection<DiagnosticPayloadPropertyContract> _properties;

        private DiagnosticPayloadContract(
            DiagnosticPayloadKind kind,
            DiagnosticPayloadContract elementContract,
            ReadOnlyCollection<DiagnosticPayloadPropertyContract> properties)
        {
            Kind = kind;
            ElementContract = elementContract;
            _properties = properties ?? Array.AsReadOnly(Array.Empty<DiagnosticPayloadPropertyContract>());
        }

        public DiagnosticPayloadKind Kind { get; }

        public DiagnosticPayloadContract ElementContract { get; }

        public IReadOnlyList<DiagnosticPayloadPropertyContract> Properties => _properties;

        public static DiagnosticPayloadContract Scalar(DiagnosticPayloadKind kind)
        {
            if (kind == DiagnosticPayloadKind.Array || kind == DiagnosticPayloadKind.Map)
            {
                throw new ArgumentException("Use ArrayOf or Map for aggregate payload contracts.", nameof(kind));
            }

            if (!Enum.IsDefined(typeof(DiagnosticPayloadKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return new DiagnosticPayloadContract(kind, null, null);
        }

        public static DiagnosticPayloadContract ArrayOf(DiagnosticPayloadContract elementContract)
        {
            return new DiagnosticPayloadContract(
                DiagnosticPayloadKind.Array,
                elementContract ?? throw new ArgumentNullException(nameof(elementContract)),
                null);
        }

        public static DiagnosticPayloadContract Map(IEnumerable<DiagnosticPayloadPropertyContract> properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            var copy = new List<DiagnosticPayloadPropertyContract>(properties);
            for (var index = 0; index < copy.Count; index++)
            {
                if (copy[index].Name == null || copy[index].Contract == null)
                {
                    throw new ArgumentException("Payload contracts must contain initialized properties.", nameof(properties));
                }
            }

            copy.Sort((left, right) => CanonicalJsonText.CompareUtf8(left.Name, right.Name));
            for (var index = 0; index < copy.Count; index++)
            {
                if (index > 0 && string.Equals(copy[index - 1].Name, copy[index].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Payload contract property names must be unique.", nameof(properties));
                }
            }

            return new DiagnosticPayloadContract(DiagnosticPayloadKind.Map, null, copy.AsReadOnly());
        }

        public static DiagnosticPayloadContract Map(params DiagnosticPayloadPropertyContract[] properties) =>
            Map((IEnumerable<DiagnosticPayloadPropertyContract>)properties);

        public void Validate(DiagnosticOperationPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            Validate(payload, "payload");
        }

        private void Validate(DiagnosticOperationPayload payload, string path)
        {
            if (payload.Kind != Kind)
            {
                throw new ArgumentException($"{path} must be {Kind}, but was {payload.Kind}.", nameof(payload));
            }

            if (Kind == DiagnosticPayloadKind.Array)
            {
                for (var index = 0; index < payload.Items.Count; index++)
                {
                    ElementContract.Validate(payload.Items[index], $"{path}[{index}]");
                }

                return;
            }

            if (Kind != DiagnosticPayloadKind.Map)
            {
                return;
            }

            var contractIndex = 0;
            var memberIndex = 0;
            while (contractIndex < _properties.Count || memberIndex < payload.Members.Count)
            {
                if (contractIndex >= _properties.Count)
                {
                    throw new ArgumentException($"{path}.{payload.Members[memberIndex].Name} is not declared by the payload contract.", nameof(payload));
                }

                var contract = _properties[contractIndex];
                if (memberIndex >= payload.Members.Count)
                {
                    if (contract.IsRequired)
                    {
                        throw new ArgumentException($"{path}.{contract.Name} is required by the payload contract.", nameof(payload));
                    }

                    contractIndex++;
                    continue;
                }

                var member = payload.Members[memberIndex];
                var comparison = CanonicalJsonText.CompareUtf8(contract.Name, member.Name);
                if (comparison < 0)
                {
                    if (contract.IsRequired)
                    {
                        throw new ArgumentException($"{path}.{contract.Name} is required by the payload contract.", nameof(payload));
                    }

                    contractIndex++;
                    continue;
                }

                if (comparison > 0)
                {
                    throw new ArgumentException($"{path}.{member.Name} is not declared by the payload contract.", nameof(payload));
                }

                contract.Contract.Validate(member.Value, $"{path}.{member.Name}");
                contractIndex++;
                memberIndex++;
            }
        }
    }
}
