using System;

namespace AIBT
{
    internal sealed class ReferenceAsyncActionHandler : IReferenceLeafHandler
    {
        internal const string TypeId = "aibt.test.async-action";
        private const int RequiredMemorySize = 16;
        private readonly ReferenceAsyncCommandContract _contract;

        internal ReferenceAsyncActionHandler(ReferenceAsyncCommandContract contract)
        {
            _contract = contract;
        }

        public void Enter(ref ReferenceNodeContext context)
        {
            RequireMemory(ref context);
        }

        public NodeStatus Tick(ref ReferenceNodeContext context)
        {
            RequireMemory(ref context);
            var memory = context.Memory;
            if (memory[0] == 0)
            {
                if (!context.TryStartOperation(_contract, context.Configuration, out var operationId))
                {
                    return NodeStatus.Failure;
                }

                memory[0] = 1;
                WriteUInt64(memory, 8, operationId.Sequence);
                return NodeStatus.Running;
            }

            var operation = CurrentOperation(ref context);
            if (!context.TryConsumeCompletion(
                operation,
                ReferenceCompletionExpectation.Any,
                out var completion))
            {
                return NodeStatus.Running;
            }

            return completion.Record.Outcome == CompletionOutcome.Succeeded
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason)
        {
            RequireMemory(ref context);
            if (context.Memory[0] == 0) return;
            context.TryCancelOperation(CurrentOperation(ref context), _contract, ReadOnlySpan<byte>.Empty);
        }

        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason)
        {
        }

        private static OperationId CurrentOperation(ref ReferenceNodeContext context)
        {
            return new OperationId(
                context.TreeInstanceId,
                context.NodeIndex,
                context.ActivationGeneration,
                ReadUInt64(context.Memory, 8));
        }

        private static void RequireMemory(ref ReferenceNodeContext context)
        {
            if (context.Memory.Length < RequiredMemorySize)
            {
                throw new InvalidOperationException("The reference async action requires 16 bytes of activation memory.");
            }
        }

        private static void WriteUInt64(Span<byte> target, int offset, ulong value)
        {
            for (var index = 0; index < 8; index++) target[offset + index] = (byte)(value >> (index * 8));
        }

        private static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++) value |= (ulong)source[offset + index] << (index * 8);
            return value;
        }
    }
}
