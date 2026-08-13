using System;
using System.Collections.Generic;

namespace AIBT.Tests.BehaviorCases
{
    internal readonly struct BehaviorCaseRegisteredValueContract
    {
        internal BehaviorCaseRegisteredValueContract(ulong typeId, uint typeVersion, int? payloadSize = null)
        {
            if (typeId == 0) throw new ArgumentOutOfRangeException(nameof(typeId));
            if (typeVersion == 0) throw new ArgumentOutOfRangeException(nameof(typeVersion));
            if (payloadSize < 0) throw new ArgumentOutOfRangeException(nameof(payloadSize));
            TypeId = typeId;
            TypeVersion = typeVersion;
            PayloadSize = payloadSize;
        }

        internal ulong TypeId { get; }
        internal uint TypeVersion { get; }
        internal int? PayloadSize { get; }
    }

    internal sealed class BehaviorCaseRegisteredValueRegistry
    {
        private readonly Dictionary<Key, BehaviorCaseRegisteredValueContract> _contracts;

        internal BehaviorCaseRegisteredValueRegistry(IEnumerable<BehaviorCaseRegisteredValueContract> contracts)
        {
            if (contracts == null) throw new ArgumentNullException(nameof(contracts));
            _contracts = new Dictionary<Key, BehaviorCaseRegisteredValueContract>();
            foreach (var contract in contracts)
            {
                var key = new Key(contract.TypeId, contract.TypeVersion);
                if (_contracts.ContainsKey(key))
                    throw new ArgumentException("Registered behavior-case value contracts must be unique.", nameof(contracts));
                _contracts.Add(key, contract);
            }
        }

        internal static BehaviorCaseRegisteredValueRegistry Empty { get; }
            = new BehaviorCaseRegisteredValueRegistry(Array.Empty<BehaviorCaseRegisteredValueContract>());

        internal bool TryValidate(BehaviorCaseValue value, out string message)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!value.IsRegistered)
            {
                message = null;
                return true;
            }

            if (!_contracts.TryGetValue(
                new Key(value.RegisteredTypeId, value.RegisteredTypeVersion),
                out var contract))
            {
                message = "Registered value type ID and version are not present in the case contract registry.";
                return false;
            }

            var bytes = value.CopyRegisteredBytes();
            if (contract.PayloadSize.HasValue && bytes.Length != contract.PayloadSize.Value)
            {
                message = "Registered value payload size does not match the case contract registry.";
                return false;
            }

            message = null;
            return true;
        }

        private readonly struct Key : IEquatable<Key>
        {
            internal Key(ulong typeId, uint version) { TypeId = typeId; Version = version; }
            private ulong TypeId { get; }
            private uint Version { get; }
            public bool Equals(Key other) => TypeId == other.TypeId && Version == other.Version;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (TypeId.GetHashCode() * 397) ^ (int)Version; }
            }
        }
    }
}
