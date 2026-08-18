using System;

namespace AIBT.Authoring
{
    public enum BlackboardReductionKind : byte
    {
        None = 0,
        Min = 1,
        Max = 2,
        Sum = 3,
        Any = 4,
        All = 5,
        First = 6,
        Last = 7,
    }

    public sealed class BlackboardScopeContract
    {
        public BlackboardScopeContract(string contractId, uint contractVersion)
        {
            if (!GeneratedIdentityRules.IsValidMemberId(contractId))
                throw new ArgumentException("Scope contract IDs must use the canonical identity grammar.", nameof(contractId));
            if (contractVersion == 0) throw new ArgumentOutOfRangeException(nameof(contractVersion));
            ContractId = contractId;
            ContractVersion = contractVersion;
        }

        public string ContractId { get; }

        public uint ContractVersion { get; }
    }
}
