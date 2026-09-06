using System;
using System.Collections.Generic;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;

namespace AIBT
{
    /// <summary>
    /// Executes one <see cref="ProductionTreeScheduler"/> wave's pending dispatch requests, all
    /// sharing one <see cref="GeneratedBurstCatalogV2"/> identity, through exactly one generated
    /// catalog executor call (immediate or one scheduled Job for the whole group), per
    /// <c>Documentation~/decisions/ADR-P7-037-production-generated-dispatch-bootstrap.md</c>'s own
    /// "Ownership and dispatch waves" section. A rejected/faulted batch commits no participant's
    /// memory or blackboard state -- see <see cref="TryExecuteGroup"/>.
    /// </summary>
    /// <remarks>
    /// Command publication is not yet supported for grouped dispatch: <see cref="NativeBurstDispatchCommandV2"/>
    /// carries no per-request identity, so a group's own shared transaction cannot be demultiplexed
    /// back to the agent that emitted each command without new bookkeeping. A group whose shared
    /// transaction reports any command is rejected with a structured diagnostic rather than silently
    /// dropping or misattributing it -- a disclosed P7-033 gap, not a silent one.
    /// </remarks>
    internal static class GeneratedDispatchGroupExecutorV2
    {
        internal readonly struct Member
        {
            internal Member(ProductionTreeHost host, uint nodeIndex, ProductionTreeHost.DispatchRequest request)
            {
                Host = host;
                NodeIndex = nodeIndex;
                Request = request;
            }
            internal ProductionTreeHost Host { get; }
            internal uint NodeIndex { get; }
            internal ProductionTreeHost.DispatchRequest Request { get; }
        }

        /// <summary>
        /// Runs one group wave. On success, every member's own <see cref="ProductionTreeHost.CompletePendingDispatch"/>
        /// has already been called with its own real status. On failure, no member's state was
        /// mutated and the caller is responsible for completing every member's pending dispatch with
        /// the returned failure (matching a single-instance rejection's own existing contract).
        /// </summary>
        internal static bool TryExecuteGroup(
            GeneratedBurstCatalogV2 catalog,
            GeneratedDispatchGroupWorkspaceV2 workspace,
            IReadOnlyList<Member> members,
            bool scheduled,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.InvalidHandle;
            if (catalog == null || workspace == null || !ReferenceEquals(workspace.Catalog, catalog))
            {
                return false;
            }
            return workspace.TryExecute(members, scheduled, out failure);
        }

        internal static bool TryExecuteGroup(
            GeneratedBurstCatalogV2 catalog,
            IReadOnlyList<Member> members,
            bool scheduled,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.Success;
            if (members == null || members.Count == 0) return true;

            var participants = new GeneratedDispatchGroupParticipantV2[members.Count];
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                var adapter = member.Host.GeneratedDispatchAdapter;
                if (adapter == null || !ReferenceEquals(adapter.Catalog, catalog))
                {
                    failure = BurstContextResult.TypeMismatch;
                    return false;
                }
                var memberRequest = member.Request;
                if (!adapter.TryPrepareGroupParticipant(
                        member.NodeIndex, in memberRequest, out participants[index], out failure))
                    return false;
            }

            var configOffsets = new uint[members.Count];
            var memoryOffsets = new uint[members.Count];
            var randomIndices = new int[members.Count];
            var bindingBaseOffsets = new uint[members.Count];
            var liveValueBaseOffsets = new uint[members.Count];
            var targetOrdinalBaseOffsets = new uint[members.Count];

            uint totalConfig = 0, totalMemory = 0, totalLiveValueBytes = 0, totalTargetOrdinals = 0;
            var totalRandom = 0;
            var totalResolvedBindings = 0;
            for (var index = 0; index < members.Count; index++)
            {
                configOffsets[index] = totalConfig;
                totalConfig = checked(totalConfig + (uint)participants[index].Configuration.Length);
                memoryOffsets[index] = totalMemory;
                totalMemory = checked(totalMemory + (uint)participants[index].Memory.Length);
                randomIndices[index] = participants[index].RandomStates.Length > 0 ? totalRandom : -1;
                totalRandom += participants[index].RandomStates.Length;
                bindingBaseOffsets[index] = (uint)totalResolvedBindings;
                totalResolvedBindings += participants[index].ResolvedBindings.Length;
                liveValueBaseOffsets[index] = totalLiveValueBytes;
                totalLiveValueBytes = checked(totalLiveValueBytes + (uint)participants[index].BindingValues.Length);

                // The validator treats equal (Scope, TargetOrdinal) as "the same logical storage
                // cell" -- correct within one tree instance's own blackboard, but a structural slot
                // index is reused verbatim across every instance of the same tree type. Without
                // remapping, two different agents' own, separately-stored slot 0 would collide and
                // be rejected as an inconsistent alias. Shift each participant's target ordinals
                // into their own disjoint range, exactly as LiveValueOffset is already shifted above.
                targetOrdinalBaseOffsets[index] = totalTargetOrdinals;
                uint participantTargetSpan = 0;
                var participantResolvedBindings = participants[index].ResolvedBindings;
                for (var bindingIndex = 0; bindingIndex < participantResolvedBindings.Length; bindingIndex++)
                {
                    var target = participantResolvedBindings[bindingIndex].TargetOrdinal;
                    if (target != uint.MaxValue && target + 1 > participantTargetSpan)
                        participantTargetSpan = target + 1;
                }
                totalTargetOrdinals = checked(totalTargetOrdinals + participantTargetSpan);
            }

