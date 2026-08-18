using System;
using AIBT.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Execution.Burst.Dispatch
{
    internal sealed class NativeBurstDispatchBatchOwnerV2
    {
        private NativeArray<NativeBurstDispatchControlV2> _control;
        private NativeList<int> _executionClaim;
        private NativeList<long> _frameCompletionClaim;
        private NativeArray<NativeBurstDispatchCaseV2> _cases;
        private NativeArray<NativeBurstDispatchRequestV2> _requests;
        private NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
        private NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
        private NativeArray<byte> _configurationBytes;
        private NativeArray<byte> _memoryBytes;
        private NativeArray<byte> _memoryStaging;
        private NativeArray<byte> _memoryWritten;
        private NativeArray<ulong> _randomStates;
        private NativeArray<ulong> _randomIncrements;
        private NativeArray<byte> _requestStatuses;
        private NativeArray<NativeBurstDispatchBindingV2> _bindings;
        private NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
        private NativeArray<NativeBurstDispatchFieldV2> _valueFields;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseCanonicalRanges;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingCanonicalRanges;
        private NativeArray<NativeBurstDispatchCanonicalRuleV2> _canonicalRules;
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
        private BurstCatalogHandshake _handshake;
        private JobHandle _dependency;
        private bool _hasDependency;
        private bool _disposed;

        private NativeBurstDispatchBatchOwnerV2() { }

        internal static bool TryCreate(
            in NativeBurstDispatchCreateInputV2 input,
            Allocator allocator,
            out NativeBurstDispatchBatchOwnerV2 owner,
            out BurstContextResult failure)
            => TryCreate(in input, allocator, -1, out owner, out failure);

        internal static bool TryCreate(
            in NativeBurstDispatchCreateInputV2 input,
            Allocator allocator,
            int failAfterAllocation,
            out NativeBurstDispatchBatchOwnerV2 owner,
            out BurstContextResult failure)
        {
            owner = null;
            if (!ValidateCreateInput(in input))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }

            var created = new NativeBurstDispatchBatchOwnerV2();
            var allocations = 0;
            try
            {
                created._control = Allocate<NativeBurstDispatchControlV2>(1, allocator, failAfterAllocation, ref allocations);
                created._executionClaim = AllocateExecutionClaim(allocator, failAfterAllocation, ref allocations);
                created._frameCompletionClaim = AllocateFrameClaim(allocator, failAfterAllocation, ref allocations);
                created._cases = Allocate<NativeBurstDispatchCaseV2>(input.Cases.Length, allocator, failAfterAllocation, ref allocations);
                created._requests = Allocate<NativeBurstDispatchRequestV2>(input.Requests.Length, allocator, failAfterAllocation, ref allocations);
                created._configurationFields = Allocate<NativeBurstDispatchFieldV2>(input.ConfigurationFields.Length, allocator, failAfterAllocation, ref allocations);
                created._memoryFields = Allocate<NativeBurstDispatchFieldV2>(input.MemoryFields.Length, allocator, failAfterAllocation, ref allocations);
                created._configurationBytes = Allocate<byte>(input.ConfigurationBytes.Length, allocator, failAfterAllocation, ref allocations);
                created._memoryBytes = Allocate<byte>(input.MemoryBytes.Length, allocator, failAfterAllocation, ref allocations);
                created._memoryStaging = Allocate<byte>(input.MemoryBytes.Length, allocator, failAfterAllocation, ref allocations);
                created._memoryWritten = Allocate<byte>(input.MemoryBytes.Length, allocator, failAfterAllocation, ref allocations);
                created._randomStates = Allocate<ulong>(input.RandomStates.Length, allocator, failAfterAllocation, ref allocations);
                created._randomIncrements = Allocate<ulong>(input.RandomIncrements.Length, allocator, failAfterAllocation, ref allocations);
                created._requestStatuses = Allocate<byte>(input.Requests.Length, allocator, failAfterAllocation, ref allocations);
                var bindingInput = input.BindingInput;
                var bindingCount = bindingInput.IsEnabled ? bindingInput.Bindings.Length : 0;
                var valueFieldCount = bindingInput.IsEnabled ? bindingInput.ValueFields.Length : 0;
                var resolvedBindingCount = bindingInput.IsEnabled ? bindingInput.ResolvedBindings.Length : 0;
                var canonicalInput = input.CanonicalInput;
                var caseCanonicalRangeCount = canonicalInput.IsCreated ? canonicalInput.CaseRanges.Length : 0;
                var bindingCanonicalRangeCount = canonicalInput.IsCreated ? canonicalInput.BindingRanges.Length : 0;
                var canonicalRuleCount = canonicalInput.IsCreated ? canonicalInput.Rules.Length : 0;
                var bindingValueByteCount = bindingInput.IsEnabled ? bindingInput.LiveValueBytes.Length : 0;
                var completionCount = bindingInput.IsEnabled ? bindingInput.Completions.Length : 0;
                var completionPayloadByteCount = bindingInput.IsEnabled ? bindingInput.CompletionPayloadBytes.Length : 0;
                var capacity = bindingInput.Capacity;
                created._bindings = Allocate<NativeBurstDispatchBindingV2>(bindingCount, allocator, failAfterAllocation, ref allocations);
                created._resolvedBindings = Allocate<NativeBurstDispatchResolvedBindingV2>(resolvedBindingCount, allocator, failAfterAllocation, ref allocations);
                created._valueFields = Allocate<NativeBurstDispatchFieldV2>(valueFieldCount, allocator, failAfterAllocation, ref allocations);
                created._caseCanonicalRanges = Allocate<NativeBurstDispatchCanonicalRangeV2>(caseCanonicalRangeCount, allocator, failAfterAllocation, ref allocations);
                created._bindingCanonicalRanges = Allocate<NativeBurstDispatchCanonicalRangeV2>(bindingCanonicalRangeCount, allocator, failAfterAllocation, ref allocations);
                created._canonicalRules = Allocate<NativeBurstDispatchCanonicalRuleV2>(canonicalRuleCount, allocator, failAfterAllocation, ref allocations);
                created._bindingValueBytes = Allocate<byte>(bindingValueByteCount, allocator, failAfterAllocation, ref allocations);
                created._completions = Allocate<NativeBurstDispatchCompletionV2>(completionCount, allocator, failAfterAllocation, ref allocations);
                created._completionPayloadBytes = Allocate<byte>(completionPayloadByteCount, allocator, failAfterAllocation, ref allocations);
                created._valueSessions = Allocate<NativeBurstDispatchValueSessionV2>((int)capacity.MaxValueSessionsPerFrame, allocator, failAfterAllocation, ref allocations);
                created._valueStagingBytes = Allocate<byte>((int)capacity.MaxValueStagingBytesPerFrame, allocator, failAfterAllocation, ref allocations);
                created._valueMarks = Allocate<byte>((int)capacity.MaxValueStagingBytesPerFrame, allocator, failAfterAllocation, ref allocations);
                created._commands = Allocate<NativeBurstDispatchCommandV2>((int)capacity.MaxCommands, allocator, failAfterAllocation, ref allocations);
                created._commandPayloadBytes = Allocate<byte>((int)capacity.MaxCommandPayloadBytes, allocator, failAfterAllocation, ref allocations);
                created._operations = Allocate<NativeBurstDispatchOperationV2>((int)capacity.MaxOperations, allocator, failAfterAllocation, ref allocations);
                created._transactionControl = Allocate<NativeBurstDispatchTransactionControlV2>(1, allocator, failAfterAllocation, ref allocations);

                Copy(input.Cases, created._cases);
                Copy(input.Requests, created._requests);
                Copy(input.ConfigurationFields, created._configurationFields);
                Copy(input.MemoryFields, created._memoryFields);
                Copy(input.ConfigurationBytes, created._configurationBytes);
                Copy(input.MemoryBytes, created._memoryBytes);
                Copy(input.RandomStates, created._randomStates);
                Copy(input.RandomIncrements, created._randomIncrements);
                if (bindingInput.IsEnabled)
                {
                    Copy(bindingInput.Bindings, created._bindings);
                    Copy(bindingInput.ResolvedBindings, created._resolvedBindings);
                    Copy(bindingInput.ValueFields, created._valueFields);
                    Copy(bindingInput.LiveValueBytes, created._bindingValueBytes);
                    Copy(bindingInput.Completions, created._completions);
                    Copy(bindingInput.CompletionPayloadBytes, created._completionPayloadBytes);
                }

                if (canonicalInput.IsCreated)
                {
                    Copy(canonicalInput.CaseRanges, created._caseCanonicalRanges);
                    Copy(canonicalInput.BindingRanges, created._bindingCanonicalRanges);
                    Copy(canonicalInput.Rules, created._canonicalRules);
                }

                if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
                {
                    created.DisposeArrays();
                    failure = BurstContextResult.Overflow;
                    return false;
                }

                created._handshake = input.Handshake;
                created._transactionControl[0] = new NativeBurstDispatchTransactionControlV2
                {
                    NextOperationSequence = capacity.FirstOperationSequence == 0
                        ? 1UL
                        : capacity.FirstOperationSequence
                };
                created._control[0] = new NativeBurstDispatchControlV2
                {
                    OwnerId = ownerId,
                    Generation = 1,
                    State = input.Requests.Length == 0
                        ? NativeBurstDispatchStateV2.Terminal
                        : NativeBurstDispatchStateV2.Ready,
                    ResultCode = BurstExecutionCode.Success
                };
                owner = created;
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

        internal bool TryAcquireImmediateBatch(out BurstExecutionBatch batch)
        {
            batch = default;
            if (_disposed || !_control.IsCreated)
            {
                return false;
            }

            var control = _control[0];
            if (control.State == NativeBurstDispatchStateV2.Disposed
                || _executionClaim[0] != 0
                || _hasDependency)
            {
                return false;
            }

            batch = new BurstExecutionBatch(CreateBacking(), NativeBurstBatchRoleV2.Host);
            return true;
        }

        internal bool TryRegisterDependency(in BurstExecutionBatch batch, JobHandle dependency)
        {
            if (_disposed
                || _hasDependency
                || batch.Role != NativeBurstBatchRoleV2.ScheduledHost
                || !_executionClaim.IsCreated
                || _executionClaim[0] != 2
                || !batch.MatchesOwner(_control))
            {
                return false;
            }

            _dependency = dependency;
            _hasDependency = true;
            return true;
        }

        internal bool TryAcquireCompletedBatch(out BurstExecutionBatch batch)
        {
            batch = default;
            if (_disposed || !_hasDependency || !_dependency.IsCompleted)
            {
                return false;
            }

            _dependency.Complete();
            SealCompletedSchedule();
            batch = new BurstExecutionBatch(CreateBacking(), NativeBurstBatchRoleV2.CompletedHost);
            return true;
        }

        internal bool TryReadMemoryInt32(uint requestOrdinal, uint fieldOrdinal, out int value)
        {
            value = default;
            if (_disposed || !EnsureHostAccess() || requestOrdinal >= _requests.Length)
            {
                return false;
            }

            var request = _requests[(int)requestOrdinal];
            var dispatchCase = _cases[(int)request.CatalogCaseIndex];
            if (fieldOrdinal >= dispatchCase.MemoryFieldCount)
            {
                return false;
            }

            var field = _memoryFields[(int)(dispatchCase.FirstMemoryField + fieldOrdinal)];
            if (field.Encoding != NativeBurstDispatchFieldEncodingV2.Int32
                || field.ElementCount != 1
                || !TryReadInt32LittleEndian(_memoryBytes, request.MemoryOffset + field.ByteOffset, out value))
            {
                value = default;
                return false;
            }

            return true;
        }

        internal bool TryReadCommittedRandomState(uint requestOrdinal, out ulong state)
        {
            state = default;
            if (_disposed || !EnsureHostAccess() || requestOrdinal >= _requests.Length)
            {
                return false;
            }

            var index = _requests[(int)requestOrdinal].RandomStateIndex;
            if (index >= _randomStates.Length)
            {
                return false;
            }

            state = _randomStates[(int)index];
            return true;
        }

        internal bool TryGetRequestStatus(uint requestOrdinal, out NodeStatus status)
        {
            status = default;
            if (_disposed || !EnsureHostAccess() || requestOrdinal >= _requestStatuses.Length)
            {
                return false;
            }

            status = (NodeStatus)_requestStatuses[(int)requestOrdinal];
            return true;
        }

        internal bool TryReadBindingValueByte(uint byteOffset, out byte value)
        {
            value = default;
            if (_disposed || !EnsureHostAccess() || byteOffset >= _bindingValueBytes.Length)
            {
                return false;
            }

            value = _bindingValueBytes[(int)byteOffset];
            return true;
        }

        internal bool TryReadCommittedMemoryByte(
            uint requestOrdinal,
            uint relativeByteOffset,
            out byte value)
        {
            value = default;
            if (_disposed || !EnsureHostAccess() || requestOrdinal >= _requests.Length)
            {
                return false;
            }

            var request = _requests[(int)requestOrdinal];
            var dispatchCase = _cases[(int)request.CatalogCaseIndex];
            if (relativeByteOffset >= dispatchCase.MemorySize
                || (ulong)request.MemoryOffset + relativeByteOffset >= (ulong)_memoryBytes.Length)
            {
                return false;
            }

            value = _memoryBytes[(int)(request.MemoryOffset + relativeByteOffset)];
            return true;
        }

        internal bool TryGetCompletion(
            uint completionOrdinal,
            out NativeBurstDispatchCompletionV2 completion)
        {
            completion = default;
            if (_disposed || !EnsureHostAccess() || completionOrdinal >= _completions.Length)
            {
                return false;
            }

            completion = _completions[(int)completionOrdinal];
            return true;
        }

        internal bool TryGetPublishedCommand(
            uint commandOrdinal,
            out NativeBurstDispatchCommandV2 command)
        {
            command = default;
            if (!TryGetTransaction(out var transaction)
                || commandOrdinal >= transaction.CommandCount)
            {
                return false;
            }

            command = _commands[(int)commandOrdinal];
            return true;
        }

        internal bool TryReadPublishedCommandPayloadByte(uint byteOffset, out byte value)
        {
            value = default;
            if (!TryGetTransaction(out var transaction)
                || byteOffset >= transaction.CommandPayloadByteCount)
            {
                return false;
            }

            value = _commandPayloadBytes[(int)byteOffset];
            return true;
        }

        internal bool TryGetPublishedOperation(
            uint operationOrdinal,
            out NativeBurstDispatchOperationV2 operation)
        {
            operation = default;
            if (!TryGetTransaction(out var transaction)
                || operationOrdinal >= transaction.OperationCount)
            {
                return false;
            }

            operation = _operations[(int)operationOrdinal];
            return true;
        }

        internal bool TryGetTransactionSnapshot(
            out NativeBurstDispatchTransactionSnapshotV2 snapshot)
        {
            snapshot = default;
            if (!TryGetTransaction(out var transaction))
            {
                return false;
            }

            snapshot = new NativeBurstDispatchTransactionSnapshotV2(
                transaction.ActiveFrameId,
                transaction.SessionCount,
                transaction.StagingByteCount,
                transaction.CommandCount,
                transaction.CommandPayloadByteCount,
                transaction.OperationCount,
                transaction.NextOperationSequence);
            return true;
        }

        internal bool TryDispose(out BurstContextResult failure)
        {
            if (_disposed)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (_executionClaim.IsCreated
                && _executionClaim.Length == 1
                && _executionClaim[0] == 2)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            if (_hasDependency && !_dependency.IsCompleted)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            if (_hasDependency)
            {
                _dependency.Complete();
                SealCompletedSchedule();
            }

            if (_control.IsCreated)
            {
                var control = _control[0];
                if (control.State == NativeBurstDispatchStateV2.Running
                    || control.ActiveFrameId != 0)
                {
                    failure = BurstContextResult.PhaseViolation;
                    return false;
                }

                control.Generation = control.Generation == uint.MaxValue ? control.Generation : control.Generation + 1;
                control.State = NativeBurstDispatchStateV2.Disposed;
                _control[0] = control;
            }

            DisposeArrays();
            _disposed = true;
            failure = BurstContextResult.Success;
            return true;
        }

        private BurstDispatchBackingV2 CreateBacking()
            => new BurstDispatchBackingV2(
                _handshake,
                _control,
                _executionClaim,
                _frameCompletionClaim,
                _cases,
                _requests,
                _configurationFields,
                _memoryFields,
                _configurationBytes,
                _memoryBytes,
                _memoryStaging,
                _memoryWritten,
                _randomStates,
                _randomIncrements,
                _requestStatuses,
                _bindings,
                _resolvedBindings,
                _valueFields,
                _caseCanonicalRanges,
                _bindingCanonicalRanges,
                _canonicalRules,
                _bindingValueBytes,
                _completions,
                _completionPayloadBytes,
                _valueSessions,
                _valueStagingBytes,
                _valueMarks,
                _commands,
                _commandPayloadBytes,
                _operations,
                _transactionControl);

        private bool EnsureHostAccess()
        {
            if (!_hasDependency)
            {
                return true;
            }

            if (!_dependency.IsCompleted)
            {
                return false;
            }

            _dependency.Complete();
            SealCompletedSchedule();
            return true;
        }

        private bool TryGetTransaction(out NativeBurstDispatchTransactionControlV2 transaction)
        {
            transaction = default;
            if (_disposed
                || !EnsureHostAccess()
                || !_transactionControl.IsCreated
                || _transactionControl.Length != 1)
            {
                return false;
            }

            transaction = _transactionControl[0];
            return transaction.CommandCount <= _commands.Length
                && transaction.CommandPayloadByteCount <= _commandPayloadBytes.Length
                && transaction.OperationCount <= _operations.Length;
        }

        private void SealCompletedSchedule()
        {
            if (_executionClaim.IsCreated && _executionClaim.Length == 1 && _executionClaim[0] == 2)
            {
                _executionClaim[0] = 3;
            }
        }

        internal static bool ValidateCreateInput(in NativeBurstDispatchCreateInputV2 input)
        {
            if (!input.Cases.IsCreated
                || !input.Requests.IsCreated
                || !input.ConfigurationFields.IsCreated
                || !input.MemoryFields.IsCreated
                || !input.ConfigurationBytes.IsCreated
                || !input.MemoryBytes.IsCreated
                || !input.RandomStates.IsCreated
                || !input.RandomIncrements.IsCreated
                || input.Cases.Length == 0
                || input.RandomStates.Length != input.RandomIncrements.Length)
            {
                return false;
            }

            for (var caseIndex = 0; caseIndex < input.Cases.Length; caseIndex++)
            {
                var dispatchCase = input.Cases[caseIndex];
                if (dispatchCase.TypeNumericId == 0
                    || dispatchCase.TypeVersion == 0
                    || dispatchCase.CatalogCaseIndex != (uint)caseIndex
                    || dispatchCase.Phases == NativeBurstDispatchPhaseMaskV2.None
                    || ((byte)dispatchCase.Phases & ~0x1f) != 0
                    || ((byte)dispatchCase.PossibleStatuses & ~0x07) != 0
                    || dispatchCase.HasRandomStream > 1
                    || !ValidateFieldRange(
                        input.ConfigurationFields,
                        dispatchCase.FirstConfigurationField,
                        dispatchCase.ConfigurationFieldCount,
                        dispatchCase.ConfigurationSize,
                        true)
                    || !ValidateFieldRange(
                        input.MemoryFields,
                        dispatchCase.FirstMemoryField,
                        dispatchCase.MemoryFieldCount,
                        dispatchCase.MemorySize,
                        false))
                {
                    return false;
                }
            }

            for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
            {
                var request = input.Requests[requestIndex];
                if (request.CatalogCaseIndex >= input.Cases.Length
                    || request.TypeNumericId == 0
                    || request.TypeVersion == 0
                    || !IsPhase(request.Phase)
                    || !IsAbortReason(request.AbortReason)
                    || !IsExitReason(request.ExitReason))
                {
                    return false;
                }

                var dispatchCase = input.Cases[(int)request.CatalogCaseIndex];
                if (!Includes(dispatchCase.Phases, request.Phase)
                    || !Range(request.ConfigurationOffset, dispatchCase.ConfigurationSize, input.ConfigurationBytes.Length)
                    || !Range(request.MemoryOffset, dispatchCase.MemorySize, input.MemoryBytes.Length)
                    || dispatchCase.HasRandomStream != 0
                        && (request.RandomStateIndex >= input.RandomStates.Length
                            || (input.RandomIncrements[(int)request.RandomStateIndex] & 1UL) == 0))
                {
                    return false;
                }
            }

            return NativeBurstDispatchBindingValidationV2.Validate(in input);
        }

        internal static bool ValidateFieldRange(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint first,
            uint count,
            uint storageSize,
            bool allowHandles)
        {
            if (!Range(first, count, fields.Length))
            {
                return false;
            }

            ulong priorEnd = 0;
            NativeBurstDispatchFieldV2 prior = default;
            for (uint descriptorIndex = 0; descriptorIndex < count; descriptorIndex++)
            {
                var field = fields[(int)(first + descriptorIndex)];
                var canonicalIdentity = descriptorIndex == 0
                    ? field.FieldOrdinal == 0 && field.FirstElementIndex == 0
                    : field.FieldOrdinal == prior.FieldOrdinal
                        ? field.FirstElementIndex == prior.FirstElementIndex + prior.ElementCount
                        : field.FieldOrdinal == prior.FieldOrdinal + 1 && field.FirstElementIndex == 0;
                if (!canonicalIdentity
                    || field.ElementCount == 0
                    || field.ElementSize != EncodingSize(field.Encoding)
                    || field.ElementSize == 0
                    || field.Encoding == NativeBurstDispatchFieldEncodingV2.GeneratedHandle && !allowHandles)
                {
                    return false;
                }

                var end = (ulong)field.ByteOffset + (ulong)field.ElementCount * field.ElementSize;
                if (field.ByteOffset < priorEnd || end > storageSize)
                {
                    return false;
                }

                priorEnd = end;
                prior = field;
            }

            return true;
        }

        private static uint EncodingSize(NativeBurstDispatchFieldEncodingV2 encoding)
        {
            switch (encoding)
            {
                case NativeBurstDispatchFieldEncodingV2.Boolean:
                case NativeBurstDispatchFieldEncodingV2.Int8:
                case NativeBurstDispatchFieldEncodingV2.UInt8:
                    return 1;
                case NativeBurstDispatchFieldEncodingV2.Int16:
                case NativeBurstDispatchFieldEncodingV2.UInt16:
                    return 2;
                case NativeBurstDispatchFieldEncodingV2.Int32:
                case NativeBurstDispatchFieldEncodingV2.UInt32:
                case NativeBurstDispatchFieldEncodingV2.Float32:
                case NativeBurstDispatchFieldEncodingV2.GeneratedHandle:
                    return 4;
                case NativeBurstDispatchFieldEncodingV2.Int64:
                case NativeBurstDispatchFieldEncodingV2.UInt64:
                case NativeBurstDispatchFieldEncodingV2.Float64:
                    return 8;
                default:
                    return 0;
            }
        }

        private static bool Includes(NativeBurstDispatchPhaseMaskV2 mask, BurstCallbackPhase phase)
            => (((byte)mask >> (int)phase) & 1) != 0;

        private static bool IsPhase(BurstCallbackPhase phase) => (byte)phase <= (byte)BurstCallbackPhase.Observer;
        private static bool IsAbortReason(BurstNodeAbortReason value) => (byte)value <= (byte)BurstNodeAbortReason.Timeout;
        private static bool IsExitReason(BurstNodeExitReason value) => (byte)value <= (byte)BurstNodeExitReason.Aborted;

        private static bool Range(uint offset, uint count, int length)
            => (ulong)offset + count <= (ulong)length;

        private static NativeArray<T> Allocate<T>(int length, Allocator allocator, int failAfter, ref int allocations)
            where T : struct
        {
            if (failAfter >= 0 && allocations == failAfter)
            {
                throw new InvalidOperationException("Injected native dispatch allocation failure.");
            }

            var array = new NativeArray<T>(length, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return array;
        }

        private static void Copy<T>(NativeArray<T>.ReadOnly source, NativeArray<T> destination)
            where T : struct
        {
            for (var index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static bool TryReadInt32LittleEndian(NativeArray<byte> source, uint offset, out int value)
        {
            value = default;
            if ((ulong)offset + 4UL > (ulong)source.Length)
            {
                return false;
            }

            var bits = source[(int)offset]
                | (uint)source[(int)(offset + 1)] << 8
                | (uint)source[(int)(offset + 2)] << 16
                | (uint)source[(int)(offset + 3)] << 24;
            value = unchecked((int)bits);
            return true;
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
            Dispose(ref _canonicalRules);
            Dispose(ref _bindingCanonicalRanges);
            Dispose(ref _caseCanonicalRanges);
            Dispose(ref _valueFields);
            Dispose(ref _resolvedBindings);
            Dispose(ref _bindings);
            Dispose(ref _requestStatuses);
            Dispose(ref _randomIncrements);
            Dispose(ref _randomStates);
            Dispose(ref _memoryWritten);
            Dispose(ref _memoryStaging);
            Dispose(ref _memoryBytes);
            Dispose(ref _configurationBytes);
            Dispose(ref _memoryFields);
            Dispose(ref _configurationFields);
            Dispose(ref _requests);
            Dispose(ref _cases);
            Dispose(ref _frameCompletionClaim);
            Dispose(ref _executionClaim);
            Dispose(ref _control);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }

            array = default;
        }

        private static NativeList<int> AllocateExecutionClaim(
            Allocator allocator,
            int failAfterAllocation,
            ref int allocations)
        {
            if (allocations == failAfterAllocation)
            {
                throw new InvalidOperationException("Injected native allocation failure.");
            }

            allocations++;
            var claim = new NativeList<int>(1, allocator);
            claim.Add(0);
            return claim;
        }

        private static NativeList<long> AllocateFrameClaim(
            Allocator allocator,
            int failAfterAllocation,
            ref int allocations)
        {
            if (allocations == failAfterAllocation)
            {
                throw new InvalidOperationException("Injected native allocation failure.");
            }

            allocations++;
            var claim = new NativeList<long>(1, allocator);
            claim.Add(0L);
            return claim;
        }

        private static void Dispose(ref NativeList<int> list)
        {
            if (list.IsCreated)
            {
                list.Dispose();
            }

            list = default;
        }

        private static void Dispose(ref NativeList<long> list)
        {
            if (list.IsCreated)
            {
                list.Dispose();
            }

            list = default;
        }
    }
}
