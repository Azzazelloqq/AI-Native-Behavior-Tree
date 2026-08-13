using System;
using System.Collections.Generic;

namespace AIBT
{
    internal readonly struct ReferenceAsyncCommandContract
    {
        private readonly byte[] _faultCancellationPayload;

        internal ReferenceAsyncCommandContract(
            CommandType startCommand,
            CommandType cancelCommand,
            byte[] faultCancellationPayload = null)
        {
            if (!startCommand.IsValid) throw new ArgumentException("A start command type is required.", nameof(startCommand));
            if (!cancelCommand.IsValid) throw new ArgumentException("A cancellation command type is required.", nameof(cancelCommand));
            StartCommand = startCommand;
            CancelCommand = cancelCommand;
            _faultCancellationPayload = faultCancellationPayload == null
                ? Array.Empty<byte>()
                : (byte[])faultCancellationPayload.Clone();
        }

        internal CommandType StartCommand { get; }
        internal CommandType CancelCommand { get; }
        internal byte[] CopyFaultCancellationPayload() => (byte[])_faultCancellationPayload.Clone();
    }

    internal sealed class ReferenceAsyncCommandCoordinator
    {
        private readonly ReferenceOperationLedger _ledger;
        private readonly ReferenceCommandBuffer _commands;
        private readonly Dictionary<OperationId, StartMetadata> _started = new Dictionary<OperationId, StartMetadata>();

        internal ReferenceAsyncCommandCoordinator(
            ReferenceOperationLedger ledger,
            ReferenceCommandBuffer commands)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            if (_ledger.TreeInstanceId != commands.TreeInstanceId)
            {
                throw new ArgumentException("The operation ledger and command buffer must belong to the same tree instance.");
            }
        }

        internal bool TryStart(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            ReferenceAsyncCommandContract contract,
            ReadOnlySpan<byte> payload,
            out OperationId operationId,
            out Diagnostic diagnostic)
        {
            operationId = default;
            if (!_ledger.TryAllocate(nodeIndex, activationGeneration, out var allocated, out diagnostic)) return false;
            if (!_commands.TryAppend(
                contract.StartCommand,
                allocated,
                CommandPhase.Execute,
                payload,
                out _,
                out diagnostic))
            {
                _ledger.MarkCancelled(allocated);
                return false;
            }

            operationId = allocated;
            _started.Add(allocated, new StartMetadata(contract, contract.CopyFaultCancellationPayload()));
            return true;
        }

        internal bool TryCancel(
            OperationId operationId,
            ReferenceAsyncCommandContract contract,
            ReadOnlySpan<byte> payload,
            out bool commandEmitted,
            out Diagnostic diagnostic)
        {
            commandEmitted = false;
            diagnostic = null;
            var transition = _ledger.MarkCancelled(operationId);
            if (transition == ReferenceOperationTransition.AlreadyApplied) return true;
            if (transition != ReferenceOperationTransition.Applied)
            {
                diagnostic = CommandAsyncDiagnostics.Create(
                    CommandAsyncDiagnosticCodes.InvalidCommand,
                    transition == ReferenceOperationTransition.Unknown
                        ? "A cancellation references an operation that was never issued by this tree instance."
                        : "A consumed operation cannot be cancelled.",
                    _ledger.TreeInstanceId);
                return false;
            }

            if (!_commands.TryAppend(
                contract.CancelCommand,
                operationId,
                CommandPhase.Cancel,
                payload,
                out _,
                out diagnostic))
            {
                return false;
            }

            commandEmitted = true;
            return true;
        }

        internal void FaultCancelAll(Action<OperationId, bool, Diagnostic> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            var active = _ledger.CopyActiveOperations();
            for (var index = 0; index < active.Length; index++)
            {
                var operationId = active[index];
                if (!_started.TryGetValue(operationId, out var metadata))
                {
                    _ledger.MarkCancelled(operationId);
                    observer(operationId, false, null);
                    continue;
                }

                TryCancel(
                    operationId,
                    metadata.Contract,
                    metadata.CancellationPayload,
                    out var emitted,
                    out var diagnostic);
                observer(operationId, emitted, diagnostic);
            }
        }

        private readonly struct StartMetadata
        {
            internal StartMetadata(ReferenceAsyncCommandContract contract, byte[] cancellationPayload)
            {
                Contract = contract;
                CancellationPayload = cancellationPayload;
            }

            internal ReferenceAsyncCommandContract Contract { get; }
            internal byte[] CancellationPayload { get; }
        }
    }
}
