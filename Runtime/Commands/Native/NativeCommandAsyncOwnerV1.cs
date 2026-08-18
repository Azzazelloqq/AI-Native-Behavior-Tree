using System;
using System.Threading;
using AIBT.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    public sealed class NativeCommandAsyncOwnerV1
    {
        private NativeArray<NativeCommandAsyncControlV1> _control;
        private NativeArray<NativeOperationRecordV1> _operations;
        private NativeArray<NativeOperationRecordV1> _operationScratch;
        private NativeArray<byte> _operationPayload;
        private NativeArray<NativePendingCompletionRecordV1> _pending;
        private NativeArray<NativePendingCompletionRecordV1> _pendingScratch;
        private NativeArray<byte> _completionPayload;
        private NativeArray<byte> _completionPayloadScratch;
        private NativeArray<NativeCompletionHighWaterV1> _highWater;
        private NativeArray<NativeCompletionHighWaterV1> _highWaterScratch;
        private NativeArray<NativeCompletionDiagnosticV1> _diagnostics;
        private NativeArray<NativeCompletionDiagnosticV1> _diagnosticScratch;
        private NativeArray<NativeCompletionInputRecordV1> _inputScratch;
        private NativeArray<NativeCommandRecordV1> _executeCommands;
        private NativeArray<NativeCommandRecordV1> _cancelCommands;
        private NativeArray<byte> _commandPayload;
        private JobHandle _dependency;
        private TreeInstanceId _treeInstanceId;
        private ulong _ownerId;
        private uint _generation;
        private ulong _activeLeaseId;
        private byte _state;
        private byte _dependencyRegistered;

        private NativeCommandAsyncOwnerV1()
        {
        }

        public ulong OwnerId => _state == 0 || _state == 3 ? 0 : _ownerId;
        public uint Generation => _state == 0 || _state == 3 ? 0 : _generation;
        public TreeInstanceId TreeInstanceId => _treeInstanceId;

        public static BurstContextResult TryCreate(
            TreeInstanceId treeInstanceId,
            NativeCommandAsyncCapacityV1 capacity,
            Allocator allocator,
            out NativeCommandAsyncOwnerV1 owner)
        {
            return TryCreate(treeInstanceId, capacity, allocator, 1, 1, out owner);
        }

        public static BurstContextResult TryCreate(
            TreeInstanceId treeInstanceId,
            NativeCommandAsyncCapacityV1 capacity,
            Allocator allocator,
            ulong firstOperationSequence,
            ulong firstCommandSequence,
            out NativeCommandAsyncOwnerV1 owner)
        {
            owner = null;
            if (!treeInstanceId.IsValid || !capacity.IsValid || allocator != Allocator.Persistent)
            {
                return BurstContextResult.InvalidEncoding;
            }

            if (firstOperationSequence == 0 || firstCommandSequence == 0)
            {
                return BurstContextResult.Overflow;
            }

            if (!NativeCommandAsyncOwnerIdentityV1.TryNext(out var ownerId))
            {
                return BurstContextResult.Overflow;
            }

            var created = new NativeCommandAsyncOwnerV1
            {
                _treeInstanceId = treeInstanceId,
                _ownerId = ownerId,
                _generation = 1,
            };
            try
            {
                created._control = Allocate<NativeCommandAsyncControlV1>(1, allocator);
                created._operations = Allocate<NativeOperationRecordV1>(capacity.OperationRecords, allocator);
                created._operationScratch = Allocate<NativeOperationRecordV1>(capacity.OperationRecords, allocator);
                created._operationPayload = Allocate<byte>(capacity.OperationCancellationPayloadBytes, allocator);
                created._pending = Allocate<NativePendingCompletionRecordV1>(capacity.PendingCompletionRecords, allocator);
                created._pendingScratch = Allocate<NativePendingCompletionRecordV1>(capacity.PendingCompletionRecords, allocator);
                created._completionPayload = Allocate<byte>(capacity.CompletionPayloadBytes, allocator);
                created._completionPayloadScratch = Allocate<byte>(capacity.CompletionPayloadBytes, allocator);
                created._highWater = Allocate<NativeCompletionHighWaterV1>(capacity.CompletionSources, allocator);
                created._highWaterScratch = Allocate<NativeCompletionHighWaterV1>(capacity.CompletionSources, allocator);
                created._diagnostics = Allocate<NativeCompletionDiagnosticV1>(capacity.DiagnosticRecords, allocator);
                created._diagnosticScratch = Allocate<NativeCompletionDiagnosticV1>(capacity.DiagnosticRecords, allocator);
                created._inputScratch = Allocate<NativeCompletionInputRecordV1>(capacity.CompletionInputRecords, allocator);
                created._executeCommands = Allocate<NativeCommandRecordV1>(capacity.ExecuteCommandRecords, allocator);
                created._cancelCommands = Allocate<NativeCommandRecordV1>(capacity.CancelCommandRecords, allocator);
                created._commandPayload = Allocate<byte>(capacity.CommandPayloadBytes, allocator);

                created._control[0] = new NativeCommandAsyncControlV1
                {
                    State = 1,
                    OwnerId = ownerId,
                    Generation = 1,
                    NextLeaseId = 1,
                    NextOperationSequence = firstOperationSequence,
                    NextCommandSequence = firstCommandSequence,
                    CompletionEpoch = 1,
                };
                created.WarmBorrowedViewSurfaces();
                created._state = 1;
                owner = created;
                return BurstContextResult.Success;
            }
            catch (Exception)
            {
                created.DisposeArrays();
                return BurstContextResult.CapacityExceeded;
            }
        }

        public BurstContextResult TryAcquireExecution(out NativeCommandAsyncLeaseV1 lease)
        {
            lease = default;
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            if (_state != 1)
            {
                return BurstContextResult.PhaseViolation;
            }

            var control = _control[0];
            if (control.NextLeaseId == 0)
            {
                return BurstContextResult.Overflow;
            }

            var leaseId = control.NextLeaseId;
            control.NextLeaseId = leaseId == ulong.MaxValue ? 0 : leaseId + 1;
            control.ActiveLeaseId = leaseId;
            control.DependencyRegistered = 0;
            control.ExecutionSealed = 0;
            control.State = 2;
            _control[0] = control;
            _dependency = default;
            _state = 2;
            _activeLeaseId = leaseId;
            _dependencyRegistered = 0;
            lease = new NativeCommandAsyncLeaseV1(
                control.OwnerId,
                control.Generation,
                leaseId,
                CreateView(leaseId));
            return BurstContextResult.Success;
        }

        public BurstContextResult TryRegisterDependency(in NativeCommandAsyncLeaseV1 lease, JobHandle dependency)
        {
            if (!TryValidateLeaseManaged(lease)) return BurstContextResult.InvalidHandle;
            if (_dependencyRegistered != 0) return BurstContextResult.AlreadyCommitted;
            _dependencyRegistered = 1;
            _dependency = dependency;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryGetCommandStream(
            in NativeCommandAsyncLeaseV1 lease,
            out NativeCommandStreamViewV1 stream)
        {
            stream = default;
            if (!TryValidateLeaseManaged(lease)) return BurstContextResult.InvalidHandle;
            if (_dependencyRegistered == 0 || !_dependency.IsCompleted)
            {
                return BurstContextResult.PhaseViolation;
            }

            _dependency.Complete();
            var control = _control[0];
            control.ExecutionSealed = 1;
            _control[0] = control;
            stream = new NativeCommandStreamViewV1(
                _control.AsReadOnly(),
                _ownerId,
                _generation,
                lease.LeaseId,
                control.NextCommandSequence,
                control.CommandSequenceExhausted,
                _executeCommands.AsReadOnly(),
                control.ExecuteCount,
                _cancelCommands.AsReadOnly(),
                control.CancelCount,
                _commandPayload.AsReadOnly(),
                control.CommandPayloadCount);
            return BurstContextResult.Success;
        }

        public BurstContextResult TryGetDiagnostic(
            in NativeCommandAsyncLeaseV1 lease,
            uint index,
            out NativeCompletionDiagnosticV1 diagnostic)
        {
            diagnostic = default;
            if (!TryValidateLeaseManaged(lease)) return BurstContextResult.InvalidHandle;
            if (_dependencyRegistered == 0 || !_dependency.IsCompleted)
            {
                return BurstContextResult.PhaseViolation;
            }

            _dependency.Complete();
            var control = _control[0];
            if (index >= control.DiagnosticCount) return BurstContextResult.InvalidHandle;
            control.ExecutionSealed = 1;
            _control[0] = control;
            diagnostic = _diagnostics[(int)index];
            return BurstContextResult.Success;
        }

        public BurstContextResult TryRelease(in NativeCommandAsyncLeaseV1 lease)
        {
            if (!TryValidateLeaseManaged(lease)) return BurstContextResult.InvalidHandle;
            if (_dependencyRegistered == 0 || !_dependency.IsCompleted)
            {
                return BurstContextResult.PhaseViolation;
            }

            _dependency.Complete();
            var control = _control[0];
            control.State = 1;
            control.ActiveLeaseId = 0;
            control.DependencyRegistered = 0;
            _control[0] = control;
            _dependency = default;
            _state = 1;
            _activeLeaseId = 0;
            _dependencyRegistered = 0;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryResetPublishedBuffers()
        {
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            if (_state != 1) return BurstContextResult.PhaseViolation;

            var control = _control[0];
            control.ExecuteCount = 0;
            control.CancelCount = 0;
            control.CommandPayloadCount = 0;
            control.DiagnosticCount = 0;
            _control[0] = control;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryGetOperationState(OperationId operationId, out NativeOperationStateV1 state)
        {
            state = NativeOperationStateV1.Empty;
            if (_state == 0 || _state == 3 || operationId.TreeInstanceId != _treeInstanceId)
            {
                return BurstContextResult.InvalidHandle;
            }

            if (_dependencyRegistered != 0)
            {
                if (!_dependency.IsCompleted) return BurstContextResult.PhaseViolation;
                _dependency.Complete();
                var sealedControl = _control[0];
                sealedControl.ExecutionSealed = 1;
                _control[0] = sealedControl;
            }

            var control = _control[0];
            if (control.State == 3) return BurstContextResult.InvalidHandle;
            for (var index = 0; index < control.OperationCount; index++)
            {
                var entry = _operations[index];
                if (entry.OperationId != operationId) continue;
                state = entry.State;
                return BurstContextResult.Success;
            }

            return BurstContextResult.InvalidHandle;
        }

        public BurstContextResult TryDispose()
        {
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            if (_state == 2) return BurstContextResult.PhaseViolation;
            if (_state != 1) return BurstContextResult.InvalidHandle;
            var control = _control[0];
            control.State = 3;
            _control[0] = control;
            _state = 3;
            DisposeArrays();
            return BurstContextResult.Success;
        }

        private NativeCommandAsyncViewV1 CreateView(ulong leaseId)
        {
            return new NativeCommandAsyncViewV1(
                _treeInstanceId,
                _control[0].OwnerId,
                _control[0].Generation,
                leaseId,
                _control,
                _operations,
                _operationScratch,
                _operationPayload,
                _pending,
                _pendingScratch,
                _completionPayload,
                _completionPayloadScratch,
                _highWater,
                _highWaterScratch,
                _diagnostics,
                _diagnosticScratch,
                _inputScratch,
                _executeCommands,
                _cancelCommands,
                _commandPayload);
        }

        private bool TryValidateLeaseManaged(in NativeCommandAsyncLeaseV1 lease)
        {
            return _state == 2
                && lease.IsValid
                && lease.OwnerId == _ownerId
                && lease.Generation == _generation
                && lease.LeaseId == _activeLeaseId;
        }

        private void WarmBorrowedViewSurfaces()
        {
            _ = _control.AsReadOnly().Length;
            _ = _completionPayload.AsReadOnly().Length;
            _ = _executeCommands.AsReadOnly().Length;
            _ = _cancelCommands.AsReadOnly().Length;
            _ = _commandPayload.AsReadOnly().Length;
        }

        private static NativeArray<T> Allocate<T>(uint capacity, Allocator allocator) where T : struct
        {
#if UNITY_INCLUDE_TESTS
            NativeCommandAsyncTestHooksV1.BeforeAllocation();
#endif
            return new NativeArray<T>((int)capacity, allocator, NativeArrayOptions.ClearMemory);
        }

        private void DisposeArrays()
        {
            Dispose(ref _commandPayload);
            Dispose(ref _cancelCommands);
            Dispose(ref _executeCommands);
            Dispose(ref _inputScratch);
            Dispose(ref _diagnosticScratch);
            Dispose(ref _diagnostics);
            Dispose(ref _highWaterScratch);
            Dispose(ref _highWater);
            Dispose(ref _completionPayloadScratch);
            Dispose(ref _completionPayload);
            Dispose(ref _pendingScratch);
            Dispose(ref _pending);
            Dispose(ref _operationPayload);
            Dispose(ref _operationScratch);
            Dispose(ref _operations);
            Dispose(ref _control);
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        {
            if (!value.IsCreated) return;
            value.Dispose();
            value = default;
        }
    }

    public struct NativeCommandAsyncViewV1
    {
        private TreeInstanceId _treeInstanceId;
        private ulong _ownerId;
        private uint _generation;
        private ulong _leaseId;
        private NativeArray<NativeCommandAsyncControlV1> _control;
        private NativeArray<NativeOperationRecordV1> _operations;
        private NativeArray<NativeOperationRecordV1> _operationScratch;
        private NativeArray<byte> _operationPayload;
        private NativeArray<NativePendingCompletionRecordV1> _pending;
        private NativeArray<NativePendingCompletionRecordV1> _pendingScratch;
        private NativeArray<byte> _completionPayload;
        private NativeArray<byte> _completionPayloadScratch;
        private NativeArray<NativeCompletionHighWaterV1> _highWater;
        private NativeArray<NativeCompletionHighWaterV1> _highWaterScratch;
        private NativeArray<NativeCompletionDiagnosticV1> _diagnostics;
        private NativeArray<NativeCompletionDiagnosticV1> _diagnosticScratch;
        private NativeArray<NativeCompletionInputRecordV1> _inputScratch;
        private NativeArray<NativeCommandRecordV1> _executeCommands;
        private NativeArray<NativeCommandRecordV1> _cancelCommands;
        private NativeArray<byte> _commandPayload;

        internal NativeCommandAsyncViewV1(
            TreeInstanceId treeInstanceId,
            ulong ownerId,
            uint generation,
            ulong leaseId,
            NativeArray<NativeCommandAsyncControlV1> control,
            NativeArray<NativeOperationRecordV1> operations,
            NativeArray<NativeOperationRecordV1> operationScratch,
            NativeArray<byte> operationPayload,
            NativeArray<NativePendingCompletionRecordV1> pending,
            NativeArray<NativePendingCompletionRecordV1> pendingScratch,
            NativeArray<byte> completionPayload,
            NativeArray<byte> completionPayloadScratch,
            NativeArray<NativeCompletionHighWaterV1> highWater,
            NativeArray<NativeCompletionHighWaterV1> highWaterScratch,
            NativeArray<NativeCompletionDiagnosticV1> diagnostics,
            NativeArray<NativeCompletionDiagnosticV1> diagnosticScratch,
            NativeArray<NativeCompletionInputRecordV1> inputScratch,
            NativeArray<NativeCommandRecordV1> executeCommands,
            NativeArray<NativeCommandRecordV1> cancelCommands,
            NativeArray<byte> commandPayload)
        {
            _treeInstanceId = treeInstanceId;
            _ownerId = ownerId;
            _generation = generation;
            _leaseId = leaseId;
            _control = control;
            _operations = operations;
            _operationScratch = operationScratch;
            _operationPayload = operationPayload;
            _pending = pending;
            _pendingScratch = pendingScratch;
            _completionPayload = completionPayload;
            _completionPayloadScratch = completionPayloadScratch;
            _highWater = highWater;
            _highWaterScratch = highWaterScratch;
            _diagnostics = diagnostics;
            _diagnosticScratch = diagnosticScratch;
            _inputScratch = inputScratch;
            _executeCommands = executeCommands;
            _cancelCommands = cancelCommands;
            _commandPayload = commandPayload;
        }

        public BurstContextResult TryGetDiagnostic(uint index, out NativeCompletionDiagnosticV1 diagnostic)
        {
            diagnostic = default;
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            if (index >= control.DiagnosticCount) return BurstContextResult.InvalidHandle;
            diagnostic = _diagnostics[(int)index];
            return BurstContextResult.Success;
        }

        public BurstContextResult TryStart(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            CommandType startCommand,
            CommandType cancelCommand,
            NativePayloadSliceV1 startPayload,
            NativePayloadSliceV1 faultCancelPayload,
            out OperationId operationId)
        {
            operationId = default;
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            if (!nodeIndex.IsValid || activationGeneration == 0 || !startCommand.IsValid || !cancelCommand.IsValid)
            {
                return BurstContextResult.InvalidEncoding;
            }

            if (!startPayload.IsValid || !faultCancelPayload.IsValid) return BurstContextResult.InvalidEncoding;
            if (control.OperationSequenceExhausted != 0) return BurstContextResult.Overflow;
            if (control.OperationCount >= (uint)_operations.Length) return BurstContextResult.CapacityExceeded;
            if ((ulong)control.OperationPayloadCount + faultCancelPayload.Size > (ulong)_operationPayload.Length)
            {
                return BurstContextResult.CapacityExceeded;
            }

            var append = CanAppendCommand(control, CommandPhase.Execute, startPayload.Size);
            if (append != BurstContextResult.Success) return append;

            operationId = new OperationId(_treeInstanceId, nodeIndex, activationGeneration, control.NextOperationSequence);
            var faultOffset = faultCancelPayload.Size == 0 ? 0u : control.OperationPayloadCount;
            CopyPayload(faultCancelPayload, _operationPayload, faultOffset);
            AppendCommandUnchecked(ref control, startCommand, operationId, CommandPhase.Execute, startPayload);
            _operations[(int)control.OperationCount] = new NativeOperationRecordV1
            {
                OperationId = operationId,
                State = NativeOperationStateV1.Active,
                CancelCommandType = cancelCommand,
                FaultCancelPayloadOffset = faultOffset,
                FaultCancelPayloadSize = faultCancelPayload.Size,
            };
            control.OperationCount++;
            control.OperationPayloadCount += faultCancelPayload.Size;
            AdvanceOperationSequence(ref control);
            _control[0] = control;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryEmitEffect(
            CommandType commandType,
            CommandPhase phase,
            NativePayloadSliceV1 payload,
            out ulong sequence)
        {
            sequence = 0;
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            if (!commandType.IsValid || !IsPhaseValid(phase) || !payload.IsValid)
            {
                return BurstContextResult.InvalidEncoding;
            }

            var append = CanAppendCommand(control, phase, payload.Size);
            if (append != BurstContextResult.Success) return append;
            sequence = control.NextCommandSequence;
            AppendCommandUnchecked(ref control, commandType, default, phase, payload);
            _control[0] = control;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryCancel(
            OperationId operationId,
            CommandType cancelCommand,
            NativePayloadSliceV1 payload,
            out bool commandEmitted)
        {
            commandEmitted = false;
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            if (operationId.TreeInstanceId != _treeInstanceId || !operationId.NodeIndex.IsValid)
            {
                return BurstContextResult.InvalidHandle;
            }

            var operationIndex = FindOperation(_operations, control.OperationCount, operationId);
            if (operationIndex < 0) return BurstContextResult.InvalidHandle;
            var operation = _operations[operationIndex];
            if (operation.State == NativeOperationStateV1.Cancelled) return BurstContextResult.Success;
            if (operation.State == NativeOperationStateV1.Consumed) return BurstContextResult.AlreadyCommitted;
            if (operation.State != NativeOperationStateV1.Active) return BurstContextResult.InvalidHandle;
            if (operation.CancelCommandType != cancelCommand) return BurstContextResult.TypeMismatch;
            if (!payload.IsValid) return BurstContextResult.InvalidEncoding;

            var requiredDiagnostics = CountPending(operationId);
            var canPublishDiagnostics = (ulong)control.DiagnosticCount + requiredDiagnostics <= (ulong)_diagnostics.Length;
            operation.State = NativeOperationStateV1.Cancelled;
            _operations[operationIndex] = operation;
            DiscardPending(
                operationId,
                NativeCommandAsyncDiagnosticCodeV1.CancelledOperation,
                canPublishDiagnostics,
                ref control);
            var result = canPublishDiagnostics
                ? BurstContextResult.Success
                : BurstContextResult.CapacityExceeded;

            var append = CanAppendCommand(control, CommandPhase.Cancel, payload.Size);
            if (append != BurstContextResult.Success)
            {
                _control[0] = control;
                return result == BurstContextResult.Success ? append : result;
            }

            AppendCommandUnchecked(ref control, cancelCommand, operationId, CommandPhase.Cancel, payload);
            commandEmitted = true;
            _control[0] = control;
            return result;
        }

        public BurstContextResult TryFaultCancelAll(out uint emittedCount)
        {
            emittedCount = 0;
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            var firstFailure = BurstContextResult.Success;
            var activeCount = 0u;
            for (var index = 0; index < control.OperationCount; index++)
            {
                var operation = _operations[index];
                if (operation.State == NativeOperationStateV1.Active) _operationScratch[(int)activeCount++] = operation;
            }

            SortOperations(_operationScratch, activeCount);
            for (var index = 0u; index < activeCount; index++)
            {
                var operation = _operationScratch[(int)index];
                var operationIndex = FindOperation(_operations, control.OperationCount, operation.OperationId);
                if (operationIndex < 0) continue;
                var requiredDiagnostics = CountPending(operation.OperationId);
                var canPublishDiagnostics =
                    (ulong)control.DiagnosticCount + requiredDiagnostics <= (ulong)_diagnostics.Length;
                operation.State = NativeOperationStateV1.Cancelled;
                _operations[operationIndex] = operation;
                DiscardPending(
                    operation.OperationId,
                    NativeCommandAsyncDiagnosticCodeV1.CancelledOperation,
                    canPublishDiagnostics,
                    ref control);
                if (!canPublishDiagnostics)
                {
                    if (firstFailure == BurstContextResult.Success) firstFailure = BurstContextResult.CapacityExceeded;
                }

                var append = CanAppendCommand(control, CommandPhase.Cancel, operation.FaultCancelPayloadSize);
                if (append != BurstContextResult.Success)
                {
                    if (firstFailure == BurstContextResult.Success) firstFailure = append;
                    continue;
                }

                var payload = new NativePayloadSliceV1(
                    _operationPayload.AsReadOnly(),
                    operation.FaultCancelPayloadOffset,
                    operation.FaultCancelPayloadSize);
                AppendCommandUnchecked(
                    ref control,
                    operation.CancelCommandType,
                    operation.OperationId,
                    CommandPhase.Cancel,
                    payload);
                emittedCount++;
            }

            _control[0] = control;
            return firstFailure;
        }

        public BurstContextResult TryRestart(out uint emittedCount)
        {
            return TryFaultCancelAll(out emittedCount);
        }

        public BurstContextResult TryConsume(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            OperationId operationId,
            NativeCompletionExpectationV1 expectation,
            out NativeConsumedCompletionV1 completion)
        {
            completion = default;
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            if (operationId.TreeInstanceId != _treeInstanceId
                || operationId.NodeIndex != nodeIndex
                || operationId.ActivationGeneration != activationGeneration)
            {
                return BurstContextResult.InvalidHandle;
            }

            if (!expectation.IsValid) return BurstContextResult.InvalidEncoding;
            var operationIndex = FindOperation(_operations, control.OperationCount, operationId);
            if (operationIndex < 0) return BurstContextResult.InvalidHandle;
            var operation = _operations[operationIndex];
            if (operation.State == NativeOperationStateV1.Cancelled || operation.State == NativeOperationStateV1.Consumed)
            {
                return BurstContextResult.StaleCompletion;
            }

            var selected = -1;
            var requiredDiagnostics = 0u;
            for (var index = 0; index < _pending.Length; index++)
            {
                var item = _pending[index];
                if (item.IsOccupied == 0 || item.OperationId != operationId) continue;
                if (selected < 0 && expectation.Matches(item)) selected = index;
                else requiredDiagnostics++;
            }

            if ((ulong)control.DiagnosticCount + requiredDiagnostics > (ulong)_diagnostics.Length)
            {
                return BurstContextResult.CapacityExceeded;
            }

            if (selected < 0)
            {
                var mismatched = false;
                for (var index = 0; index < _pending.Length; index++)
                {
                    var item = _pending[index];
                    if (item.IsOccupied == 0 || item.OperationId != operationId) continue;
                    AppendDiagnostic(ref control, NativeCommandAsyncDiagnosticCodeV1.CompletionPayloadMismatch, item);
                    item.IsOccupied = 0;
                    _pending[index] = item;
                    control.PendingCount--;
                    mismatched = true;
                }

                _control[0] = control;
                return mismatched ? BurstContextResult.TypeMismatch : BurstContextResult.IncompleteValue;
            }

            var selectedRecord = _pending[selected];
            completion = new NativeConsumedCompletionV1(
                selectedRecord,
                _completionPayload.AsReadOnly(),
                _control.AsReadOnly(),
                _ownerId,
                _generation,
                _leaseId,
                control.CompletionEpoch);
            for (var index = 0; index < selected; index++)
            {
                var item = _pending[index];
                if (item.IsOccupied == 0 || item.OperationId != operationId) continue;
                AppendDiagnostic(ref control, NativeCommandAsyncDiagnosticCodeV1.CompletionPayloadMismatch, item);
                item.IsOccupied = 0;
                _pending[index] = item;
                control.PendingCount--;
            }

            var selectedItem = _pending[selected];
            selectedItem.IsOccupied = 0;
            _pending[selected] = selectedItem;
            control.PendingCount--;
            for (var index = _pending.Length - 1; index > selected; index--)
            {
                var item = _pending[index];
                if (item.IsOccupied == 0 || item.OperationId != operationId) continue;
                AppendDiagnostic(ref control, NativeCommandAsyncDiagnosticCodeV1.AlreadyConsumedOperation, item);
                item.IsOccupied = 0;
                _pending[index] = item;
                control.PendingCount--;
            }

            operation.State = NativeOperationStateV1.Consumed;
            _operations[operationIndex] = operation;
            _control[0] = control;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryNormalizeCompletions(
            NativeArray<NativeCompletionInputRecordV1>.ReadOnly input,
            NativeArray<byte>.ReadOnly inputPayload,
            NativeArray<uint>.ReadOnly activationGenerations)
        {
            if (!TryGetControl(out var control)) return BurstContextResult.InvalidHandle;
            if (!input.IsCreated && input.Length != 0) return BurstContextResult.InvalidHandle;
            if ((uint)input.Length > (uint)_inputScratch.Length) return BurstContextResult.CapacityExceeded;
            if (control.CompletionEpoch == ulong.MaxValue) return BurstContextResult.Overflow;

            for (var index = 0; index < input.Length; index++)
            {
                var record = input[index];
                if (!IsCompletionStructurallyValid(record, inputPayload))
                {
                    return BurstContextResult.InvalidEncoding;
                }

                _inputScratch[index] = record;
            }

            SortInput(_inputScratch, input.Length);
            for (var index = 0; index < control.OperationCount; index++) _operationScratch[index] = _operations[index];
            for (var index = 0; index < control.HighWaterCount; index++) _highWaterScratch[index] = _highWater[index];

            var stagedPendingCount = 0u;
            var stagedPayloadCount = 0u;
            for (var index = 0; index < _pending.Length; index++)
            {
                var item = _pending[index];
                if (item.IsOccupied == 0) continue;
                if (stagedPendingCount >= (uint)_pendingScratch.Length
                    || (ulong)stagedPayloadCount + item.PayloadSize > (ulong)_completionPayloadScratch.Length)
                {
                    return BurstContextResult.CapacityExceeded;
                }

                var rewritten = item;
                rewritten.PayloadOffset = item.PayloadSize == 0 ? 0u : stagedPayloadCount;
                CopyBytes(_completionPayload, item.PayloadOffset, _completionPayloadScratch, stagedPayloadCount, item.PayloadSize);
                _pendingScratch[(int)stagedPendingCount++] = rewritten;
                stagedPayloadCount += item.PayloadSize;
            }

            var stagedHighWaterCount = control.HighWaterCount;
            var stagedDiagnosticCount = control.DiagnosticCount;
            if (stagedDiagnosticCount > (uint)_diagnosticScratch.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            for (var index = 0u; index < stagedDiagnosticCount; index++)
            {
                _diagnosticScratch[(int)index] = _diagnostics[(int)index];
            }

            var cursor = 0;
            while (cursor < input.Length)
            {
                var groupEnd = cursor + 1;
                while (groupEnd < input.Length && SameOrderingKey(_inputScratch[cursor], _inputScratch[groupEnd])) groupEnd++;
                if (groupEnd - cursor > 1)
                {
                    if (!TryStageDiagnostic(
                        ref stagedDiagnosticCount,
                        NativeCommandAsyncDiagnosticCodeV1.DuplicateCompletionOrderingKey,
                        _inputScratch[cursor]))
                    {
                        return BurstContextResult.CapacityExceeded;
                    }

                    cursor = groupEnd;
                    continue;
                }

                var record = _inputScratch[cursor++];
                var highWaterIndex = FindHighWater(_highWaterScratch, stagedHighWaterCount, record.SourceId);
                if (highWaterIndex >= 0 && record.SourceSequence <= _highWaterScratch[highWaterIndex].Sequence)
                {
                    if (!TryStageDiagnostic(
                        ref stagedDiagnosticCount,
                        NativeCommandAsyncDiagnosticCodeV1.NonIncreasingSourceSequence,
                        record))
                    {
                        return BurstContextResult.CapacityExceeded;
                    }

                    continue;
                }

                if (highWaterIndex < 0)
                {
                    if (stagedHighWaterCount >= (uint)_highWaterScratch.Length) return BurstContextResult.CapacityExceeded;
                    highWaterIndex = (int)stagedHighWaterCount++;
                    _highWaterScratch[highWaterIndex] = new NativeCompletionHighWaterV1 { SourceId = record.SourceId };
                }

                var highWater = _highWaterScratch[highWaterIndex];
                highWater.Sequence = record.SourceSequence;
                _highWaterScratch[highWaterIndex] = highWater;

                var operationIndex = FindOperation(_operationScratch, control.OperationCount, record.OperationId);
                if (operationIndex < 0)
                {
                    if (!TryStageDiagnostic(ref stagedDiagnosticCount, NativeCommandAsyncDiagnosticCodeV1.UnknownOperation, record))
                        return BurstContextResult.CapacityExceeded;
                    continue;
                }

                var operation = _operationScratch[operationIndex];
                if (operation.State == NativeOperationStateV1.Cancelled)
                {
                    if (!TryStageDiagnostic(ref stagedDiagnosticCount, NativeCommandAsyncDiagnosticCodeV1.CancelledOperation, record))
                        return BurstContextResult.CapacityExceeded;
                    continue;
                }

                if (operation.State == NativeOperationStateV1.Consumed)
                {
                    if (!TryStageDiagnostic(ref stagedDiagnosticCount, NativeCommandAsyncDiagnosticCodeV1.AlreadyConsumedOperation, record))
                        return BurstContextResult.CapacityExceeded;
                    continue;
                }

                var nodeValue = record.OperationId.NodeIndex.Value;
                if (!activationGenerations.IsCreated
                    || nodeValue >= (uint)activationGenerations.Length
                    || activationGenerations[(int)nodeValue] != record.OperationId.ActivationGeneration)
                {
                    operation.State = NativeOperationStateV1.Cancelled;
                    _operationScratch[operationIndex] = operation;
                    RemoveStagedPending(record.OperationId, ref stagedPendingCount);
                    CompactStagedPendingPayload(stagedPendingCount, ref stagedPayloadCount);
                    if (!TryStageDiagnostic(ref stagedDiagnosticCount, NativeCommandAsyncDiagnosticCodeV1.StaleOperationGeneration, record))
                        return BurstContextResult.CapacityExceeded;
                    continue;
                }

                if (stagedPendingCount >= (uint)_pendingScratch.Length
                    || (ulong)stagedPayloadCount + record.PayloadSize > (ulong)_completionPayloadScratch.Length)
                {
                    return BurstContextResult.CapacityExceeded;
                }

                var payloadOffset = record.PayloadSize == 0 ? 0u : stagedPayloadCount;
                CopyBytes(inputPayload, record.PayloadOffset, _completionPayloadScratch, payloadOffset, record.PayloadSize);
                _pendingScratch[(int)stagedPendingCount++] = new NativePendingCompletionRecordV1
                {
                    OperationId = record.OperationId,
                    Outcome = record.Outcome,
                    PayloadType = record.PayloadType,
                    PayloadOffset = payloadOffset,
                    PayloadSize = record.PayloadSize,
                    SourceId = record.SourceId,
                    SourceSequence = record.SourceSequence,
                    SnapshotRevision = record.SnapshotRevision,
                    IsOccupied = 1,
                };
                stagedPayloadCount += record.PayloadSize;
            }

            SortPending(_pendingScratch, stagedPendingCount);
            for (var index = 0u; index < control.OperationCount; index++) _operations[(int)index] = _operationScratch[(int)index];
            for (var index = 0u; index < stagedPendingCount; index++) _pending[(int)index] = _pendingScratch[(int)index];
            for (var index = stagedPendingCount; index < (uint)_pending.Length; index++) _pending[(int)index] = default;
            for (var index = 0u; index < stagedPayloadCount; index++) _completionPayload[(int)index] = _completionPayloadScratch[(int)index];
            for (var index = 0u; index < stagedHighWaterCount; index++) _highWater[(int)index] = _highWaterScratch[(int)index];
            for (var index = 0u; index < stagedDiagnosticCount; index++) _diagnostics[(int)index] = _diagnosticScratch[(int)index];
            control.PendingCount = stagedPendingCount;
            control.CompletionPayloadCount = stagedPayloadCount;
            control.HighWaterCount = stagedHighWaterCount;
            control.DiagnosticCount = stagedDiagnosticCount;
            control.CompletionEpoch++;
            _control[0] = control;
            return BurstContextResult.Success;
        }

        private bool TryGetControl(out NativeCommandAsyncControlV1 control)
        {
            control = default;
            if (!_control.IsCreated) return false;
            control = _control[0];
            return control.State == 2
                && control.OwnerId == _ownerId
                && control.Generation == _generation
                && control.ActiveLeaseId == _leaseId
                && control.ExecutionSealed == 0;
        }

        private BurstContextResult CanAppendCommand(
            in NativeCommandAsyncControlV1 control,
            CommandPhase phase,
            uint payloadSize)
        {
            if (control.CommandSequenceExhausted != 0) return BurstContextResult.Overflow;
            if (!IsPhaseValid(phase)) return BurstContextResult.InvalidEncoding;
            if (phase == CommandPhase.Execute && control.ExecuteCount >= (uint)_executeCommands.Length)
                return BurstContextResult.CapacityExceeded;
            if (phase == CommandPhase.Cancel && control.CancelCount >= (uint)_cancelCommands.Length)
                return BurstContextResult.CapacityExceeded;
            if ((ulong)control.CommandPayloadCount + payloadSize > (ulong)_commandPayload.Length)
                return BurstContextResult.CapacityExceeded;
            return BurstContextResult.Success;
        }

        private void AppendCommandUnchecked(
            ref NativeCommandAsyncControlV1 control,
            CommandType type,
            OperationId operationId,
            CommandPhase phase,
            NativePayloadSliceV1 payload)
        {
            var payloadOffset = payload.Size == 0 ? 0u : control.CommandPayloadCount;
            CopyPayload(payload, _commandPayload, payloadOffset);
            var record = new NativeCommandRecordV1(
                type,
                operationId,
                payloadOffset,
                payload.Size,
                phase,
                _treeInstanceId,
                control.NextCommandSequence);
            if (phase == CommandPhase.Execute) _executeCommands[(int)control.ExecuteCount++] = record;
            else _cancelCommands[(int)control.CancelCount++] = record;
            control.CommandPayloadCount += payload.Size;
            if (control.NextCommandSequence == ulong.MaxValue) control.CommandSequenceExhausted = 1;
            else control.NextCommandSequence++;
        }

        private static void AdvanceOperationSequence(ref NativeCommandAsyncControlV1 control)
        {
            if (control.NextOperationSequence == ulong.MaxValue) control.OperationSequenceExhausted = 1;
            else control.NextOperationSequence++;
        }

        private uint CountPending(OperationId operationId)
        {
            var count = 0u;
            for (var index = 0; index < _pending.Length; index++)
            {
                var item = _pending[index];
                if (item.IsOccupied != 0 && item.OperationId == operationId) count++;
            }

            return count;
        }

        private void DiscardPending(
            OperationId operationId,
            NativeCommandAsyncDiagnosticCodeV1 code,
            bool publishDiagnostics,
            ref NativeCommandAsyncControlV1 control)
        {
            for (var index = _pending.Length - 1; index >= 0; index--)
            {
                var item = _pending[index];
                if (item.IsOccupied == 0 || item.OperationId != operationId) continue;
                if (publishDiagnostics) AppendDiagnostic(ref control, code, item);
                item.IsOccupied = 0;
                _pending[index] = item;
                control.PendingCount--;
            }
        }

        private static void SortOperations(NativeArray<NativeOperationRecordV1> values, uint count)
        {
            for (var index = 1u; index < count; index++)
            {
                var value = values[(int)index];
                var cursor = (int)index - 1;
                while (cursor >= 0 && CompareOperation(values[cursor].OperationId, value.OperationId) > 0)
                {
                    values[cursor + 1] = values[cursor];
                    cursor--;
                }

                values[cursor + 1] = value;
            }
        }

        private void AppendDiagnostic(
            ref NativeCommandAsyncControlV1 control,
            NativeCommandAsyncDiagnosticCodeV1 code,
            in NativePendingCompletionRecordV1 item)
        {
            _diagnostics[(int)control.DiagnosticCount++] = new NativeCompletionDiagnosticV1(
                code,
                item.OperationId,
                item.SourceId,
                item.SourceSequence);
        }

        private bool TryStageDiagnostic(
            ref uint count,
            NativeCommandAsyncDiagnosticCodeV1 code,
            in NativeCompletionInputRecordV1 item)
        {
            if (count >= (uint)_diagnosticScratch.Length) return false;
            _diagnosticScratch[(int)count++] = new NativeCompletionDiagnosticV1(
                code,
                item.OperationId,
                item.SourceId,
                item.SourceSequence);
            return true;
        }

        private void RemoveStagedPending(OperationId operationId, ref uint count)
        {
            var destination = 0u;
            for (var index = 0u; index < count; index++)
            {
                var item = _pendingScratch[(int)index];
                if (item.OperationId == operationId) continue;
                if (destination != index) _pendingScratch[(int)destination] = item;
                destination++;
            }

            count = destination;
        }

        private void CompactStagedPendingPayload(uint count, ref uint payloadCount)
        {
            var destinationOffset = 0u;
            for (var index = 0u; index < count; index++)
            {
                var item = _pendingScratch[(int)index];
                if (item.PayloadSize != 0)
                {
                    for (var payloadIndex = 0u; payloadIndex < item.PayloadSize; payloadIndex++)
                    {
                        _completionPayloadScratch[(int)(destinationOffset + payloadIndex)] =
                            _completionPayloadScratch[(int)(item.PayloadOffset + payloadIndex)];
                    }

                    item.PayloadOffset = destinationOffset;
                    _pendingScratch[(int)index] = item;
                    destinationOffset += item.PayloadSize;
                }
            }

            payloadCount = destinationOffset;
        }

        private static bool IsCompletionStructurallyValid(
            in NativeCompletionInputRecordV1 record,
            NativeArray<byte>.ReadOnly payload)
        {
            if (!record.OperationId.IsValid || record.SourceId == 0 || (byte)record.Outcome > (byte)CompletionOutcome.Cancelled)
                return false;
            if (record.PayloadType.IsValid)
            {
                if (record.PayloadSize == 0) return false;
            }
            else if (record.PayloadOffset != 0 || record.PayloadSize != 0)
            {
                return false;
            }

            if (record.PayloadSize == 0) return record.PayloadOffset == 0;
            return payload.IsCreated && (ulong)record.PayloadOffset + record.PayloadSize <= (ulong)payload.Length;
        }

        private static int FindOperation(
            NativeArray<NativeOperationRecordV1> operations,
            uint count,
            OperationId operationId)
        {
            for (var index = 0; index < count; index++)
                if (operations[(int)index].OperationId == operationId) return (int)index;
            return -1;
        }

        private static int FindHighWater(
            NativeArray<NativeCompletionHighWaterV1> values,
            uint count,
            ulong sourceId)
        {
            for (var index = 0; index < count; index++)
                if (values[(int)index].SourceId == sourceId) return (int)index;
            return -1;
        }

        private static bool SameOrderingKey(
            in NativeCompletionInputRecordV1 left,
            in NativeCompletionInputRecordV1 right)
            => left.SourceId == right.SourceId && left.SourceSequence == right.SourceSequence;

        private static void SortInput(NativeArray<NativeCompletionInputRecordV1> values, int count)
        {
            for (var index = 1; index < count; index++)
            {
                var value = values[index];
                var cursor = index - 1;
                while (cursor >= 0 && Compare(values[cursor], value) > 0)
                {
                    values[cursor + 1] = values[cursor];
                    cursor--;
                }

                values[cursor + 1] = value;
            }
        }

        private static int Compare(in NativeCompletionInputRecordV1 left, in NativeCompletionInputRecordV1 right)
        {
            var result = left.SourceId.CompareTo(right.SourceId);
            if (result != 0) return result;
            result = left.SourceSequence.CompareTo(right.SourceSequence);
            if (result != 0) return result;
            result = CompareOperation(left.OperationId, right.OperationId);
            if (result != 0) return result;
            result = ((byte)left.Outcome).CompareTo((byte)right.Outcome);
            if (result != 0) return result;
            result = left.PayloadType.TypeId.CompareTo(right.PayloadType.TypeId);
            if (result != 0) return result;
            result = left.PayloadType.Version.CompareTo(right.PayloadType.Version);
            if (result != 0) return result;
            result = left.PayloadOffset.CompareTo(right.PayloadOffset);
            if (result != 0) return result;
            result = left.PayloadSize.CompareTo(right.PayloadSize);
            return result != 0 ? result : left.SnapshotRevision.Value.CompareTo(right.SnapshotRevision.Value);
        }

        private static int CompareOperation(OperationId left, OperationId right)
        {
            var result = left.TreeInstanceId.Value.CompareTo(right.TreeInstanceId.Value);
            if (result != 0) return result;
            result = left.NodeIndex.Value.CompareTo(right.NodeIndex.Value);
            if (result != 0) return result;
            result = left.ActivationGeneration.CompareTo(right.ActivationGeneration);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }

        private static void SortPending(NativeArray<NativePendingCompletionRecordV1> values, uint count)
        {
            for (var index = 1; index < count; index++)
            {
                var value = values[(int)index];
                var cursor = (int)index - 1;
                while (cursor >= 0 && ComparePending(values[cursor], value) > 0)
                {
                    values[cursor + 1] = values[cursor];
                    cursor--;
                }

                values[cursor + 1] = value;
            }
        }

        private static int ComparePending(in NativePendingCompletionRecordV1 left, in NativePendingCompletionRecordV1 right)
        {
            var result = left.SourceId.CompareTo(right.SourceId);
            return result != 0 ? result : left.SourceSequence.CompareTo(right.SourceSequence);
        }

        private static bool IsPhaseValid(CommandPhase phase)
            => phase == CommandPhase.Execute || phase == CommandPhase.Cancel;

        private static void CopyPayload(NativePayloadSliceV1 source, NativeArray<byte> destination, uint offset)
        {
            for (var index = 0u; index < source.Size; index++)
                destination[(int)(offset + index)] = source.Bytes[(int)(source.Offset + index)];
        }

        private static void CopyBytes(
            NativeArray<byte> source,
            uint sourceOffset,
            NativeArray<byte> destination,
            uint destinationOffset,
            uint size)
        {
            for (var index = 0u; index < size; index++)
                destination[(int)(destinationOffset + index)] = source[(int)(sourceOffset + index)];
        }

        private static void CopyBytes(
            NativeArray<byte>.ReadOnly source,
            uint sourceOffset,
            NativeArray<byte> destination,
            uint destinationOffset,
            uint size)
        {
            for (var index = 0u; index < size; index++)
                destination[(int)(destinationOffset + index)] = source[(int)(sourceOffset + index)];
        }
    }

    internal static class NativeCommandAsyncOwnerIdentityV1
    {
        private static long s_nextOwnerId;

        internal static bool TryNext(out ulong ownerId)
        {
            ownerId = 0;
            while (true)
            {
                var current = Volatile.Read(ref s_nextOwnerId);
                if (current == long.MaxValue) return false;
                var next = current + 1;
                if (Interlocked.CompareExchange(ref s_nextOwnerId, next, current) != current) continue;
                ownerId = (ulong)next;
                return true;
            }
        }
    }
}
