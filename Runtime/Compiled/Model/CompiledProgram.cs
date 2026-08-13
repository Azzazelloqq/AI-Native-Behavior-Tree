using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    public sealed class CompiledProgram
    {
        private readonly ReadOnlyCollection<CompiledNodeRecord> _nodes;
        private readonly ReadOnlyCollection<uint> _childIndices;
        private readonly ReadOnlyCollection<uint> _readSlotIndices;
        private readonly ReadOnlyCollection<uint> _writeSlotIndices;
        private readonly ReadOnlyCollection<CompiledBlackboardSlotRecord> _blackboardSlots;
        private readonly ReadOnlyCollection<CompiledObserverRecord> _observers;
        private readonly ReadOnlyCollection<uint> _watchedSlotIndices;
        private readonly ReadOnlyCollection<byte> _configBlob;
        private readonly ReadOnlyCollection<byte> _defaultValueBlob;
        private readonly ReadOnlyCollection<CompiledDebugMapEntry> _debugMap;

        public CompiledProgram(
            CompiledProgramHeader header,
            IEnumerable<CompiledNodeRecord> nodes,
            IEnumerable<uint> childIndices,
            IEnumerable<uint> readSlotIndices,
            IEnumerable<uint> writeSlotIndices,
            IEnumerable<CompiledBlackboardSlotRecord> blackboardSlots,
            IEnumerable<CompiledObserverRecord> observers,
            IEnumerable<uint> watchedSlotIndices,
            IEnumerable<byte> configBlob,
            IEnumerable<byte> defaultValueBlob,
            IEnumerable<CompiledDebugMapEntry> debugMap)
        {
            var nodeArray = Copy(nodes, nameof(nodes));
            var childIndexArray = Copy(childIndices, nameof(childIndices));
            var readSlotIndexArray = Copy(readSlotIndices, nameof(readSlotIndices));
            var writeSlotIndexArray = Copy(writeSlotIndices, nameof(writeSlotIndices));
            var slotArray = Copy(blackboardSlots, nameof(blackboardSlots));
            var observerArray = Copy(observers, nameof(observers));
            var watchedSlotIndexArray = Copy(watchedSlotIndices, nameof(watchedSlotIndices));
            var configBlobArray = Copy(configBlob, nameof(configBlob));
            var defaultValueBlobArray = Copy(defaultValueBlob, nameof(defaultValueBlob));
            var debugMapArray = Copy(debugMap, nameof(debugMap));

            ValidateHeader(
                header,
                nodeArray,
                childIndexArray,
                slotArray,
                configBlobArray,
                debugMapArray);
            ValidateNodes(
                header,
                nodeArray,
                childIndexArray,
                readSlotIndexArray,
                writeSlotIndexArray,
                slotArray,
                configBlobArray,
                debugMapArray);
            ValidateBlackboardSlots(slotArray, observerArray, watchedSlotIndexArray, defaultValueBlobArray);
            ValidateObservers(nodeArray, slotArray, observerArray, watchedSlotIndexArray);
            ValidateDebugMap(nodeArray, debugMapArray);

            Header = header;
            _nodes = Array.AsReadOnly(nodeArray);
            _childIndices = Array.AsReadOnly(childIndexArray);
            _readSlotIndices = Array.AsReadOnly(readSlotIndexArray);
            _writeSlotIndices = Array.AsReadOnly(writeSlotIndexArray);
            _blackboardSlots = Array.AsReadOnly(slotArray);
            _observers = Array.AsReadOnly(observerArray);
            _watchedSlotIndices = Array.AsReadOnly(watchedSlotIndexArray);
            _configBlob = Array.AsReadOnly(configBlobArray);
            _defaultValueBlob = Array.AsReadOnly(defaultValueBlobArray);
            _debugMap = Array.AsReadOnly(debugMapArray);
        }

        public CompiledProgramHeader Header { get; }

        public IReadOnlyList<CompiledNodeRecord> Nodes => _nodes;

        public IReadOnlyList<uint> ChildIndices => _childIndices;

        public IReadOnlyList<uint> ReadSlotIndices => _readSlotIndices;

        public IReadOnlyList<uint> WriteSlotIndices => _writeSlotIndices;

        public IReadOnlyList<CompiledBlackboardSlotRecord> BlackboardSlots => _blackboardSlots;

        public IReadOnlyList<CompiledObserverRecord> Observers => _observers;

        public IReadOnlyList<uint> WatchedSlotIndices => _watchedSlotIndices;

        public IReadOnlyList<byte> ConfigBlob => _configBlob;

        public IReadOnlyList<byte> DefaultValueBlob => _defaultValueBlob;

        public IReadOnlyList<CompiledDebugMapEntry> DebugMap => _debugMap;

        private static T[] Copy<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var collection = source as ICollection<T>;
            if (collection != null)
            {
                var result = new T[collection.Count];
                collection.CopyTo(result, 0);
                return result;
            }

            return new List<T>(source).ToArray();
        }

        private static void ValidateHeader(
            CompiledProgramHeader header,
            CompiledNodeRecord[] nodes,
            uint[] childIndices,
            CompiledBlackboardSlotRecord[] slots,
            byte[] configBlob,
            CompiledDebugMapEntry[] debugMap)
        {
            if (header.Magic != CompiledProgramHeader.ExpectedMagic)
            {
                throw new ArgumentException("The compiled-program header is uninitialized.", nameof(header));
            }

            RequireCount(header.NodeCount, nodes.Length, nameof(header.NodeCount));
            RequireCount(header.ChildIndexCount, childIndices.Length, nameof(header.ChildIndexCount));
            RequireCount(header.BlackboardSlotCount, slots.Length, nameof(header.BlackboardSlotCount));
            RequireCount(header.DebugMapCount, debugMap.Length, nameof(header.DebugMapCount));
            RequireCount(header.ConfigBlobSize, configBlob.Length, nameof(header.ConfigBlobSize));

            if (header.RootNodeIndex >= header.NodeCount)
            {
                throw new ArgumentException("The root node index is outside the node table.", nameof(header));
            }

            if (header.InstanceNodeMemorySize % header.RequiredMaximumAlignment != 0)
            {
                throw new ArgumentException("The instance node-memory size must satisfy the header alignment.", nameof(header));
            }
        }

        private static void ValidateNodes(
            CompiledProgramHeader header,
            CompiledNodeRecord[] nodes,
            uint[] childIndices,
            uint[] readSlotIndices,
            uint[] writeSlotIndices,
            CompiledBlackboardSlotRecord[] slots,
            byte[] configBlob,
            CompiledDebugMapEntry[] debugMap)
        {
            var configRanges = new List<StorageRange>();
            var memoryRanges = new List<StorageRange>();
            uint requiredAlignment = 1;

            for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                ValidateRange(node.Children, childIndices.Length, "child-index");
                ValidateRange(node.ReadSlots, readSlotIndices.Length, "read-slot access");
                ValidateRange(node.WriteSlots, writeSlotIndices.Length, "write-slot access");
                ValidateBlobRange(node.ConfigOffset, node.ConfigSize, configBlob.Length, "node config");
                ValidateBlobRange(node.InstanceMemoryOffset, node.InstanceMemorySize, header.InstanceNodeMemorySize, "node memory");

                if (node.ConfigSize != 0)
                {
                    configRanges.Add(new StorageRange(node.ConfigOffset, node.ConfigSize, nodeIndex));
                }

                if (node.InstanceMemorySize != 0)
                {
                    memoryRanges.Add(new StorageRange(node.InstanceMemoryOffset, node.InstanceMemorySize, nodeIndex));
                    if (node.InstanceMemoryAlignment > requiredAlignment)
                    {
                        requiredAlignment = node.InstanceMemoryAlignment;
                    }
                }

                if (node.DebugIdentityIndex != CompiledIndex.Invalid && node.DebugIdentityIndex >= debugMap.Length)
                {
                    throw new ArgumentException("A node references a debug identity outside the debug map.", nameof(nodes));
                }
            }

            if (requiredAlignment != header.RequiredMaximumAlignment)
            {
                throw new ArgumentException("The header maximum alignment does not match the node records.", nameof(header));
            }

            ValidateNoOverlap(configRanges, "Node config ranges overlap.");
            ValidateNoOverlap(memoryRanges, "Node instance-memory ranges overlap.");

            ValidateNodeIndices(childIndices, nodes.Length, "child index");
            ValidateSlotIndices(readSlotIndices, slots, CompiledBlackboardAccessFlags.Read, "read-slot index");
            ValidateSlotIndices(writeSlotIndices, slots, CompiledBlackboardAccessFlags.Write, "write-slot index");
        }

        private static void ValidateBlackboardSlots(
            CompiledBlackboardSlotRecord[] slots,
            CompiledObserverRecord[] observers,
            uint[] watchedSlotIndices,
            byte[] defaultValueBlob)
        {
            var stableKeyIds = new HashSet<ulong>();
            var treeRanges = new List<StorageRange>();
            var agentRanges = new List<StorageRange>();
            var sharedRanges = new List<StorageRange>();
            var observedSlots = new bool[slots.Length];

            for (var observerIndex = 0; observerIndex < observers.Length; observerIndex++)
            {
                var range = observers[observerIndex].WatchedSlots;
                ValidateRange(range, watchedSlotIndices.Length, "watched-slot");
                var end = (int)(range.Offset + range.Count);
                for (var index = (int)range.Offset; index < end; index++)
                {
                    var slotIndex = watchedSlotIndices[index];
                    if (slotIndex >= slots.Length)
                    {
                        throw new ArgumentException("A watched-slot index is outside the blackboard table.", nameof(watchedSlotIndices));
                    }

                    if (slots[slotIndex].Scope != BlackboardScope.Tree)
                    {
                        throw new ArgumentException("Phase 1 observers may watch only Tree-scope slots.", nameof(watchedSlotIndices));
                    }

                    if (index > (int)range.Offset)
                    {
                        var previousSlotIndex = watchedSlotIndices[index - 1];
                        if (slots[previousSlotIndex].StableKeyId >= slots[slotIndex].StableKeyId)
                        {
                            throw new ArgumentException(
                                "An observer's watched slots must be strictly ordered by stable key ID.",
                                nameof(watchedSlotIndices));
                        }
                    }

                    observedSlots[slotIndex] = true;
                }
            }

            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (!stableKeyIds.Add(slot.StableKeyId))
                {
                    throw new ArgumentException("Stable blackboard key identities must be unique.", nameof(slots));
                }

                ValidateBlobRange(slot.DefaultValueOffset, slot.Size, defaultValueBlob.Length, "blackboard default value");

                var range = new StorageRange(slot.Offset, slot.Size, slotIndex);
                switch (slot.Scope)
                {
                    case BlackboardScope.Tree:
                        treeRanges.Add(range);
                        break;
                    case BlackboardScope.Agent:
                        agentRanges.Add(range);
                        break;
                    case BlackboardScope.Shared:
                        sharedRanges.Add(range);
                        break;
                    default:
                        throw new ArgumentException("The blackboard slot scope is invalid.", nameof(slots));
                }

                var hasObservedFlag = (slot.AccessFlags & CompiledBlackboardAccessFlags.Observed) != 0;
                if (hasObservedFlag != observedSlots[slotIndex])
                {
                    throw new ArgumentException("Blackboard observer metadata does not match the observer table.", nameof(slots));
                }
            }

            ValidateNoOverlap(treeRanges, "Tree-scope blackboard memory ranges overlap.");
            ValidateNoOverlap(agentRanges, "Agent-scope blackboard memory ranges overlap.");
            ValidateNoOverlap(sharedRanges, "Shared-scope blackboard memory ranges overlap.");
        }

        private static void ValidateObservers(
            CompiledNodeRecord[] nodes,
            CompiledBlackboardSlotRecord[] slots,
            CompiledObserverRecord[] observers,
            uint[] watchedSlotIndices)
        {
            var observerNodes = new HashSet<uint>();
            for (var observerIndex = 0; observerIndex < observers.Length; observerIndex++)
            {
                var observer = observers[observerIndex];
                if (observer.ObserverNodeIndex >= nodes.Length || observer.OwningReactiveCompositeIndex >= nodes.Length)
                {
                    throw new ArgumentException("An observer references a node outside the node table.", nameof(observers));
                }

                if (observer.ObserverNodeIndex == observer.OwningReactiveCompositeIndex)
                {
                    throw new ArgumentException("An observer node cannot own itself.", nameof(observers));
                }

                if (!observerNodes.Add(observer.ObserverNodeIndex))
                {
                    throw new ArgumentException("A node can have at most one observer record.", nameof(observers));
                }

                var watched = new HashSet<uint>();
                var range = observer.WatchedSlots;
                var end = (int)(range.Offset + range.Count);
                for (var index = (int)range.Offset; index < end; index++)
                {
                    var slotIndex = watchedSlotIndices[index];
                    if (slotIndex >= slots.Length || !watched.Add(slotIndex))
                    {
                        throw new ArgumentException("An observer's watched slots must be valid and unique.", nameof(watchedSlotIndices));
                    }
                }
            }
        }

        private static void ValidateDebugMap(CompiledNodeRecord[] nodes, CompiledDebugMapEntry[] debugMap)
        {
            var mappedNodes = new HashSet<uint>();
            for (var debugIndex = 0; debugIndex < debugMap.Length; debugIndex++)
            {
                var entry = debugMap[debugIndex];
                if (entry.RuntimeNodeIndex >= nodes.Length || !mappedNodes.Add(entry.RuntimeNodeIndex))
                {
                    throw new ArgumentException("Debug-map node indices must be valid and unique.", nameof(debugMap));
                }

                if (nodes[entry.RuntimeNodeIndex].DebugIdentityIndex != (uint)debugIndex)
                {
                    throw new ArgumentException("A debug-map entry must be referenced by its mapped node.", nameof(debugMap));
                }
            }

            for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                var debugIndex = nodes[nodeIndex].DebugIdentityIndex;
                if (debugIndex != CompiledIndex.Invalid && debugMap[debugIndex].RuntimeNodeIndex != (uint)nodeIndex)
                {
                    throw new ArgumentException("A node's debug identity must map back to that node.", nameof(nodes));
                }
            }
        }

        private static void ValidateNodeIndices(uint[] indices, int nodeCount, string label)
        {
            for (var index = 0; index < indices.Length; index++)
            {
                if (indices[index] >= nodeCount)
                {
                    throw new ArgumentException("A " + label + " is outside the node table.", nameof(indices));
                }
            }
        }

        private static void ValidateSlotIndices(
            uint[] indices,
            CompiledBlackboardSlotRecord[] slots,
            CompiledBlackboardAccessFlags requiredFlag,
            string label)
        {
            for (var index = 0; index < indices.Length; index++)
            {
                var slotIndex = indices[index];
                if (slotIndex >= slots.Length)
                {
                    throw new ArgumentException("A " + label + " is outside the blackboard table.", nameof(indices));
                }

                if ((slots[slotIndex].AccessFlags & requiredFlag) == 0)
                {
                    throw new ArgumentException("A " + label + " conflicts with the slot access metadata.", nameof(indices));
                }
            }
        }

        private static void ValidateRange(CompiledRange range, int tableLength, string label)
        {
            if (range.EndExclusive > (uint)tableLength)
            {
                throw new ArgumentException("A " + label + " range is outside its table.");
            }
        }

        private static void ValidateBlobRange(uint offset, uint size, int blobLength, string label)
            => ValidateBlobRange(offset, size, (uint)blobLength, label);

        private static void ValidateBlobRange(uint offset, uint size, uint blobLength, string label)
        {
            if ((ulong)offset + size > blobLength)
            {
                throw new ArgumentException("A " + label + " range is outside its storage.");
            }
        }

        private static void ValidateNoOverlap(List<StorageRange> ranges, string message)
        {
            ranges.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            for (var index = 1; index < ranges.Count; index++)
            {
                if (ranges[index].Offset < ranges[index - 1].EndExclusive)
                {
                    throw new ArgumentException(message);
                }
            }
        }

        private static void RequireCount(uint expected, int actual, string fieldName)
        {
            if (expected != (uint)actual)
            {
                throw new ArgumentException("Header count does not match its table.", fieldName);
            }
        }

        private readonly struct StorageRange
        {
            public StorageRange(uint offset, uint size, int ownerIndex)
            {
                Offset = offset;
                Size = size;
                OwnerIndex = ownerIndex;
            }

            public uint Offset { get; }

            public uint Size { get; }

            public int OwnerIndex { get; }

            public ulong EndExclusive => (ulong)Offset + Size;
        }
    }
}
