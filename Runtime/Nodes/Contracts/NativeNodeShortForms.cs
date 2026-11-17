using System;
using AIBT.Burst;

// P9-005 / ADR-P9-005: short declarations for Burst nodes. These attributes live in AIBT.Native,
// deliberately outside the AIBT.Burst namespace that the ABI v2 public contract covers. They carry
// no ABI of their own: the AIBT.CodeGen generator and analyzer expand each one into exactly the
// long AIBT.Burst attribute(s) it stands for, so a node declared either way produces the same
// manifest, metadata, fingerprint and generated dispatch.
namespace AIBT.Native
{
    /// <summary>
    /// Declares a Burst Action. Short for <c>[AibtBurstNode(id, Version, BurstNodeKind.Action, typeof(TConfig),
    /// typeof(TMemory), MemoryLifetime, Deterministic, Cancellation, Cost, Statuses)]</c>, where TConfig and
    /// TMemory are the first two parameter types of the node's <c>Tick</c>.
    /// Defaults: version 1, activation memory, deterministic, <see cref="BurstCancellationMode.AbortOnly"/>,
    /// <see cref="BurstNodeCost.Low"/>, Success | Failure | Running.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtActionAttribute : Attribute
    {
        public AibtActionAttribute(string canonicalTypeId) { CanonicalTypeId = canonicalTypeId; }
        public string CanonicalTypeId { get; }
        public uint Version { get; set; } = 1u;
        public NodeMemoryLifetime MemoryLifetime { get; set; } = NodeMemoryLifetime.Activation;
        public bool Deterministic { get; set; } = true;
        public BurstCancellationMode Cancellation { get; set; } = BurstCancellationMode.AbortOnly;
        public BurstNodeCost Cost { get; set; } = BurstNodeCost.Low;
        public BurstNodeStatusMask Statuses { get; set; } = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running;
    }

    /// <summary>
    /// Declares a Burst Condition. Short for <c>[AibtBurstNode(id, Version, BurstNodeKind.Condition, typeof(TConfig),
    /// typeof(TMemory), MemoryLifetime, Deterministic, Cancellation, Cost, Statuses)]</c>, where TConfig and
    /// TMemory are the first two parameter types of the node's <c>Tick</c>.
    /// Defaults: version 1, activation memory, deterministic, <see cref="BurstCancellationMode.NotApplicable"/>,
    /// <see cref="BurstNodeCost.Trivial"/>, Success | Failure.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtConditionAttribute : Attribute
    {
        public AibtConditionAttribute(string canonicalTypeId) { CanonicalTypeId = canonicalTypeId; }
        public string CanonicalTypeId { get; }
        public uint Version { get; set; } = 1u;
        public NodeMemoryLifetime MemoryLifetime { get; set; } = NodeMemoryLifetime.Activation;
        public bool Deterministic { get; set; } = true;
        public BurstCancellationMode Cancellation { get; set; } = BurstCancellationMode.NotApplicable;
        public BurstNodeCost Cost { get; set; } = BurstNodeCost.Trivial;
        public BurstNodeStatusMask Statuses { get; set; } = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure;
    }

    /// <summary>
    /// A node parameter (configuration field). Short for <c>[AibtConfigField(id, valueType, 1u)]</c> with the
    /// value type taken from the field: bool → Bool, uint → UInt32, ulong → UInt64, float → Float32.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtParamAttribute : Attribute
    {
        public AibtParamAttribute(string fieldId) { FieldId = fieldId; }
        public string FieldId { get; }
    }

    /// <summary>
    /// A node memory field. Short for <c>[AibtMemoryField(id, valueType, version)]</c> with the value type and
    /// version taken from the field type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtMemoryAttribute : Attribute
    {
        public AibtMemoryAttribute(string fieldId) { FieldId = fieldId; }
        public string FieldId { get; }
    }

    /// <summary>
    /// A blackboard key on a <c>BlackboardReadHandle&lt;T&gt;</c>, <c>BlackboardWriteHandle&lt;T&gt;</c> or
    /// <c>BlackboardReadWriteHandle&lt;T&gt;</c> field. Short for
    /// <c>[AibtConfigField(key, "GeneratedHandle", 1u), AibtBlackboardBinding(key, access, Scope, valueType, version)]</c>
    /// with the access taken from the handle and the value type from T. Default scope: Tree.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtKeyAttribute : Attribute
    {
        public AibtKeyAttribute(string key) { Key = key; }
        public string Key { get; }
        public BlackboardScope Scope { get; set; } = BlackboardScope.Tree;
    }

    /// <summary>
    /// A command (effect) on a <c>CommandHandle&lt;T&gt;</c> field. Short for
    /// <c>[AibtConfigField(id, "GeneratedHandle", 1u), AibtCommandBinding(id, payloadType, version)]</c> with the
    /// payload type taken from T.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCommandAttribute : Attribute
    {
        public AibtCommandAttribute(string commandId) { CommandId = commandId; }
        public string CommandId { get; }
    }

    /// <summary>
    /// A simulation input on a <c>SnapshotReadHandle&lt;T&gt;</c> field: the current agent's row of the
    /// game array bound to the input channel <paramref name="inputId"/> of an <c>AIBT.Simulation.TreeGroup</c>.
    /// Short for <c>[AibtConfigField(id, "GeneratedHandle", 1u), AibtSnapshotBinding(id, valueType, version)]</c>
    /// with the value type taken from T. Read it with <c>TryReadSnapshot</c>. Experimental (ADR-P10-008) until stage G5.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtInputAttribute : Attribute
    {
        public AibtInputAttribute(string inputId) { InputId = inputId; }
        public string InputId { get; }
    }
}
