using AIBT.AgentFrame;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace AIBT
{
    /// <summary>
    /// P10-004: one generated leaf's dispatch state shared by the main-thread
    /// <see cref="GeneratedTreeDispatchAdapterV2"/> slot and the JobsAgentFrame framing, so both
    /// paths continue the same activation generation and published-command record.
    /// </summary>
    internal struct AgentFrameSlotStateV1
    {
        internal uint ActivationGeneration;
        internal uint PublishedCommandCount;
        internal uint PublishedPayloadByteCount;
        internal byte CatalogValidated;
    }

    /// <summary>One binding's precomputed tree-value location (the adapter's BindingResolution plus slot layout).</summary>
    internal struct AgentFrameResolutionV1
    {
        internal byte IsBlackboard;
        internal byte Writes;
        internal uint TargetOrdinal;
        internal uint LiveOffset;
        internal uint StorageOffset;
        internal uint Size;
    }

    /// <summary>A catalog effect descriptor for one effect binding, precomputed on the main thread.</summary>
    internal struct AgentFrameEffectDescriptorV1
    {
        internal uint TargetOrdinal;
        internal ProductionTreeEffectDescriptor Descriptor;
        internal byte Found;
    }

    /// <summary>Handles to one generated leaf slot's native storage; ownership stays with the adapter slot.</summary>
    internal struct AgentFrameSlotV1
    {
        internal byte Present;
        internal byte ActivationMemory;
        internal uint NodeIndex;
        internal uint CaseIndex;
        internal ulong NodeTypeId;
        internal uint NodeTypeVersion;
        internal uint BindingCount;
        internal uint MaximumEffectCommandCount;
        internal NativeArray<AgentFrameSlotStateV1> State;
        internal NativeArray<AgentFrameResolutionV1> Resolutions;
        internal NativeArray<AgentFrameEffectDescriptorV1> EffectDescriptors;
        internal NativeArray<byte> Configuration;
        internal NativeArray<byte> Memory;
        internal NativeArray<ulong> RandomStates;
        internal NativeArray<ulong> RandomIncrements;
        internal NativeArray<NativeBurstDispatchResolvedBindingV2> ResolvedBindings;
        internal NativeArray<byte> BindingValues;
        internal NativeArray<NativeBurstDispatchCompletionV2> Completions;
        internal NativeArray<byte> CompletionPayload;
        internal NativeArray<NativeBurstDispatchCommandV2> Commands;
        internal NativeArray<byte> CommandPayload;
        internal NativeArray<NativeBurstDispatchOperationV2> Operations;
        internal NativeArray<NativeBurstDispatchTransactionControlV2> Transaction;
        internal NativeArray<NativeBurstDispatchCommandV2> PublishedCommands;
        internal NativeArray<byte> PublishedPayload;
        internal NativeBurstDispatchWorkspaceArraysV1 Workspace;
    }

    internal enum AgentFrameOutcomeV1 : byte
    {
        None = 0,
        Terminal = 1,
        AdvanceFailed = 2,
        CallbackRejected = 3,
        StepLimit = 4,
    }

    /// <summary>One trace call the main thread replays in order: a step, or a dispatch completion with its status.</summary>
    internal struct AgentFrameTraceEventV1
    {
        internal NativeLifecycleStepResultV1 Step;
        internal NodeStatus Status;
        internal byte IsCompletion;
    }

    /// <summary>One agent's update inside the agent-frame job: inputs set by the host, outputs drained in agent order.</summary>
    internal struct AgentFrameAgentV1
    {
        // Inputs (main thread, before scheduling).
        internal NativeLifecycleMachineV1 Machine;
        internal UnsafeList<AgentFrameSlotV1> Slots;
        internal NativeArray<byte> TreeValues;
        internal NativeArray<ulong> TreeVersions;
        internal long TimeMicroseconds;
        internal ulong StepsThisUpdate;
        internal uint MaximumSteps;
        internal int EffectFree;
        internal byte TraceEnabled;

        // Outputs (job).
        internal AgentFrameOutcomeV1 Outcome;
        internal NativeLifecycleStepResultV1 TerminalStep;
        internal NativeRuntimeFailureV1 Failure;
        internal BurstContextResult RejectedResult;
        internal byte Executed;
        // Diagnostics: dispatches run, the job thread index, and whether the body ran without Burst.
        internal uint Dispatches;
        internal int ThreadIndex;
        internal byte RanManaged;
        internal UnsafeList<AgentFrameTraceEventV1> Trace;
        internal UnsafeList<ProductionTreeEffect> Effects;
    }

    /// <summary>
    /// P10-004: the JobsAgentFrame framing. <see cref="RunAgent{TBody}"/> is
    /// <c>ProductionTreeHost.RunSegment</c> (no budget) with the generated dispatch inlined: the same
    /// step limit, the same effect capacity reservation and capture, and per dispatch the same
    /// sequence <c>GeneratedTreeDispatchAdapterV2.DispatchSlot.Execute</c> runs through the workspace
    /// owner (begin, validate, acquire, execute, consume, commit, publish, acknowledge, reset). Traces
    /// and effects are logged for the main thread, which replays them in agent order. Workspace ledger
    /// floors (a corruption check across requests) are not tracked here.
    /// </summary>
    internal static class AgentFrameCoreV1
    {
        internal static void RunAgent<TBody>(ref AgentFrameAgentV1 agent, ref TBody body)
            where TBody : struct, IAgentFrameBodyV1
        {
            agent.Outcome = AgentFrameOutcomeV1.None;
            agent.Executed = 0;
            agent.Dispatches = 0;
            agent.ThreadIndex = JobsUtility.ThreadIndex;
            agent.RanManaged = 0;
            MarkManaged(ref agent.RanManaged);
            agent.Trace.Clear();
            agent.Effects.Clear();
            while (true)
            {
                if (agent.StepsThisUpdate >= agent.MaximumSteps)
                {
                    agent.Outcome = AgentFrameOutcomeV1.StepLimit;
                    return;
                }
                agent.StepsThisUpdate++;
                if (!agent.Machine.TryAdvance(out var step, out var failure))
                {
                    agent.Outcome = AgentFrameOutcomeV1.AdvanceFailed;
                    agent.Failure = failure;
                    return;
                }
                agent.Executed = 1;
                if (agent.TraceEnabled != 0 && IsTracedStep(step.Kind))
                    agent.Trace.Add(new AgentFrameTraceEventV1 { Step = step });
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    BurstContextResult result;
                    NodeStatus status;
                    if (step.NodeIndex >= (uint)agent.Slots.Length)
                    {
                        // Same results as the adapter's reservation and Dispatch for an unknown node.
                        result = agent.EffectFree >= 0 ? BurstContextResult.CapacityExceeded : BurstContextResult.InvalidHandle;
                        status = agent.EffectFree >= 0 ? NodeStatus.Failure : NodeStatus.Running;
                    }
                    else
                    {
                        ref var slot = ref agent.Slots.ElementAt((int)step.NodeIndex);
                        if (slot.Present == 0)
                        {
                            // The adapter has no slot for this node: same result as its Dispatch.
                            if (agent.EffectFree >= 0)
                            {
                                result = BurstContextResult.CapacityExceeded;
                                status = NodeStatus.Failure;
                            }
                            else
                            {
                                result = BurstContextResult.TypeMismatch;
                                status = NodeStatus.Running;
                            }
                        }
                        else if (agent.EffectFree >= 0 && slot.MaximumEffectCommandCount > (uint)agent.EffectFree)
                        {
                            result = BurstContextResult.CapacityExceeded;
                            status = NodeStatus.Failure;
                        }
                        else
                        {
                            agent.Dispatches++;
                            result = ExecuteSlot(ref agent, ref slot, in step, ref body, out status);
                            if (result == BurstContextResult.Success && agent.EffectFree >= 0
                                && !TryCaptureEffects(ref agent, ref slot, out result))
                                status = NodeStatus.Failure;
                        }
                    }
                    if (!agent.Machine.TryCompleteDispatch(step.DispatchToken, result, status, out failure))
                    {
                        agent.Outcome = AgentFrameOutcomeV1.CallbackRejected;
                        agent.RejectedResult = result;
                        agent.Failure = failure;
                        return;
                    }
                    if (agent.TraceEnabled != 0)
                        agent.Trace.Add(new AgentFrameTraceEventV1 { Step = step, Status = status, IsCompletion = 1 });
                }
                else if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    agent.Outcome = AgentFrameOutcomeV1.Terminal;
                    agent.TerminalStep = step;
                    return;
                }
            }
        }

        /// <summary>Runs only when the job is not Burst-compiled (Burst removes the call).</summary>
        [BurstDiscard]
        private static void MarkManaged(ref byte flag) => flag = 1;

        /// <summary>The step kinds <c>NativeTraceRecorderV1.RecordStep</c> turns into records.</summary>
        private static bool IsTracedStep(NativeLifecycleStepKindV1 kind)
            => kind == NativeLifecycleStepKindV1.CompositeEntered
                || kind == NativeLifecycleStepKindV1.CompositeExited
                || kind == NativeLifecycleStepKindV1.CompositeAborted
                || kind == NativeLifecycleStepKindV1.Completed;

        private static BurstContextResult ExecuteSlot<TBody>(
            ref AgentFrameAgentV1 agent,
            ref AgentFrameSlotV1 slot,
            in NativeLifecycleStepResultV1 step,
            ref TBody body,
            out NodeStatus status)
            where TBody : struct, IAgentFrameBodyV1
        {
            status = NodeStatus.Running;
            var state = slot.State[0];
            state.PublishedCommandCount = 0;
            state.PublishedPayloadByteCount = 0;
            if (step.Phase == BurstCallbackPhase.Enter)
            {
                if (state.ActivationGeneration == uint.MaxValue)
                {
                    slot.State[0] = state;
                    return BurstContextResult.Overflow;
                }
                state.ActivationGeneration++;
                if (slot.ActivationMemory != 0) Clear(slot.Memory);
            }
            if (state.ActivationGeneration == 0) state.ActivationGeneration = 1;
            slot.State[0] = state;

            // SnapshotTreeValues.
            for (var index = 0; index < slot.Resolutions.Length; index++)
            {
                var resolution = slot.Resolutions[index];
                if (resolution.IsBlackboard == 0) continue;
                if ((ulong)resolution.StorageOffset + resolution.Size > (uint)agent.TreeValues.Length
                    || (ulong)resolution.LiveOffset + resolution.Size > (uint)slot.BindingValues.Length)
                    return BurstContextResult.InvalidHandle;
                for (uint offset = 0; offset < resolution.Size; offset++)
                    slot.BindingValues[(int)(resolution.LiveOffset + offset)] = agent.TreeValues[(int)(resolution.StorageOffset + offset)];
            }

            var request = new NativeBurstDispatchRequestV2(
                0u, slot.NodeIndex, slot.NodeTypeId, slot.NodeTypeVersion, slot.CaseIndex,
                step.Phase, 0u, 0u, 0u, agent.TimeMicroseconds,
                slot.Transaction[0].TreeInstanceId, state.ActivationGeneration, 0u,
                slot.BindingCount, step.AbortReason, step.ExitReason);

            ref var workspace = ref slot.Workspace;
            if (!TryBeginRequest(ref slot, ref workspace, in request, out var requestMutationVersion, out var failure))
                return failure;
            if (workspace.ExecutionClaim[0] != 0) return BurstContextResult.PhaseViolation;
            var batch = new BurstExecutionBatch(CreateBacking(ref slot, ref workspace), NativeBurstBatchRoleV2.Host);
            body.Execute(ref batch);

            // TryConsumeResult.
            if (workspace.ExecutionClaim[0] == 2) return BurstContextResult.PhaseViolation;
            var control = workspace.Control[0];
            if (control.State != NativeBurstDispatchStateV2.Terminal) return BurstContextResult.PhaseViolation;
            var transaction = slot.Transaction[0];
            if (!ValidateLedger(in transaction, request.TreeInstanceId, slot.Commands.Length,
                    slot.CommandPayload.Length, slot.Operations.Length, true, out failure))
                return failure;
            if (control.NextFrameId != 0 && transaction.MutationVersion <= requestMutationVersion)
                return BurstContextResult.InvalidHandle;
            var execution = new BurstExecutionResult(
                control.ResultCode, control.DiagnosticNumber, control.InstancesVisited, control.SegmentSteps);
            var resultStatus = (NodeStatus)workspace.RequestStatuses[0];
            var callback = control.FirstFailure;
            var commandCount = transaction.CommandCount;
            var commandPayloadByteCount = transaction.CommandPayloadByteCount;

            if (!execution.Success && callback == BurstContextResult.Success)
                callback = BurstContextResult.InvalidEncoding;
            if (callback == BurstContextResult.Success && !CommitTreeValues(ref agent, ref slot))
                callback = BurstContextResult.Overflow;
            if (callback == BurstContextResult.Success)
            {
                // PublishCommands.
                state = slot.State[0];
                state.PublishedCommandCount = commandCount;
                state.PublishedPayloadByteCount = commandPayloadByteCount;
                slot.State[0] = state;
                for (uint index = 0; index < commandCount; index++)
                    slot.PublishedCommands[(int)index] = slot.Commands[(int)index];
                for (uint index = 0; index < commandPayloadByteCount; index++)
                    slot.PublishedPayload[(int)index] = slot.CommandPayload[(int)index];
            }
            status = resultStatus;

            // TryAcknowledgePublishedCommands.
            transaction = slot.Transaction[0];
            if (!ValidateLedger(in transaction, request.TreeInstanceId, slot.Commands.Length,
                    slot.CommandPayload.Length, slot.Operations.Length, true, out failure))
                return failure;
            if (transaction.MutationVersion == ulong.MaxValue) return BurstContextResult.Overflow;
            transaction.CommandCount = 0;
            transaction.CommandPayloadByteCount = 0;
            NativeBurstDispatchTransactionLedgerV2.Advance(ref transaction);
            slot.Transaction[0] = transaction;
            Clear(slot.Commands);
            Clear(slot.CommandPayload);

            // TryReset.
            control = workspace.Control[0];
            if (control.Generation == uint.MaxValue) return BurstContextResult.Overflow;
            control.Generation++;
            control.State = NativeBurstDispatchStateV2.Ready;
            control.Cursor = 0;
            control.ActiveFrameId = 0;
            control.NextFrameId = 0;
            control.MemoryCommitted = 0;
            control.FirstFailure = BurstContextResult.Success;
            control.ResultCode = BurstExecutionCode.Success;
            control.DiagnosticNumber = 0;
            control.InstancesVisited = 0;
            control.SegmentSteps = 0;
            workspace.Control[0] = control;
            workspace.ExecutionClaim[0] = 0;
            workspace.FrameCompletionClaim[0] = 0L;
            workspace.Requests[0] = default;
            ClearStaging(ref workspace);
            return callback;
        }

        private static bool TryBeginRequest(
            ref AgentFrameSlotV1 slot,
            ref NativeBurstDispatchWorkspaceArraysV1 workspace,
            in NativeBurstDispatchRequestV2 request,
            out ulong requestMutationVersion,
            out BurstContextResult failure)
        {
            requestMutationVersion = 0;
            if (!workspace.Control.IsCreated)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            workspace.Requests[0] = request;
            if (!ValidateRequest(ref slot, ref workspace, in request, out failure))
            {
                workspace.Requests[0] = default;
                return false;
            }
            requestMutationVersion = slot.Transaction[0].MutationVersion;
            ClearStaging(ref workspace);
            workspace.ExecutionClaim[0] = 0;
            workspace.FrameCompletionClaim[0] = 0L;
            var control = workspace.Control[0];
            control.State = NativeBurstDispatchStateV2.Ready;
            control.Cursor = 0;
            control.ActiveFrameId = 0;
            control.NextFrameId = 0;
            control.MemoryCommitted = 0;
            control.FirstFailure = BurstContextResult.Success;
            control.ResultCode = BurstExecutionCode.Success;
            control.DiagnosticNumber = 0;
            control.InstancesVisited = 0;
            control.SegmentSteps = 0;
            workspace.Control[0] = control;
            failure = BurstContextResult.Success;
            return true;
        }

        /// <summary>The owner's ValidateRequestViews, over this slot's own views.</summary>
        private static bool ValidateRequest(
            ref AgentFrameSlotV1 slot,
            ref NativeBurstDispatchWorkspaceArraysV1 workspace,
            in NativeBurstDispatchRequestV2 request,
            out BurstContextResult failure)
        {
            if (request.CatalogCaseIndex >= (uint)workspace.Cases.Length
                || slot.Transaction.Length != 1)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            var dispatchCase = workspace.Cases[(int)request.CatalogCaseIndex];
            var expectedRandomCount = dispatchCase.HasRandomStream != 0 ? 1 : 0;
            if (request.ResolvedBindingCount != dispatchCase.BindingCount
                || slot.Configuration.Length != dispatchCase.ConfigurationSize
                || slot.Memory.Length != dispatchCase.MemorySize
                || slot.RandomStates.Length != expectedRandomCount
                || slot.RandomIncrements.Length != expectedRandomCount)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            var bindingCapacity = workspace.Capacity.BindingCapacity;
            if (slot.Commands.Length != bindingCapacity.MaxCommands
                || slot.CommandPayload.Length != bindingCapacity.MaxCommandPayloadBytes
                || slot.Operations.Length != bindingCapacity.MaxOperations)
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
            var transaction = slot.Transaction[0];
            if (!ValidateLedger(in transaction, request.TreeInstanceId, slot.Commands.Length,
                    slot.CommandPayload.Length, slot.Operations.Length, false, out failure))
                return false;

            var canonical = workspace.HasCanonicalMetadata != 0
                ? new NativeBurstDispatchCanonicalInputV2(
                    workspace.CaseCanonicalRanges.AsReadOnly(),
                    workspace.BindingCanonicalRanges.AsReadOnly(),
                    workspace.CanonicalRules.AsReadOnly())
                : default;
            NativeBurstDispatchBindingInputV2 bindingInput;
            if (workspace.HasBindings != 0)
            {
                if (slot.ResolvedBindings.Length != dispatchCase.BindingCount)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }
                bindingInput = new NativeBurstDispatchBindingInputV2(
                    workspace.Bindings.AsReadOnly(),
                    slot.ResolvedBindings.AsReadOnly(),
                    workspace.ValueFields.AsReadOnly(),
                    slot.BindingValues.AsReadOnly(),
                    slot.Completions.AsReadOnly(),
                    slot.CompletionPayload.AsReadOnly(),
                    bindingCapacity,
                    canonical);
            }
            else
            {
                if (slot.ResolvedBindings.Length != 0 || slot.BindingValues.Length != 0
                    || slot.Completions.Length != 0 || slot.CompletionPayload.Length != 0)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }
                bindingInput = default;
            }
            var input = new NativeBurstDispatchCreateInputV2(
                workspace.Handshake,
                workspace.Cases.AsReadOnly(),
                workspace.Requests.AsReadOnly(),
                workspace.ConfigurationFields.AsReadOnly(),
                workspace.MemoryFields.AsReadOnly(),
                slot.Configuration.AsReadOnly(),
                slot.Memory.AsReadOnly(),
                slot.RandomStates.AsReadOnly(),
                slot.RandomIncrements.AsReadOnly(),
                bindingInput,
                canonical);
            var state = slot.State[0];
            if (!NativeBurstDispatchBatchOwnerV2.ValidateCreateInput(in input, state.CatalogValidated != 0))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            state.CatalogValidated = 1;
            slot.State[0] = state;
            failure = BurstContextResult.Success;
            return true;
        }

        /// <summary>The owner's ValidateLedger without its cross-request floors.</summary>
        private static bool ValidateLedger(
            in NativeBurstDispatchTransactionControlV2 transaction,
            TreeInstanceId requestTreeInstanceId,
            int commandCapacity,
            int commandPayloadCapacity,
            int operationCapacity,
            bool allowTerminalMutationMaximum,
            out BurstContextResult failure)
        {
            if (transaction.LedgerToken == 0
                || !transaction.TreeInstanceId.IsValid
                || !NativeBurstDispatchTransactionLedgerV2.IsValid(in transaction)
                || transaction.ActiveFrameId != 0
                || transaction.SessionCount != 0
                || transaction.StagingByteCount != 0
                || transaction.CommandCount > commandCapacity
                || transaction.CommandPayloadByteCount > commandPayloadCapacity
                || transaction.OperationCount > operationCapacity
                || transaction.NextOperationSequence == 0)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            if (transaction.TreeInstanceId != requestTreeInstanceId)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            if (!allowTerminalMutationMaximum && transaction.MutationVersion == ulong.MaxValue)
            {
                failure = BurstContextResult.Overflow;
                return false;
            }
            failure = BurstContextResult.Success;
            return true;
        }

        private static BurstDispatchBackingV2 CreateBacking(ref AgentFrameSlotV1 slot, ref NativeBurstDispatchWorkspaceArraysV1 workspace)
            => new BurstDispatchBackingV2(
                workspace.Handshake,
                workspace.Control,
                workspace.ExecutionClaim,
                workspace.FrameCompletionClaim,
                workspace.Cases,
                workspace.Requests,
                workspace.ConfigurationFields,
                workspace.MemoryFields,
                slot.Configuration,
                slot.Memory,
                workspace.MemoryStaging,
                workspace.MemoryWritten,
                slot.RandomStates,
                slot.RandomIncrements,
                workspace.RequestStatuses,
                workspace.Bindings,
                slot.ResolvedBindings,
                workspace.ValueFields,
                workspace.CaseCanonicalRanges,
                workspace.BindingCanonicalRanges,
                workspace.CanonicalRules,
                slot.BindingValues,
                slot.Completions,
                slot.CompletionPayload,
                workspace.ValueSessions,
                workspace.ValueStagingBytes,
                workspace.ValueMarks,
                slot.Commands,
                slot.CommandPayload,
                slot.Operations,
                slot.Transaction);

        /// <summary>CanCommitTreeValues then CommitTreeValues.</summary>
        private static bool CommitTreeValues(ref AgentFrameAgentV1 agent, ref AgentFrameSlotV1 slot)
        {
            for (var index = 0; index < slot.Resolutions.Length; index++)
            {
                var resolution = slot.Resolutions[index];
                if (resolution.Writes == 0) continue;
                var changed = false;
                for (uint offset = 0; offset < resolution.Size; offset++)
                    if (agent.TreeValues[(int)(resolution.StorageOffset + offset)]
                        != slot.BindingValues[(int)(resolution.LiveOffset + offset)])
                    { changed = true; break; }
                if (changed && agent.TreeVersions[(int)resolution.TargetOrdinal] == ulong.MaxValue) return false;
            }
            for (var index = 0; index < slot.Resolutions.Length; index++)
            {
                var resolution = slot.Resolutions[index];
                if (resolution.Writes == 0) continue;
                var changed = false;
                for (uint offset = 0; offset < resolution.Size; offset++)
                {
                    var candidate = slot.BindingValues[(int)(resolution.LiveOffset + offset)];
                    var target = (int)(resolution.StorageOffset + offset);
                    if (agent.TreeValues[target] == candidate) continue;
                    agent.TreeValues[target] = candidate;
                    changed = true;
                }
                if (changed) agent.TreeVersions[(int)resolution.TargetOrdinal]++;
            }
            return true;
        }

        /// <summary>The adapter's TryConsumePublishedEffects against the host queue's free count at frame start.</summary>
        private static bool TryCaptureEffects(ref AgentFrameAgentV1 agent, ref AgentFrameSlotV1 slot, out BurstContextResult failure)
        {
            var state = slot.State[0];
            var count = state.PublishedCommandCount;
            if (count > (uint)agent.EffectFree)
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
            for (uint index = 0; index < count; index++)
                if (!TryReadEffect(ref slot, in state, index, out _, out failure))
                    return false;
            for (uint index = 0; index < count; index++)
            {
                TryReadEffect(ref slot, in state, index, out var effect, out _);
                agent.Effects.Add(effect);
            }
            agent.EffectFree -= (int)count;
            state.PublishedCommandCount = 0;
            state.PublishedPayloadByteCount = 0;
            slot.State[0] = state;
            failure = BurstContextResult.Success;
            return true;
        }

        private static bool TryReadEffect(
            ref AgentFrameSlotV1 slot,
            in AgentFrameSlotStateV1 state,
            uint commandIndex,
            out ProductionTreeEffect effect,
            out BurstContextResult failure)
        {
            effect = default;
            var command = slot.PublishedCommands[(int)commandIndex];
            var descriptor = default(ProductionTreeEffectDescriptor);
            var found = false;
            for (var index = 0; index < slot.EffectDescriptors.Length; index++)
            {
                var entry = slot.EffectDescriptors[index];
                if (entry.TargetOrdinal != command.TargetOrdinal) continue;
                found = entry.Found != 0;
                descriptor = entry.Descriptor;
                break;
            }
            if (command.Kind != NativeBurstDispatchCommandKindV2.Effect || !found || !descriptor.IsSupported)
            {
                failure = BurstContextResult.TypeMismatch;
                return false;
            }
            switch (descriptor.ValueKind)
            {
                case ProductionTreeEffectValueKind.Int32:
                    if (command.PayloadSize != 4u || !TryReadU32(ref slot, in state, command.PayloadOffset, out var intBits))
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                    effect = new ProductionTreeEffect(descriptor, unchecked((int)intBits));
                    failure = BurstContextResult.Success;
                    return true;
                case ProductionTreeEffectValueKind.Float32:
                    if (command.PayloadSize != 4u || !TryReadU32(ref slot, in state, command.PayloadOffset, out var floatBits))
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                    effect = new ProductionTreeEffect(descriptor, ToSingle(floatBits));
                    failure = BurstContextResult.Success;
                    return true;
                case ProductionTreeEffectValueKind.Float3:
                    if (command.PayloadSize != 12u
                        || !TryReadU32(ref slot, in state, command.PayloadOffset, out var xBits)
                        || !TryReadU32(ref slot, in state, command.PayloadOffset + 4u, out var yBits)
                        || !TryReadU32(ref slot, in state, command.PayloadOffset + 8u, out var zBits))
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                    effect = new ProductionTreeEffect(descriptor, new Float3Value(ToSingle(xBits), ToSingle(yBits), ToSingle(zBits)));
                    failure = BurstContextResult.Success;
                    return true;
                default:
                    failure = BurstContextResult.TypeMismatch;
                    return false;
            }
        }

        private static bool TryReadU32(ref AgentFrameSlotV1 slot, in AgentFrameSlotStateV1 state, uint offset, out uint value)
        {
            value = 0;
            if ((ulong)offset + 4u > state.PublishedPayloadByteCount) return false;
            value = slot.PublishedPayload[(int)offset]
                | (uint)slot.PublishedPayload[(int)(offset + 1u)] << 8
                | (uint)slot.PublishedPayload[(int)(offset + 2u)] << 16
                | (uint)slot.PublishedPayload[(int)(offset + 3u)] << 24;
            return true;
        }

        private static float ToSingle(uint bits)
        {
            var value = unchecked((int)bits);
            return UnsafeUtility.As<int, float>(ref value);
        }

        private static void ClearStaging(ref NativeBurstDispatchWorkspaceArraysV1 workspace)
        {
            Clear(workspace.MemoryStaging);
            Clear(workspace.MemoryWritten);
            Clear(workspace.RequestStatuses);
            Clear(workspace.ValueSessions);
            Clear(workspace.ValueStagingBytes);
            Clear(workspace.ValueMarks);
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            for (var index = 0; index < array.Length; index++) array[index] = default;
        }
    }

    /// <summary>
    /// P10-004: process-wide JobsAgentFrame counters for benchmarks and evidence (read through
    /// reflection by the perf harness): job batches, agents, agents that ran on a job worker thread,
    /// agents whose job did not run Burst-compiled, generated dispatches, and Stopwatch ticks spent
    /// in schedule plus complete on the main thread.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    internal static class AgentFrameDiagnosticsV1
    {
        internal static long Batches;
        internal static long Agents;
        internal static long AgentsOnWorkers;
        internal static long ManagedAgents;
        internal static long Dispatches;
        internal static long JobTicks;

        [UnityEngine.Scripting.Preserve]
        internal static void Reset()
        {
            Batches = 0;
            Agents = 0;
            AgentsOnWorkers = 0;
            ManagedAgents = 0;
            Dispatches = 0;
            JobTicks = 0;
        }

        [UnityEngine.Scripting.Preserve]
        internal static string Snapshot()
            => "{\"batches\":" + Batches + ",\"agents\":" + Agents + ",\"agentsOnWorkers\":" + AgentsOnWorkers
                + ",\"managedAgents\":" + ManagedAgents + ",\"dispatches\":" + Dispatches
                + ",\"jobMicroseconds\":" + (JobTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency) + "}";
    }
}
