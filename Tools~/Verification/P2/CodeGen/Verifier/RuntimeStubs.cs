using System;

namespace AIBT
{
    internal static class NativeBuiltInBlackboardTypeIdsV1
    {
        internal const ulong Bool = 17851659414560344221UL;
        internal const ulong Int32 = 13376016304518341055UL;
        internal const ulong Int64 = 13371240026006338596UL;
        internal const ulong Float32 = 262399420830678928UL;
        internal const ulong Float64 = 259465923807179655UL;
        internal const ulong Float2 = 10476436347436866485UL;
        internal const ulong Float3 = 10476435247925238274UL;
        internal const ulong Quaternion = 17419964233870898637UL;
        internal const ulong Enum32 = 10202037613171143745UL;
        internal const ulong FixedString32 = 3747676040207735061UL;
        internal const ulong FixedString64 = 3742886567556194070UL;
        internal const ulong FixedString128 = 2407326393709615197UL;
        internal const ulong FixedString512 = 18358319809526921596UL;
        internal const ulong AgentId = 3037422141412130939UL;
        internal const ulong EntityId = 15061901384457708023UL;
        internal const ulong OperationId = 12623251205181426651UL;
        internal const ulong AssetId = 7042187404466474134UL;
    }

    public enum NodeStatus : byte { Success, Failure, Running }
    public enum NodeMemoryLifetime : byte { Activation, Instance }
    public enum BlackboardScope : byte { NodeLocal, Tree, Agent, Shared }
    public readonly struct TreeInstanceId : IEquatable<TreeInstanceId>
    {
        public TreeInstanceId(ulong value) { Value = value; }
        public ulong Value { get; }
        public bool IsValid => Value != 0;
        public bool Equals(TreeInstanceId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is TreeInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(TreeInstanceId left, TreeInstanceId right) => left.Equals(right);
        public static bool operator !=(TreeInstanceId left, TreeInstanceId right) => !left.Equals(right);
    }
    public readonly struct RuntimeNodeIndex : IEquatable<RuntimeNodeIndex>
    {
        public RuntimeNodeIndex(uint value) { Value = value; }
        public uint Value { get; }
        public bool IsValid => Value != uint.MaxValue;
        public bool Equals(RuntimeNodeIndex other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is RuntimeNodeIndex other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public static bool operator ==(RuntimeNodeIndex left, RuntimeNodeIndex right) => left.Equals(right);
        public static bool operator !=(RuntimeNodeIndex left, RuntimeNodeIndex right) => !left.Equals(right);
    }
    public readonly struct OperationId : IEquatable<OperationId>
    {
        public OperationId(TreeInstanceId treeInstanceId, RuntimeNodeIndex nodeIndex, uint activationGeneration, ulong sequence)
        { TreeInstanceId = treeInstanceId; NodeIndex = nodeIndex; ActivationGeneration = activationGeneration; Sequence = sequence; }
        public TreeInstanceId TreeInstanceId { get; }
        public RuntimeNodeIndex NodeIndex { get; }
        public uint ActivationGeneration { get; }
        public ulong Sequence { get; }
        public bool IsValid => TreeInstanceId.IsValid && NodeIndex.IsValid;
        public bool Equals(OperationId other) => TreeInstanceId == other.TreeInstanceId && NodeIndex == other.NodeIndex && ActivationGeneration == other.ActivationGeneration && Sequence == other.Sequence;
        public override bool Equals(object? obj) => obj is OperationId other && Equals(other);
        public override int GetHashCode() => Sequence.GetHashCode();
        public static bool operator ==(OperationId left, OperationId right) => left.Equals(right);
        public static bool operator !=(OperationId left, OperationId right) => !left.Equals(right);
    }

    public readonly struct Float2Value
    {
        public Float2Value(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
    }
    public readonly struct AssetId
    {
        public AssetId(ulong guidHigh, ulong guidLow, long localFileId = 0, bool hasLocalFileId = false)
        { GuidHigh = guidHigh; GuidLow = guidLow; LocalFileId = hasLocalFileId ? localFileId : 0; HasLocalFileId = hasLocalFileId; }
        public ulong GuidHigh { get; }
        public ulong GuidLow { get; }
        public long LocalFileId { get; }
        public bool HasLocalFileId { get; }
    }
}

namespace Unity.Burst
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method)]
    public sealed class BurstCompileAttribute : Attribute { }
}

namespace Unity.Collections
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableParallelForRestrictionAttribute : Attribute { }

    public struct NativeArray<T> where T : struct
    {
        public bool IsCreated => false;
        public int Length => 0;
        public readonly ref T this[int index] => throw new NotSupportedException();
        public readonly ref T ElementAt(int index) => throw new NotSupportedException();
        public NativeArray<T> GetSubArray(int start, int length) => default;
        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct => default;
        public ReadOnly AsReadOnly() => default;

        public readonly struct ReadOnly
        {
            public bool IsCreated => false;
            public int Length => 0;
            public T this[int index] => default;
            public ReadOnly GetSubArray(int start, int length) => default;
        }
    }

    public struct NativeList<T> where T : struct
    {
        public bool IsCreated => false;
        public int Length => 0;
        public ref T this[int index] => throw new NotSupportedException();
        public ref T ElementAt(int index) => throw new NotSupportedException();
    }

    public struct FixedString32Bytes
    {
        private ushort utf8LengthInBytes;
        public int Length { readonly get => utf8LengthInBytes; set => utf8LengthInBytes = (ushort)value; }
        public byte this[int index] { readonly get => 0; set { } }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableContainerSafetyRestrictionAttribute : Attribute { }

    public static class UnsafeUtility
    {
        public static int SizeOf<T>() where T : unmanaged => 0;
        public static TTo As<TFrom, TTo>(ref TFrom value)
            where TFrom : unmanaged
            where TTo : unmanaged => default;
    }
}

namespace Unity.Jobs
{
    public interface IJob { void Execute(); }
    public readonly struct JobHandle { }
    public static class IJobExtensions
    {
        public static JobHandle Schedule<T>(this T job, JobHandle dependency) where T : struct, IJob { return dependency; }
    }
}
