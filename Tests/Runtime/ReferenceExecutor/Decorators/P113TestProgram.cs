using System;
using System.Collections.Generic;

namespace AIBT.Tests.Runtime
{
    internal sealed class P113TestNode
    {
        internal P113TestNode(
            string typeId,
            byte[] configuration,
            uint configurationAlignment,
            uint memorySize,
            uint memoryAlignment,
            NodeMemoryLifetime memoryLifetime,
            IReferenceLeafHandler leaf = null,
            params P113TestNode[] children)
        {
            TypeId = StableHash.Fnv1A64(typeId);
            Configuration = configuration ?? Array.Empty<byte>();
            ConfigurationAlignment = configurationAlignment;
            MemorySize = memorySize;
            MemoryAlignment = memoryAlignment;
            MemoryLifetime = memoryLifetime;
            Leaf = leaf;
            Children = children ?? Array.Empty<P113TestNode>();
        }

        internal ulong TypeId { get; }
        internal byte[] Configuration { get; }
        internal uint ConfigurationAlignment { get; }
        internal uint MemorySize { get; }
        internal uint MemoryAlignment { get; }
        internal NodeMemoryLifetime MemoryLifetime { get; }
        internal IReferenceLeafHandler Leaf { get; }
        internal IReadOnlyList<P113TestNode> Children { get; }
    }

    internal sealed class P113Fixture
    {
        internal P113Fixture(ReferenceExecutionMachine machine, RecordingReferenceTraceSink trace)
        {
            Machine = machine;
            Trace = trace;
        }

        internal ReferenceExecutionMachine Machine { get; }
        internal RecordingReferenceTraceSink Trace { get; }
    }

    internal static class P113TestProgram
    {
        private static readonly CompiledHash Hash = new CompiledHash(new string('d', CompiledHash.HexLength));

        internal static P113TestNode Leaf(string name, ScriptedReferenceLeaf handler)
            => new P113TestNode("aibt.test.p113." + name, null, 1, 0, 1, NodeMemoryLifetime.Activation, handler);

        internal static P113TestNode Decorator(
            string typeId,
            byte[] configuration,
            uint configurationAlignment,
            uint memorySize,
            uint memoryAlignment,
            NodeMemoryLifetime lifetime,
            P113TestNode child)
            => new P113TestNode(typeId, configuration, configurationAlignment, memorySize, memoryAlignment, lifetime, null, child);

        internal static P113TestNode Parallel(byte[] configuration, params P113TestNode[] children)
            => new P113TestNode("aibt.core.parallel", configuration, 4, 8, 4, NodeMemoryLifetime.Activation, null, children);

        internal static byte[] RepeaterConfiguration(uint count, bool stopOnFailure)
        {
            var bytes = new byte[8];
            WriteUInt32(bytes, 0, count);
            bytes[4] = stopOnFailure ? (byte)1 : (byte)0;
            return bytes;
        }

        internal static byte[] TimedConfiguration(long duration, NodeStatus result, byte policy = 0)
        {
            var bytes = new byte[16];
            WriteUInt64(bytes, 0, checked((ulong)duration));
            bytes[8] = result == NodeStatus.Success ? (byte)1 : (byte)0;
            bytes[9] = policy;
            return bytes;
        }

        internal static byte[] ParallelConfiguration(
            ReferenceParallelPolicy policy,
            uint successThreshold = 0,
            uint failureThreshold = 0,
            ReferenceParallelTieBreak tieBreak = ReferenceParallelTieBreak.FailureFirst)
        {
            var bytes = new byte[16];
            bytes[0] = (byte)policy;
            WriteUInt32(bytes, 4, successThreshold);
            WriteUInt32(bytes, 8, failureThreshold);
            bytes[12] = (byte)tieBreak;
            return bytes;
        }

        internal static P113Fixture Create(P113TestNode root)
        {
            var flattened = new List<P113TestNode>();
            Flatten(root, flattened);
            var indices = new Dictionary<P113TestNode, uint>();
            for (var index = 0; index < flattened.Count; index++) indices.Add(flattened[index], (uint)index);

            var childIndices = new List<uint>();
            var configBlob = new List<byte>();
            var records = new List<CompiledNodeRecord>();
            var leafBindings = new List<ReferenceLeafBinding>();
            uint memoryOffset = 0;
            uint requiredMemoryAlignment = 1;
            for (var index = 0; index < flattened.Count; index++)
            {
                var source = flattened[index];
                if (source.MemorySize != 0)
                    requiredMemoryAlignment = Math.Max(requiredMemoryAlignment, source.MemoryAlignment);
                var recordMemoryOffset = 0u;
                if (source.MemorySize != 0)
                {
                    memoryOffset = Align(memoryOffset, source.MemoryAlignment);
                    recordMemoryOffset = memoryOffset;
                }
                var configurationOffset = 0u;
                if (source.Configuration.Length != 0)
                {
                    configurationOffset = Align((uint)configBlob.Count, source.ConfigurationAlignment);
                    while (configBlob.Count < configurationOffset) configBlob.Add(0);
                    configBlob.AddRange(source.Configuration);
                }
                var childOffset = (uint)childIndices.Count;
                for (var child = 0; child < source.Children.Count; child++) childIndices.Add(indices[source.Children[child]]);

                records.Add(new CompiledNodeRecord(
                    source.TypeId,
                    1,
                    configurationOffset,
                    (uint)source.Configuration.Length,
                    source.ConfigurationAlignment,
                    recordMemoryOffset,
                    source.MemorySize,
                    source.MemoryAlignment,
                    source.MemoryLifetime,
                    new CompiledRange(childOffset, (uint)source.Children.Count),
                    CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                    CompiledIndex.Invalid,
                    new CompiledRange(0, 0),
                    new CompiledRange(0, 0)));
                if (source.Leaf != null) leafBindings.Add(new ReferenceLeafBinding(source.TypeId, 1, source.Leaf));
                if (source.MemorySize != 0) memoryOffset = checked(memoryOffset + source.MemorySize);
            }

            memoryOffset = Align(memoryOffset, requiredMemoryAlignment);
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash, Hash, Hash, 1, Hash,
                0, (uint)records.Count, (uint)childIndices.Count, 0, 0, (uint)configBlob.Count,
                memoryOffset, requiredMemoryAlignment, 0, true);
            var program = new CompiledProgram(
                header,
                records,
                childIndices,
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(),
                Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(),
                configBlob,
                Array.Empty<byte>(),
                Array.Empty<CompiledDebugMapEntry>());
            var trace = new RecordingReferenceTraceSink();
            return new P113Fixture(
                new ReferenceExecutionMachine(
                    program,
                    new TreeInstanceId(113),
                    new ReferenceLeafRegistry(leafBindings),
                    trace,
                    ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                    ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                    ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                    ReferenceParallelRegistry.CreatePhase1BuiltIns()),
                trace);
        }

        private static void Flatten(P113TestNode node, List<P113TestNode> result)
        {
            result.Add(node);
            for (var index = 0; index < node.Children.Count; index++) Flatten(node.Children[index], result);
        }

        private static uint Align(uint value, uint alignment)
            => checked((value + alignment - 1) / alignment * alignment);

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            for (var index = 0; index < 4; index++) bytes[offset + index] = (byte)(value >> (index * 8));
        }

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            for (var index = 0; index < 8; index++) bytes[offset + index] = (byte)(value >> (index * 8));
        }
    }
}
