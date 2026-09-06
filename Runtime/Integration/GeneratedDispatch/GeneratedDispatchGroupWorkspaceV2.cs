using System;
using System.Collections.Generic;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    /// <summary>
    /// Scheduler-owned fixed-capacity backing for immediate grouped generated dispatch. The owner
    /// is deliberately separate from the one-request workspace: every active request is exposed to
    /// the catalog in one bounded batch, while unused capacity is never part of that batch.
    /// </summary>
    internal sealed class GeneratedDispatchGroupWorkspaceV2
    {
        private readonly GeneratedBurstCatalogV2 _catalog;
        private readonly int _maximumParticipants;
        private readonly GeneratedDispatchGroupParticipantV2[] _participants;
        private readonly uint[] _targetOrdinalBaseOffsets;
        private readonly uint[] _liveValueBaseOffsets;

        private NativeArray<NativeBurstDispatchControlV2> _control;
        private NativeList<int> _executionClaim;
        private NativeList<long> _frameCompletionClaim;
        private NativeArray<NativeBurstDispatchCaseV2> _cases;
        private NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
        private NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
        private NativeArray<NativeBurstDispatchBindingV2> _bindings;
        private NativeArray<NativeBurstDispatchFieldV2> _valueFields;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseCanonicalRanges;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingCanonicalRanges;
        private NativeArray<NativeBurstDispatchCanonicalRuleV2> _canonicalRules;
        private NativeArray<NativeBurstDispatchRequestV2> _requests;
        private NativeArray<byte> _configurationBytes;
        private NativeArray<byte> _memoryBytes;
        private NativeArray<byte> _memoryStaging;
        private NativeArray<byte> _memoryWritten;
        private NativeArray<ulong> _randomStates;
        private NativeArray<ulong> _randomIncrements;
        private NativeArray<byte> _requestStatuses;
        private NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
        private NativeArray<byte> _bindingValueBytes;
        private NativeArray<NativeBurstDispatchCompletionV2> _completions;
        private NativeArray<byte> _completionPayloadBytes;
        private NativeArray<NativeBurstDispatchValueSessionV2> _valueSessions;
        private NativeArray<byte> _valueStagingBytes;
        private NativeArray<byte> _valueMarks;
        private NativeArray<NativeBurstDispatchCommandV2> _commands;
        private NativeArray<byte> _commandPayloadBytes;
        private NativeArray<NativeBurstDispatchOperationV2> _operations;
        private NativeArray<NativeBurstDispatchTransactionControlV2> _transactionControl;
        private NativeBurstDispatchBindingCapacityV2 _bindingCapacity;
        private ulong _ownerId;
        private uint _generation;
        private bool _leased;
        private bool _disposed;

        private GeneratedDispatchGroupWorkspaceV2(
            GeneratedBurstCatalogV2 catalog,
            int maximumParticipants,
            GeneratedDispatchGroupParticipantV2[] participants,
            uint[] targetOrdinalBaseOffsets,
            uint[] liveValueBaseOffsets)
        {
            _catalog = catalog;
            _maximumParticipants = maximumParticipants;
            _participants = participants;
            _targetOrdinalBaseOffsets = targetOrdinalBaseOffsets;
            _liveValueBaseOffsets = liveValueBaseOffsets;
        }

        internal GeneratedBurstCatalogV2 Catalog => _catalog;
        internal int MaximumParticipants => _maximumParticipants;
        internal bool IsLeased => _leased;

        internal static bool TryCreate(
            GeneratedBurstCatalogV2 catalog,
            int maximumParticipants,
            out GeneratedDispatchGroupWorkspaceV2 workspace,
            out BurstContextResult failure)
        {
            workspace = null;
            failure = BurstContextResult.InvalidHandle;
            if (catalog == null || !catalog.IsCreated || maximumParticipants < 2)
            {
                return false;
            }

            if (!TryDescribeCapacity(catalog, maximumParticipants, out var capacity, out failure))
            {
                return false;
            }

            var created = new GeneratedDispatchGroupWorkspaceV2(
                catalog,
                maximumParticipants,
                new GeneratedDispatchGroupParticipantV2[maximumParticipants],
                new uint[maximumParticipants],
                new uint[maximumParticipants]);
            try
            {
                created._control = Allocate<NativeBurstDispatchControlV2>(1);
                created._executionClaim = new NativeList<int>(1, Allocator.Persistent);
                created._executionClaim.Add(0);
                created._frameCompletionClaim = new NativeList<long>(1, Allocator.Persistent);
                created._frameCompletionClaim.Add(0L);
                created._cases = CopyOf(catalog.Cases);
                created._configurationFields = CopyOf(catalog.ConfigurationFields);
                created._memoryFields = CopyOf(catalog.MemoryFields);
                created._bindings = CopyOf(catalog.Bindings);
                created._valueFields = CopyOf(catalog.ValueFields);
                var canonical = catalog.CanonicalInput;
                created._caseCanonicalRanges = CopyOf(canonical.CaseRanges);
                created._bindingCanonicalRanges = CopyOf(canonical.BindingRanges);
                created._canonicalRules = CopyOf(canonical.Rules);
                created._requests = Allocate<NativeBurstDispatchRequestV2>(maximumParticipants);
                created._configurationBytes = Allocate<byte>(capacity.ConfigurationBytes);
                created._memoryBytes = Allocate<byte>(capacity.MemoryBytes);
                created._memoryStaging = Allocate<byte>(capacity.MemoryBytes);
                created._memoryWritten = Allocate<byte>(capacity.MemoryBytes);
                created._randomStates = Allocate<ulong>(capacity.RandomStates);
                created._randomIncrements = Allocate<ulong>(capacity.RandomStates);
                created._requestStatuses = Allocate<byte>(maximumParticipants);
                created._resolvedBindings = Allocate<NativeBurstDispatchResolvedBindingV2>(capacity.ResolvedBindings);
                created._bindingValueBytes = Allocate<byte>(capacity.LiveValueBytes);
                // Keep zero-length transport arrays constructed: binding validation distinguishes
                // an allocated empty array from default.
                created._completions = Allocate<NativeBurstDispatchCompletionV2>(0);
                created._completionPayloadBytes = Allocate<byte>(0);
                created._valueSessions = Allocate<NativeBurstDispatchValueSessionV2>(capacity.ValueSessions);
                created._valueStagingBytes = Allocate<byte>(capacity.ValueStagingBytes);
                created._valueMarks = Allocate<byte>(capacity.ValueStagingBytes);
                created._commands = Allocate<NativeBurstDispatchCommandV2>(capacity.Commands);
                created._commandPayloadBytes = Allocate<byte>(capacity.CommandPayloadBytes);
                created._operations = Allocate<NativeBurstDispatchOperationV2>(capacity.Operations);
                created._transactionControl = Allocate<NativeBurstDispatchTransactionControlV2>(1);
                created._bindingCapacity = capacity.BindingCapacity;
                if (!NativeOwnerIdentityV1.TryNext(out created._ownerId))
                {
                    created.DisposeArrays();
                    failure = BurstContextResult.Overflow;
                    return false;
                }

                created._generation = 1u;
                created.ResetControl();
                workspace = created;
                failure = BurstContextResult.Success;
                return true;
            }
            catch (Exception)
            {
                created.DisposeArrays();
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
        }

        internal bool TryExecute(
            IReadOnlyList<GeneratedDispatchGroupExecutorV2.Member> members,
            bool scheduled,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.InvalidHandle;
            if (_disposed || _leased || members == null)
            {
                return false;
            }
            if (members.Count < 2)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            if (members.Count > _maximumParticipants)
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }

            _leased = true;
            try
            {
                if (!TryPrepare(members, out var activeCount, out var configurationLength,
                        out var memoryLength, out var randomCount, out var resolvedBindingCount,
                        out var liveValueLength, out failure))
                {
                    return false;
                }

                var backing = CreateBacking(
                    activeCount, configurationLength, memoryLength, randomCount,
                    resolvedBindingCount, liveValueLength);
                var batch = new BurstExecutionBatch(backing, NativeBurstBatchRoleV2.Host);
                if (scheduled)
                {
                    var dependency = _catalog.Executor.Schedule(ref batch, default(JobHandle));
                    if (_executionClaim[0] != 2)
                    {
                        failure = BurstContextResult.PhaseViolation;
                        return false;
                    }
                    dependency.Complete();
                    _executionClaim[0] = 3;
                }
                else
                {
                    var execution = _catalog.Executor.ExecuteImmediate(ref batch);
                    if (!execution.Success)
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                }

                if (!TryValidateTransaction(out failure)) return false;
                if (_transactionControl[0].CommandCount != 0)
                {
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }

                if (!TryValidateCommits(members, activeCount, out failure)) return false;
                for (var index = 0; index < activeCount; index++)
                {
                    var participant = _participants[index];
                    var request = _requests[index];
                    var adapter = members[index].Host.GeneratedDispatchAdapter;
                    var committedMemory = _memoryBytes.GetSubArray((int)request.MemoryOffset, participant.Memory.Length);
                    var committedBindingValues = _bindingValueBytes.GetSubArray(
                        (int)_liveValueBaseOffsets[index], participant.BindingValues.Length);
                    if (!adapter.TryCommitGroupParticipant(
                            members[index].NodeIndex,
                            committedMemory.AsReadOnly(),
                            committedBindingValues.AsReadOnly(),
                            out failure))
                    {
                        return false;
                    }
                    members[index].Host.CompletePendingDispatch(
                        BurstContextResult.Success, (NodeStatus)_requestStatuses[index]);
                }

                failure = BurstContextResult.Success;
                return true;
            }
            catch (OverflowException)
            {
                failure = BurstContextResult.Overflow;
                return false;
            }
            catch (Exception)
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
            finally
            {
                _leased = false;
            }
        }

        internal bool TryDispose(out BurstContextResult failure)
        {
            if (_disposed || _leased)
            {
                failure = _disposed ? BurstContextResult.InvalidHandle : BurstContextResult.PhaseViolation;
                return false;
            }

            DisposeArrays();
            _disposed = true;
            failure = BurstContextResult.Success;
            return true;
        }

        private bool TryPrepare(
            IReadOnlyList<GeneratedDispatchGroupExecutorV2.Member> members,
            out int activeCount,
            out int configurationLength,
            out int memoryLength,
            out int randomCount,
            out int resolvedBindingCount,
            out int liveValueLength,
            out BurstContextResult failure)
        {
            activeCount = members.Count;
            configurationLength = 0;
            memoryLength = 0;
            randomCount = 0;
            resolvedBindingCount = 0;
            liveValueLength = 0;
            failure = BurstContextResult.InvalidEncoding;
            uint totalTargetOrdinals = 0;

            for (var index = 0; index < activeCount; index++)
            {
                var member = members[index];
                var adapter = member.Host?.GeneratedDispatchAdapter;
                var memberRequest = member.Request;
                if (adapter == null || !ReferenceEquals(adapter.Catalog, _catalog)
                    || !adapter.TryPrepareGroupParticipant(
                        member.NodeIndex, in memberRequest, out var participant, out failure))
                {
                    return false;
                }

                if (participant.CatalogCaseIndex >= _cases.Length)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }
                var dispatchCase = _cases[(int)participant.CatalogCaseIndex];
                if (participant.Configuration.Length != dispatchCase.ConfigurationSize
                    || participant.Memory.Length != dispatchCase.MemorySize
                    || participant.RandomStates.Length != participant.RandomIncrements.Length
                    || participant.RandomStates.Length != (dispatchCase.HasRandomStream == 0 ? 0 : 1)
                    || participant.ResolvedBindings.Length != dispatchCase.BindingCount
                    || participant.BindingValues.Length != LiveValueBytes(in dispatchCase))
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }

                if (!TryAdd(ref configurationLength, participant.Configuration.Length)
                    || !TryAdd(ref memoryLength, participant.Memory.Length)
                    || !TryAdd(ref randomCount, participant.RandomStates.Length)
                    || !TryAdd(ref resolvedBindingCount, participant.ResolvedBindings.Length)
                    || !TryAdd(ref liveValueLength, participant.BindingValues.Length))
                {
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }

                _targetOrdinalBaseOffsets[index] = totalTargetOrdinals;
                uint participantTargetSpan = 0;
                for (var bindingIndex = 0; bindingIndex < participant.ResolvedBindings.Length; bindingIndex++)
                {
                    var targetOrdinal = participant.ResolvedBindings[bindingIndex].TargetOrdinal;
                    if (targetOrdinal != uint.MaxValue && targetOrdinal + 1u > participantTargetSpan)
                        participantTargetSpan = targetOrdinal + 1u;
                }
                if ((ulong)totalTargetOrdinals + participantTargetSpan > uint.MaxValue)
                {
                    failure = BurstContextResult.Overflow;
                    return false;
                }
                totalTargetOrdinals += participantTargetSpan;
                _participants[index] = participant;
            }

            if (configurationLength > _configurationBytes.Length
                || memoryLength > _memoryBytes.Length
                || randomCount > _randomStates.Length
                || resolvedBindingCount > _resolvedBindings.Length
                || liveValueLength > _bindingValueBytes.Length)
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }

            Clear(_memoryStaging);
            Clear(_memoryWritten);
            Clear(_requestStatuses);
            Clear(_valueSessions);
            Clear(_valueStagingBytes);
            Clear(_valueMarks);
            Clear(_commands);
            Clear(_commandPayloadBytes);
            Clear(_operations);
            _executionClaim[0] = 0;
            _frameCompletionClaim[0] = 0L;
            ResetControl();
            _transactionControl[0] = new NativeBurstDispatchTransactionControlV2
            {
                NextOperationSequence = _bindingCapacity.FirstOperationSequence == 0
                    ? 1UL
                    : _bindingCapacity.FirstOperationSequence
            };

            var configurationOffset = 0;
            var memoryOffset = 0;
            var randomOffset = 0;
            var bindingOffset = 0;
            var liveValueOffset = 0;
            for (var index = 0; index < activeCount; index++)
            {
                var participant = _participants[index];
                Copy(participant.Configuration, _configurationBytes, configurationOffset);
                Copy(participant.Memory, _memoryBytes, memoryOffset);
                if (participant.RandomStates.Length != 0)
                {
                    _randomStates[randomOffset] = participant.RandomStates[0];
                    _randomIncrements[randomOffset] = participant.RandomIncrements[0];
                }
                for (var bindingIndex = 0; bindingIndex < participant.ResolvedBindings.Length; bindingIndex++)
                {
                    var resolved = participant.ResolvedBindings[bindingIndex];
                    var remappedTarget = resolved.TargetOrdinal == uint.MaxValue
                        ? uint.MaxValue
                        : checked(resolved.TargetOrdinal + _targetOrdinalBaseOffsets[index]);
                    _resolvedBindings[bindingOffset + bindingIndex] = new NativeBurstDispatchResolvedBindingV2(
                        resolved.BindingOrdinal, remappedTarget,
                        checked(resolved.LiveValueOffset + (uint)liveValueOffset));
                }
                Copy(participant.BindingValues, _bindingValueBytes, liveValueOffset);
                _liveValueBaseOffsets[index] = (uint)liveValueOffset;
                _requests[index] = new NativeBurstDispatchRequestV2(
                    (uint)index, members[index].NodeIndex, participant.TypeNumericId, participant.TypeVersion,
                    participant.CatalogCaseIndex, participant.Phase, (uint)configurationOffset, (uint)memoryOffset,
                    (uint)(participant.RandomStates.Length == 0 ? 0 : randomOffset), members[index].Request.TimeMicroseconds,
                    participant.TreeInstanceId, participant.ActivationGeneration,
                    (uint)bindingOffset, (uint)participant.ResolvedBindings.Length,
                    participant.AbortReason, participant.ExitReason);
                configurationOffset += participant.Configuration.Length;
                memoryOffset += participant.Memory.Length;
                randomOffset += participant.RandomStates.Length;
                bindingOffset += participant.ResolvedBindings.Length;
                liveValueOffset += participant.BindingValues.Length;
            }

            var input = new NativeBurstDispatchCreateInputV2(
                _catalog.Handshake,
                _cases.AsReadOnly(), _requests.GetSubArray(0, activeCount).AsReadOnly(),
                _configurationFields.AsReadOnly(), _memoryFields.AsReadOnly(),
                _configurationBytes.GetSubArray(0, configurationLength).AsReadOnly(),
                _memoryBytes.GetSubArray(0, memoryLength).AsReadOnly(),
                _randomStates.GetSubArray(0, randomCount).AsReadOnly(),
                _randomIncrements.GetSubArray(0, randomCount).AsReadOnly(),
                new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    _resolvedBindings.GetSubArray(0, resolvedBindingCount).AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    _bindingValueBytes.GetSubArray(0, liveValueLength).AsReadOnly(),
                    _completions.AsReadOnly(), _completionPayloadBytes.AsReadOnly(),
                    _bindingCapacity,
                    CanonicalInput()),
                CanonicalInput());
            if (!NativeBurstDispatchBatchOwnerV2.ValidateCreateInput(in input))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }

            failure = BurstContextResult.Success;
            return true;
        }

        private BurstDispatchBackingV2 CreateBacking(
            int activeCount,
            int configurationLength,
            int memoryLength,
            int randomCount,
            int resolvedBindingCount,
            int liveValueLength)
            => new BurstDispatchBackingV2(
                _catalog.Handshake, _control, _executionClaim, _frameCompletionClaim,
                _cases, _requests.GetSubArray(0, activeCount), _configurationFields, _memoryFields,
                _configurationBytes.GetSubArray(0, configurationLength),
                _memoryBytes.GetSubArray(0, memoryLength),
                _memoryStaging.GetSubArray(0, memoryLength),
                _memoryWritten.GetSubArray(0, memoryLength),
                _randomStates.GetSubArray(0, randomCount),
                _randomIncrements.GetSubArray(0, randomCount),
                _requestStatuses.GetSubArray(0, activeCount),
                _bindings, _resolvedBindings.GetSubArray(0, resolvedBindingCount), _valueFields,
                _caseCanonicalRanges, _bindingCanonicalRanges, _canonicalRules,
                _bindingValueBytes.GetSubArray(0, liveValueLength),
                _completions, _completionPayloadBytes,
                _valueSessions, _valueStagingBytes, _valueMarks,
                _commands, _commandPayloadBytes, _operations, _transactionControl);

        private bool TryValidateCommits(
            IReadOnlyList<GeneratedDispatchGroupExecutorV2.Member> members,
            int activeCount,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.InvalidHandle;
            for (var index = 0; index < activeCount; index++)
            {
                var participant = _participants[index];
                var request = _requests[index];
                var adapter = members[index].Host?.GeneratedDispatchAdapter;
                if (adapter == null
                    || !adapter.TryValidateGroupParticipantCommit(
                        members[index].NodeIndex,
                        _memoryBytes.GetSubArray((int)request.MemoryOffset, participant.Memory.Length).AsReadOnly(),
                        _bindingValueBytes.GetSubArray(
                            (int)_liveValueBaseOffsets[index], participant.BindingValues.Length).AsReadOnly(),
                        out failure))
                {
                    return false;
                }
            }
            failure = BurstContextResult.Success;
            return true;
        }

        private bool TryValidateTransaction(out BurstContextResult failure)
        {
            var transaction = _transactionControl[0];
            if (transaction.CommandCount > _commands.Length
                || transaction.CommandPayloadByteCount > _commandPayloadBytes.Length
                || transaction.OperationCount > _operations.Length)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            failure = BurstContextResult.Success;
            return true;
        }

        private void ResetControl()
        {
            _control[0] = new NativeBurstDispatchControlV2
            {
                OwnerId = _ownerId,
                Generation = _generation,
                State = NativeBurstDispatchStateV2.Ready,
                ResultCode = BurstExecutionCode.Success,
                FirstFailure = BurstContextResult.Success
            };
        }

        private NativeBurstDispatchCanonicalInputV2 CanonicalInput()
            => new NativeBurstDispatchCanonicalInputV2(
                _caseCanonicalRanges.AsReadOnly(),
                _bindingCanonicalRanges.AsReadOnly(),
                _canonicalRules.AsReadOnly());

        private uint LiveValueBytes(in NativeBurstDispatchCaseV2 dispatchCase)
        {
            uint total = 0;
            for (uint index = 0; index < dispatchCase.BindingCount; index++)
                total = checked(total + _bindings[(int)(dispatchCase.FirstBinding + index)].PrimaryValueSize);
            return total;
        }

        private static bool TryDescribeCapacity(
            GeneratedBurstCatalogV2 catalog,
            int maximumParticipants,
            out Capacity capacity,
            out BurstContextResult failure)
        {
            capacity = default;
            failure = BurstContextResult.InvalidEncoding;
            try
            {
                uint maxConfiguration = 0;
                uint maxMemory = 0;
                uint maxBindings = 0;
                uint maxLiveValues = 0;
                for (var caseIndex = 0; caseIndex < catalog.Cases.Length; caseIndex++)
                {
                    var dispatchCase = catalog.Cases[caseIndex];
                    if (dispatchCase.ConfigurationSize > maxConfiguration) maxConfiguration = dispatchCase.ConfigurationSize;
                    if (dispatchCase.MemorySize > maxMemory) maxMemory = dispatchCase.MemorySize;
                    if (dispatchCase.BindingCount > maxBindings) maxBindings = dispatchCase.BindingCount;
                    uint liveValues = 0;
                    for (uint bindingIndex = 0; bindingIndex < dispatchCase.BindingCount; bindingIndex++)
                        liveValues = checked(liveValues + catalog.Bindings[(int)(dispatchCase.FirstBinding + bindingIndex)].PrimaryValueSize);
                    if (liveValues > maxLiveValues) maxLiveValues = liveValues;
                }

                var perRequestCapacity = catalog.CreateWorkspaceCapacity().BindingCapacity;
                var participantCount = (uint)maximumParticipants;
                var bindingCapacity = new NativeBurstDispatchBindingCapacityV2(
                    checked(perRequestCapacity.MaxValueSessionsPerFrame * participantCount),
                    checked(perRequestCapacity.MaxValueStagingBytesPerFrame * participantCount),
                    checked(perRequestCapacity.MaxCommands * participantCount),
                    checked(perRequestCapacity.MaxCommandPayloadBytes * participantCount),
                    checked(perRequestCapacity.MaxOperations * participantCount),
                    perRequestCapacity.FirstOperationSequence == 0 ? 1UL : perRequestCapacity.FirstOperationSequence);
                if (!TryLength(maxConfiguration, participantCount, out var configurationBytes)
                    || !TryLength(maxMemory, participantCount, out var memoryBytes)
                    || !TryLength(1u, participantCount, out var randomStates)
                    || !TryLength(maxBindings, participantCount, out var resolvedBindings)
                    || !TryLength(maxLiveValues, participantCount, out var liveValueBytes)
                    || !TryLength(bindingCapacity.MaxValueSessionsPerFrame, 1u, out var valueSessions)
                    || !TryLength(bindingCapacity.MaxValueStagingBytesPerFrame, 1u, out var valueStagingBytes)
                    || !TryLength(bindingCapacity.MaxCommands, 1u, out var commands)
                    || !TryLength(bindingCapacity.MaxCommandPayloadBytes, 1u, out var commandPayloadBytes)
                    || !TryLength(bindingCapacity.MaxOperations, 1u, out var operations))
                {
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }

                capacity = new Capacity(
                    configurationBytes, memoryBytes, randomStates, resolvedBindings, liveValueBytes,
                    valueSessions, valueStagingBytes, commands, commandPayloadBytes, operations,
                    bindingCapacity);
                failure = BurstContextResult.Success;
                return true;
            }
            catch (OverflowException)
            {
                failure = BurstContextResult.Overflow;
                return false;
            }
        }

        private static bool TryLength(uint elementCount, uint multiplier, out int length)
        {
            var result = (ulong)elementCount * multiplier;
            if (result > int.MaxValue)
            {
                length = 0;
                return false;
            }
            length = (int)result;
            return true;
        }

        private static bool TryAdd(ref int total, int value)
        {
            if (value < 0 || total > int.MaxValue - value) return false;
            total += value;
            return true;
        }

        private static NativeArray<T> Allocate<T>(int length) where T : struct
            => new NativeArray<T>(length, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        private static NativeArray<T> CopyOf<T>(NativeArray<T>.ReadOnly source) where T : struct
        {
            var destination = Allocate<T>(source.Length);
            for (var index = 0; index < source.Length; index++) destination[index] = source[index];
            return destination;
        }

        private static void Copy<T>(NativeArray<T> source, NativeArray<T> destination, int offset)
            where T : struct
        {
            for (var index = 0; index < source.Length; index++) destination[offset + index] = source[index];
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            for (var index = 0; index < array.Length; index++) array[index] = default;
        }

        private void DisposeArrays()
        {
            Dispose(ref _transactionControl);
            Dispose(ref _operations);
            Dispose(ref _commandPayloadBytes);
            Dispose(ref _commands);
            Dispose(ref _valueMarks);
            Dispose(ref _valueStagingBytes);
            Dispose(ref _valueSessions);
            Dispose(ref _completionPayloadBytes);
            Dispose(ref _completions);
            Dispose(ref _bindingValueBytes);
            Dispose(ref _resolvedBindings);
            Dispose(ref _requestStatuses);
            Dispose(ref _randomIncrements);
            Dispose(ref _randomStates);
            Dispose(ref _memoryWritten);
            Dispose(ref _memoryStaging);
            Dispose(ref _memoryBytes);
            Dispose(ref _configurationBytes);
            Dispose(ref _requests);
            Dispose(ref _canonicalRules);
            Dispose(ref _bindingCanonicalRanges);
            Dispose(ref _caseCanonicalRanges);
            Dispose(ref _valueFields);
            Dispose(ref _bindings);
            Dispose(ref _memoryFields);
            Dispose(ref _configurationFields);
            Dispose(ref _cases);
            Dispose(ref _frameCompletionClaim);
            Dispose(ref _executionClaim);
            Dispose(ref _control);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
            array = default;
        }

        private static void Dispose(ref NativeList<int> list)
        {
            if (list.IsCreated) list.Dispose();
            list = default;
        }

        private static void Dispose(ref NativeList<long> list)
        {
            if (list.IsCreated) list.Dispose();
            list = default;
        }

        private readonly struct Capacity
        {
            internal Capacity(
                int configurationBytes, int memoryBytes, int randomStates, int resolvedBindings,
                int liveValueBytes, int valueSessions, int valueStagingBytes, int commands,
                int commandPayloadBytes, int operations, NativeBurstDispatchBindingCapacityV2 bindingCapacity)
            {
                ConfigurationBytes = configurationBytes;
                MemoryBytes = memoryBytes;
                RandomStates = randomStates;
                ResolvedBindings = resolvedBindings;
                LiveValueBytes = liveValueBytes;
                ValueSessions = valueSessions;
                ValueStagingBytes = valueStagingBytes;
                Commands = commands;
                CommandPayloadBytes = commandPayloadBytes;
                Operations = operations;
                BindingCapacity = bindingCapacity;
            }

            internal int ConfigurationBytes { get; }
            internal int MemoryBytes { get; }
            internal int RandomStates { get; }
            internal int ResolvedBindings { get; }
            internal int LiveValueBytes { get; }
            internal int ValueSessions { get; }
            internal int ValueStagingBytes { get; }
            internal int Commands { get; }
            internal int CommandPayloadBytes { get; }
            internal int Operations { get; }
            internal NativeBurstDispatchBindingCapacityV2 BindingCapacity { get; }
        }
    }
}
