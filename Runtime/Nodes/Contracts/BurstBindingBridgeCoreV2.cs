using System.Threading;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT.Burst
{
    internal static class BurstBindingBridgeCoreV2
    {
        private const uint NoOrdinal = uint.MaxValue;

        internal static BurstContextResult TryReadBlackboardReadHandle<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong typeNumericId,
            uint typeVersion,
            out BlackboardReadHandle<T> value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.BlackboardRead,
                typeNumericId,
                typeVersion,
                0,
                0,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new BlackboardReadHandle<T>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryReadBlackboardWriteHandle<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong typeNumericId,
            uint typeVersion,
            out BlackboardWriteHandle<T> value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.BlackboardWrite,
                typeNumericId,
                typeVersion,
                0,
                0,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new BlackboardWriteHandle<T>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryReadBlackboardReadWriteHandle<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong typeNumericId,
            uint typeVersion,
            out BlackboardReadWriteHandle<T> value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.BlackboardReadWrite,
                typeNumericId,
                typeVersion,
                0,
                0,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new BlackboardReadWriteHandle<T>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryReadSnapshotHandle<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong typeNumericId,
            uint typeVersion,
            out SnapshotReadHandle<T> value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.SnapshotRead,
                typeNumericId,
                typeVersion,
                0,
                0,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new SnapshotReadHandle<T>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryReadCommandHandle<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong typeNumericId,
            uint typeVersion,
            out CommandHandle<T> value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.EffectCommand,
                typeNumericId,
                typeVersion,
                0,
                0,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new CommandHandle<T>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryReadAsyncOperationHandle<TStart, TCancel>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong startTypeNumericId,
            uint startTypeVersion,
            ulong cancelTypeNumericId,
            uint cancelTypeVersion,
            out AsyncOperationHandle<TStart, TCancel> value)
            where TStart : unmanaged
            where TCancel : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.AsyncOperation,
                startTypeNumericId,
                startTypeVersion,
                cancelTypeNumericId,
                cancelTypeVersion,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new AsyncOperationHandle<TStart, TCancel>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryReadCompletionHandle<T>(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            ulong typeNumericId,
            uint typeVersion,
            out CompletionHandle<T> value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveDecodedBinding(
                ref reader,
                fieldOrdinal,
                NativeBurstDispatchBindingKindV2.Completion,
                typeNumericId,
                typeVersion,
                0,
                0,
                out _,
                out var resolved,
                out var accessToken);
            if (result == BurstContextResult.Success)
            {
                value = new CompletionHandle<T>(resolved.TargetOrdinal, accessToken);
            }

            return result;
        }

        internal static BurstContextResult TryGetTime(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            out long value)
        {
            value = default;
            var gate = GateContext(runtime, frameId, validationToken, role, out _, out _, out _);
            return gate == BurstContextResult.Success
                ? BurstDispatchBridgeCoreV2.TryGetTime(runtime, frameId, out value)
                : gate;
        }

        internal static BurstContextResult TryNextUInt32(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            ulong increment,
            ref ulong state,
            out uint value)
        {
            value = default;
            var gate = GateContext(runtime, frameId, validationToken, role, out _, out _, out _);
            return gate == BurstContextResult.Success
                ? BurstDispatchBridgeCoreV2.TryNextUInt32(runtime, frameId, increment, ref state, out value)
                : gate;
        }

        internal static BurstContextResult TryNextUInt32(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            ulong increment,
            ref ulong state,
            uint boundExclusive,
            out uint value)
        {
            value = default;
            var gate = GateContext(runtime, frameId, validationToken, role, out _, out _, out _);
            return gate == BurstContextResult.Success
                ? BurstDispatchBridgeCoreV2.TryNextUInt32(
                    runtime,
                    frameId,
                    increment,
                    ref state,
                    boundExclusive,
                    out value)
                : gate;
        }

        internal static BurstContextResult TryNextFloat32(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            ulong increment,
            ref ulong state,
            out float value)
        {
            value = default;
            var gate = GateContext(runtime, frameId, validationToken, role, out _, out _, out _);
            return gate == BurstContextResult.Success
                ? BurstDispatchBridgeCoreV2.TryNextFloat32(runtime, frameId, increment, ref state, out value)
                : gate;
        }

        internal static BurstContextResult TryBeginBlackboardRead(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            NativeBurstDispatchBindingKindV2 kind,
            BurstCallbackPhase phase,
            out BurstValueReader reader)
        {
            reader = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                kind,
                phase,
                out var bindingOrdinal,
                out var binding,
                out var resolved,
                out _,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            result = TryAllocateBlackboardReader(
                runtime,
                frameId,
                bindingOrdinal,
                in binding,
                in resolved,
                out var sessionOrdinal);
            if (result != BurstContextResult.Success)
            {
                return Latch(runtime, frameId, result);
            }

            reader = new BurstValueReader(
                validationToken,
                runtime,
                frameId,
                bindingOrdinal,
                sessionOrdinal,
                role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryBeginBlackboardWrite(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            NativeBurstDispatchBindingKindV2 kind,
            BurstCallbackPhase phase,
            out BurstValueWriter writer)
        {
            writer = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                kind,
                phase,
                out var bindingOrdinal,
                out var binding,
                out _,
                out _,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            result = TryAllocateSession(
                runtime,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.BlackboardWrite,
                binding.PrimaryValueSize,
                false,
                default,
                0,
                NoOrdinal,
                default,
                out var sessionOrdinal);
            if (result != BurstContextResult.Success)
            {
                return Latch(runtime, frameId, result);
            }

            writer = new BurstValueWriter(
                validationToken,
                runtime,
                frameId,
                bindingOrdinal,
                sessionOrdinal,
                role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryBeginSnapshotRead(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            BurstCallbackPhase phase,
            out BurstValueReader reader)
        {
            reader = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                NativeBurstDispatchBindingKindV2.SnapshotRead,
                phase,
                out var bindingOrdinal,
                out var binding,
                out var resolved,
                out _,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            result = TryAllocateSession(
                runtime,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.SnapshotRead,
                binding.PrimaryValueSize,
                true,
                runtime.BindingValueBytes,
                resolved.LiveValueOffset,
                NoOrdinal,
                default,
                out var sessionOrdinal);
            if (result != BurstContextResult.Success)
            {
                return Latch(runtime, frameId, result);
            }

            reader = new BurstValueReader(
                validationToken,
                runtime,
                frameId,
                bindingOrdinal,
                sessionOrdinal,
                role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryBeginConsume(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            BurstCallbackPhase phase,
            OperationId operationId,
            out BurstCompletionOutcome outcome,
            out BurstValueReader reader)
        {
            outcome = default;
            reader = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                NativeBurstDispatchBindingKindV2.Completion,
                phase,
                out var bindingOrdinal,
                out var binding,
                out var resolved,
                out var request,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (!OwnsOperation(in request, operationId))
            {
                return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
            }

            var completionOrdinal = NoOrdinal;
            for (var index = 0; index < runtime.Completions.Length; index++)
            {
                var candidate = runtime.Completions[index];
                if (candidate.TargetOrdinal == resolved.TargetOrdinal
                    && candidate.OperationId == operationId)
                {
                    if (candidate.State != NativeBurstDispatchCompletionStateV2.Available)
                    {
                        return Latch(runtime, frameId, BurstContextResult.StaleCompletion);
                    }

                    completionOrdinal = (uint)index;
                    outcome = candidate.Outcome;
                    result = TryAllocateSession(
                        runtime,
                        frameId,
                        bindingOrdinal,
                        NativeBurstDispatchValueSessionKindV2.CompletionRead,
                        binding.PrimaryValueSize,
                        true,
                        runtime.CompletionPayloadBytes,
                        candidate.PayloadOffset,
                        completionOrdinal,
                        operationId,
                        out var sessionOrdinal);
                    if (result != BurstContextResult.Success)
                    {
                        outcome = default;
                        return Latch(runtime, frameId, result);
                    }

                    reader = new BurstValueReader(
                        validationToken,
                        runtime,
                        frameId,
                        bindingOrdinal,
                        sessionOrdinal,
                        role);
                    return BurstContextResult.Success;
                }
            }

            return Latch(runtime, frameId, BurstContextResult.StaleCompletion);
        }

        internal static BurstContextResult TryBeginEffect(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            BurstCallbackPhase phase,
            out BurstValueWriter writer)
        {
            writer = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                NativeBurstDispatchBindingKindV2.EffectCommand,
                phase,
                out var bindingOrdinal,
                out var binding,
                out _,
                out _,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            result = TryAllocateSession(
                runtime,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.EffectWrite,
                binding.PrimaryValueSize,
                false,
                default,
                0,
                NoOrdinal,
                default,
                out var sessionOrdinal);
            if (result != BurstContextResult.Success)
            {
                return Latch(runtime, frameId, result);
            }

            writer = new BurstValueWriter(
                validationToken,
                runtime,
                frameId,
                bindingOrdinal,
                sessionOrdinal,
                role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryBeginStart(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            BurstCallbackPhase phase,
            out BurstValueWriter startWriter,
            out BurstValueWriter faultCancelWriter)
        {
            startWriter = default;
            faultCancelWriter = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                NativeBurstDispatchBindingKindV2.AsyncOperation,
                phase,
                out var bindingOrdinal,
                out var binding,
                out _,
                out _,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (!TryTransaction(runtime, frameId, out var transaction)
                || (ulong)transaction.SessionCount + 2UL > (ulong)runtime.ValueSessions.Length
                || (ulong)transaction.StagingByteCount
                    + binding.PrimaryValueSize
                    + binding.SecondaryValueSize > (ulong)runtime.ValueStagingBytes.Length)
            {
                return Latch(runtime, frameId, BurstContextResult.CapacityExceeded);
            }

            var startOrdinal = transaction.SessionCount;
            var cancelOrdinal = startOrdinal + 1u;
            InitializeSession(
                runtime,
                ref transaction,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.StartWrite,
                binding.PrimaryValueSize,
                false,
                default,
                0,
                NoOrdinal,
                default,
                cancelOrdinal);
            InitializeSession(
                runtime,
                ref transaction,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.FaultCancelWrite,
                binding.SecondaryValueSize,
                false,
                default,
                0,
                NoOrdinal,
                default,
                startOrdinal);
            WriteTransaction(runtime, transaction);
            startWriter = new BurstValueWriter(validationToken, runtime, frameId, bindingOrdinal, startOrdinal, role);
            faultCancelWriter = new BurstValueWriter(validationToken, runtime, frameId, bindingOrdinal, cancelOrdinal, role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryBeginCancel(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            OperationId operationId,
            out BurstValueWriter writer)
        {
            writer = default;
            var result = TryResolveOpaqueHandle(
                runtime,
                frameId,
                validationToken,
                role,
                targetOrdinal,
                accessToken,
                NativeBurstDispatchBindingKindV2.AsyncOperation,
                BurstCallbackPhase.Abort,
                out var bindingOrdinal,
                out var binding,
                out var resolved,
                out var request,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (!OwnsOperation(in request, operationId)
                || !TryFindActiveOperation(
                    runtime,
                    operationId,
                    resolved.TargetOrdinal,
                    in binding,
                    out _))
            {
                return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
            }

            result = TryAllocateSession(
                runtime,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.CancelWrite,
                binding.SecondaryValueSize,
                false,
                default,
                0,
                NoOrdinal,
                operationId,
                out var sessionOrdinal);
            if (result != BurstContextResult.Success)
            {
                return Latch(runtime, frameId, result);
            }

            writer = new BurstValueWriter(
                validationToken,
                runtime,
                frameId,
                bindingOrdinal,
                sessionOrdinal,
                role);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryReadValue<T>(
            ref BurstValueReader reader,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            out T value)
            where T : unmanaged
        {
            value = default;
            var result = TryResolveReader(
                ref reader,
                NativeBurstDispatchValueSessionStateV2.Active,
                out var sessionOrdinal,
                out var session,
                out var binding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            GetSessionLayout(in session, in binding, out var firstField, out var fieldCount, out _);
            if (!TryFindValueField(
                    reader.Runtime.ValueFields,
                    firstField,
                    fieldCount,
                    fieldOrdinal,
                    elementIndex,
                    encoding,
                    out var fieldOffset,
                    out var fieldSize))
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.TypeMismatch);
            }

            result = TryReadCanonical(
                reader.Runtime.ValueStagingBytes,
                session.StagingOffset + fieldOffset,
                encoding,
                out value);
            if (result != BurstContextResult.Success)
            {
                return Latch(reader.Runtime, reader.FrameId, result);
            }

            MarkRange(reader.Runtime.ValueMarks, session.StagingOffset + fieldOffset, fieldSize);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCompleteValueRead(ref BurstValueReader reader)
        {
            var result = TryResolveReader(
                ref reader,
                NativeBurstDispatchValueSessionStateV2.Active,
                out var sessionOrdinal,
                out var session,
                out var binding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            GetSessionLayout(in session, in binding, out var firstField, out var fieldCount, out _);
            if (!AllFieldsMarked(
                    reader.Runtime,
                    session.StagingOffset,
                    firstField,
                    fieldCount))
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.IncompleteValue);
            }

            session.State = NativeBurstDispatchValueSessionStateV2.ReadComplete;
            WriteSession(reader.Runtime, sessionOrdinal, session);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryWriteValue<T>(
            ref BurstValueWriter writer,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            T value)
            where T : unmanaged
        {
            var result = TryResolveWriter(
                ref writer,
                NativeBurstDispatchValueSessionStateV2.Active,
                out _,
                out var session,
                out var binding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            GetSessionLayout(in session, in binding, out var firstField, out var fieldCount, out _);
            if (!TryFindValueField(
                    writer.Runtime.ValueFields,
                    firstField,
                    fieldCount,
                    fieldOrdinal,
                    elementIndex,
                    encoding,
                    out var fieldOffset,
                    out var fieldSize))
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.TypeMismatch);
            }

            if (AnyMarked(writer.Runtime.ValueMarks, session.StagingOffset + fieldOffset, fieldSize))
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.AlreadyCommitted);
            }

            result = TryWriteCanonical(
                writer.Runtime.ValueStagingBytes,
                session.StagingOffset + fieldOffset,
                encoding,
                value);
            if (result != BurstContextResult.Success)
            {
                return Latch(writer.Runtime, writer.FrameId, result);
            }

            MarkRange(writer.Runtime.ValueMarks, session.StagingOffset + fieldOffset, fieldSize);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCommitBlackboardWrite(ref BurstValueWriter writer)
            => TrySealWriter(
                ref writer,
                NativeBurstDispatchValueSessionKindV2.BlackboardWrite,
                false,
                false);

        internal static BurstContextResult TryCommitEffect(ref BurstValueWriter writer)
            => TrySealWriter(
                ref writer,
                NativeBurstDispatchValueSessionKindV2.EffectWrite,
                true,
                false);

        internal static BurstContextResult TryCommitConsume(ref BurstValueReader reader)
        {
            var result = TryResolveReader(
                ref reader,
                NativeBurstDispatchValueSessionStateV2.ReadComplete,
                out var sessionOrdinal,
                out var session,
                out _);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (session.Kind != NativeBurstDispatchValueSessionKindV2.CompletionRead
                || session.CompletionOrdinal >= reader.Runtime.Completions.Length
                || reader.Runtime.Completions[(int)session.CompletionOrdinal].State
                    != NativeBurstDispatchCompletionStateV2.Available)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.StaleCompletion);
            }

            if (!TryTransaction(reader.Runtime, reader.FrameId, out var transaction))
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle);
            }

            for (uint index = 0; index < transaction.SessionCount; index++)
            {
                if (index == sessionOrdinal)
                {
                    continue;
                }

                var candidate = reader.Runtime.ValueSessions[(int)index];
                if (candidate.FrameId == reader.FrameId
                    && candidate.Kind == NativeBurstDispatchValueSessionKindV2.CompletionRead
                    && candidate.State == NativeBurstDispatchValueSessionStateV2.ConsumeSealed
                    && candidate.CompletionOrdinal == session.CompletionOrdinal)
                {
                    return Latch(reader.Runtime, reader.FrameId, BurstContextResult.AlreadyCommitted);
                }
            }

            session.State = NativeBurstDispatchValueSessionStateV2.ConsumeSealed;
            WriteSession(reader.Runtime, sessionOrdinal, session);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCommitStart(
            ref BurstValueWriter startWriter,
            ref BurstValueWriter faultCancelWriter,
            out OperationId operationId)
        {
            operationId = default;
            var result = TryResolveWriter(
                ref startWriter,
                NativeBurstDispatchValueSessionStateV2.Active,
                out var startOrdinal,
                out var startSession,
                out var binding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            result = TryResolveWriter(
                ref faultCancelWriter,
                NativeBurstDispatchValueSessionStateV2.Active,
                out var cancelOrdinal,
                out var cancelSession,
                out var cancelBinding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (!SameRuntime(startWriter.Runtime, faultCancelWriter.Runtime)
                || startWriter.FrameId != faultCancelWriter.FrameId
                || startWriter.BindingOrdinal != faultCancelWriter.BindingOrdinal
                || startSession.Kind != NativeBurstDispatchValueSessionKindV2.StartWrite
                || cancelSession.Kind != NativeBurstDispatchValueSessionKindV2.FaultCancelWrite
                || startSession.CompanionSessionOrdinal != cancelOrdinal
                || cancelSession.CompanionSessionOrdinal != startOrdinal
                || binding.Kind != NativeBurstDispatchBindingKindV2.AsyncOperation
                || cancelBinding.Kind != NativeBurstDispatchBindingKindV2.AsyncOperation)
            {
                return Latch(startWriter.Runtime, startWriter.FrameId, BurstContextResult.InvalidHandle);
            }

            var startComplete = ValidateSessionComplete(
                startWriter.Runtime,
                in startSession,
                in binding);
            if (startComplete != BurstContextResult.Success)
            {
                return Latch(startWriter.Runtime, startWriter.FrameId, startComplete);
            }

            var cancelComplete = ValidateSessionComplete(
                faultCancelWriter.Runtime,
                in cancelSession,
                in cancelBinding);
            if (cancelComplete != BurstContextResult.Success)
            {
                return Latch(startWriter.Runtime, startWriter.FrameId, cancelComplete);
            }

            if (!TryTransaction(startWriter.Runtime, startWriter.FrameId, out var transaction)
                || transaction.NextOperationSequence == ulong.MaxValue)
            {
                return Latch(startWriter.Runtime, startWriter.FrameId, BurstContextResult.Overflow);
            }

            if (!CanSealReversible(
                    startWriter.Runtime,
                    in transaction,
                    1UL,
                    (ulong)binding.PrimaryValueSize + binding.SecondaryValueSize,
                    1UL))
            {
                return Latch(startWriter.Runtime, startWriter.FrameId, BurstContextResult.CapacityExceeded);
            }

            if (!TryActiveFrame(
                    startWriter.Runtime,
                    startWriter.FrameId,
                    startWriter.ValidationToken,
                    out _,
                    out var request,
                    out _))
            {
                return BurstContextResult.InvalidHandle;
            }

            operationId = new OperationId(
                request.TreeInstanceId,
                new RuntimeNodeIndex(request.RuntimeNodeIndex),
                request.ActivationGeneration,
                transaction.NextOperationSequence);
            transaction.NextOperationSequence++;
            startSession.OperationId = operationId;
            startSession.State = NativeBurstDispatchValueSessionStateV2.Sealed;
            cancelSession.OperationId = operationId;
            cancelSession.State = NativeBurstDispatchValueSessionStateV2.Sealed;
            WriteSession(startWriter.Runtime, startOrdinal, startSession);
            WriteSession(startWriter.Runtime, cancelOrdinal, cancelSession);
            WriteTransaction(startWriter.Runtime, transaction);
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryCommitCancel(ref BurstValueWriter writer)
        {
            var result = TryResolveWriter(
                ref writer,
                NativeBurstDispatchValueSessionStateV2.Active,
                out var sessionOrdinal,
                out var session,
                out var binding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (session.Kind != NativeBurstDispatchValueSessionKindV2.CancelWrite)
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.InvalidHandle);
            }

            var complete = ValidateSessionComplete(writer.Runtime, in session, in binding);
            if (complete != BurstContextResult.Success)
            {
                return Latch(writer.Runtime, writer.FrameId, complete);
            }

            if (!TryActiveFrame(
                    writer.Runtime,
                    writer.FrameId,
                    writer.ValidationToken,
                    out _,
                    out var request,
                    out _)
                || !TryResolvedBinding(
                    writer.Runtime,
                    in request,
                    in binding,
                    out var resolved)
                || !TryFindActiveOperation(
                    writer.Runtime,
                    session.OperationId,
                    resolved.TargetOrdinal,
                    in binding,
                    out var operationOrdinal))
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.InvalidHandle);
            }

            var operation = writer.Runtime.Operations[(int)operationOrdinal];
            operation.State = NativeBurstDispatchOperationStateV2.Tombstoned;
            var runtime = writer.Runtime;
            runtime.Operations[(int)operationOrdinal] = operation;
            session.State = NativeBurstDispatchValueSessionStateV2.Sealed;
            WriteSession(writer.Runtime, sessionOrdinal, session);

            if (!TryTransaction(writer.Runtime, writer.FrameId, out var transaction)
                || (ulong)transaction.CommandCount + 1UL > (ulong)writer.Runtime.Commands.Length
                || (ulong)transaction.CommandPayloadByteCount + binding.SecondaryValueSize
                    > (ulong)writer.Runtime.CommandPayloadBytes.Length)
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.CapacityExceeded);
            }

            AppendCommand(
                writer.Runtime,
                ref transaction,
                NativeBurstDispatchCommandKindV2.Cancel,
                resolved.TargetOrdinal,
                session.OperationId,
                session.StagingOffset,
                binding.SecondaryValueSize);
            WriteTransaction(writer.Runtime, transaction);
            return BurstContextResult.Success;
        }

        internal static void PrepareFrame(BurstDispatchBackingV2 runtime, uint frameId)
        {
            if (!runtime.TransactionControl.IsCreated || runtime.TransactionControl.Length != 1)
            {
                return;
            }

            var transaction = runtime.TransactionControl[0];
            transaction.ActiveFrameId = frameId;
            transaction.SessionCount = 0;
            transaction.StagingByteCount = 0;
            WriteTransaction(runtime, transaction);
        }

        internal static BurstContextResult TryPreflightFramePublish(
            BurstDispatchBackingV2 runtime,
            uint frameId)
        {
            if (!TryTransaction(runtime, frameId, out var transaction))
            {
                return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
            }

            ulong commandCount = 0;
            ulong payloadBytes = 0;
            ulong operationCount = 0;
            for (uint index = 0; index < transaction.SessionCount; index++)
            {
                var session = runtime.ValueSessions[(int)index];
                if (session.FrameId != frameId)
                {
                    return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
                }

                switch (session.Kind)
                {
                    case NativeBurstDispatchValueSessionKindV2.BlackboardRead:
                    case NativeBurstDispatchValueSessionKindV2.SnapshotRead:
                        if (session.State != NativeBurstDispatchValueSessionStateV2.ReadComplete)
                        {
                            return Latch(runtime, frameId, BurstContextResult.IncompleteValue);
                        }

                        break;
                    case NativeBurstDispatchValueSessionKindV2.CompletionRead:
                        if (session.State != NativeBurstDispatchValueSessionStateV2.ConsumeSealed
                            || session.CompletionOrdinal >= runtime.Completions.Length
                            || runtime.Completions[(int)session.CompletionOrdinal].State
                                != NativeBurstDispatchCompletionStateV2.Available)
                        {
                            return Latch(runtime, frameId, BurstContextResult.IncompleteValue);
                        }

                        break;
                    case NativeBurstDispatchValueSessionKindV2.BlackboardWrite:
                        if (session.State != NativeBurstDispatchValueSessionStateV2.Sealed)
                        {
                            return Latch(runtime, frameId, BurstContextResult.IncompleteValue);
                        }

                        break;
                    case NativeBurstDispatchValueSessionKindV2.EffectWrite:
                        if (session.State != NativeBurstDispatchValueSessionStateV2.Sealed)
                        {
                            return Latch(runtime, frameId, BurstContextResult.IncompleteValue);
                        }

                        commandCount++;
                        payloadBytes += runtime.Bindings[(int)session.BindingOrdinal].PrimaryValueSize;
                        break;
                    case NativeBurstDispatchValueSessionKindV2.StartWrite:
                        if (session.State != NativeBurstDispatchValueSessionStateV2.Sealed
                            || session.CompanionSessionOrdinal >= transaction.SessionCount)
                        {
                            return Latch(runtime, frameId, BurstContextResult.IncompleteValue);
                        }

                        var startBinding = runtime.Bindings[(int)session.BindingOrdinal];
                        commandCount++;
                        operationCount++;
                        payloadBytes += (ulong)startBinding.PrimaryValueSize + startBinding.SecondaryValueSize;
                        break;
                    case NativeBurstDispatchValueSessionKindV2.FaultCancelWrite:
                    case NativeBurstDispatchValueSessionKindV2.CancelWrite:
                        if (session.State != NativeBurstDispatchValueSessionStateV2.Sealed)
                        {
                            return Latch(runtime, frameId, BurstContextResult.IncompleteValue);
                        }

                        break;
                    default:
                        return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
                }
            }

            return HasPublishedCapacity(runtime, in transaction, commandCount, payloadBytes, operationCount)
                ? BurstContextResult.Success
                : Latch(runtime, frameId, BurstContextResult.CapacityExceeded);
        }

        internal static void PublishFrame(BurstDispatchBackingV2 runtime, uint frameId)
        {
            if (!TryTransaction(runtime, frameId, out var transaction)
                || !TryActiveFrame(runtime, frameId, out _, out var request, out _))
            {
                return;
            }

            for (uint index = 0; index < transaction.SessionCount; index++)
            {
                var session = runtime.ValueSessions[(int)index];
                var binding = runtime.Bindings[(int)session.BindingOrdinal];
                if (!TryResolvedBinding(runtime, in request, in binding, out var resolved))
                {
                    continue;
                }

                switch (session.Kind)
                {
                    case NativeBurstDispatchValueSessionKindV2.BlackboardWrite:
                        CopyRange(
                            runtime.ValueStagingBytes,
                            session.StagingOffset,
                            runtime.BindingValueBytes,
                            resolved.LiveValueOffset,
                            binding.PrimaryValueSize);
                        break;
                    case NativeBurstDispatchValueSessionKindV2.CompletionRead:
                    {
                        var completion = runtime.Completions[(int)session.CompletionOrdinal];
                        completion.State = NativeBurstDispatchCompletionStateV2.Consumed;
                        runtime.Completions[(int)session.CompletionOrdinal] = completion;
                        break;
                    }
                    case NativeBurstDispatchValueSessionKindV2.EffectWrite:
                        AppendCommand(
                            runtime,
                            ref transaction,
                            NativeBurstDispatchCommandKindV2.Effect,
                            resolved.TargetOrdinal,
                            default,
                            session.StagingOffset,
                            binding.PrimaryValueSize);
                        break;
                    case NativeBurstDispatchValueSessionKindV2.StartWrite:
                    {
                        var faultSession = runtime.ValueSessions[(int)session.CompanionSessionOrdinal];
                        AppendCommand(
                            runtime,
                            ref transaction,
                            NativeBurstDispatchCommandKindV2.Start,
                            resolved.TargetOrdinal,
                            session.OperationId,
                            session.StagingOffset,
                            binding.PrimaryValueSize);
                        var faultOffset = transaction.CommandPayloadByteCount;
                        CopyRange(
                            runtime.ValueStagingBytes,
                            faultSession.StagingOffset,
                            runtime.CommandPayloadBytes,
                            faultOffset,
                            binding.SecondaryValueSize);
                        transaction.CommandPayloadByteCount += binding.SecondaryValueSize;
                        runtime.Operations[(int)transaction.OperationCount] = new NativeBurstDispatchOperationV2(
                            session.OperationId,
                            resolved.TargetOrdinal,
                            binding.PrimaryTypeNumericId,
                            binding.PrimaryTypeVersion,
                            binding.SecondaryTypeNumericId,
                            binding.SecondaryTypeVersion,
                            faultOffset,
                            binding.SecondaryValueSize);
                        transaction.OperationCount++;
                        break;
                    }
                }
            }

            transaction.ActiveFrameId = 0;
            transaction.SessionCount = 0;
            transaction.StagingByteCount = 0;
            WriteTransaction(runtime, transaction);
        }

        internal static void DiscardFrame(BurstDispatchBackingV2 runtime, uint frameId)
        {
            if (!TryTransaction(runtime, frameId, out var transaction))
            {
                return;
            }

            transaction.ActiveFrameId = 0;
            transaction.SessionCount = 0;
            transaction.StagingByteCount = 0;
            WriteTransaction(runtime, transaction);
        }

        internal static BurstContextResult GateFrameCarrier(in BurstDispatchFrame frame)
        {
            var gate = GateRole(frame.Runtime, frame.Role);
            if (gate != BurstContextResult.Success)
            {
                return gate;
            }

            return TryActiveFrame(
                    frame.Runtime,
                    frame.FrameId,
                    frame.ValidationToken,
                    out var control,
                    out _,
                    out _)
                && control.Cursor == frame.RequestOrdinal
                    ? BurstContextResult.Success
                    : BurstContextResult.InvalidHandle;
        }

        internal static bool ValidateFrameCarrier(in BurstDispatchFrame frame)
            => GateFrameCarrier(in frame) == BurstContextResult.Success;

        internal static BurstContextResult GateCarrierRole(
            BurstDispatchBackingV2 runtime,
            NativeBurstBatchRoleV2 role)
            => GateRole(runtime, role);

        internal static BurstContextResult GateConfigurationReader(in BurstConfigurationReader reader)
        {
            var gate = GateRole(reader.Runtime, reader.Role);
            if (gate != BurstContextResult.Success)
            {
                return gate;
            }

            return TryActiveFrame(
                    reader.Runtime,
                    reader.FrameId,
                    reader.ValidationToken,
                    out var control,
                    out _,
                    out _)
                && control.Cursor == reader.RequestOrdinal
                    ? BurstContextResult.Success
                    : BurstContextResult.InvalidHandle;
        }

        internal static bool ValidateConfigurationReader(in BurstConfigurationReader reader)
            => GateConfigurationReader(in reader) == BurstContextResult.Success;

        internal static BurstContextResult GateMemoryAccessor(in BurstMemoryAccessor accessor)
        {
            var gate = GateRole(accessor.Runtime, accessor.Role);
            if (gate != BurstContextResult.Success)
            {
                return gate;
            }

            return TryActiveFrame(
                    accessor.Runtime,
                    accessor.FrameId,
                    accessor.ValidationToken,
                    out var control,
                    out _,
                    out _)
                && control.Cursor == accessor.RequestOrdinal
                    ? BurstContextResult.Success
                    : BurstContextResult.InvalidHandle;
        }

        internal static bool ValidateMemoryAccessor(in BurstMemoryAccessor accessor)
            => GateMemoryAccessor(in accessor) == BurstContextResult.Success;

        private static BurstContextResult TryResolveDecodedBinding(
            ref BurstConfigurationReader reader,
            uint fieldOrdinal,
            NativeBurstDispatchBindingKindV2 expectedKind,
            ulong primaryTypeNumericId,
            uint primaryTypeVersion,
            ulong secondaryTypeNumericId,
            uint secondaryTypeVersion,
            out uint globalBindingOrdinal,
            out NativeBurstDispatchResolvedBindingV2 resolved,
            out uint accessToken)
        {
            globalBindingOrdinal = default;
            resolved = default;
            accessToken = default;
            var gate = GateContext(
                reader.Runtime,
                reader.FrameId,
                reader.ValidationToken,
                reader.Role,
                out var control,
                out var request,
                out var dispatchCase);
            if (gate != BurstContextResult.Success || control.Cursor != reader.RequestOrdinal)
            {
                return gate == BurstContextResult.Success
                    ? Latch(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle)
                    : gate;
            }

            if (!TryFindValueField(
                    reader.Runtime.ConfigurationFields,
                    dispatchCase.FirstConfigurationField,
                    dispatchCase.ConfigurationFieldCount,
                    fieldOrdinal,
                    0,
                    NativeBurstDispatchFieldEncodingV2.GeneratedHandle,
                    out var fieldOffset,
                    out _)
                || (ulong)request.ConfigurationOffset + fieldOffset + 4UL
                    > (ulong)reader.Runtime.ConfigurationBytes.Length)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.TypeMismatch);
            }

            var localOrdinal = ReadUInt32(
                reader.Runtime.ConfigurationBytes,
                request.ConfigurationOffset + fieldOffset);
            if (localOrdinal == uint.MaxValue
                || localOrdinal >= dispatchCase.BindingCount
                || localOrdinal >= request.ResolvedBindingCount)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle);
            }

            globalBindingOrdinal = dispatchCase.FirstBinding + localOrdinal;
            var resolvedOrdinal = request.FirstResolvedBinding + localOrdinal;
            if (globalBindingOrdinal >= reader.Runtime.Bindings.Length
                || resolvedOrdinal >= reader.Runtime.ResolvedBindings.Length)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle);
            }

            var binding = reader.Runtime.Bindings[(int)globalBindingOrdinal];
            resolved = reader.Runtime.ResolvedBindings[(int)resolvedOrdinal];
            if (binding.BindingOrdinal != localOrdinal
                || resolved.BindingOrdinal != localOrdinal
                || binding.ConfigurationFieldOrdinal != fieldOrdinal
                || binding.Kind != expectedKind
                || binding.PrimaryTypeNumericId != primaryTypeNumericId
                || binding.PrimaryTypeVersion != primaryTypeVersion
                || binding.SecondaryTypeNumericId != secondaryTypeNumericId
                || binding.SecondaryTypeVersion != secondaryTypeVersion)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.TypeMismatch);
            }

            accessToken = BindingAccessToken(
                control.OwnerId,
                control.Generation,
                reader.FrameId,
                globalBindingOrdinal);
            return BurstContextResult.Success;
        }

        private static BurstContextResult TryResolveOpaqueHandle(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            uint targetOrdinal,
            uint accessToken,
            NativeBurstDispatchBindingKindV2 expectedKind,
            BurstCallbackPhase expectedPhase,
            out uint globalBindingOrdinal,
            out NativeBurstDispatchBindingV2 binding,
            out NativeBurstDispatchResolvedBindingV2 resolved,
            out NativeBurstDispatchRequestV2 request,
            out NativeBurstDispatchCaseV2 dispatchCase)
        {
            globalBindingOrdinal = default;
            binding = default;
            resolved = default;
            request = default;
            dispatchCase = default;
            var gate = GateContext(
                runtime,
                frameId,
                validationToken,
                role,
                out var control,
                out request,
                out dispatchCase);
            if (gate != BurstContextResult.Success)
            {
                return gate;
            }

            if (request.Phase != expectedPhase)
            {
                return Latch(runtime, frameId, BurstContextResult.PhaseViolation);
            }

            for (uint localOrdinal = 0; localOrdinal < dispatchCase.BindingCount; localOrdinal++)
            {
                var candidateGlobal = dispatchCase.FirstBinding + localOrdinal;
                var candidateResolved = request.FirstResolvedBinding + localOrdinal;
                if (candidateGlobal >= runtime.Bindings.Length
                    || candidateResolved >= runtime.ResolvedBindings.Length)
                {
                    return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
                }

                var candidateBinding = runtime.Bindings[(int)candidateGlobal];
                var candidate = runtime.ResolvedBindings[(int)candidateResolved];
                if (candidateBinding.Kind == expectedKind
                    && candidate.TargetOrdinal == targetOrdinal
                    && accessToken == BindingAccessToken(
                        control.OwnerId,
                        control.Generation,
                        frameId,
                        candidateGlobal))
                {
                    globalBindingOrdinal = candidateGlobal;
                    binding = candidateBinding;
                    resolved = candidate;
                    return BurstContextResult.Success;
                }
            }

            return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
        }

        private static BurstContextResult GateContext(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            NativeBurstBatchRoleV2 role,
            out NativeBurstDispatchControlV2 control,
            out NativeBurstDispatchRequestV2 request,
            out NativeBurstDispatchCaseV2 dispatchCase)
        {
            var roleGate = GateRole(runtime, role);
            if (roleGate != BurstContextResult.Success)
            {
                control = default;
                request = default;
                dispatchCase = default;
                return roleGate;
            }

            if (!TryActiveFrame(
                    runtime,
                    frameId,
                    validationToken,
                    out control,
                    out request,
                    out dispatchCase))
            {
                return Latch(runtime, frameId, BurstContextResult.InvalidHandle);
            }

            return control.FirstFailure == BurstContextResult.Success
                ? BurstContextResult.Success
                : control.FirstFailure;
        }

        private static BurstContextResult GateRole(
            BurstDispatchBackingV2 runtime,
            NativeBurstBatchRoleV2 role)
        {
            var claims = runtime.ExecutionClaim;
            if (!claims.IsCreated || claims.Length != 1)
            {
                return BurstContextResult.InvalidHandle;
            }

            var claim = Interlocked.CompareExchange(ref claims.ElementAt(0), 0, 0);
            if (role == NativeBurstBatchRoleV2.Job)
            {
                return claim == 2
                    ? BurstContextResult.Success
                    : BurstContextResult.InvalidHandle;
            }

            if (role == NativeBurstBatchRoleV2.Host)
            {
                return claim == 1
                    ? BurstContextResult.Success
                    : claim == 2
                        ? BurstContextResult.PhaseViolation
                        : BurstContextResult.InvalidHandle;
            }

            return BurstContextResult.InvalidHandle;
        }

        private static bool TryActiveFrame(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            ulong validationToken,
            out NativeBurstDispatchControlV2 control,
            out NativeBurstDispatchRequestV2 request,
            out NativeBurstDispatchCaseV2 dispatchCase)
        {
            request = default;
            dispatchCase = default;
            if (!TryActiveFrame(runtime, frameId, out control, out request, out dispatchCase))
            {
                return false;
            }

            return validationToken == FrameToken(control.OwnerId, control.Generation, frameId);
        }

        private static bool TryActiveFrame(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            out NativeBurstDispatchControlV2 control,
            out NativeBurstDispatchRequestV2 request,
            out NativeBurstDispatchCaseV2 dispatchCase)
        {
            control = default;
            request = default;
            dispatchCase = default;
            if (runtime.OwnerId == 0
                || runtime.Generation == 0
                || frameId == 0
                || !runtime.Control.IsCreated
                || runtime.Control.Length != 1)
            {
                return false;
            }

            control = runtime.Control[0];
            if (control.OwnerId != runtime.OwnerId
                || control.Generation != runtime.Generation
                || control.State != NativeBurstDispatchStateV2.Running
                || control.ActiveFrameId != frameId
                || control.Cursor >= runtime.Requests.Length)
            {
                return false;
            }

            request = runtime.Requests[(int)control.Cursor];
            if (request.CatalogCaseIndex >= runtime.Cases.Length)
            {
                request = default;
                return false;
            }

            dispatchCase = runtime.Cases[(int)request.CatalogCaseIndex];
            return true;
        }

        private static BurstContextResult Latch(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            BurstContextResult failure)
        {
            if (failure == BurstContextResult.Success
                || !TryActiveFrame(runtime, frameId, out var control, out _, out _))
            {
                return failure;
            }

            if (control.FirstFailure == BurstContextResult.Success)
            {
                control.FirstFailure = failure;
                runtime.Control[0] = control;
            }

            return failure;
        }

        private static ulong FrameToken(ulong ownerId, uint generation, uint frameId)
        {
            var token = ownerId ^ ((ulong)generation << 32) ^ frameId ^ 0x9e3779b97f4a7c15UL;
            return token == 0 ? 1UL : token;
        }

        private static uint BindingAccessToken(
            ulong ownerId,
            uint generation,
            uint frameId,
            uint globalBindingOrdinal)
        {
            var value = FrameToken(ownerId, generation, frameId)
                ^ ((ulong)globalBindingOrdinal + 0x9e3779b9UL) * 0xbf58476d1ce4e5b9UL;
            value ^= value >> 30;
            value *= 0x94d049bb133111ebUL;
            value ^= value >> 31;
            var token = (uint)(value ^ (value >> 32));
            return token == 0 ? 1u : token;
        }

        private static bool OwnsOperation(
            in NativeBurstDispatchRequestV2 request,
            OperationId operationId)
            => operationId.IsValid
                && operationId.TreeInstanceId == request.TreeInstanceId
                && operationId.NodeIndex == new RuntimeNodeIndex(request.RuntimeNodeIndex)
                && operationId.ActivationGeneration == request.ActivationGeneration;

        private static bool SameRuntime(BurstDispatchBackingV2 left, BurstDispatchBackingV2 right)
            => left.OwnerId != 0
                && left.OwnerId == right.OwnerId
                && left.Generation == right.Generation
                && left.Control.Equals(right.Control);

        private static bool TryResolvedBinding(
            BurstDispatchBackingV2 runtime,
            in NativeBurstDispatchRequestV2 request,
            in NativeBurstDispatchBindingV2 binding,
            out NativeBurstDispatchResolvedBindingV2 resolved)
        {
            resolved = default;
            if (binding.BindingOrdinal >= request.ResolvedBindingCount)
            {
                return false;
            }

            var index = request.FirstResolvedBinding + binding.BindingOrdinal;
            if (index >= runtime.ResolvedBindings.Length)
            {
                return false;
            }

            resolved = runtime.ResolvedBindings[(int)index];
            return resolved.BindingOrdinal == binding.BindingOrdinal;
        }

        private static BurstContextResult TryAllocateBlackboardReader(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            uint bindingOrdinal,
            in NativeBurstDispatchBindingV2 binding,
            in NativeBurstDispatchResolvedBindingV2 resolved,
            out uint sessionOrdinal)
        {
            sessionOrdinal = default;
            if (!TryActiveFrame(runtime, frameId, out _, out var request, out _)
                || !TryTransaction(runtime, frameId, out var transaction))
            {
                return BurstContextResult.InvalidHandle;
            }

            var source = runtime.BindingValueBytes;
            var sourceOffset = resolved.LiveValueOffset;
            for (uint index = 0; index < transaction.SessionCount; index++)
            {
                var candidate = runtime.ValueSessions[(int)index];
                if (candidate.FrameId != frameId
                    || candidate.Kind != NativeBurstDispatchValueSessionKindV2.BlackboardWrite
                    || candidate.State != NativeBurstDispatchValueSessionStateV2.Sealed
                    || candidate.BindingOrdinal >= runtime.Bindings.Length)
                {
                    continue;
                }

                var candidateBinding = runtime.Bindings[(int)candidate.BindingOrdinal];
                if (!TryResolvedBinding(runtime, in request, in candidateBinding, out var candidateResolved)
                    || candidateBinding.Scope != binding.Scope
                    || candidateResolved.TargetOrdinal != resolved.TargetOrdinal
                    || candidateBinding.PrimaryTypeNumericId != binding.PrimaryTypeNumericId
                    || candidateBinding.PrimaryTypeVersion != binding.PrimaryTypeVersion)
                {
                    continue;
                }

                source = runtime.ValueStagingBytes;
                sourceOffset = candidate.StagingOffset;
            }

            return TryAllocateSession(
                runtime,
                frameId,
                bindingOrdinal,
                NativeBurstDispatchValueSessionKindV2.BlackboardRead,
                binding.PrimaryValueSize,
                true,
                source,
                sourceOffset,
                NoOrdinal,
                default,
                out sessionOrdinal);
        }

        private static BurstContextResult TryAllocateSession(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            uint bindingOrdinal,
            NativeBurstDispatchValueSessionKindV2 kind,
            uint valueSize,
            bool copySource,
            NativeArray<byte> source,
            uint sourceOffset,
            uint completionOrdinal,
            OperationId operationId,
            out uint sessionOrdinal)
        {
            sessionOrdinal = default;
            if (!TryTransaction(runtime, frameId, out var transaction))
            {
                return BurstContextResult.InvalidHandle;
            }

            if ((ulong)transaction.SessionCount + 1UL > (ulong)runtime.ValueSessions.Length
                || (ulong)transaction.StagingByteCount + valueSize > (ulong)runtime.ValueStagingBytes.Length
                || copySource && (!source.IsCreated
                    || (ulong)sourceOffset + valueSize > (ulong)source.Length))
            {
                return BurstContextResult.CapacityExceeded;
            }

            sessionOrdinal = transaction.SessionCount;
            InitializeSession(
                runtime,
                ref transaction,
                frameId,
                bindingOrdinal,
                kind,
                valueSize,
                copySource,
                source,
                sourceOffset,
                completionOrdinal,
                operationId,
                NoOrdinal);
            WriteTransaction(runtime, transaction);
            return BurstContextResult.Success;
        }

        private static void InitializeSession(
            BurstDispatchBackingV2 runtime,
            ref NativeBurstDispatchTransactionControlV2 transaction,
            uint frameId,
            uint bindingOrdinal,
            NativeBurstDispatchValueSessionKindV2 kind,
            uint valueSize,
            bool copySource,
            NativeArray<byte> source,
            uint sourceOffset,
            uint completionOrdinal,
            OperationId operationId,
            uint companionOrdinal)
        {
            var sessionOrdinal = transaction.SessionCount;
            var stagingOffset = transaction.StagingByteCount;
            ClearRange(runtime.ValueStagingBytes, stagingOffset, valueSize);
            ClearRange(runtime.ValueMarks, stagingOffset, valueSize);
            if (copySource)
            {
                CopyRange(source, sourceOffset, runtime.ValueStagingBytes, stagingOffset, valueSize);
            }

            runtime.ValueSessions[(int)sessionOrdinal] = new NativeBurstDispatchValueSessionV2
            {
                FrameId = frameId,
                BindingOrdinal = bindingOrdinal,
                StagingOffset = stagingOffset,
                ValueSize = valueSize,
                CompanionSessionOrdinal = companionOrdinal,
                CompletionOrdinal = completionOrdinal,
                OperationId = operationId,
                Kind = kind,
                State = NativeBurstDispatchValueSessionStateV2.Active
            };
            transaction.SessionCount++;
            transaction.StagingByteCount += valueSize;
        }

        private static bool TryTransaction(
            BurstDispatchBackingV2 runtime,
            uint frameId,
            out NativeBurstDispatchTransactionControlV2 transaction)
        {
            transaction = default;
            if (!runtime.TransactionControl.IsCreated
                || runtime.TransactionControl.Length != 1
                || !runtime.ValueSessions.IsCreated
                || !runtime.ValueStagingBytes.IsCreated
                || !runtime.ValueMarks.IsCreated
                || !runtime.Commands.IsCreated
                || !runtime.CommandPayloadBytes.IsCreated
                || !runtime.Operations.IsCreated)
            {
                return false;
            }

            transaction = runtime.TransactionControl[0];
            return transaction.ActiveFrameId == frameId
                && frameId != 0
                && transaction.SessionCount <= runtime.ValueSessions.Length
                && transaction.StagingByteCount <= runtime.ValueStagingBytes.Length
                && transaction.CommandCount <= runtime.Commands.Length
                && transaction.CommandPayloadByteCount <= runtime.CommandPayloadBytes.Length
                && transaction.OperationCount <= runtime.Operations.Length;
        }

        private static BurstContextResult TryResolveReader(
            ref BurstValueReader reader,
            NativeBurstDispatchValueSessionStateV2 expectedState,
            out uint sessionOrdinal,
            out NativeBurstDispatchValueSessionV2 session,
            out NativeBurstDispatchBindingV2 binding)
        {
            sessionOrdinal = default;
            session = default;
            binding = default;
            var gate = GateContext(
                reader.Runtime,
                reader.FrameId,
                reader.ValidationToken,
                reader.Role,
                out _,
                out _,
                out _);
            if (gate != BurstContextResult.Success)
            {
                return gate;
            }

            if (!reader.TryGetSessionOrdinal(out sessionOrdinal)
                || !TryTransaction(reader.Runtime, reader.FrameId, out var transaction)
                || sessionOrdinal >= transaction.SessionCount)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle);
            }

            session = reader.Runtime.ValueSessions[(int)sessionOrdinal];
            if (session.FrameId != reader.FrameId
                || session.BindingOrdinal != reader.BindingOrdinal
                || session.BindingOrdinal >= reader.Runtime.Bindings.Length
                || !IsReader(session.Kind))
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.InvalidHandle);
            }

            if (session.State != expectedState)
            {
                return Latch(reader.Runtime, reader.FrameId, BurstContextResult.AlreadyCommitted);
            }

            binding = reader.Runtime.Bindings[(int)session.BindingOrdinal];
            return BurstContextResult.Success;
        }

        private static BurstContextResult TryResolveWriter(
            ref BurstValueWriter writer,
            NativeBurstDispatchValueSessionStateV2 expectedState,
            out uint sessionOrdinal,
            out NativeBurstDispatchValueSessionV2 session,
            out NativeBurstDispatchBindingV2 binding)
        {
            sessionOrdinal = default;
            session = default;
            binding = default;
            var gate = GateContext(
                writer.Runtime,
                writer.FrameId,
                writer.ValidationToken,
                writer.Role,
                out _,
                out _,
                out _);
            if (gate != BurstContextResult.Success)
            {
                return gate;
            }

            if (!writer.TryGetSessionOrdinal(out sessionOrdinal)
                || !TryTransaction(writer.Runtime, writer.FrameId, out var transaction)
                || sessionOrdinal >= transaction.SessionCount)
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.InvalidHandle);
            }

            session = writer.Runtime.ValueSessions[(int)sessionOrdinal];
            if (session.FrameId != writer.FrameId
                || session.BindingOrdinal != writer.BindingOrdinal
                || session.BindingOrdinal >= writer.Runtime.Bindings.Length
                || IsReader(session.Kind))
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.InvalidHandle);
            }

            if (session.State != expectedState)
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.AlreadyCommitted);
            }

            binding = writer.Runtime.Bindings[(int)session.BindingOrdinal];
            return BurstContextResult.Success;
        }

        private static bool IsReader(NativeBurstDispatchValueSessionKindV2 kind)
            => kind == NativeBurstDispatchValueSessionKindV2.BlackboardRead
                || kind == NativeBurstDispatchValueSessionKindV2.SnapshotRead
                || kind == NativeBurstDispatchValueSessionKindV2.CompletionRead;

        private static void GetSessionLayout(
            in NativeBurstDispatchValueSessionV2 session,
            in NativeBurstDispatchBindingV2 binding,
            out uint firstField,
            out uint fieldCount,
            out uint valueSize)
        {
            var secondary = session.Kind == NativeBurstDispatchValueSessionKindV2.FaultCancelWrite
                || session.Kind == NativeBurstDispatchValueSessionKindV2.CancelWrite;
            firstField = secondary ? binding.FirstSecondaryValueField : binding.FirstPrimaryValueField;
            fieldCount = secondary ? binding.SecondaryValueFieldCount : binding.PrimaryValueFieldCount;
            valueSize = secondary ? binding.SecondaryValueSize : binding.PrimaryValueSize;
        }

        private static BurstContextResult TrySealWriter(
            ref BurstValueWriter writer,
            NativeBurstDispatchValueSessionKindV2 expectedKind,
            bool reservesCommand,
            bool reservesOperation)
        {
            var result = TryResolveWriter(
                ref writer,
                NativeBurstDispatchValueSessionStateV2.Active,
                out var sessionOrdinal,
                out var session,
                out var binding);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            if (session.Kind != expectedKind)
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.InvalidHandle);
            }

            var complete = ValidateSessionComplete(writer.Runtime, in session, in binding);
            if (complete != BurstContextResult.Success)
            {
                return Latch(writer.Runtime, writer.FrameId, complete);
            }

            if (reservesCommand
                && (!TryTransaction(writer.Runtime, writer.FrameId, out var transaction)
                    || !CanSealReversible(
                        writer.Runtime,
                        in transaction,
                        1,
                        binding.PrimaryValueSize,
                        reservesOperation ? 1UL : 0UL)))
            {
                return Latch(writer.Runtime, writer.FrameId, BurstContextResult.CapacityExceeded);
            }

            session.State = NativeBurstDispatchValueSessionStateV2.Sealed;
            WriteSession(writer.Runtime, sessionOrdinal, session);
            return BurstContextResult.Success;
        }

        private static BurstContextResult ValidateSessionComplete(
            BurstDispatchBackingV2 runtime,
            in NativeBurstDispatchValueSessionV2 session,
            in NativeBurstDispatchBindingV2 binding)
        {
            GetSessionLayout(
                in session,
                in binding,
                out var firstField,
                out var fieldCount,
                out var valueSize);
            if (!AllFieldsMarked(runtime, session.StagingOffset, firstField, fieldCount))
            {
                return BurstContextResult.IncompleteValue;
            }

            var secondary = session.Kind == NativeBurstDispatchValueSessionKindV2.FaultCancelWrite
                || session.Kind == NativeBurstDispatchValueSessionKindV2.CancelWrite;
            var rangeIndex = (ulong)session.BindingOrdinal * 2UL + (secondary ? 1UL : 0UL);
            if (rangeIndex >= (ulong)runtime.BindingCanonicalRanges.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            var range = runtime.BindingCanonicalRanges[(int)rangeIndex];
            return NativeBurstDispatchCanonicalV2.ValidateBytes(
                    runtime.CanonicalRules.AsReadOnly(),
                    in range,
                    runtime.ValueStagingBytes.AsReadOnly(),
                    session.StagingOffset,
                    valueSize)
                ? BurstContextResult.Success
                : BurstContextResult.InvalidEncoding;
        }

        private static bool AllFieldsMarked(
            BurstDispatchBackingV2 runtime,
            uint stagingOffset,
            uint firstField,
            uint fieldCount)
        {
            if ((ulong)firstField + fieldCount > (ulong)runtime.ValueFields.Length)
            {
                return false;
            }

            for (uint fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var field = runtime.ValueFields[(int)(firstField + fieldIndex)];
                var size = field.ElementCount * field.ElementSize;
                for (uint byteIndex = 0; byteIndex < size; byteIndex++)
                {
                    var index = (ulong)stagingOffset + field.ByteOffset + byteIndex;
                    if (index >= (ulong)runtime.ValueMarks.Length
                        || runtime.ValueMarks[(int)index] == 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool CanSealReversible(
            BurstDispatchBackingV2 runtime,
            in NativeBurstDispatchTransactionControlV2 transaction,
            ulong addedCommands,
            ulong addedPayloadBytes,
            ulong addedOperations)
        {
            ulong commands = addedCommands;
            ulong payloadBytes = addedPayloadBytes;
            ulong operations = addedOperations;
            for (uint index = 0; index < transaction.SessionCount; index++)
            {
                var session = runtime.ValueSessions[(int)index];
                if (session.State != NativeBurstDispatchValueSessionStateV2.Sealed
                    || session.BindingOrdinal >= runtime.Bindings.Length)
                {
                    continue;
                }

                var binding = runtime.Bindings[(int)session.BindingOrdinal];
                if (session.Kind == NativeBurstDispatchValueSessionKindV2.EffectWrite)
                {
                    commands++;
                    payloadBytes += binding.PrimaryValueSize;
                }
                else if (session.Kind == NativeBurstDispatchValueSessionKindV2.StartWrite)
                {
                    commands++;
                    operations++;
                    payloadBytes += (ulong)binding.PrimaryValueSize + binding.SecondaryValueSize;
                }
            }

            return HasPublishedCapacity(runtime, in transaction, commands, payloadBytes, operations);
        }

        private static bool HasPublishedCapacity(
            BurstDispatchBackingV2 runtime,
            in NativeBurstDispatchTransactionControlV2 transaction,
            ulong stagedCommands,
            ulong stagedPayloadBytes,
            ulong stagedOperations)
            => (ulong)transaction.CommandCount + stagedCommands <= (ulong)runtime.Commands.Length
                && (ulong)transaction.CommandPayloadByteCount + stagedPayloadBytes
                    <= (ulong)runtime.CommandPayloadBytes.Length
                && (ulong)transaction.OperationCount + stagedOperations <= (ulong)runtime.Operations.Length;

        private static bool TryFindActiveOperation(
            BurstDispatchBackingV2 runtime,
            OperationId operationId,
            uint targetOrdinal,
            in NativeBurstDispatchBindingV2 binding,
            out uint operationOrdinal)
        {
            operationOrdinal = default;
            if (!runtime.TransactionControl.IsCreated || runtime.TransactionControl.Length != 1)
            {
                return false;
            }

            var transaction = runtime.TransactionControl[0];
            if (transaction.OperationCount > runtime.Operations.Length)
            {
                return false;
            }

            for (uint index = 0; index < transaction.OperationCount; index++)
            {
                var operation = runtime.Operations[(int)index];
                if (operation.OperationId == operationId
                    && operation.TargetOrdinal == targetOrdinal
                    && operation.StartTypeNumericId == binding.PrimaryTypeNumericId
                    && operation.StartTypeVersion == binding.PrimaryTypeVersion
                    && operation.CancelTypeNumericId == binding.SecondaryTypeNumericId
                    && operation.CancelTypeVersion == binding.SecondaryTypeVersion
                    && operation.State == NativeBurstDispatchOperationStateV2.Active)
                {
                    operationOrdinal = index;
                    return true;
                }
            }

            return false;
        }

        private static void AppendCommand(
            BurstDispatchBackingV2 runtime,
            ref NativeBurstDispatchTransactionControlV2 transaction,
            NativeBurstDispatchCommandKindV2 kind,
            uint targetOrdinal,
            OperationId operationId,
            uint stagingOffset,
            uint payloadSize)
        {
            var payloadOffset = transaction.CommandPayloadByteCount;
            CopyRange(
                runtime.ValueStagingBytes,
                stagingOffset,
                runtime.CommandPayloadBytes,
                payloadOffset,
                payloadSize);
            runtime.Commands[(int)transaction.CommandCount] = new NativeBurstDispatchCommandV2(
                kind,
                targetOrdinal,
                operationId,
                payloadOffset,
                payloadSize);
            transaction.CommandCount++;
            transaction.CommandPayloadByteCount += payloadSize;
        }

        private static bool TryFindValueField(
            NativeArray<NativeBurstDispatchFieldV2> fields,
            uint first,
            uint count,
            uint fieldOrdinal,
            uint elementIndex,
            NativeBurstDispatchFieldEncodingV2 encoding,
            out uint relativeOffset,
            out uint elementSize)
        {
            relativeOffset = default;
            elementSize = default;
            if (!fields.IsCreated || (ulong)first + count > (ulong)fields.Length)
            {
                return false;
            }

            for (uint index = 0; index < count; index++)
            {
                var field = fields[(int)(first + index)];
                if (field.FieldOrdinal != fieldOrdinal
                    || elementIndex < field.FirstElementIndex
                    || elementIndex - field.FirstElementIndex >= field.ElementCount)
                {
                    continue;
                }

                if (field.Encoding != encoding || field.ElementSize != EncodingSize(encoding))
                {
                    return false;
                }

                var relativeElement = elementIndex - field.FirstElementIndex;
                var offset = (ulong)field.ByteOffset + (ulong)relativeElement * field.ElementSize;
                if (offset > uint.MaxValue)
                {
                    return false;
                }

                relativeOffset = (uint)offset;
                elementSize = field.ElementSize;
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
                var raw = ReadUInt16(source, offset);
                value = UnsafeUtility.As<ushort, T>(ref raw);
            }
            else if (expectedSize == 4)
            {
                var raw = ReadUInt32(source, offset);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Float32
                    && (raw == 0x80000000u || (raw & 0x7f800000u) == 0x7f800000u))
                {
                    return BurstContextResult.InvalidEncoding;
                }

                value = UnsafeUtility.As<uint, T>(ref raw);
            }
            else
            {
                var raw = ReadUInt64(source, offset);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Float64
                    && (raw == 0x8000000000000000UL
                        || (raw & 0x7ff0000000000000UL) == 0x7ff0000000000000UL))
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
                var raw = UnsafeUtility.As<T, byte>(ref value);
                if (encoding == NativeBurstDispatchFieldEncodingV2.Boolean && raw > 1)
                {
                    return BurstContextResult.InvalidEncoding;
                }

                destination[(int)offset] = raw;
            }
            else if (expectedSize == 2)
            {
                var raw = UnsafeUtility.As<T, ushort>(ref value);
                WriteUInt16(destination, offset, raw);
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

                WriteUInt32(destination, offset, raw);
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

                WriteUInt64(destination, offset, raw);
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

        private static ushort ReadUInt16(NativeArray<byte> source, uint offset)
            => (ushort)(source[(int)offset] | source[(int)(offset + 1)] << 8);

        private static uint ReadUInt32(NativeArray<byte> source, uint offset)
            => source[(int)offset]
                | (uint)source[(int)(offset + 1)] << 8
                | (uint)source[(int)(offset + 2)] << 16
                | (uint)source[(int)(offset + 3)] << 24;

        private static ulong ReadUInt64(NativeArray<byte> source, uint offset)
            => ReadUInt32(source, offset) | (ulong)ReadUInt32(source, offset + 4) << 32;

        private static void WriteUInt16(NativeArray<byte> destination, uint offset, ushort value)
        {
            destination[(int)offset] = (byte)value;
            destination[(int)(offset + 1)] = (byte)(value >> 8);
        }

        private static void WriteUInt32(NativeArray<byte> destination, uint offset, uint value)
        {
            destination[(int)offset] = (byte)value;
            destination[(int)(offset + 1)] = (byte)(value >> 8);
            destination[(int)(offset + 2)] = (byte)(value >> 16);
            destination[(int)(offset + 3)] = (byte)(value >> 24);
        }

        private static void WriteUInt64(NativeArray<byte> destination, uint offset, ulong value)
        {
            WriteUInt32(destination, offset, (uint)value);
            WriteUInt32(destination, offset + 4, (uint)(value >> 32));
        }

        private static bool AnyMarked(NativeArray<byte> marks, uint offset, uint count)
        {
            if ((ulong)offset + count > (ulong)marks.Length)
            {
                return true;
            }

            for (uint index = 0; index < count; index++)
            {
                if (marks[(int)(offset + index)] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void MarkRange(NativeArray<byte> values, uint offset, uint count)
        {
            for (uint index = 0; index < count; index++)
            {
                values[(int)(offset + index)] = 1;
            }
        }

        private static void ClearRange(NativeArray<byte> values, uint offset, uint count)
        {
            for (uint index = 0; index < count; index++)
            {
                values[(int)(offset + index)] = 0;
            }
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

        private static void WriteSession(
            BurstDispatchBackingV2 runtime,
            uint sessionOrdinal,
            NativeBurstDispatchValueSessionV2 session)
        {
            var sessions = runtime.ValueSessions;
            sessions[(int)sessionOrdinal] = session;
        }

        private static void WriteTransaction(
            BurstDispatchBackingV2 runtime,
            NativeBurstDispatchTransactionControlV2 transaction)
        {
            NativeBurstDispatchTransactionLedgerV2.Advance(ref transaction);
            var controls = runtime.TransactionControl;
            controls[0] = transaction;
        }
    }
}
