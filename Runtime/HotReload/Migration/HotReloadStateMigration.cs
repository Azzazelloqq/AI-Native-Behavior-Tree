using System;
using System.Collections.Generic;

namespace AIBT
{
    /// <summary>
    /// Implements the reference-executor side of <c>ADR-P5-001</c>'s shared reload mechanism at
    /// its two remaining exclusion sets: empty (compatible migration, <c>P5-006</c>) and localized
    /// subtree (subtree restart, <c>P5-005</c>). Both are the same code path here -- the exclusion
    /// set is exactly <see cref="HotReloadClassificationResult.RestartSubtreeRootNodeIds"/>,
    /// expanded to full subtrees, which is empty when nothing classified <c>IncompatibleRestart</c>
    /// anywhere. Falls back to <see cref="HotReloadFullRestart"/> whenever the old instance is not
    /// idle -- migration only ever runs between rounds, never mid-flight, per this card's own
    /// disclosed scope (see <c>Planning~/Evidence/P5-006/README.md</c>).
    /// </summary>
    internal static class HotReloadStateMigration
    {
        internal static ReferenceExecutionMachine Migrate(
            ReferenceExecutionMachine oldMachine,
            CompiledProgram oldProgram,
            CompiledProgram newProgram,
            HotReloadClassificationResult classification,
            ReferenceUpdateContext fallbackAbortUpdateContext,
            TreeInstanceId treeInstanceId,
            ReferenceLeafRegistry leafRegistry,
            IReferenceTraceSink traceSink,
            ReferenceMemoryCompositeRegistry memoryCompositeRegistry,
            ReferenceReactiveCompositeRegistry reactiveCompositeRegistry,
            ReferenceDecoratorRegistry decoratorRegistry,
            ReferenceParallelRegistry parallelRegistry,
            RegisteredBlackboardRegistry registeredBlackboardRegistry,
            ReferenceObserverConditionRegistry observerRegistry,
            out HotReloadMigrationReport report)
        {
            if (oldMachine == null) throw new ArgumentNullException(nameof(oldMachine));
            if (oldProgram == null) throw new ArgumentNullException(nameof(oldProgram));
            if (newProgram == null) throw new ArgumentNullException(nameof(newProgram));
            if (classification == null) throw new ArgumentNullException(nameof(classification));

            var inspection = oldMachine.CaptureInspection();
            if (inspection.ActiveNodeCount > 0)
            {
                var restarted = HotReloadFullRestart.Restart(
                    oldMachine, newProgram, fallbackAbortUpdateContext, treeInstanceId, leafRegistry, traceSink,
                    memoryCompositeRegistry, reactiveCompositeRegistry, decoratorRegistry, parallelRegistry,
                    registeredBlackboardRegistry, observerRegistry, out var restartReport);
                report = HotReloadMigrationReport.FellBack(restartReport);
                return restarted;
            }

            var newMap = HotReloadProgramIdentityMap.Build(newProgram);
            var excluded = ExpandRestartSubtrees(newProgram, newMap, classification.RestartSubtreeRootNodeIds);
            var initialBlackboard = ConvertBlackboard(inspection.Blackboard, newProgram);

            var freshMachine = new ReferenceExecutionMachine(
                newProgram, treeInstanceId, leafRegistry, traceSink, memoryCompositeRegistry,
                reactiveCompositeRegistry, decoratorRegistry, parallelRegistry, registeredBlackboardRegistry,
                observerRegistry, default, initialBlackboard);

            var oldMap = HotReloadProgramIdentityMap.Build(oldProgram);
            uint migratedCount = 0, resetCount = 0, droppedCount = 0;
            foreach (var pair in classification.NodeVerdicts)
            {
                if (pair.Value.Category == HotReloadNodeVerdictCategory.Dropped)
                {
                    droppedCount++;
                    continue;
                }

                if (!newMap.TryGetRuntimeIndex(pair.Key, out var newIndex)) continue;

                if (pair.Value.Category != HotReloadNodeVerdictCategory.Migrate || excluded.Contains(pair.Key))
                {
                    // New nodes, Incompatible nodes, and anything nested under a restarting
                    // subtree keep the fresh instance's default state -- exactly "restart fresh"
                    // for that node, without a separate mechanism.
                    resetCount++;
                    continue;
                }

                if (!oldMap.TryGetRuntimeIndex(pair.Key, out var oldIndex)) continue;
                var snapshot = oldMachine.CaptureNodeState(new RuntimeNodeIndex(oldIndex));
                freshMachine.SeedNodeState(new RuntimeNodeIndex(newIndex), snapshot);
                migratedCount++;
            }

            report = HotReloadMigrationReport.Migrated(migratedCount, resetCount, droppedCount);
            return freshMachine;
        }

        private static HashSet<NodeId> ExpandRestartSubtrees(
            CompiledProgram newProgram, HotReloadProgramIdentityMap newMap, IReadOnlyCollection<NodeId> restartRootNodeIds)
        {
            var result = new HashSet<NodeId>();
            if (restartRootNodeIds.Count == 0) return result;

            var indexToId = new Dictionary<uint, NodeId>();
            foreach (var entry in newProgram.DebugMap) indexToId[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;

            foreach (var rootId in restartRootNodeIds)
            {
                if (newMap.TryGetRuntimeIndex(rootId, out var rootIndex)) CollectSubtreeIds(newProgram, indexToId, rootIndex, result);
            }

            return result;
        }

        private static void CollectSubtreeIds(
            CompiledProgram program, IReadOnlyDictionary<uint, NodeId> indexToId, uint nodeIndex, HashSet<NodeId> into)
        {
            if (indexToId.TryGetValue(nodeIndex, out var nodeId) && !into.Add(nodeId)) return;

            var range = program.Nodes[(int)nodeIndex].Children;
            for (var offset = 0; offset < range.Count; offset++)
            {
                CollectSubtreeIds(program, indexToId, program.ChildIndices[(int)(range.Offset + offset)], into);
            }
        }

        // Filters to keys the new program still declares with the same compiled slot type --
        // ReferenceBlackboardStorage rejects an initial value naming an unknown stable key, or one
        // whose type disagrees with the slot's own declared type, outright (a whole-construction
        // failure), which would fault the fresh instance entirely rather than simply drop the one
        // removed/retyped key.
        private static IReadOnlyList<ReferenceBlackboardInitialValue> ConvertBlackboard(
            ReferenceBlackboardSnapshot snapshot, CompiledProgram newProgram)
        {
            var newSlotsByKey = new Dictionary<ulong, CompiledBlackboardSlotRecord>();
            foreach (var slot in newProgram.BlackboardSlots) newSlotsByKey[slot.StableKeyId] = slot;

            var result = new List<ReferenceBlackboardInitialValue>(snapshot.Entries.Count);
            foreach (var entry in snapshot.Entries)
            {
                if (!newSlotsByKey.TryGetValue(entry.StableKeyId, out var newSlot)) continue;
                if (newSlot.TypeId != entry.Type.TypeId || newSlot.TypeVersion != entry.Type.Version) continue;

                result.Add(entry.IsRegistered
                    ? ReferenceBlackboardInitialValue.Registered(entry.StableKeyId, entry.Type.TypeId, entry.Type.Version, entry.CopyRegisteredBytes())
                    : ReferenceBlackboardInitialValue.BuiltIn(entry.StableKeyId, entry.BuiltInValue));
            }

            return result;
        }
    }
}
