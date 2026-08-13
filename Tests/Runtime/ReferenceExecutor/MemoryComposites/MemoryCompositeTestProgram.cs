using System;
using System.Collections.Generic;

namespace AIBT.Tests.Runtime
{
    internal sealed class MemoryCompositeFixture
    {
        internal MemoryCompositeFixture(
            ReferenceExecutionMachine machine,
            IReadOnlyList<ScriptedReferenceLeaf> leaves,
            RecordingReferenceTraceSink trace)
        {
            Machine = machine;
            Leaves = leaves;
            Trace = trace;
        }

        internal ReferenceExecutionMachine Machine { get; }
        internal IReadOnlyList<ScriptedReferenceLeaf> Leaves { get; }
        internal RecordingReferenceTraceSink Trace { get; }
    }

    internal static class MemoryCompositeTestProgram
    {
        private static readonly CompiledHash Hash = new CompiledHash(new string('b', CompiledHash.HexLength));

        internal static MemoryCompositeFixture Sequence(params ScriptedReferenceLeaf[] leaves)
            => Create("aibt.core.memory-sequence", 4, 4, leaves);

        internal static MemoryCompositeFixture Selector(params ScriptedReferenceLeaf[] leaves)
            => Create("aibt.core.memory-selector", 4, 4, leaves);

        internal static MemoryCompositeFixture InvalidSequenceStorage(uint size, uint alignment)
            => Create("aibt.core.memory-sequence", size, alignment, Array.Empty<ScriptedReferenceLeaf>());

        internal static MemoryCompositeFixture InvalidSequenceStorage(
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
            => Create("aibt.core.memory-sequence", size, alignment, Array.Empty<ScriptedReferenceLeaf>(), lifetime);

        internal static MemoryCompositeFixture WithCompositeRegistry(
            ReferenceMemoryCompositeRegistry registry,
            uint compositeVersion = 1)
            => Create(
                "aibt.core.memory-sequence",
                4,
                4,
                Array.Empty<ScriptedReferenceLeaf>(),
                NodeMemoryLifetime.Activation,
                registry,
                compositeVersion);

        internal static MemoryCompositeFixture WithCompositeRegistry(
            ReferenceMemoryCompositeRegistry registry,
            ScriptedReferenceLeaf child)
            => Create(
                "aibt.core.memory-sequence",
                4,
                4,
                new[] { child },
                NodeMemoryLifetime.Activation,
                registry);

        internal static MemoryCompositeFixture LeafBindingWithChild(
            ScriptedReferenceLeaf rootHandler,
            ScriptedReferenceLeaf childHandler)
        {
            return Create(
                "aibt.test.bound-leaf-root",
                4,
                4,
                new[] { childHandler },
                NodeMemoryLifetime.Activation,
                ReferenceMemoryCompositeRegistry.Empty,
                1,
                rootHandler);
        }

        private static MemoryCompositeFixture Create(
            string compositeTypeId,
            uint memorySize,
            uint memoryAlignment,
            ScriptedReferenceLeaf[] leaves,
            NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation,
            ReferenceMemoryCompositeRegistry compositeRegistry = null,
            uint compositeVersion = 1,
            ScriptedReferenceLeaf rootLeafHandler = null)
        {
            var nodes = new List<CompiledNodeRecord>();
            var children = new uint[leaves.Length];
            nodes.Add(new CompiledNodeRecord(
                StableHash.Fnv1A64(compositeTypeId),
                compositeVersion,
                0,
                0,
                1,
                0,
                memorySize,
                memoryAlignment,
                memoryLifetime,
                new CompiledRange(0, (uint)leaves.Length),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0),
                new CompiledRange(0, 0)));

            var leafBindings = new List<ReferenceLeafBinding>();
            if (rootLeafHandler != null)
            {
                leafBindings.Add(new ReferenceLeafBinding(
                    StableHash.Fnv1A64(compositeTypeId),
                    compositeVersion,
                    rootLeafHandler));
            }
            for (var index = 0; index < leaves.Length; index++)
            {
                var typeId = StableHash.Fnv1A64("aibt.test.memory.child." + index);
                var nodeIndex = checked((uint)index + 1);
                children[index] = nodeIndex;
                nodes.Add(new CompiledNodeRecord(
                    typeId,
                    1,
                    0,
                    0,
                    1,
                    0,
                    0,
                    1,
                    NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 0),
                    CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                    CompiledIndex.Invalid,
                    new CompiledRange(0, 0),
                    new CompiledRange(0, 0)));
                leafBindings.Add(new ReferenceLeafBinding(typeId, 1, leaves[index]));
            }

            var requiredAlignment = memorySize == 0 ? 1u : memoryAlignment;
            var instanceMemorySize = memorySize == 0
                ? 0u
                : Align(memorySize, requiredAlignment);
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
                (uint)nodes.Count,
                (uint)children.Length,
                0,
                0,
                0,
                instanceMemorySize,
                requiredAlignment,
                0,
                true);
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
                new TreeInstanceId(71),
                new ReferenceLeafRegistry(leafBindings),
                trace,
                compositeRegistry ?? ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns());
            return new MemoryCompositeFixture(machine, leaves, trace);
        }

        private static uint Align(uint value, uint alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }
    }
}
