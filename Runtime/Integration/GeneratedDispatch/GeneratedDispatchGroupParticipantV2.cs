using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;

namespace AIBT
{
    /// <summary>
    /// One agent's own request/config/memory/binding data, prepared by
    /// <see cref="GeneratedTreeDispatchAdapterV2"/> for a <see cref="ProductionTreeScheduler"/> group
    /// wave. Every array is that agent's own persistent storage (not a copy) -- the group executor
    /// reads from it to build one shared batch and, after execution, writes results back into the
    /// exact same slot via <see cref="GeneratedTreeDispatchAdapterV2"/>'s matching commit call. Never
    /// constructed or read outside the P7-033 group dispatch path.
    /// </summary>
    internal readonly struct GeneratedDispatchGroupParticipantV2
    {
        internal GeneratedDispatchGroupParticipantV2(
            ulong typeNumericId,
            uint typeVersion,
            uint catalogCaseIndex,
            BurstCallbackPhase phase,
            NativeArray<byte> configuration,
            NativeArray<byte> memory,
            NativeArray<ulong> randomStates,
            NativeArray<ulong> randomIncrements,
            NativeArray<NativeBurstDispatchResolvedBindingV2> resolvedBindings,
            NativeArray<byte> bindingValues,
            TreeInstanceId treeInstanceId,
            uint activationGeneration,
            BurstNodeAbortReason abortReason,
            BurstNodeExitReason exitReason)
        {
            TypeNumericId = typeNumericId;
            TypeVersion = typeVersion;
            CatalogCaseIndex = catalogCaseIndex;
            Phase = phase;
            Configuration = configuration;
            Memory = memory;
            RandomStates = randomStates;
            RandomIncrements = randomIncrements;
            ResolvedBindings = resolvedBindings;
            BindingValues = bindingValues;
            TreeInstanceId = treeInstanceId;
            ActivationGeneration = activationGeneration;
            AbortReason = abortReason;
            ExitReason = exitReason;
        }

        internal ulong TypeNumericId { get; }
        internal uint TypeVersion { get; }
        internal uint CatalogCaseIndex { get; }
        internal BurstCallbackPhase Phase { get; }
        internal NativeArray<byte> Configuration { get; }
        internal NativeArray<byte> Memory { get; }
        internal NativeArray<ulong> RandomStates { get; }
        internal NativeArray<ulong> RandomIncrements { get; }
        internal NativeArray<NativeBurstDispatchResolvedBindingV2> ResolvedBindings { get; }
        internal NativeArray<byte> BindingValues { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal uint ActivationGeneration { get; }
        internal BurstNodeAbortReason AbortReason { get; }
        internal BurstNodeExitReason ExitReason { get; }
    }
}
