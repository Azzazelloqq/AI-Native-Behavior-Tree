using System;
using System.Collections.Generic;

namespace AIBT.Tests.Runtime
{
    internal static class ReactiveCompositeTestProgram
    {
        private static readonly CompiledHash Hash = new CompiledHash(new string('c', CompiledHash.HexLength));

        internal static MemoryCompositeFixture Sequence(params ScriptedReferenceLeaf[] leaves)
            => Create("aibt.core.reactive-sequence", leaves);

        internal static MemoryCompositeFixture Selector(params ScriptedReferenceLeaf[] leaves)
            => Create("aibt.core.reactive-selector", leaves);

        internal static MemoryCompositeFixture SequenceWithRegistry(
            ReferenceReactiveCompositeRegistry registry,
            uint version = 1,
            params ScriptedReferenceLeaf[] leaves)
            => Create("aibt.core.reactive-sequence", leaves, registry, version);

        internal static MemoryCompositeFixture InvalidSequenceStorage(
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
            => Create(
                "aibt.core.reactive-sequence",
                Array.Empty<ScriptedReferenceLeaf>(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                1,
                size,
                alignment,
                lifetime);

        internal static MemoryCompositeFixture NestedSequences(ScriptedReferenceLeaf leaf)
        {
            var outerType = StableHash.Fnv1A64("aibt.core.reactive-sequence");
            var nodes = new[]
            {
                Composite(outerType, 0, 1, 0),
                Composite(outerType, 1, 1, 4),
                Leaf(StableHash.Fnv1A64("aibt.test.reactive.nested-leaf")),
            };
            return CreateFixture(
                nodes,
                new uint[] { 1, 2 },
                new[] { new ReferenceLeafBinding(nodes[2].NodeTypeId, 1, leaf) },
                8);
        }

        private static MemoryCompositeFixture Create(
            string type,
            ScriptedReferenceLeaf[] leaves,
            ReferenceReactiveCompositeRegistry registry = null,
            uint version = 1,
            uint memorySize = 4,
            uint memoryAlignment = 4,
            NodeMemoryLifetime lifetime = NodeMemoryLifetime.Activation)
        {
            var nodes = new List<CompiledNodeRecord>();
            var children = new uint[leaves.Length];
            nodes.Add(Composite(
                StableHash.Fnv1A64(type),
                0,
                (uint)leaves.Length,
                0,
                version,
                memorySize,
                memoryAlignment,
                lifetime));
            var bindings = new List<ReferenceLeafBinding>();
            for (var index = 0; index < leaves.Length; index++)
            {
                var typeId = StableHash.Fnv1A64("aibt.test.reactive.child." + index);
                children[index] = checked((uint)index + 1);
                nodes.Add(Leaf(typeId));
                bindings.Add(new ReferenceLeafBinding(typeId, 1, leaves[index]));
            }

            var totalMemory = memorySize == 0 ? 0 : Align(memorySize, memoryAlignment);
            return CreateFixture(nodes, children, bindings, totalMemory, registry, memorySize == 0 ? 1 : memoryAlignment);
        }

        private static MemoryCompositeFixture CreateFixture(
            IReadOnlyList<CompiledNodeRecord> nodes,
            uint[] children,
            IEnumerable<ReferenceLeafBinding> bindings,
            uint memorySize,
            ReferenceReactiveCompositeRegistry registry = null,
            uint requiredAlignment = 4)
        {
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                Hash, Hash, Hash, 1, Hash,
                0, (uint)nodes.Count, (uint)children.Length, 0, 0, 0,
                memorySize, requiredAlignment, 0, true);
            var program = new CompiledProgram(
                header,
                nodes,
                children,
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(),
                Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(),
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                Array.Empty<CompiledDebugMapEntry>());
            var trace = new RecordingReferenceTraceSink();
            var machine = new ReferenceExecutionMachine(
                program,
                new TreeInstanceId(81),
                new ReferenceLeafRegistry(bindings),
                trace,
                ReferenceMemoryCompositeRegistry.Empty,
                registry ?? ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns());
            return new MemoryCompositeFixture(machine, Array.Empty<ScriptedReferenceLeaf>(), trace);
        }

        private static CompiledNodeRecord Composite(
            ulong typeId,
            uint childOffset,
            uint childCount,
            uint memoryOffset,
            uint version = 1,
            uint memorySize = 4,
            uint memoryAlignment = 4,
            NodeMemoryLifetime lifetime = NodeMemoryLifetime.Activation)
        {
            return new CompiledNodeRecord(
                typeId, version, 0, 0, 1,
                memorySize == 0 ? 0 : memoryOffset,
                memorySize,
                memoryAlignment,
                lifetime,
                new CompiledRange(childOffset, childCount),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0), new CompiledRange(0, 0));
        }

        private static uint Align(uint value, uint alignment)
            => checked((value + alignment - 1) / alignment * alignment);

        private static CompiledNodeRecord Leaf(ulong typeId)
        {
            return new CompiledNodeRecord(
                typeId, 1, 0, 0, 1,
                0, 0, 1, NodeMemoryLifetime.Activation,
                new CompiledRange(0, 0),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0), new CompiledRange(0, 0));
        }
    }
}
