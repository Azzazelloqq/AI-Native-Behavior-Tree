using System;
using AIBT.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Execution.Burst.Dispatch
{
    internal sealed class NativeBurstDispatchWorkspaceOwnerV2
    {
        private NativeArray<NativeBurstDispatchControlV2> _control;
        private NativeList<int> _executionClaim;
        private NativeList<long> _frameCompletionClaim;
        private NativeArray<NativeBurstDispatchCaseV2> _cases;
        private NativeArray<NativeBurstDispatchRequestV2> _requests;
        private NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
        private NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
        private NativeArray<NativeBurstDispatchBindingV2> _bindings;
        private NativeArray<NativeBurstDispatchFieldV2> _valueFields;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseCanonicalRanges;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingCanonicalRanges;
        private NativeArray<NativeBurstDispatchCanonicalRuleV2> _canonicalRules;
        private NativeArray<byte> _memoryStaging;
        private NativeArray<byte> _memoryWritten;
        private NativeArray<byte> _requestStatuses;
        private NativeArray<NativeBurstDispatchValueSessionV2> _valueSessions;
        private NativeArray<byte> _valueStagingBytes;
        private NativeArray<byte> _valueMarks;

        private NativeArray<byte> _configurationBytes;
        private NativeArray<byte> _memoryBytes;
        private NativeArray<ulong> _randomStates;
        private NativeArray<ulong> _randomIncrements;
        private NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
        private NativeArray<byte> _bindingValueBytes;
        private NativeArray<NativeBurstDispatchCompletionV2> _completions;
        private NativeArray<byte> _completionPayloadBytes;
        private NativeArray<NativeBurstDispatchCommandV2> _commands;
        private NativeArray<byte> _commandPayloadBytes;
        private NativeArray<NativeBurstDispatchOperationV2> _operations;
        private NativeArray<NativeBurstDispatchTransactionControlV2> _transactionControl;

        private BurstCatalogHandshake _handshake;
        private NativeBurstDispatchWorkspaceCapacityV2 _capacity;
        private bool _hasBindings;
        private bool _hasCanonicalMetadata;
        private NativeBurstDispatchWorkspaceStateV2 _state;
        private ulong _ownerId;
        private uint _generation;
        private ulong _ledgerToken;
        private TreeInstanceId _ledgerTreeInstanceId;
        private ulong _nextOperationSequenceFloor;
        private ulong _mutationVersionFloor;
        private ulong _requestMutationVersion;
        private uint _commandCountFloor;
        private uint _commandPayloadByteCountFloor;
        private uint _operationCountFloor;
        private JobHandle _dependency;
        private bool _hasDependency;
        private bool _disposed;

        private NativeBurstDispatchWorkspaceOwnerV2() { }

        internal NativeBurstDispatchWorkspaceStateV2 State
        {
            get
            {
                RefreshExecutionState();
                return _state;
            }
        }

        internal static bool TryCreate(
            in NativeBurstDispatchWorkspaceShapeV2 shape,
            in NativeBurstDispatchWorkspaceCapacityV2 capacity,
            Allocator allocator,
            out NativeBurstDispatchWorkspaceOwnerV2 owner,
            out BurstContextResult failure)
            => TryCreate(in shape, in capacity, allocator, -1, 1u, out owner, out failure);

        internal static bool TryCreate(
            in NativeBurstDispatchWorkspaceShapeV2 shape,
            in NativeBurstDispatchWorkspaceCapacityV2 capacity,
            Allocator allocator,
            int failAfterAllocation,
            out NativeBurstDispatchWorkspaceOwnerV2 owner,
            out BurstContextResult failure)
            => TryCreate(
                in shape,
                in capacity,
                allocator,
                failAfterAllocation,
                1u,
                out owner,
                out failure);

        internal static bool TryCreate(
            in NativeBurstDispatchWorkspaceShapeV2 shape,
            in NativeBurstDispatchWorkspaceCapacityV2 capacity,
            Allocator allocator,
            int failAfterAllocation,
            uint initialGeneration,
            out NativeBurstDispatchWorkspaceOwnerV2 owner,
            out BurstContextResult failure)
        {
            owner = null;
            failure = BurstContextResult.InvalidEncoding;
            if (initialGeneration == 0
                || !ValidateShape(in shape, in capacity, out failure))
            {
                return false;
            }

            var created = new NativeBurstDispatchWorkspaceOwnerV2();
            var allocations = 0;
            try
            {
                created._control = Allocate<NativeBurstDispatchControlV2>(
                    1, allocator, failAfterAllocation, ref allocations);
                created._executionClaim = AllocateExecutionClaim(
                    allocator, failAfterAllocation, ref allocations);
                created._frameCompletionClaim = AllocateFrameClaim(
                    allocator, failAfterAllocation, ref allocations);
                created._cases = Allocate<NativeBurstDispatchCaseV2>(
                    shape.Cases.Length, allocator, failAfterAllocation, ref allocations);
                created._requests = Allocate<NativeBurstDispatchRequestV2>(
                    1, allocator, failAfterAllocation, ref allocations);
                created._configurationFields = Allocate<NativeBurstDispatchFieldV2>(
                    shape.ConfigurationFields.Length, allocator, failAfterAllocation, ref allocations);
                created._memoryFields = Allocate<NativeBurstDispatchFieldV2>(
                    shape.MemoryFields.Length, allocator, failAfterAllocation, ref allocations);
                // Unity's job transport requires every nested native container to be
                // constructed, including logically absent zero-length tables. Presence
                // remains explicit below; IsCreated is transport state, not semantics.
                created._bindings = Allocate<NativeBurstDispatchBindingV2>(
                    shape.Bindings.IsCreated ? shape.Bindings.Length : 0,
                    allocator,
                    failAfterAllocation,
                    ref allocations);
                created._valueFields = Allocate<NativeBurstDispatchFieldV2>(
                    shape.ValueFields.IsCreated ? shape.ValueFields.Length : 0,
                    allocator,
                    failAfterAllocation,
                    ref allocations);
                created._caseCanonicalRanges = Allocate<NativeBurstDispatchCanonicalRangeV2>(
                    shape.CanonicalInput.IsCreated ? shape.CanonicalInput.CaseRanges.Length : 0,
                    allocator,
                    failAfterAllocation,
                    ref allocations);
                created._bindingCanonicalRanges = Allocate<NativeBurstDispatchCanonicalRangeV2>(
                    shape.CanonicalInput.IsCreated ? shape.CanonicalInput.BindingRanges.Length : 0,
                    allocator,
                    failAfterAllocation,
                    ref allocations);
                created._canonicalRules = Allocate<NativeBurstDispatchCanonicalRuleV2>(
                    shape.CanonicalInput.IsCreated ? shape.CanonicalInput.Rules.Length : 0,
                    allocator,
                    failAfterAllocation,
                    ref allocations);

                created._memoryStaging = Allocate<byte>(
                    (int)capacity.MaxMemoryBytes, allocator, failAfterAllocation, ref allocations);
                created._memoryWritten = Allocate<byte>(
                    (int)capacity.MaxMemoryBytes, allocator, failAfterAllocation, ref allocations);
                created._requestStatuses = Allocate<byte>(
                    1, allocator, failAfterAllocation, ref allocations);
                created._valueSessions = Allocate<NativeBurstDispatchValueSessionV2>(
                    (int)capacity.BindingCapacity.MaxValueSessionsPerFrame,
                    allocator,
                    failAfterAllocation,
                    ref allocations);
                created._valueStagingBytes = Allocate<byte>(
                    (int)capacity.BindingCapacity.MaxValueStagingBytesPerFrame,
                    allocator,
                    failAfterAllocation,
                    ref allocations);
                created._valueMarks = Allocate<byte>(
                    (int)capacity.BindingCapacity.MaxValueStagingBytesPerFrame,
                    allocator,
                    failAfterAllocation,
                    ref allocations);

                Copy(shape.Cases, created._cases);
                Copy(shape.ConfigurationFields, created._configurationFields);
                Copy(shape.MemoryFields, created._memoryFields);
                if (shape.Bindings.IsCreated)
                {
                    Copy(shape.Bindings, created._bindings);
                    Copy(shape.ValueFields, created._valueFields);
                }

                if (shape.CanonicalInput.IsCreated)
                {
                    Copy(shape.CanonicalInput.CaseRanges, created._caseCanonicalRanges);
                    Copy(shape.CanonicalInput.BindingRanges, created._bindingCanonicalRanges);
                    Copy(shape.CanonicalInput.Rules, created._canonicalRules);
                }

                if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
                {
                    created.DisposeOwnedArrays();
                    failure = BurstContextResult.Overflow;
                    return false;
                }

                created._handshake = shape.Handshake;
                created._capacity = capacity;
                created._hasBindings = shape.Bindings.IsCreated;
                created._hasCanonicalMetadata = shape.CanonicalInput.IsCreated;
                created._ownerId = ownerId;
                created._generation = initialGeneration;
                created._control[0] = new NativeBurstDispatchControlV2
                {
                    OwnerId = ownerId,
                    Generation = initialGeneration,
                    State = NativeBurstDispatchStateV2.Ready,
                    ResultCode = BurstExecutionCode.Success
                };
                created._state = NativeBurstDispatchWorkspaceStateV2.Idle;
                owner = created;
                failure = BurstContextResult.Success;
                return true;
            }
            catch (Exception)
            {
                created.DisposeOwnedArrays();
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
        }

        internal bool TryBeginRequest(
            in NativeBurstDispatchWorkspaceRequestViewsV2 views,
            out NativeBurstDispatchWorkspaceLeaseV2 lease,
            out BurstContextResult failure)
        {
            lease = default;
            if (_disposed || !_control.IsCreated)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (_state != NativeBurstDispatchWorkspaceStateV2.Idle || _hasDependency)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            _requests[0] = views.Request;
            if (!ValidateRequestViews(in views, out failure))
            {
                _requests[0] = default;
                return false;
            }

            var durableTransaction = views.TransactionControl[0];
            if (_ledgerToken == 0)
            {
                _ledgerToken = durableTransaction.LedgerToken;
                _ledgerTreeInstanceId = durableTransaction.TreeInstanceId;
                _nextOperationSequenceFloor = durableTransaction.NextOperationSequence;
                _mutationVersionFloor = durableTransaction.MutationVersion;
                _commandCountFloor = durableTransaction.CommandCount;
                _commandPayloadByteCountFloor = durableTransaction.CommandPayloadByteCount;
                _operationCountFloor = durableTransaction.OperationCount;
            }

            _requestMutationVersion = durableTransaction.MutationVersion;

            _configurationBytes = views.ConfigurationBytes;
            _memoryBytes = views.MemoryBytes;
            _randomStates = views.RandomStates;
            _randomIncrements = views.RandomIncrements;
            _resolvedBindings = views.ResolvedBindings;
            _bindingValueBytes = views.BindingValueBytes;
            _completions = views.Completions;
            _completionPayloadBytes = views.CompletionPayloadBytes;
            _commands = views.Commands;
            _commandPayloadBytes = views.CommandPayloadBytes;
            _operations = views.Operations;
            _transactionControl = views.TransactionControl;

            Clear(_memoryStaging);
            Clear(_memoryWritten);
            Clear(_requestStatuses);
            Clear(_valueSessions);
            Clear(_valueStagingBytes);
            Clear(_valueMarks);
            _executionClaim[0] = 0;
            _frameCompletionClaim[0] = 0L;

            var control = _control[0];
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
            _control[0] = control;

            _state = NativeBurstDispatchWorkspaceStateV2.Prepared;
            lease = new NativeBurstDispatchWorkspaceLeaseV2(_ownerId, _generation);
            failure = BurstContextResult.Success;
            return true;
        }

        internal bool TryAcquireImmediateBatch(
            in NativeBurstDispatchWorkspaceLeaseV2 lease,
            out BurstExecutionBatch batch,
            out BurstContextResult failure)
        {
            batch = default;
            if (!ValidateLease(in lease))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (_state != NativeBurstDispatchWorkspaceStateV2.Prepared
                || _hasDependency
                || _executionClaim[0] != 0)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            batch = new BurstExecutionBatch(CreateBacking(), NativeBurstBatchRoleV2.Host);
            _state = NativeBurstDispatchWorkspaceStateV2.Ready;
            failure = BurstContextResult.Success;
            return true;
        }

        internal bool TryRegisterDependency(
            in NativeBurstDispatchWorkspaceLeaseV2 lease,
            in BurstExecutionBatch batch,
            JobHandle dependency,
            out BurstContextResult failure)
        {
            if (!ValidateLease(in lease))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (_hasDependency
                || (_state != NativeBurstDispatchWorkspaceStateV2.Ready
                    && _state != NativeBurstDispatchWorkspaceStateV2.Running)
                || batch.Role != NativeBurstBatchRoleV2.ScheduledHost
                || _executionClaim[0] != 2
                || !batch.MatchesOwner(_control))
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            _dependency = dependency;
            _hasDependency = true;
            failure = BurstContextResult.Success;
            return true;
        }

        internal bool TryAcquireCompletedBatch(
            in NativeBurstDispatchWorkspaceLeaseV2 lease,
            out BurstExecutionBatch batch,
            out BurstContextResult failure)
        {
            batch = default;
            if (!ValidateLease(in lease))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (!_hasDependency || !_dependency.IsCompleted)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            CompleteDependency();
            batch = new BurstExecutionBatch(CreateBacking(), NativeBurstBatchRoleV2.CompletedHost);
            RefreshExecutionState();
            failure = BurstContextResult.Success;
            return true;
        }

        internal bool TryConsumeResult(
            in NativeBurstDispatchWorkspaceLeaseV2 lease,
            out NativeBurstDispatchWorkspaceResultV2 result,
            out BurstContextResult failure)
        {
            result = default;
            if (!ValidateLease(in lease))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (_executionClaim[0] == 2 || !EnsureHostAccess())
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            RefreshExecutionState();
            if (_state != NativeBurstDispatchWorkspaceStateV2.Terminal)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            var control = _control[0];
            var transaction = _transactionControl[0];
            if (!ValidateLedger(
                    in transaction,
                    _requests[0].TreeInstanceId,
                    _commands.Length,
                    _commandPayloadBytes.Length,
                    _operations.Length,
                    true,
                    out failure))
            {
                return false;
            }


            if (control.NextFrameId != 0
                && transaction.MutationVersion <= _requestMutationVersion)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (transaction.NextOperationSequence > _nextOperationSequenceFloor)
            {
                _nextOperationSequenceFloor = transaction.NextOperationSequence;
            }
            if (transaction.MutationVersion > _mutationVersionFloor)
            {
                _mutationVersionFloor = transaction.MutationVersion;
            }
            if (transaction.CommandCount > _commandCountFloor)
            {
                _commandCountFloor = transaction.CommandCount;
            }
            if (transaction.CommandPayloadByteCount > _commandPayloadByteCountFloor)
            {
                _commandPayloadByteCountFloor = transaction.CommandPayloadByteCount;
            }
            if (transaction.OperationCount > _operationCountFloor)
            {
                _operationCountFloor = transaction.OperationCount;
            }
            result = new NativeBurstDispatchWorkspaceResultV2(
                new BurstExecutionResult(
                    control.ResultCode,
                    control.DiagnosticNumber,
                    control.InstancesVisited,
                    control.SegmentSteps),
                _requestStatuses[0],
                control.FirstFailure,
                control.NextFrameId != 0,
                new NativeBurstDispatchTransactionSnapshotV2(
                    transaction.ActiveFrameId,
                    transaction.SessionCount,
                    transaction.StagingByteCount,
                    transaction.CommandCount,
                    transaction.CommandPayloadByteCount,
                    transaction.OperationCount,
                    transaction.NextOperationSequence));
            _state = NativeBurstDispatchWorkspaceStateV2.Consumed;
            failure = BurstContextResult.Success;
            return true;
        }

        internal bool TryReset(
            in NativeBurstDispatchWorkspaceLeaseV2 lease,
            out BurstContextResult failure)
        {
            if (!ValidateLease(in lease))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if ((_executionClaim.IsCreated
                    && _executionClaim.Length == 1
                    && _executionClaim[0] == 2)
                || !EnsureHostAccess())
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            RefreshExecutionState();
            if (_state != NativeBurstDispatchWorkspaceStateV2.Consumed)
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            var control = _control[0];
            if (control.Generation == uint.MaxValue)
            {
                failure = BurstContextResult.Overflow;
                return false;
            }

            control.Generation++;
            _generation = control.Generation;
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
            _control[0] = control;
            _executionClaim[0] = 0;
            _frameCompletionClaim[0] = 0L;
            _requests[0] = default;
            Clear(_memoryStaging);
            Clear(_memoryWritten);
            Clear(_requestStatuses);
            Clear(_valueSessions);
            Clear(_valueStagingBytes);
            Clear(_valueMarks);
            ClearBorrowedViews();
            _state = NativeBurstDispatchWorkspaceStateV2.Idle;
            failure = BurstContextResult.Success;
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

            if (!EnsureHostAccess()
                || (_state != NativeBurstDispatchWorkspaceStateV2.Idle
                    && _state != NativeBurstDispatchWorkspaceStateV2.Consumed))
            {
                failure = BurstContextResult.PhaseViolation;
                return false;
            }

            var control = _control[0];
            control.Generation = control.Generation == uint.MaxValue
                ? control.Generation
                : control.Generation + 1u;
            control.State = NativeBurstDispatchStateV2.Disposed;
            _control[0] = control;
            ClearBorrowedViews();
            DisposeOwnedArrays();
            _disposed = true;
            _state = NativeBurstDispatchWorkspaceStateV2.Disposed;
            failure = BurstContextResult.Success;
            return true;
        }

        private bool ValidateRequestViews(
            in NativeBurstDispatchWorkspaceRequestViewsV2 views,
            out BurstContextResult failure)
        {
            var request = views.Request;
            if (request.CatalogCaseIndex >= _cases.Length
                || !views.ConfigurationBytes.IsCreated
                || !views.MemoryBytes.IsCreated
                || !views.RandomStates.IsCreated
                || !views.RandomIncrements.IsCreated
                || !views.Commands.IsCreated
                || !views.CommandPayloadBytes.IsCreated
                || !views.Operations.IsCreated
                || !views.TransactionControl.IsCreated
                || views.TransactionControl.Length != 1)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }

            var dispatchCase = _cases[(int)request.CatalogCaseIndex];
            var expectedRandomCount = dispatchCase.HasRandomStream != 0 ? 1 : 0;
            if (request.ConfigurationOffset != 0
                || request.MemoryOffset != 0
                || request.RandomStateIndex != 0
                || request.FirstResolvedBinding != 0
                || request.ResolvedBindingCount != dispatchCase.BindingCount
                || views.ConfigurationBytes.Length != dispatchCase.ConfigurationSize
                || views.MemoryBytes.Length != dispatchCase.MemorySize
                || views.RandomStates.Length != expectedRandomCount
                || views.RandomIncrements.Length != expectedRandomCount)
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }

            var bindingCapacity = _capacity.BindingCapacity;
            if (views.Commands.Length != bindingCapacity.MaxCommands
                || views.CommandPayloadBytes.Length != bindingCapacity.MaxCommandPayloadBytes
                || views.Operations.Length != bindingCapacity.MaxOperations)
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }

            var transaction = views.TransactionControl[0];
            if (!ValidateLedger(
                    in transaction,
                    request.TreeInstanceId,
                    views.Commands.Length,
                    views.CommandPayloadBytes.Length,
                    views.Operations.Length,
                    false,
                    out failure))
            {
                return false;
            }

            NativeBurstDispatchBindingInputV2 bindingInput;
            if (_hasBindings)
            {
                if (!views.ResolvedBindings.IsCreated
                    || !views.BindingValueBytes.IsCreated
                    || !views.Completions.IsCreated
                    || !views.CompletionPayloadBytes.IsCreated
                    || views.ResolvedBindings.Length != dispatchCase.BindingCount)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }

                bindingInput = new NativeBurstDispatchBindingInputV2(
                    _bindings.AsReadOnly(),
                    views.ResolvedBindings.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    views.BindingValueBytes.AsReadOnly(),
                    views.Completions.AsReadOnly(),
                    views.CompletionPayloadBytes.AsReadOnly(),
                    bindingCapacity,
                    CanonicalInput());
            }
            else
            {
                if (!views.ResolvedBindings.IsCreated
                    || !views.BindingValueBytes.IsCreated
                    || !views.Completions.IsCreated
                    || !views.CompletionPayloadBytes.IsCreated
                    || views.ResolvedBindings.Length != 0
                    || views.BindingValueBytes.Length != 0
                    || views.Completions.Length != 0
                    || views.CompletionPayloadBytes.Length != 0)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }

                bindingInput = default;
            }

            var input = new NativeBurstDispatchCreateInputV2(
                _handshake,
                _cases.AsReadOnly(),
                _requests.AsReadOnly(),
                _configurationFields.AsReadOnly(),
                _memoryFields.AsReadOnly(),
                views.ConfigurationBytes.AsReadOnly(),
                views.MemoryBytes.AsReadOnly(),
                views.RandomStates.AsReadOnly(),
                views.RandomIncrements.AsReadOnly(),
                bindingInput,
                CanonicalInput());
            if (!NativeBurstDispatchBatchOwnerV2.ValidateCreateInput(in input))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }

            failure = BurstContextResult.Success;
            return true;
        }

        private bool ValidateLedger(
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

            if (!allowTerminalMutationMaximum
                && transaction.MutationVersion == ulong.MaxValue)
            {
                failure = BurstContextResult.Overflow;
                return false;
            }

            if (_ledgerToken != 0
                && (transaction.LedgerToken != _ledgerToken
                    || transaction.TreeInstanceId != _ledgerTreeInstanceId
                    || transaction.MutationVersion < _mutationVersionFloor
                    || transaction.NextOperationSequence < _nextOperationSequenceFloor
                    || transaction.CommandCount < _commandCountFloor
                    || transaction.CommandPayloadByteCount < _commandPayloadByteCountFloor
                    || transaction.OperationCount < _operationCountFloor))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

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

        private NativeBurstDispatchCanonicalInputV2 CanonicalInput()
            => _hasCanonicalMetadata
                ? new NativeBurstDispatchCanonicalInputV2(
                    _caseCanonicalRanges.AsReadOnly(),
                    _bindingCanonicalRanges.AsReadOnly(),
                    _canonicalRules.AsReadOnly())
                : default;

        private bool ValidateLease(in NativeBurstDispatchWorkspaceLeaseV2 lease)
        {
            if (_disposed || !_control.IsCreated || lease.OwnerId == 0 || lease.Generation == 0)
            {
                return false;
            }

            return lease.OwnerId == _ownerId && lease.Generation == _generation;
        }

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

            CompleteDependency();
            return true;
        }

        private void CompleteDependency()
        {
            _dependency.Complete();
            if (_executionClaim.IsCreated && _executionClaim.Length == 1 && _executionClaim[0] == 2)
            {
                _executionClaim[0] = 3;
            }

            _dependency = default;
            _hasDependency = false;
        }

        private void RefreshExecutionState()
        {
            if (_disposed || !_control.IsCreated
                || _hasDependency
                || (_executionClaim.IsCreated
                    && _executionClaim.Length == 1
                    && _executionClaim[0] == 2)
                || (_state != NativeBurstDispatchWorkspaceStateV2.Ready
                    && _state != NativeBurstDispatchWorkspaceStateV2.Running
                    && _state != NativeBurstDispatchWorkspaceStateV2.Terminal))
            {
                return;
            }

            var dispatchState = _control[0].State;
            if (dispatchState == NativeBurstDispatchStateV2.Running)
            {
                _state = NativeBurstDispatchWorkspaceStateV2.Running;
            }
            else if (dispatchState == NativeBurstDispatchStateV2.Terminal)
            {
                _state = NativeBurstDispatchWorkspaceStateV2.Terminal;
            }
            else if (dispatchState == NativeBurstDispatchStateV2.Ready)
            {
                _state = NativeBurstDispatchWorkspaceStateV2.Ready;
            }
        }

        private void ClearBorrowedViews()
        {
            _configurationBytes = default;
            _memoryBytes = default;
            _randomStates = default;
            _randomIncrements = default;
            _resolvedBindings = default;
            _bindingValueBytes = default;
            _completions = default;
            _completionPayloadBytes = default;
            _commands = default;
            _commandPayloadBytes = default;
            _operations = default;
            _transactionControl = default;
        }

        private static bool ValidateShape(
            in NativeBurstDispatchWorkspaceShapeV2 shape,
            in NativeBurstDispatchWorkspaceCapacityV2 capacity,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.InvalidEncoding;
            if (!shape.Cases.IsCreated
                || !shape.ConfigurationFields.IsCreated
                || !shape.MemoryFields.IsCreated
                || shape.Cases.Length == 0
                || !FitsInt(capacity.MaxMemoryBytes)
                || !FitsInt(capacity.BindingCapacity.MaxValueSessionsPerFrame)
                || !FitsInt(capacity.BindingCapacity.MaxValueStagingBytesPerFrame)
                || !FitsInt(capacity.BindingCapacity.MaxCommands)
                || !FitsInt(capacity.BindingCapacity.MaxCommandPayloadBytes)
                || !FitsInt(capacity.BindingCapacity.MaxOperations))
            {
                return false;
            }

            uint bindingCursor = 0;
            uint configurationCursor = 0;
            uint memoryCursor = 0;
            for (var caseIndex = 0; caseIndex < shape.Cases.Length; caseIndex++)
            {
                var dispatchCase = shape.Cases[caseIndex];
                if (dispatchCase.TypeNumericId == 0
                    || dispatchCase.TypeVersion == 0
                    || dispatchCase.CatalogCaseIndex != (uint)caseIndex
                    || dispatchCase.FirstBinding != bindingCursor
                    || dispatchCase.Phases == NativeBurstDispatchPhaseMaskV2.None
                    || ((byte)dispatchCase.Phases & ~0x1f) != 0
                    || ((byte)dispatchCase.PossibleStatuses & ~0x07) != 0
                    || dispatchCase.HasRandomStream > 1
                    || dispatchCase.FirstConfigurationField != configurationCursor
                    || dispatchCase.FirstMemoryField != memoryCursor
                    || dispatchCase.MemorySize > capacity.MaxMemoryBytes
                    || !NativeBurstDispatchBatchOwnerV2.ValidateFieldRange(
                        shape.ConfigurationFields,
                        dispatchCase.FirstConfigurationField,
                        dispatchCase.ConfigurationFieldCount,
                        dispatchCase.ConfigurationSize,
                        true)
                    || !NativeBurstDispatchBatchOwnerV2.ValidateFieldRange(
                        shape.MemoryFields,
                        dispatchCase.FirstMemoryField,
                        dispatchCase.MemoryFieldCount,
                        dispatchCase.MemorySize,
                        false))
                {
                    if (dispatchCase.MemorySize > capacity.MaxMemoryBytes)
                    {
                        failure = BurstContextResult.CapacityExceeded;
                    }

                    return false;
                }

                bindingCursor += dispatchCase.BindingCount;
                configurationCursor += dispatchCase.ConfigurationFieldCount;
                memoryCursor += dispatchCase.MemoryFieldCount;
            }

            if (configurationCursor != shape.ConfigurationFields.Length
                || memoryCursor != shape.MemoryFields.Length)
            {
                return false;
            }

            var bindingCapacity = capacity.BindingCapacity;
            if (!shape.Bindings.IsCreated)
            {
                if (bindingCursor != 0
                    || shape.ValueFields.IsCreated
                    || HasBindingCapacity(in bindingCapacity))
                {
                    return false;
                }
            }
            else if (shape.Bindings.Length == 0
                || !shape.ValueFields.IsCreated
                || bindingCursor != shape.Bindings.Length
                || !shape.CanonicalInput.CaseRanges.IsCreated
                || !shape.CanonicalInput.BindingRanges.IsCreated
                || !shape.CanonicalInput.Rules.IsCreated
                || bindingCapacity.FirstOperationSequence == 0)
            {
                return false;
            }

            var canonical = shape.CanonicalInput;
            if (canonical.IsCreated
                && (!canonical.BindingRanges.IsCreated || !canonical.Rules.IsCreated))
            {
                return false;
            }

            if (!NativeBurstDispatchBindingValidationV2.ValidateShapeMetadata(
                    shape.Cases,
                    shape.ConfigurationFields,
                    shape.MemoryFields,
                    shape.Bindings,
                    shape.ValueFields,
                    in canonical))
            {
                return false;
            }

            if (shape.Bindings.IsCreated)
            {
                uint requiredSessions = 0;
                uint requiredStagingBytes = 0;
                for (var index = 0; index < shape.Bindings.Length; index++)
                {
                    var binding = shape.Bindings[index];
                    var sessions = binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation
                        ? 2u
                        : 1u;
                    var stagingBytes = binding.PrimaryValueSize
                        + (binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation
                            ? binding.SecondaryValueSize
                            : 0u);
                    if (sessions > requiredSessions) requiredSessions = sessions;
                    if (stagingBytes > requiredStagingBytes) requiredStagingBytes = stagingBytes;
                }

                if (bindingCapacity.MaxValueSessionsPerFrame < requiredSessions
                    || bindingCapacity.MaxValueStagingBytesPerFrame < requiredStagingBytes)
                {
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }
            }

            failure = BurstContextResult.Success;
            return true;
        }

        private static bool HasBindingCapacity(in NativeBurstDispatchBindingCapacityV2 capacity)
            => capacity.MaxValueSessionsPerFrame != 0
                || capacity.MaxValueStagingBytesPerFrame != 0
                || capacity.MaxCommands != 0
                || capacity.MaxCommandPayloadBytes != 0
                || capacity.MaxOperations != 0
                || capacity.FirstOperationSequence != 0;

        private static bool FitsInt(uint value) => value <= int.MaxValue;

        private static NativeArray<T> Allocate<T>(
            int length,
            Allocator allocator,
            int failAfter,
            ref int allocations)
            where T : struct
        {
            if (failAfter >= 0 && allocations == failAfter)
            {
                throw new InvalidOperationException("Injected native dispatch workspace allocation failure.");
            }

            var array = new NativeArray<T>(length, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return array;
        }

        private static NativeList<int> AllocateExecutionClaim(
            Allocator allocator,
            int failAfter,
            ref int allocations)
        {
            if (failAfter >= 0 && allocations == failAfter)
            {
                throw new InvalidOperationException("Injected native dispatch workspace allocation failure.");
            }

            var result = new NativeList<int>(1, allocator);
            result.Add(0);
            allocations++;
            return result;
        }

        private static NativeList<long> AllocateFrameClaim(
            Allocator allocator,
            int failAfter,
            ref int allocations)
        {
            if (failAfter >= 0 && allocations == failAfter)
            {
                throw new InvalidOperationException("Injected native dispatch workspace allocation failure.");
            }

            var result = new NativeList<long>(1, allocator);
            result.Add(0L);
            allocations++;
            return result;
        }

        private static void Copy<T>(NativeArray<T>.ReadOnly source, NativeArray<T> destination)
            where T : struct
        {
            for (var index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            for (var index = 0; index < array.Length; index++)
            {
                array[index] = default;
            }
        }

        private void DisposeOwnedArrays()
        {
            Dispose(ref _valueMarks);
            Dispose(ref _valueStagingBytes);
            Dispose(ref _valueSessions);
            Dispose(ref _requestStatuses);
            Dispose(ref _memoryWritten);
            Dispose(ref _memoryStaging);
            Dispose(ref _canonicalRules);
            Dispose(ref _bindingCanonicalRanges);
            Dispose(ref _caseCanonicalRanges);
            Dispose(ref _valueFields);
            Dispose(ref _bindings);
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
