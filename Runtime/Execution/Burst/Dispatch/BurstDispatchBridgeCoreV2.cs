using System;
using System.Threading;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT.Burst
{
    internal static class BurstDispatchBridgeCoreV2
    {
        private const ulong PcgMultiplier = 6364136223846793005UL;

        internal static BurstContextResult TryGetCatalogHandshake(
            in BurstExecutionBatch batch,
            out BurstCatalogHandshake handshake)
        {
            handshake = default;
            var roleGate = GateBatchRead(in batch, false);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            if (!TryControl(batch.Runtime, out _))
            {
                return BurstContextResult.InvalidHandle;
            }

            handshake = batch.Runtime.Handshake;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryRejectBatch(
            ref BurstExecutionBatch batch,
            in BurstCatalogValidationResult validation)
        {
            if (!TryExecutionClaim(batch.Runtime, out var executionClaim))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (batch.Role != NativeBurstBatchRoleV2.Host || executionClaim != 0)
            {
                return BurstContextResult.PhaseViolation;
            }

            if (!TryControl(batch.Runtime, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (validation.Success
                || validation.CodeWord != (ushort)(byte)validation.Code
                || (byte)validation.Code < (byte)BurstCatalogValidationCode.AbiVersionMismatch
                || (byte)validation.Code > (byte)BurstCatalogValidationCode.AccessLayoutMismatch
                || validation.DiagnosticNumber != 5012)
            {
                return BurstContextResult.InvalidStatus;
            }

            var rejectableEmpty = batch.Runtime.Requests.Length == 0
                && control.State == NativeBurstDispatchStateV2.Terminal
                && control.ResultCode == BurstExecutionCode.Success
                && control.Cursor == 0;
            if ((!rejectableEmpty && control.State != NativeBurstDispatchStateV2.Ready)
                || control.ActiveFrameId != 0
                || batch.Runtime.ExecutionClaim[0] != 0)
            {
                return BurstContextResult.PhaseViolation;
            }

            control.State = NativeBurstDispatchStateV2.Terminal;
            control.ResultCode = BurstExecutionCode.ValidationFailed;
            control.DiagnosticNumber = 5012;
            WriteControl(batch.Runtime, control);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryGetExecutionRequest(
            in BurstExecutionBatch batch,
            out uint instanceOrdinal,
            out uint runtimeNodeIndex,
            out uint catalogCaseIndex,
            out BurstCallbackPhase phase,
            out bool hasWork)
        {
            instanceOrdinal = default;
            runtimeNodeIndex = default;
            catalogCaseIndex = default;
            phase = default;
            hasWork = false;
            var roleGate = GateBatchRead(in batch, false);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            if (!TryControl(batch.Runtime, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (!TryClaimExecution(in batch, ref control))
            {
                return BurstContextResult.PhaseViolation;
            }

            if (control.State == NativeBurstDispatchStateV2.Terminal)
            {
                if (control.ResultCode != BurstExecutionCode.Success)
                {
                    return BurstContextResult.PhaseViolation;
                }

                return BurstContextResult.Success;
            }

            if (control.State != NativeBurstDispatchStateV2.Ready
                || control.ActiveFrameId != 0
                || control.Cursor >= batch.Runtime.Requests.Length)
            {
                return BurstContextResult.PhaseViolation;
            }

            var request = batch.Runtime.Requests[(int)control.Cursor];
            instanceOrdinal = request.InstanceOrdinal;
            runtimeNodeIndex = request.RuntimeNodeIndex;
            catalogCaseIndex = request.CatalogCaseIndex;
            phase = request.Phase;
            hasWork = true;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryGetExecutionResult(
            in BurstExecutionBatch batch,
            out BurstExecutionResult result)
        {
            result = default;
            var roleGate = GateBatchRead(in batch, true);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            if (!TryControl(batch.Runtime, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (control.State != NativeBurstDispatchStateV2.Terminal)
            {
                return BurstContextResult.PhaseViolation;
            }

            result = new BurstExecutionResult(
                control.ResultCode,
                control.DiagnosticNumber,
                control.InstancesVisited,
                control.SegmentSteps);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryPrepareSchedule(
            ref BurstExecutionBatch batch,
            out BurstExecutionBatch scheduledView)
        {
            scheduledView = default;
            if (!TryExecutionClaim(batch.Runtime, out var executionClaim))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (batch.Role != NativeBurstBatchRoleV2.Host || executionClaim != 0)
            {
                return BurstContextResult.PhaseViolation;
            }

            if (!TryControl(batch.Runtime, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (batch.Role != NativeBurstBatchRoleV2.Host
                || control.State != NativeBurstDispatchStateV2.Ready
                || control.Cursor != 0
                || control.ActiveFrameId != 0
                || !TryClaimSchedule(batch.Runtime,
                    2,
                    0))
            {
                return BurstContextResult.PhaseViolation;
            }

            scheduledView = new BurstExecutionBatch(batch.Runtime, NativeBurstBatchRoleV2.Job);
            batch.Role = NativeBurstBatchRoleV2.ScheduledHost;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryAcquireDispatchFrame(
            ref BurstExecutionBatch batch,
            uint instanceOrdinal,
            uint runtimeNodeIndex,
            uint catalogCaseIndex,
            BurstCallbackPhase phase,
            out BurstDispatchFrame frame)
        {
            frame = default;
            var roleGate = GateBatchExecute(in batch);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            if (!TryControl(batch.Runtime, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (!RoleCanExecute(in batch, control)
                || control.State != NativeBurstDispatchStateV2.Ready
                || control.ActiveFrameId != 0
                || control.Cursor >= batch.Runtime.Requests.Length)
            {
                return FaultWithoutFrame(batch.Runtime, BurstContextResult.PhaseViolation);
            }

            var request = batch.Runtime.Requests[(int)control.Cursor];
            if (request.InstanceOrdinal != instanceOrdinal
                || request.RuntimeNodeIndex != runtimeNodeIndex
                || request.CatalogCaseIndex != catalogCaseIndex
                || request.Phase != phase
                || catalogCaseIndex >= batch.Runtime.Cases.Length)
            {
                return FaultWithoutFrame(batch.Runtime, BurstContextResult.InvalidHandle);
            }

            var dispatchCase = batch.Runtime.Cases[(int)catalogCaseIndex];
            if (dispatchCase.CatalogCaseIndex != catalogCaseIndex
                || dispatchCase.TypeNumericId != request.TypeNumericId
                || dispatchCase.TypeVersion != request.TypeVersion
                || !Includes(dispatchCase.Phases, phase))
            {
                return FaultWithoutFrame(batch.Runtime, BurstContextResult.TypeMismatch);
            }

            if (control.NextFrameId == uint.MaxValue)
            {
                return FaultWithoutFrame(batch.Runtime, BurstContextResult.Overflow);
            }

            var frameId = control.NextFrameId + 1;
            if (!TryClaimFrameAcquisition(batch.Runtime, frameId))
            {
                return BurstContextResult.PhaseViolation;
            }

            CopyRange(
                batch.Runtime.MemoryBytes,
                request.MemoryOffset,
                batch.Runtime.MemoryStaging,
                request.MemoryOffset,
                dispatchCase.MemorySize);
            ClearRange(batch.Runtime.MemoryWritten, request.MemoryOffset, dispatchCase.MemorySize);
            control.NextFrameId = frameId;
            control.ActiveFrameId = frameId;
            control.MemoryCommitted = 0;
            control.FirstFailure = BurstContextResult.Success;
            control.State = NativeBurstDispatchStateV2.Running;
            WriteControl(batch.Runtime, control);
            BurstBindingBridgeCoreV2.PrepareFrame(batch.Runtime, frameId);
            frame = new BurstDispatchFrame(
                Token(control.OwnerId, control.Generation, frameId),
                batch.Runtime,
                frameId,
                control.Cursor,
                batch.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCreateConfigurationReader(
            in BurstDispatchFrame frame,
            out BurstConfigurationReader reader)
        {
            reader = default;
            var carrierGate = BurstBindingBridgeCoreV2.GateFrameCarrier(in frame);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            reader = new BurstConfigurationReader(
                Token(control.OwnerId, control.Generation, frame.FrameId),
                frame.Runtime,
                frame.FrameId,
                frame.RequestOrdinal,
                frame.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCreateMemoryAccessor(
            in BurstDispatchFrame frame,
            out BurstMemoryAccessor accessor)
        {
            accessor = default;
            var carrierGate = BurstBindingBridgeCoreV2.GateFrameCarrier(in frame);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            var request = frame.Runtime.Requests[(int)control.Cursor];
            if (request.Phase == BurstCallbackPhase.Observer)
            {
                return LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.PhaseViolation);
            }

            accessor = new BurstMemoryAccessor(
                Token(control.OwnerId, control.Generation, frame.FrameId),
                frame.Runtime,
                frame.FrameId,
                frame.RequestOrdinal,
                frame.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryReadConfiguration<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            out T value)
            where T : unmanaged
        {
            value = default;
            var carrierGate = BurstBindingBridgeCoreV2.GateConfigurationReader(in reader);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(reader.Runtime, reader.FrameId, reader.RequestOrdinal, out var control))
            {
                return LatchFrameFailure(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle);
            }

            var request = reader.Runtime.Requests[(int)control.Cursor];
            var dispatchCase = reader.Runtime.Cases[(int)request.CatalogCaseIndex];
            if (!TryField(
                    reader.Runtime.ConfigurationFields,
                    dispatchCase.FirstConfigurationField,
                    dispatchCase.ConfigurationFieldCount,
                    fieldOrdinal,
                    elementIndex,
                    encoding,
                    out var field,
                    out var relativeOffset))
            {
                return LatchFrameFailure(reader.Runtime, reader.FrameId, BurstContextResult.TypeMismatch);
            }

            var result = TryReadCanonical(
                reader.Runtime.ConfigurationBytes,
                request.ConfigurationOffset + relativeOffset,
                encoding,
                out value);
            return result == BurstContextResult.Success
                ? result
                : LatchFrameFailure(reader.Runtime, reader.FrameId, result);
        }

        internal static BurstContextResult TryReadMemory<T>(
            ref BurstMemoryAccessor accessor,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            out T value)
            where T : unmanaged
        {
            value = default;
            var carrierGate = BurstBindingBridgeCoreV2.GateMemoryAccessor(in accessor);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(accessor.Runtime, accessor.FrameId, accessor.RequestOrdinal, out var control))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.InvalidHandle);
            }

            var request = accessor.Runtime.Requests[(int)control.Cursor];
            var dispatchCase = accessor.Runtime.Cases[(int)request.CatalogCaseIndex];
            if (!TryField(
                    accessor.Runtime.MemoryFields,
                    dispatchCase.FirstMemoryField,
                    dispatchCase.MemoryFieldCount,
                    fieldOrdinal,
                    elementIndex,
                    encoding,
                    out _,
                    out var relativeOffset))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.TypeMismatch);
            }

            var result = TryReadCanonical(
                accessor.Runtime.MemoryStaging,
                request.MemoryOffset + relativeOffset,
                encoding,
                out value);
            return result == BurstContextResult.Success
                ? result
                : LatchFrameFailure(accessor.Runtime, accessor.FrameId, result);
        }

        internal static BurstContextResult TryWriteMemory<T>(
            ref BurstMemoryAccessor accessor,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            T value)
            where T : unmanaged
        {
            var carrierGate = BurstBindingBridgeCoreV2.GateMemoryAccessor(in accessor);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(accessor.Runtime, accessor.FrameId, accessor.RequestOrdinal, out var control))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.InvalidHandle);
            }

            if (control.FirstFailure != BurstContextResult.Success)
            {
                return control.FirstFailure;
            }

            if (control.MemoryCommitted != 0)
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.AlreadyCommitted);
            }

            var request = accessor.Runtime.Requests[(int)control.Cursor];
            var dispatchCase = accessor.Runtime.Cases[(int)request.CatalogCaseIndex];
            if (!TryField(
                    accessor.Runtime.MemoryFields,
                    dispatchCase.FirstMemoryField,
                    dispatchCase.MemoryFieldCount,
                    fieldOrdinal,
                    elementIndex,
                    encoding,
                    out var field,
                    out var relativeOffset))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.TypeMismatch);
            }

            var result = TryWriteCanonical(
                accessor.Runtime.MemoryStaging,
                request.MemoryOffset + relativeOffset,
                encoding,
                value);
            if (result != BurstContextResult.Success)
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, result);
            }

            MarkRange(
                accessor.Runtime.MemoryWritten,
                request.MemoryOffset + relativeOffset,
                field.ElementSize);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryWriteMemoryFloat32(
            ref BurstMemoryAccessor accessor,
            uint fieldOrdinal,
            uint elementIndex,
            float value)
        {
            var carrierGate = BurstBindingBridgeCoreV2.GateMemoryAccessor(in accessor);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.InvalidEncoding);
            }

            if (value == 0f)
            {
                value = 0f;
            }

            return TryWriteMemory(
                ref accessor,
                fieldOrdinal,
                elementIndex,
                NativeBurstDispatchFieldEncodingV2.Float32,
                value);
        }

        internal static BurstContextResult TryWriteMemoryFloat64(
            ref BurstMemoryAccessor accessor,
            uint fieldOrdinal,
            uint elementIndex,
            double value)
        {
            var carrierGate = BurstBindingBridgeCoreV2.GateMemoryAccessor(in accessor);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.InvalidEncoding);
            }

            if (value == 0d)
            {
                value = 0d;
            }

            return TryWriteMemory(
                ref accessor,
                fieldOrdinal,
                elementIndex,
                NativeBurstDispatchFieldEncodingV2.Float64,
                value);
        }

        internal static BurstContextResult TryCommitMemory(ref BurstMemoryAccessor accessor)
        {
            var carrierGate = BurstBindingBridgeCoreV2.GateMemoryAccessor(in accessor);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(accessor.Runtime, accessor.FrameId, accessor.RequestOrdinal, out var control))
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.InvalidHandle);
            }

            if (control.FirstFailure != BurstContextResult.Success)
            {
                return control.FirstFailure;
            }

            if (control.MemoryCommitted != 0)
            {
                return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.AlreadyCommitted);
            }

            var request = accessor.Runtime.Requests[(int)control.Cursor];
            var dispatchCase = accessor.Runtime.Cases[(int)request.CatalogCaseIndex];
            for (uint ordinal = 0; ordinal < dispatchCase.MemoryFieldCount; ordinal++)
            {
                var field = accessor.Runtime.MemoryFields[(int)(dispatchCase.FirstMemoryField + ordinal)];
                var bytes = (ulong)field.ElementCount * field.ElementSize;
                var start = request.MemoryOffset + field.ByteOffset;
                for (ulong index = 0; index < bytes; index++)
                {
                    if (accessor.Runtime.MemoryWritten[(int)(start + index)] == 0)
                    {
                        return LatchFrameFailure(accessor.Runtime, accessor.FrameId, BurstContextResult.IncompleteValue);
                    }
                }
            }

            if (accessor.Runtime.CaseCanonicalRanges.Length != 0)
            {
                var canonicalRangeIndex = (ulong)request.CatalogCaseIndex * 2UL + 1UL;
                if (canonicalRangeIndex >= (ulong)accessor.Runtime.CaseCanonicalRanges.Length)
                {
                    return LatchFrameFailure(
                        accessor.Runtime,
                        accessor.FrameId,
                        BurstContextResult.InvalidHandle);
                }

                var canonicalRange = accessor.Runtime.CaseCanonicalRanges[(int)canonicalRangeIndex];
                if (!NativeBurstDispatchCanonicalV2.ValidateBytes(
                        accessor.Runtime.CanonicalRules.AsReadOnly(),
                        in canonicalRange,
                        accessor.Runtime.MemoryStaging.AsReadOnly(),
                        request.MemoryOffset,
                        dispatchCase.MemorySize,
                        NativeBurstDispatchCanonicalStoragePolicyV2.AllowZeroOpaqueSentinel))
                {
                    return LatchFrameFailure(
                        accessor.Runtime,
                        accessor.FrameId,
                        BurstContextResult.InvalidEncoding);
                }
            }

            control.MemoryCommitted = 1;
            WriteControl(accessor.Runtime, control);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCreateEnterContext(
            in BurstDispatchFrame frame,
            out BurstEnterContext context)
        {
            context = default;
            if (!TryPhase(in frame, BurstCallbackPhase.Enter, out var control, out var request, out var dispatchCase))
            {
                return PhaseFailure(in frame);
            }

            GetRandom(frame.Runtime, in request, in dispatchCase, out var state, out var increment);
            context = new BurstEnterContext(
                Token(control.OwnerId, control.Generation, frame.FrameId),
                state,
                increment,
                frame.Runtime,
                frame.FrameId,
                frame.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCreateTickContext(
            in BurstDispatchFrame frame,
            out BurstTickContext context)
        {
            context = default;
            if (!TryPhase(in frame, BurstCallbackPhase.Tick, out var control, out var request, out var dispatchCase))
            {
                return PhaseFailure(in frame);
            }

            GetRandom(frame.Runtime, in request, in dispatchCase, out var state, out var increment);
            context = new BurstTickContext(
                Token(control.OwnerId, control.Generation, frame.FrameId),
                state,
                increment,
                frame.Runtime,
                frame.FrameId,
                frame.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCreateAbortContext(
            in BurstDispatchFrame frame,
            out BurstAbortContext context)
        {
            context = default;
            if (!TryPhase(in frame, BurstCallbackPhase.Abort, out var control, out _, out _))
            {
                return PhaseFailure(in frame);
            }

            context = new BurstAbortContext(
                Token(control.OwnerId, control.Generation, frame.FrameId),
                frame.Runtime,
                frame.FrameId,
                frame.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCreateExitContext(
            in BurstDispatchFrame frame,
            out BurstExitContext context)
        {
            context = default;
            var carrierGate = BurstBindingBridgeCoreV2.GateFrameCarrier(in frame);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out _))
            {
                return BurstContextResult.InvalidHandle;
            }

            return TryPhase(in frame, BurstCallbackPhase.Exit, out _, out _, out _)
                ? BurstContextResult.Success
                : LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.PhaseViolation);
        }

        internal static BurstContextResult TryCreateObserverContext(
            in BurstDispatchFrame frame,
            out BurstObserverContext context)
        {
            context = default;
            if (!TryPhase(in frame, BurstCallbackPhase.Observer, out var control, out _, out _))
            {
                return PhaseFailure(in frame);
            }

            context = new BurstObserverContext(
                Token(control.OwnerId, control.Generation, frame.FrameId),
                frame.Runtime,
                frame.FrameId,
                frame.Role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryGetAbortReason(
            in BurstDispatchFrame frame,
            out BurstNodeAbortReason reason)
        {
            reason = default;
            if (!TryPhase(in frame, BurstCallbackPhase.Abort, out _, out var request, out _))
            {
                return PhaseFailure(in frame);
            }

            reason = request.AbortReason;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryGetExitReason(
            in BurstDispatchFrame frame,
            out BurstNodeExitReason reason)
        {
            reason = default;
            if (!TryPhase(in frame, BurstCallbackPhase.Exit, out _, out var request, out _))
            {
                return PhaseFailure(in frame);
            }

            reason = request.ExitReason;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCompleteEnter(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            ref BurstEnterContext context)
        {
            var contextGate = ValidateCompletionContext(
                in batch,
                in frame,
                context.Runtime,
                context.FrameId,
                context.ValidationToken);
            if (contextGate != BurstContextResult.Success)
            {
                return contextGate;
            }

            return Complete(
                ref batch,
                in frame,
                BurstCallbackPhase.Enter,
                default,
                default,
                context.RandomState,
                context.RandomIncrement);
        }

        internal static BurstContextResult TryCompleteTick(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            ref BurstTickContext context,
            NodeStatus status)
        {
            var contextGate = ValidateCompletionContext(
                in batch,
                in frame,
                context.Runtime,
                context.FrameId,
                context.ValidationToken);
            if (contextGate != BurstContextResult.Success)
            {
                return contextGate;
            }

            return Complete(
                ref batch,
                in frame,
                BurstCallbackPhase.Tick,
                status,
                default,
                context.RandomState,
                context.RandomIncrement);
        }

        internal static BurstContextResult TryCompleteSimple(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            BurstCallbackPhase phase)
            => Complete(ref batch, in frame, phase, default, default, 0, 1);

        internal static BurstContextResult TryCompleteObserver(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            ConditionResult result)
        {
            var roleGate = GateBatchExecute(in batch);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            var carrierRoleGate = BurstBindingBridgeCoreV2.GateCarrierRole(
                frame.Runtime,
                frame.Role);
            if (carrierRoleGate != BurstContextResult.Success)
            {
                return carrierRoleGate;
            }

            if (!SameBacking(batch.Runtime, frame.Runtime))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out _))
            {
                return CompletedFrameResult(frame.Runtime, frame.FrameId, frame.RequestOrdinal);
            }

            var frameGate = BurstBindingBridgeCoreV2.GateFrameCarrier(in frame);
            if (frameGate != BurstContextResult.Success)
            {
                return frameGate;
            }

            if ((byte)result > (byte)ConditionResult.Failure)
            {
                return LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.InvalidStatus);
            }

            return Complete(ref batch, in frame, BurstCallbackPhase.Observer, default, result, 0, 1);
        }

        internal static BurstContextResult TryFailDispatch(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            BurstContextResult failure)
        {
            var roleGate = GateBatchExecute(in batch);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            if (frame.Role != batch.Role
                || !SameBacking(batch.Runtime, frame.Runtime))
            {
                return BurstContextResult.InvalidHandle;
            }

            var carrierGate = BurstBindingBridgeCoreV2.GateCarrierRole(
                frame.Runtime,
                frame.Role);
            if (carrierGate != BurstContextResult.Success)
            {
                return carrierGate;
            }

            if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out var control)
                || frame.ValidationToken != Token(
                    control.OwnerId,
                    control.Generation,
                    frame.FrameId))
            {
                return CompletedFrameResult(frame.Runtime, frame.FrameId, frame.RequestOrdinal);
            }

            if (failure == BurstContextResult.Success
                || (byte)failure > (byte)BurstContextResult.InvalidStatus)
            {
                return LatchFrameFailure(
                    frame.Runtime,
                    frame.FrameId,
                    BurstContextResult.InvalidStatus);
            }

            if (control.FirstFailure != BurstContextResult.Success)
            {
                failure = control.FirstFailure;
            }
            else
            {
                LatchFrameFailure(frame.Runtime, frame.FrameId, failure);
                control.FirstFailure = failure;
            }

            if (!TryClaimFrameCompletion(frame.Runtime, frame.FrameId))
            {
                return BurstContextResult.StaleCompletion;
            }

            BurstBindingBridgeCoreV2.DiscardFrame(frame.Runtime, frame.FrameId);
            control.ActiveFrameId = 0;
            control.MemoryCommitted = 0;
            control.State = NativeBurstDispatchStateV2.Terminal;
            control.ResultCode = BurstExecutionCode.Faulted;
            control.DiagnosticNumber = Diagnostic(failure);
            WriteControl(batch.Runtime, control);
            return failure;
        }

        internal static BurstContextResult TryGetTime(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            out long value)
        {
            value = default;
            if (!TryActiveRequest(runtime, frameId, out _, out var request, out _))
            {
                return BurstContextResult.InvalidHandle;
            }

            value = request.TimeMicroseconds;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryNextUInt32(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong increment,
            ref ulong state,
            out uint value)
        {
            value = default;
            var validation = ValidateRandom(runtime, frameId, increment);
            if (validation != BurstContextResult.Success)
            {
                return LatchFrameFailure(runtime, frameId, validation);
            }

            value = Next(ref state, increment);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryNextUInt32(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong increment,
            ref ulong state,
            uint boundExclusive,
            out uint value)
        {
            value = default;
            var validation = ValidateRandom(runtime, frameId, increment);
            if (validation != BurstContextResult.Success)
            {
                return LatchFrameFailure(runtime, frameId, validation);
            }

            if (boundExclusive == 0)
            {
                return LatchFrameFailure(runtime, frameId, BurstContextResult.InvalidStatus);
            }

            var threshold = unchecked((uint)(0u - boundExclusive)) % boundExclusive;
            uint sample;
            do
            {
                sample = Next(ref state, increment);
            }
            while (sample < threshold);

            value = sample % boundExclusive;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryNextFloat32(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong increment,
            ref ulong state,
            out float value)
        {
            value = default;
            var validation = ValidateRandom(runtime, frameId, increment);
            if (validation != BurstContextResult.Success)
            {
                return LatchFrameFailure(runtime, frameId, validation);
            }

            value = (Next(ref state, increment) >> 8) * (1.0f / 16777216.0f);
            return BurstContextResult.Success;
        }

        private static BurstContextResult Complete(
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            BurstCallbackPhase phase,
            NodeStatus status,
            ConditionResult observerResult,
            ulong randomState,
            ulong randomIncrement)
        {
            var roleGate = GateBatchExecute(in batch);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            var carrierRoleGate = BurstBindingBridgeCoreV2.GateCarrierRole(
                frame.Runtime,
                frame.Role);
            if (carrierRoleGate != BurstContextResult.Success)
            {
                return carrierRoleGate;
            }

            if (!SameBacking(batch.Runtime, frame.Runtime))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (!TryPhase(in frame, phase, out var control, out var request, out var dispatchCase))
            {
                if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out _))
                {
                    return CompletedFrameResult(frame.Runtime, frame.FrameId, frame.RequestOrdinal);
                }

                var frameGate = BurstBindingBridgeCoreV2.GateFrameCarrier(in frame);
                return frameGate == BurstContextResult.Success
                    ? LatchFrameFailure(
                        frame.Runtime,
                        frame.FrameId,
                        BurstContextResult.PhaseViolation)
                    : frameGate;
            }

            if (control.FirstFailure != BurstContextResult.Success)
            {
                return control.FirstFailure;
            }

            if (phase != BurstCallbackPhase.Observer && control.MemoryCommitted == 0)
            {
                return LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.IncompleteValue);
            }

            if (phase == BurstCallbackPhase.Tick
                && (!IsStatus(status)
                    || !StatusAllowed(dispatchCase.PossibleStatuses, status)))
            {
                return LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.InvalidStatus);
            }

            if ((phase == BurstCallbackPhase.Enter || phase == BurstCallbackPhase.Tick)
                && (dispatchCase.HasRandomStream != 0
                    ? (randomIncrement & 1) == 0
                        || randomIncrement != frame.Runtime.RandomIncrements[(int)request.RandomStateIndex]
                    : randomState != 0 || randomIncrement != 1))
            {
                return LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.PhaseViolation);
            }

            var bindingPreflight = BurstBindingBridgeCoreV2.TryPreflightFramePublish(
                frame.Runtime,
                frame.FrameId);
            if (bindingPreflight != BurstContextResult.Success)
            {
                return bindingPreflight;
            }

            if (!TryClaimFrameCompletion(frame.Runtime, frame.FrameId))
            {
                return BurstContextResult.StaleCompletion;
            }

            BurstBindingBridgeCoreV2.PublishFrame(frame.Runtime, frame.FrameId);

            if (phase != BurstCallbackPhase.Observer)
            {
                CopyRange(
                    frame.Runtime.MemoryStaging,
                    request.MemoryOffset,
                    frame.Runtime.MemoryBytes,
                    request.MemoryOffset,
                    dispatchCase.MemorySize);
            }

            if ((phase == BurstCallbackPhase.Enter || phase == BurstCallbackPhase.Tick)
                && dispatchCase.HasRandomStream != 0)
            {
                Write(frame.Runtime.RandomStates, (int)request.RandomStateIndex, randomState);
            }

            Write(
                frame.Runtime.RequestStatuses,
                (int)control.Cursor,
                phase == BurstCallbackPhase.Tick
                    ? (byte)status
                    : phase == BurstCallbackPhase.Observer
                        ? (byte)observerResult
                        : (byte)0);
            control.ActiveFrameId = 0;
            control.MemoryCommitted = 0;
            control.Cursor++;
            control.InstancesVisited++;
            control.SegmentSteps++;
            control.State = control.Cursor == frame.Runtime.Requests.Length
                ? NativeBurstDispatchStateV2.Terminal
                : NativeBurstDispatchStateV2.Ready;
            control.ResultCode = BurstExecutionCode.Success;
            control.DiagnosticNumber = 0;
            WriteControl(frame.Runtime, control);
            return BurstContextResult.Success;
        }

        private static bool TryControl(
            BurstDispatchBackingV2 runtime,
            out NativeBurstDispatchControlV2 control)
        {
            control = default;
            if (!runtime.Control.IsCreated || runtime.Control.Length != 1)
            {
                return false;
            }

            control = runtime.Control[0];
            return runtime.OwnerId != 0
                && runtime.Generation != 0
                && control.OwnerId == runtime.OwnerId
                && control.Generation == runtime.Generation
                && control.State != NativeBurstDispatchStateV2.Disposed;
        }

        private static bool TryExecutionClaim(
            BurstDispatchBackingV2 runtime,
            out int claim)
        {
            claim = default;
            if (!runtime.ExecutionClaim.IsCreated || runtime.ExecutionClaim.Length != 1)
            {
                return false;
            }

            claim = runtime.ExecutionClaim[0];
            return claim >= 0 && claim <= 3;
        }

        private static bool TryClaimFrameAcquisition(BurstDispatchBackingV2 runtime, uint frameId)
        {
            var claims = runtime.FrameCompletionClaim;
            var expected = (long)frameId - 1L;
            var active = -(long)frameId;
            return claims.IsCreated
                && claims.Length == 1
                && frameId != 0
                && Interlocked.CompareExchange(ref claims.ElementAt(0), active, expected) == expected;
        }

        private static bool TryClaimFrameCompletion(BurstDispatchBackingV2 runtime, uint frameId)
        {
            var claims = runtime.FrameCompletionClaim;
            return claims.IsCreated
                && claims.Length == 1
                && frameId != 0
                && Interlocked.CompareExchange(
                    ref claims.ElementAt(0),
                    (long)frameId,
                    -(long)frameId) == -(long)frameId;
        }

        private static BurstContextResult CompletedFrameResult(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            uint requestOrdinal)
        {
            if (!TryControl(runtime, out var control)
                || !runtime.FrameCompletionClaim.IsCreated
                || runtime.FrameCompletionClaim.Length != 1
                || frameId == 0
                || frameId > control.NextFrameId
                || requestOrdinal >= runtime.Requests.Length
                || (runtime.FrameCompletionClaim[0] != frameId
                    && requestOrdinal >= control.Cursor))
            {
                return BurstContextResult.InvalidHandle;
            }

            return BurstContextResult.StaleCompletion;
        }

        private static BurstContextResult GateBatchRead(
            in BurstExecutionBatch batch,
            bool allowCompleted)
        {
            if (!TryExecutionClaim(batch.Runtime, out var claim))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (batch.Role == NativeBurstBatchRoleV2.Job)
            {
                return claim == 2
                    ? BurstContextResult.Success
                    : BurstContextResult.InvalidHandle;
            }

            if (claim == 2)
            {
                return BurstContextResult.PhaseViolation;
            }

            if (claim == 3 && !allowCompleted)
            {
                return BurstContextResult.InvalidHandle;
            }

            return batch.Role == NativeBurstBatchRoleV2.Host
                    || batch.Role == NativeBurstBatchRoleV2.CompletedHost
                    || batch.Role == NativeBurstBatchRoleV2.ScheduledHost && allowCompleted && claim == 3
                ? BurstContextResult.Success
                : BurstContextResult.InvalidHandle;
        }

        private static BurstContextResult GateBatchExecute(in BurstExecutionBatch batch)
        {
            if (!TryExecutionClaim(batch.Runtime, out var claim))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (batch.Role == NativeBurstBatchRoleV2.Job)
            {
                return claim == 2
                    ? BurstContextResult.Success
                    : BurstContextResult.InvalidHandle;
            }

            if (batch.Role == NativeBurstBatchRoleV2.Host)
            {
                return claim == 1
                    ? BurstContextResult.Success
                    : claim == 2
                        ? BurstContextResult.PhaseViolation
                        : BurstContextResult.InvalidHandle;
            }

            return claim == 2
                ? BurstContextResult.PhaseViolation
                : BurstContextResult.InvalidHandle;
        }

        private static bool TryClaimExecution(
            in BurstExecutionBatch batch,
            ref NativeBurstDispatchControlV2 control)
        {
            if (batch.Role == NativeBurstBatchRoleV2.ScheduledHost)
            {
                return false;
            }

            if (batch.Role == NativeBurstBatchRoleV2.Job)
            {
                return batch.Runtime.ExecutionClaim[0] == 2;
            }

            if (batch.Runtime.ExecutionClaim[0] == 0)
            {
                var claims = batch.Runtime.ExecutionClaim;
                var claim = Interlocked.CompareExchange(
                    ref claims.ElementAt(0),
                    1,
                    0);
                if (claim != 0)
                {
                    return false;
                }

            }

            return batch.Runtime.ExecutionClaim[0] == 1;
        }

        private static bool RoleCanExecute(
            in BurstExecutionBatch batch,
            in NativeBurstDispatchControlV2 control)
            => batch.Role == NativeBurstBatchRoleV2.Host && batch.Runtime.ExecutionClaim[0] == 1
                || batch.Role == NativeBurstBatchRoleV2.Job && batch.Runtime.ExecutionClaim[0] == 2;

        private static bool TryFrame(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            uint requestOrdinal,
            out NativeBurstDispatchControlV2 control)
            => TryControl(runtime, out control)
                && control.State == NativeBurstDispatchStateV2.Running
                && frameId != 0
                && control.ActiveFrameId == frameId
                && control.Cursor == requestOrdinal
                && requestOrdinal < runtime.Requests.Length;

        private static BurstContextResult PhaseFailure(in BurstDispatchFrame frame)
        {
            var roleGate = BurstBindingBridgeCoreV2.GateCarrierRole(
                frame.Runtime,
                frame.Role);
            if (roleGate != BurstContextResult.Success)
            {
                return roleGate;
            }

            return BurstBindingBridgeCoreV2.ValidateFrameCarrier(in frame)
                    && TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out _)
                ? LatchFrameFailure(frame.Runtime, frame.FrameId, BurstContextResult.PhaseViolation)
                : BurstContextResult.InvalidHandle;
        }

        private static BurstContextResult LatchFrameFailure(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            BurstContextResult failure)
        {
            if (failure == BurstContextResult.Success
                || !TryActiveRequest(runtime, frameId, out var control, out _, out _))
            {
                return failure;
            }

            if (control.FirstFailure == BurstContextResult.Success)
            {
                control.FirstFailure = failure;
                WriteControl(runtime, control);
            }

            return failure;
        }

        private static bool TryPhase(
            in BurstDispatchFrame frame,
            BurstCallbackPhase phase,
            out NativeBurstDispatchControlV2 control,
            out NativeBurstDispatchRequestV2 request,
            out NativeBurstDispatchCaseV2 dispatchCase)
        {
            control = default;
            request = default;
            dispatchCase = default;
            if (!BurstBindingBridgeCoreV2.ValidateFrameCarrier(in frame)
                || !TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out control))
            {
                return false;
            }

            request = frame.Runtime.Requests[(int)control.Cursor];
            dispatchCase = frame.Runtime.Cases[(int)request.CatalogCaseIndex];
            return request.Phase == phase && Includes(dispatchCase.Phases, phase);
        }

        private static bool TryActiveRequest(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            out NativeBurstDispatchControlV2 control,
            out NativeBurstDispatchRequestV2 request,
            out NativeBurstDispatchCaseV2 dispatchCase)
        {
            request = default;
            dispatchCase = default;
            if (!TryControl(runtime, out control)
                || control.State != NativeBurstDispatchStateV2.Running
                || control.ActiveFrameId != frameId
                || control.Cursor >= runtime.Requests.Length)
            {
                return false;
            }

            request = runtime.Requests[(int)control.Cursor];
            dispatchCase = runtime.Cases[(int)request.CatalogCaseIndex];
            return true;
        }

        private static bool ContextMatches(
            in BurstDispatchFrame frame,
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong token)
            => SameBacking(frame.Runtime, runtime)
                && BurstBindingBridgeCoreV2.ValidateFrameCarrier(in frame)
                && TryFrame(runtime, frameId, frame.RequestOrdinal, out var control)
                && token == frame.ValidationToken
                && token == Token(control.OwnerId, control.Generation, frameId);

        private static BurstContextResult ValidateCompletionContext(
            in BurstExecutionBatch batch,
            in BurstDispatchFrame frame,
            BurstDispatchBackingV2 contextRuntime,
            uint contextFrameId,
            ulong contextToken)
        {
            var batchGate = GateBatchExecute(in batch);
            if (batchGate != BurstContextResult.Success)
            {
                return batchGate;
            }

            var carrierRoleGate = BurstBindingBridgeCoreV2.GateCarrierRole(
                frame.Runtime,
                frame.Role);
            if (carrierRoleGate != BurstContextResult.Success)
            {
                return carrierRoleGate;
            }

            if (frame.Role != batch.Role || !SameBacking(batch.Runtime, frame.Runtime))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (!TryFrame(frame.Runtime, frame.FrameId, frame.RequestOrdinal, out _))
            {
                return CompletedFrameResult(frame.Runtime, frame.FrameId, frame.RequestOrdinal);
            }

            var frameGate = BurstBindingBridgeCoreV2.GateFrameCarrier(in frame);
            if (frameGate != BurstContextResult.Success)
            {
                return frameGate;
            }

            return ContextMatches(
                    in frame,
                    contextRuntime,
                    contextFrameId,
                    contextToken)
                ? BurstContextResult.Success
                : LatchFrameFailure(
                    frame.Runtime,
                    frame.FrameId,
                    BurstContextResult.InvalidHandle);
        }

        private static bool SameBacking(
            BurstDispatchBackingV2 left,
            BurstDispatchBackingV2 right)
            => left.OwnerId != 0
                && left.OwnerId == right.OwnerId
                && left.Generation == right.Generation
                && left.Control.Equals(right.Control);

        private static bool TryField(
            NativeArray<NativeBurstDispatchFieldV2> fields,
            uint first,
            uint count,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            out NativeBurstDispatchFieldV2 field,
            out uint relativeOffset)
        {
            field = default;
            relativeOffset = default;
            if ((ulong)first + count > (ulong)fields.Length)
            {
                return false;
            }

            for (uint descriptorIndex = 0; descriptorIndex < count; descriptorIndex++)
            {
                var candidate = fields[(int)(first + descriptorIndex)];
                if (candidate.FieldOrdinal != fieldOrdinal
                    || elementIndex < candidate.FirstElementIndex
                    || elementIndex - candidate.FirstElementIndex >= candidate.ElementCount)
                {
                    continue;
                }

                if (candidate.Encoding != encoding)
                {
                    return false;
                }

                field = candidate;
                var relativeElement = elementIndex - candidate.FirstElementIndex;
                var offset = (ulong)candidate.ByteOffset + (ulong)relativeElement * candidate.ElementSize;
                if (offset > uint.MaxValue)
                {
                    field = default;
                    return false;
                }

                relativeOffset = (uint)offset;
                return true;
            }

            return false;
        }

        private static BurstContextResult TryReadCanonical<T>(
            NativeArray<byte> source,
            uint offset,
            NativeBurstDispatchFieldEncodingV2 encoding,
            out T value)
            where T : unmanaged
        {
            value = default;
            var size = UnsafeUtility.SizeOf<T>();
            var expectedSize = EncodingSize(encoding);
            if (expectedSize == 0
                || size != expectedSize
                || (ulong)offset + expectedSize > (ulong)source.Length)
            {
                return BurstContextResult.TypeMismatch;
            }

            if (expectedSize == 1)
            {
                var raw = source[(int)offset];
                if (encoding == NativeBurstDispatchFieldEncodingV2.Boolean && raw > 1)
                {
                    return BurstContextResult.InvalidEncoding;
                }

                value = UnsafeUtility.As<byte, T>(ref raw);
            }
            else if (expectedSize == 2)
            {
                var raw = ReadUInt16LittleEndian(source, offset);
                value = UnsafeUtility.As<ushort, T>(ref raw);
            }
            else if (expectedSize == 4)
            {
                var raw = ReadUInt32LittleEndian(source, offset);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Float32
                    && (raw == 0x80000000u || (raw & 0x7f800000u) == 0x7f800000u))
                {
                    return BurstContextResult.InvalidEncoding;
                }

                value = UnsafeUtility.As<uint, T>(ref raw);
            }
            else
            {
                var raw = ReadUInt64LittleEndian(source, offset);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Float64
                    && (raw == 0x8000000000000000UL || (raw & 0x7ff0000000000000UL) == 0x7ff0000000000000UL))
                {
                    return BurstContextResult.InvalidEncoding;
                }

                value = UnsafeUtility.As<ulong, T>(ref raw);
            }

            return BurstContextResult.Success;
        }

        private static BurstContextResult TryWriteCanonical<T>(
            NativeArray<byte> destination,
            uint offset,
            NativeBurstDispatchFieldEncodingV2 encoding,
            T value)
            where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            var expectedSize = EncodingSize(encoding);
            if (expectedSize == 0
                || size != expectedSize
                || (ulong)offset + expectedSize > (ulong)destination.Length)
            {
                return BurstContextResult.TypeMismatch;
            }

            if (expectedSize == 1)
            {
                destination[(int)offset] = UnsafeUtility.As<T, byte>(ref value);
            }
            else if (expectedSize == 2)
            {
                var raw = UnsafeUtility.As<T, ushort>(ref value);
                WriteUInt16LittleEndian(destination, offset, raw);
            }
            else if (expectedSize == 4)
            {
                var raw = UnsafeUtility.As<T, uint>(ref value);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Float32)
                {
                    if ((raw & 0x7f800000u) == 0x7f800000u)
                    {
                        return BurstContextResult.InvalidEncoding;
                    }

                    if (raw == 0x80000000u)
                    {
                        raw = 0;
                    }
                }

                WriteUInt32LittleEndian(destination, offset, raw);
            }
            else
            {
                var raw = UnsafeUtility.As<T, ulong>(ref value);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Float64)
                {
                    if ((raw & 0x7ff0000000000000UL) == 0x7ff0000000000000UL)
                    {
                        return BurstContextResult.InvalidEncoding;
                    }

                    if (raw == 0x8000000000000000UL)
                    {
                        raw = 0;
                    }
                }

                WriteUInt64LittleEndian(destination, offset, raw);
            }

            return BurstContextResult.Success;
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
                    return 4;
                case NativeBurstDispatchFieldEncodingV2.Int64:
                case NativeBurstDispatchFieldEncodingV2.UInt64:
                case NativeBurstDispatchFieldEncodingV2.Float64:
                    return 8;
                case NativeBurstDispatchFieldEncodingV2.GeneratedHandle:
                    return 4;
                default:
                    return 0;
            }
        }

        private static ushort ReadUInt16LittleEndian(NativeArray<byte> source, uint offset)
            => (ushort)(source[(int)offset] | source[(int)(offset + 1)] << 8);

        private static uint ReadUInt32LittleEndian(NativeArray<byte> source, uint offset)
            => source[(int)offset]
                | (uint)source[(int)(offset + 1)] << 8
                | (uint)source[(int)(offset + 2)] << 16
                | (uint)source[(int)(offset + 3)] << 24;

        private static ulong ReadUInt64LittleEndian(NativeArray<byte> source, uint offset)
            => ReadUInt32LittleEndian(source, offset)
                | (ulong)ReadUInt32LittleEndian(source, offset + 4) << 32;

        private static void WriteUInt16LittleEndian(NativeArray<byte> destination, uint offset, ushort value)
        {
            destination[(int)offset] = (byte)value;
            destination[(int)(offset + 1)] = (byte)(value >> 8);
        }

        private static void WriteUInt32LittleEndian(NativeArray<byte> destination, uint offset, uint value)
        {
            destination[(int)offset] = (byte)value;
            destination[(int)(offset + 1)] = (byte)(value >> 8);
            destination[(int)(offset + 2)] = (byte)(value >> 16);
            destination[(int)(offset + 3)] = (byte)(value >> 24);
        }

        private static void WriteUInt64LittleEndian(NativeArray<byte> destination, uint offset, ulong value)
        {
            WriteUInt32LittleEndian(destination, offset, (uint)value);
            WriteUInt32LittleEndian(destination, offset + 4, (uint)(value >> 32));
        }

        private static void GetRandom(
            BurstDispatchBackingV2 runtime,
            in NativeBurstDispatchRequestV2 request,
            in NativeBurstDispatchCaseV2 dispatchCase,
            out ulong state,
            out ulong increment)
        {
            if (dispatchCase.HasRandomStream == 0)
            {
                state = 0;
                increment = 1;
                return;
            }

            state = runtime.RandomStates[(int)request.RandomStateIndex];
            increment = runtime.RandomIncrements[(int)request.RandomStateIndex];
        }

        private static BurstContextResult ValidateRandom(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong increment)
        {
            if (!TryActiveRequest(runtime, frameId, out _, out var request, out var dispatchCase))
            {
                return BurstContextResult.InvalidHandle;
            }

            if (dispatchCase.HasRandomStream == 0)
            {
                return BurstContextResult.PhaseViolation;
            }

            if ((increment & 1UL) == 0
                || request.RandomStateIndex >= runtime.RandomIncrements.Length
                || runtime.RandomIncrements[(int)request.RandomStateIndex] != increment)
            {
                return BurstContextResult.PhaseViolation;
            }

            return BurstContextResult.Success;
        }

        private static uint Next(ref ulong state, ulong increment)
        {
            var oldState = state;
            state = unchecked(oldState * PcgMultiplier + increment);
            var xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            var rotation = (int)(oldState >> 59);
            return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
        }

        private static BurstContextResult FaultWithoutFrame(
            BurstDispatchBackingV2 runtime,
            BurstContextResult failure)
        {
            if (!TryControl(runtime, out var control))
            {
                return BurstContextResult.InvalidHandle;
            }

            control.ActiveFrameId = 0;
            control.MemoryCommitted = 0;
            control.State = NativeBurstDispatchStateV2.Terminal;
            control.ResultCode = BurstExecutionCode.Faulted;
            control.DiagnosticNumber = Diagnostic(failure);
            WriteControl(runtime, control);
            return failure;
        }

        private static ushort Diagnostic(BurstContextResult failure)
        {
            switch (failure)
            {
                case BurstContextResult.InvalidHandle: return 4202;
                case BurstContextResult.TypeMismatch: return 4203;
                case BurstContextResult.PhaseViolation: return 4204;
                case BurstContextResult.CapacityExceeded: return 4307;
                case BurstContextResult.Overflow: return 4303;
                case BurstContextResult.StaleCompletion:
                case BurstContextResult.AlreadyCommitted: return 4311;
                default: return 4205;
            }
        }

        private static bool Includes(NativeBurstDispatchPhaseMaskV2 mask, BurstCallbackPhase phase)
            => (((byte)mask >> (int)phase) & 1) != 0;

        private static bool IsStatus(NodeStatus status)
            => status == NodeStatus.Success || status == NodeStatus.Failure || status == NodeStatus.Running;

        private static bool StatusAllowed(BurstNodeStatusMask mask, NodeStatus status)
        {
            var bit = status == NodeStatus.Success
                ? BurstNodeStatusMask.Success
                : status == NodeStatus.Failure
                    ? BurstNodeStatusMask.Failure
                    : BurstNodeStatusMask.Running;
            return (mask & bit) != 0;
        }

        private static ulong Token(ulong ownerId, uint generation, uint frameId)
        {
            var token = ownerId ^ ((ulong)generation << 32) ^ frameId ^ 0x9e3779b97f4a7c15UL;
            return token == 0 ? 1UL : token;
        }

        private static void CopyRange(
            NativeArray<byte> source,
            uint sourceOffset,
            NativeArray<byte> destination,
            uint destinationOffset,
            uint count)
        {
            for (uint index = 0; index < count; index++)
            {
                destination[(int)(destinationOffset + index)] = source[(int)(sourceOffset + index)];
            }
        }

        private static void ClearRange(NativeArray<byte> array, uint offset, uint count)
        {
            for (uint index = 0; index < count; index++)
            {
                array[(int)(offset + index)] = 0;
            }
        }

        private static void MarkRange(NativeArray<byte> array, uint offset, uint count)
        {
            for (uint index = 0; index < count; index++)
            {
                array[(int)(offset + index)] = 1;
            }
        }

        private static bool TryClaimSchedule(
            BurstDispatchBackingV2 runtime,
            int next,
            int expected)
        {
            var controls = runtime.ExecutionClaim;
            return Interlocked.CompareExchange(
                ref controls.ElementAt(0),
                next,
                expected) == expected;
        }

        private static void WriteControl(
            BurstDispatchBackingV2 runtime,
            NativeBurstDispatchControlV2 control)
        {
            var controls = runtime.Control;
            controls[0] = control;
        }

        private static void Write<T>(NativeArray<T> array, int index, T value)
            where T : struct
            => array[index] = value;
    }
}
