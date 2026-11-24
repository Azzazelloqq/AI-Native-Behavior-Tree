using AIBT.Burst;
using Unity.Collections;

namespace AIBT.Execution.Burst.Dispatch
{
    /// <summary>
    /// P10-004: handles to one <see cref="NativeBurstDispatchWorkspaceOwnerV2"/>'s own native arrays,
    /// so the JobsAgentFrame framing can run the owner's request/consume/acknowledge/reset sequence
    /// inside a Burst job while the owner itself is idle. Holds no ownership. P11-016: the fields every
    /// workspace of one catalog shares are in <see cref="NativeBurstDispatchWorkspaceSharedArraysV1"/>, held
    /// once per agent.
    /// </summary>
    internal struct NativeBurstDispatchWorkspaceArraysV1
    {
        internal NativeBurstDispatchWorkspaceCapacityV2 Capacity;
        internal NativeArray<NativeBurstDispatchControlV2> Control;
        internal NativeList<int> ExecutionClaim;
        internal NativeList<long> FrameCompletionClaim;
        internal NativeArray<NativeBurstDispatchRequestV2> Requests;
        internal NativeArray<byte> MemoryStaging;
        internal NativeArray<byte> MemoryWritten;
        internal NativeArray<byte> RequestStatuses;
        internal NativeArray<NativeBurstDispatchValueSessionV2> ValueSessions;
        internal NativeArray<byte> ValueStagingBytes;
        internal NativeArray<byte> ValueMarks;
    }

    /// <summary>
    /// P11-016: the part of a workspace that is the same for every workspace created from one catalog's
    /// shape (<c>GeneratedBurstCatalogV2.CreateWorkspaceShape</c>): the handshake, whether the catalog has
    /// bindings and canonical metadata, and its eight metadata arrays (the catalog's own arrays when the
    /// workspace borrows them). Holds no ownership.
    /// </summary>
    internal struct NativeBurstDispatchWorkspaceSharedArraysV1
    {
        internal BurstCatalogHandshake Handshake;
        internal byte HasBindings;
        internal byte HasCanonicalMetadata;
        internal NativeArray<NativeBurstDispatchCaseV2> Cases;
        internal NativeArray<NativeBurstDispatchFieldV2> ConfigurationFields;
        internal NativeArray<NativeBurstDispatchFieldV2> MemoryFields;
        internal NativeArray<NativeBurstDispatchBindingV2> Bindings;
        internal NativeArray<NativeBurstDispatchFieldV2> ValueFields;
        internal NativeArray<NativeBurstDispatchCanonicalRangeV2> CaseCanonicalRanges;
        internal NativeArray<NativeBurstDispatchCanonicalRangeV2> BindingCanonicalRanges;
        internal NativeArray<NativeBurstDispatchCanonicalRuleV2> CanonicalRules;
    }
}
