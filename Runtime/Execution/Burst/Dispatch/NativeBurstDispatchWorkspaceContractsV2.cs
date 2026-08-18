using AIBT.Burst;
using Unity.Collections;

namespace AIBT.Execution.Burst.Dispatch
{
    internal enum NativeBurstDispatchWorkspaceStateV2 : byte
    {
        Idle = 0,
        Prepared = 1,
        Ready = 2,
        Running = 3,
        Terminal = 4,
        Consumed = 5,
        Disposed = 6
    }

    internal readonly struct NativeBurstDispatchWorkspaceLeaseV2
    {
        internal NativeBurstDispatchWorkspaceLeaseV2(ulong ownerId, uint generation)
        {
            OwnerId = ownerId;
            Generation = generation;
        }

        internal ulong OwnerId { get; }
        internal uint Generation { get; }
    }

    internal readonly struct NativeBurstDispatchWorkspaceCapacityV2
    {
        internal NativeBurstDispatchWorkspaceCapacityV2(
            uint maxMemoryBytes,
            NativeBurstDispatchBindingCapacityV2 bindingCapacity)
        {
            MaxMemoryBytes = maxMemoryBytes;
            BindingCapacity = bindingCapacity;
        }

        internal uint MaxMemoryBytes { get; }
        internal NativeBurstDispatchBindingCapacityV2 BindingCapacity { get; }
    }

    internal readonly struct NativeBurstDispatchWorkspaceShapeV2
    {
        internal NativeBurstDispatchWorkspaceShapeV2(
            BurstCatalogHandshake handshake,
            NativeArray<NativeBurstDispatchCaseV2>.ReadOnly cases,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly configurationFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly memoryFields,
            NativeArray<NativeBurstDispatchBindingV2>.ReadOnly bindings,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly valueFields,
            NativeBurstDispatchCanonicalInputV2 canonicalInput)
        {
            Handshake = handshake;
            Cases = cases;
            ConfigurationFields = configurationFields;
            MemoryFields = memoryFields;
            Bindings = bindings;
            ValueFields = valueFields;
            CanonicalInput = canonicalInput;
        }

        internal BurstCatalogHandshake Handshake { get; }
        internal NativeArray<NativeBurstDispatchCaseV2>.ReadOnly Cases { get; }
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly ConfigurationFields { get; }
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly MemoryFields { get; }
        internal NativeArray<NativeBurstDispatchBindingV2>.ReadOnly Bindings { get; }
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly ValueFields { get; }
        internal NativeBurstDispatchCanonicalInputV2 CanonicalInput { get; }
    }

    // These arrays are borrowed for one lease. Their owner keeps them alive and does not
    // mutate read-only inputs or transaction metadata until the lease is consumed/reset
    // and any registered dependency has completed.
    internal readonly struct NativeBurstDispatchWorkspaceRequestViewsV2
    {
        internal NativeBurstDispatchWorkspaceRequestViewsV2(
            NativeBurstDispatchRequestV2 request,
            NativeArray<byte> configurationBytes,
            NativeArray<byte> memoryBytes,
            NativeArray<ulong> randomStates,
            NativeArray<ulong> randomIncrements,
            NativeArray<NativeBurstDispatchResolvedBindingV2> resolvedBindings,
            NativeArray<byte> bindingValueBytes,
            NativeArray<NativeBurstDispatchCompletionV2> completions,
            NativeArray<byte> completionPayloadBytes,
            NativeArray<NativeBurstDispatchCommandV2> commands,
            NativeArray<byte> commandPayloadBytes,
            NativeArray<NativeBurstDispatchOperationV2> operations,
            NativeArray<NativeBurstDispatchTransactionControlV2> transactionControl)
        {
            Request = request;
            ConfigurationBytes = configurationBytes;
            MemoryBytes = memoryBytes;
            RandomStates = randomStates;
            RandomIncrements = randomIncrements;
            ResolvedBindings = resolvedBindings;
            BindingValueBytes = bindingValueBytes;
            Completions = completions;
            CompletionPayloadBytes = completionPayloadBytes;
            Commands = commands;
            CommandPayloadBytes = commandPayloadBytes;
            Operations = operations;
            TransactionControl = transactionControl;
        }

        internal NativeBurstDispatchRequestV2 Request { get; }
        internal NativeArray<byte> ConfigurationBytes { get; }
        internal NativeArray<byte> MemoryBytes { get; }
        internal NativeArray<ulong> RandomStates { get; }
        internal NativeArray<ulong> RandomIncrements { get; }
        internal NativeArray<NativeBurstDispatchResolvedBindingV2> ResolvedBindings { get; }
        internal NativeArray<byte> BindingValueBytes { get; }
        internal NativeArray<NativeBurstDispatchCompletionV2> Completions { get; }
        internal NativeArray<byte> CompletionPayloadBytes { get; }
        internal NativeArray<NativeBurstDispatchCommandV2> Commands { get; }
        internal NativeArray<byte> CommandPayloadBytes { get; }
        internal NativeArray<NativeBurstDispatchOperationV2> Operations { get; }
        internal NativeArray<NativeBurstDispatchTransactionControlV2> TransactionControl { get; }
    }

    internal readonly struct NativeBurstDispatchWorkspaceResultV2
    {
        internal NativeBurstDispatchWorkspaceResultV2(
            BurstExecutionResult execution,
            byte statusCode,
            BurstContextResult callbackFailure,
            bool frameAcquired,
            NativeBurstDispatchTransactionSnapshotV2 transaction)
        {
            Execution = execution;
            StatusCode = statusCode;
            CallbackFailure = callbackFailure;
            FrameAcquired = frameAcquired;
            Transaction = transaction;
        }

        internal BurstExecutionResult Execution { get; }
        internal byte StatusCode { get; }
        internal NodeStatus Status => (NodeStatus)StatusCode;
        internal BurstContextResult CallbackFailure { get; }
        internal bool FrameAcquired { get; }
        internal NativeBurstDispatchTransactionSnapshotV2 Transaction { get; }
    }
}
