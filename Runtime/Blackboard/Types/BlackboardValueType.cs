namespace AIBT
{
    public enum BlackboardValueType : byte
    {
        Invalid = 0,
        Bool = 1,
        Int32 = 2,
        Int64 = 3,
        Float32 = 4,
        Float64 = 5,
        Float2 = 6,
        Float3 = 7,
        Quaternion = 8,
        Enum32 = 9,
        FixedString32 = 10,
        FixedString64 = 11,
        FixedString128 = 12,
        FixedString512 = 13,
        AgentId = 14,
        EntityId = 15,
        OperationId = 16,
        AssetId = 17,
        Registered = 18,
    }
}
