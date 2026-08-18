using System;

namespace AIBT
{
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
}

namespace Unity.Burst
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method)]
    public sealed class BurstCompileAttribute : Attribute { }
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
