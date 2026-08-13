using System;
using System.Collections.Generic;

namespace AIBT.Tests.Runtime
{
    internal sealed class RecordingReferenceTraceSink : IReferenceTraceSink
    {
        internal List<ReferenceTraceRecord> Records { get; } = new List<ReferenceTraceRecord>();

        public void Record(in ReferenceTraceRecord record) => Records.Add(record);
    }

    internal sealed class ScriptedReferenceLeaf : IReferenceLeafHandler
    {
        private readonly Queue<NodeStatus> _statuses;

        internal ScriptedReferenceLeaf(params NodeStatus[] statuses)
        {
            _statuses = new Queue<NodeStatus>(statuses);
        }

        internal List<string> Calls { get; } = new List<string>();
        internal List<uint> EnterGenerations { get; } = new List<uint>();
        internal List<byte> MemoryAtEnter { get; } = new List<byte>();
        internal List<long> TimesAtTick { get; } = new List<long>();
        internal ReferenceExecutionMachine ReentrantMachine { get; set; }
        internal ReferenceExecutionEnvelope? ReentrantResult { get; private set; }
        internal bool ReenterOnTick { get; set; }
        internal bool InspectOnTick { get; set; }
        internal Exception InspectionException { get; private set; }
        internal string ThrowOn { get; set; }
        internal byte TickMemoryValue { get; set; }
        internal byte ExitMemoryValue { get; set; }

        public void Enter(ref ReferenceNodeContext context)
        {
            Calls.Add("Enter");
            EnterGenerations.Add(context.ActivationGeneration);
            MemoryAtEnter.Add(context.Memory.Length == 0 ? (byte)0 : context.Memory[0]);
            ThrowIfRequested("Enter");
        }

        public NodeStatus Tick(ref ReferenceNodeContext context)
        {
            Calls.Add("Tick");
            TimesAtTick.Add(context.Update.TimeMicroseconds);
            if (context.Memory.Length != 0) context.Memory[0] = TickMemoryValue;
            if (ReenterOnTick)
            {
                ReentrantResult = ReentrantMachine.Update(new ReferenceUpdateContext(999, new Revision(999), 999));
            }
            if (InspectOnTick)
            {
                try
                {
                    ReentrantMachine.CaptureInspection();
                }
                catch (Exception exception)
                {
                    InspectionException = exception;
                }
            }

            ThrowIfRequested("Tick");
            return _statuses.Count == 0 ? NodeStatus.Running : _statuses.Dequeue();
        }

        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason)
        {
            Calls.Add("Abort:" + reason);
            ThrowIfRequested("Abort");
        }

        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason)
        {
            Calls.Add("Exit:" + reason);
            if (context.Memory.Length != 0 && ExitMemoryValue != 0) context.Memory[0] = ExitMemoryValue;
            ThrowIfRequested("Exit");
        }

        private void ThrowIfRequested(string callback)
        {
            if (string.Equals(ThrowOn, callback, StringComparison.Ordinal))
            {
                throw new TestHandlerException();
            }
        }

        private sealed class TestHandlerException : Exception
        {
        }
    }

    internal static class ReferenceExecutionTestProgram
    {
        private const ulong TypeId = 17;
        private const uint TypeVersion = 1;
        private static readonly CompiledHash Hash = new CompiledHash(new string('a', CompiledHash.HexLength));

        internal static ReferenceExecutionMachine Create(
            ScriptedReferenceLeaf handler,
            RecordingReferenceTraceSink trace = null,
            NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation,
            uint memorySize = 1)
        {
            var program = CreateProgram(memoryLifetime, memorySize);
            var registry = new ReferenceLeafRegistry(new[]
            {
                new ReferenceLeafBinding(TypeId, TypeVersion, handler),
            });
            return new ReferenceExecutionMachine(program, new TreeInstanceId(41), registry, trace);
        }

        internal static ReferenceExecutionMachine CreateWithoutHandler(RecordingReferenceTraceSink trace = null)
        {
            return new ReferenceExecutionMachine(
                CreateProgram(NodeMemoryLifetime.Activation, 1),
                new TreeInstanceId(41),
                new ReferenceLeafRegistry(Array.Empty<ReferenceLeafBinding>()),
                trace);
        }

        internal static ReferenceUpdateContext Update(ulong id)
            => new ReferenceUpdateContext(id, new Revision(id + 100), checked((long)id * 10));

        private static CompiledProgram CreateProgram(NodeMemoryLifetime memoryLifetime, uint memorySize)
        {
            var alignment = memorySize == 0 ? 1u : 1u;
            var node = new CompiledNodeRecord(
                TypeId,
                TypeVersion,
                0,
                0,
                1,
                0,
                memorySize,
                alignment,
                memoryLifetime,
                new CompiledRange(0, 0),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0),
                new CompiledRange(0, 0));
            var header = new CompiledProgramHeader(
                1,
                1,
                new CompiledCompilerVersion(1, 0, 0, 0),
                Hash,
                Hash,
                Hash,
                1,
                Hash,
                0,
                1,
                0,
                0,
                0,
                0,
                memorySize,
                1,
                0,
                true);
            return new CompiledProgram(
                header,
                new[] { node },
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(),
                Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(),
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                Array.Empty<CompiledDebugMapEntry>());
        }
    }
}