            var configurationBytes = new NativeArray<byte>((int)totalConfig, Allocator.Temp);
            var memoryBytes = new NativeArray<byte>((int)totalMemory, Allocator.Temp);
            var randomStates = new NativeArray<ulong>(totalRandom, Allocator.Temp);
            var randomIncrements = new NativeArray<ulong>(totalRandom, Allocator.Temp);
            var resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>(totalResolvedBindings, Allocator.Temp);
            var liveValueBytes = new NativeArray<byte>((int)totalLiveValueBytes, Allocator.Temp);
            var requests = new NativeArray<NativeBurstDispatchRequestV2>(members.Count, Allocator.Temp);
            // Real, empty (not `default`) allocations -- async-operation completions are not wired
            // up for this adapter, matching the single-instance path's own established convention
            // (DispatchSlot.TryCreate) exactly: a genuinely allocated zero-length array reports
            // IsCreated=true, which NativeBurstDispatchBindingInputV2.IsEnabled's own consistency
            // gate requires; `default` does not and fails validation before ever inspecting cases.
            var completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Temp);
            var completionPayload = new NativeArray<byte>(0, Allocator.Temp);
            NativeBurstDispatchBatchOwnerV2 owner = null;
            try
            {
                for (var index = 0; index < members.Count; index++)
                {
                    var participant = participants[index];
                    CopyBytes(participant.Configuration, configurationBytes, (int)configOffsets[index]);
                    CopyBytes(participant.Memory, memoryBytes, (int)memoryOffsets[index]);
                    if (participant.RandomStates.Length > 0)
                    {
                        randomStates[randomIndices[index]] = participant.RandomStates[0];
                        randomIncrements[randomIndices[index]] = participant.RandomIncrements[0];
                    }
                    for (var bindingIndex = 0; bindingIndex < participant.ResolvedBindings.Length; bindingIndex++)
                    {
                        var resolved = participant.ResolvedBindings[bindingIndex];
                        var remappedTarget = resolved.TargetOrdinal == uint.MaxValue
                            ? resolved.TargetOrdinal
                            : resolved.TargetOrdinal + targetOrdinalBaseOffsets[index];
                        resolvedBindings[(int)bindingBaseOffsets[index] + bindingIndex] = new NativeBurstDispatchResolvedBindingV2(
                            resolved.BindingOrdinal, remappedTarget, resolved.LiveValueOffset + liveValueBaseOffsets[index]);
                    }
                    CopyBytes(participant.BindingValues, liveValueBytes, (int)liveValueBaseOffsets[index]);

                    requests[index] = new NativeBurstDispatchRequestV2(
                        (uint)index, members[index].NodeIndex, participant.TypeNumericId, participant.TypeVersion,
                        participant.CatalogCaseIndex, participant.Phase, configOffsets[index], memoryOffsets[index],
                        (uint)(randomIndices[index] < 0 ? 0 : randomIndices[index]), members[index].Request.TimeMicroseconds,
                        participant.TreeInstanceId, participant.ActivationGeneration,
                        bindingBaseOffsets[index], (uint)participant.ResolvedBindings.Length,
                        participant.AbortReason, participant.ExitReason);
                }

                var perRequestCapacity = catalog.CreateWorkspaceCapacity().BindingCapacity;
                var groupCapacity = new NativeBurstDispatchBindingCapacityV2(
                    checked(perRequestCapacity.MaxValueSessionsPerFrame * (uint)members.Count),
                    checked(perRequestCapacity.MaxValueStagingBytesPerFrame * (uint)members.Count),
                    checked(perRequestCapacity.MaxCommands * (uint)members.Count),
                    checked(perRequestCapacity.MaxCommandPayloadBytes * (uint)members.Count),
                    checked(perRequestCapacity.MaxOperations * (uint)members.Count));
                var bindingInput = new NativeBurstDispatchBindingInputV2(
                    catalog.Bindings, resolvedBindings.AsReadOnly(), catalog.ValueFields, liveValueBytes.AsReadOnly(),
                    completions.AsReadOnly(), completionPayload.AsReadOnly(), groupCapacity, catalog.CanonicalInput);
                var createInput = new NativeBurstDispatchCreateInputV2(
                    catalog.Handshake, catalog.Cases, requests.AsReadOnly(), catalog.ConfigurationFields, catalog.MemoryFields,
                    configurationBytes.AsReadOnly(), memoryBytes.AsReadOnly(), randomStates.AsReadOnly(), randomIncrements.AsReadOnly(),
                    bindingInput);

                if (!NativeBurstDispatchBatchOwnerV2.TryCreate(in createInput, Allocator.Persistent, out owner, out failure))
                {
                    return false;
                }

                if (!owner.TryAcquireImmediateBatch(out var batch))
                {
                    failure = BurstContextResult.PhaseViolation;
                    return false;
                }

                if (scheduled)
                {
                    var dependency = catalog.Executor.Schedule(ref batch, default);
                    if (!owner.TryRegisterDependency(in batch, dependency))
                    {
                        failure = BurstContextResult.PhaseViolation;
                        return false;
                    }
                    dependency.Complete();
                    if (!owner.TryAcquireCompletedBatch(out _))
                    {
                        failure = BurstContextResult.PhaseViolation;
                        return false;
                    }
                }
                else
                {
                    var execution = catalog.Executor.ExecuteImmediate(ref batch);
                    if (!execution.Success)
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                }

                if (!owner.TryGetTransactionSnapshot(out var transactionSnapshot))
                {
                    failure = BurstContextResult.InvalidHandle;
                    return false;
                }
                if (transactionSnapshot.CommandCount != 0)
                {
                    // Disclosed gap: see this type's own remarks. Fail the whole group closed rather
                    // than silently dropping or misattributing a command to the wrong agent.
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }

                var committedMemory = new byte[members.Count][];
                var committedBindingValues = new byte[members.Count][];
                var statuses = new NodeStatus[members.Count];
                for (var index = 0; index < members.Count; index++)
                {
                    var participant = participants[index];
                    var agentMemory = new byte[participant.Memory.Length];
                    for (var byteIndex = 0; byteIndex < agentMemory.Length; byteIndex++)
                    {
                        if (!owner.TryReadCommittedMemoryByte((uint)index, (uint)byteIndex, out agentMemory[byteIndex]))
                        {
                            failure = BurstContextResult.InvalidHandle;
                            return false;
                        }
                    }
                    committedMemory[index] = agentMemory;

                    var agentBindingValues = new byte[participant.BindingValues.Length];
                    for (var byteIndex = 0; byteIndex < agentBindingValues.Length; byteIndex++)
                    {
                        if (!owner.TryReadBindingValueByte(liveValueBaseOffsets[index] + (uint)byteIndex, out agentBindingValues[byteIndex]))
                        {
                            failure = BurstContextResult.InvalidHandle;
                            return false;
                        }
                    }
                    committedBindingValues[index] = agentBindingValues;

                    if (!owner.TryGetRequestStatus((uint)index, out statuses[index]))
                    {
                        failure = BurstContextResult.InvalidHandle;
                        return false;
                    }
                }

                for (var index = 0; index < members.Count; index++)
                {
                    var adapter = members[index].Host.GeneratedDispatchAdapter;
                    var memoryArray = new NativeArray<byte>(committedMemory[index], Allocator.Temp);
                    var bindingArray = new NativeArray<byte>(committedBindingValues[index], Allocator.Temp);
                    try
                    {
                        if (!adapter.TryCommitGroupParticipant(
                                members[index].NodeIndex, memoryArray.AsReadOnly(), bindingArray.AsReadOnly(), out var commitFailure))
                        {
                            failure = commitFailure;
                            return false;
                        }
                    }
                    finally
                    {
                        memoryArray.Dispose();
                        bindingArray.Dispose();
                    }
                    members[index].Host.CompletePendingDispatch(BurstContextResult.Success, statuses[index]);
                }

                failure = BurstContextResult.Success;
                return true;
            }
            finally
            {
                owner?.TryDispose(out _);
                completionPayload.Dispose();
                completions.Dispose();
                requests.Dispose();
                liveValueBytes.Dispose();
                resolvedBindings.Dispose();
                randomIncrements.Dispose();
                randomStates.Dispose();
                memoryBytes.Dispose();
                configurationBytes.Dispose();
            }
        }

        private static void CopyBytes(NativeArray<byte> source, NativeArray<byte> destination, int offset)
        {
            for (var index = 0; index < source.Length; index++) destination[offset + index] = source[index];
        }
    }
}
