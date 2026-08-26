using System;
using System.Collections.Generic;
using System.Linq;

namespace AIBT
{
    /// <summary>
    /// Implements <c>Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md</c>'s
    /// per-node classification and subtree-localization rules over two compiled programs. Pure and
    /// side-effect-free: it never touches a live tree instance and performs no restart or
    /// migration itself (that is <c>P5-004</c>/<c>P5-005</c>/<c>P5-006</c>'s job).
    /// </summary>
    public static class HotReloadCompatibilityClassifier
    {
        public static HotReloadClassificationResult Classify(CompiledProgram oldProgram, CompiledProgram newProgram)
        {
            if (oldProgram == null) throw new ArgumentNullException(nameof(oldProgram));
            if (newProgram == null) throw new ArgumentNullException(nameof(newProgram));

            var oldMap = HotReloadProgramIdentityMap.Build(oldProgram);
            var newMap = HotReloadProgramIdentityMap.Build(newProgram);
            var verdicts = ClassifyNodes(oldMap, newMap);
            var structuralChangeNodeIds = FindStructuralChildChanges(oldProgram, newProgram, verdicts);

            var incompatibleNodeIds = verdicts
                .Where(pair => pair.Value.Category == HotReloadNodeVerdictCategory.IncompatibleRestart)
                .Select(pair => pair.Key)
                .ToArray();
            var restartRoots = LocalizeRestartSubtrees(newProgram, newMap, incompatibleNodeIds);

            var rootNodeId = ResolveRootNodeId(newProgram);
            var requiresFullRestart = rootNodeId == null || restartRoots.Contains(rootNodeId.Value);

            return new HotReloadClassificationResult(verdicts, structuralChangeNodeIds, restartRoots, requiresFullRestart);
        }

        private static Dictionary<NodeId, HotReloadNodeVerdict> ClassifyNodes(
            HotReloadProgramIdentityMap oldMap, HotReloadProgramIdentityMap newMap)
        {
            var verdicts = new Dictionary<NodeId, HotReloadNodeVerdict>();
            foreach (var nodeId in oldMap.NodeIds)
            {
                oldMap.TryGetSignature(nodeId, out var oldSignature);
                if (!newMap.TryGetSignature(nodeId, out var newSignature))
                {
                    verdicts[nodeId] = new HotReloadNodeVerdict(
                        HotReloadNodeVerdictCategory.Dropped,
                        "Node does not exist in the new compiled program.");
                    continue;
                }

                if (!oldSignature.HasSameTypeAndVersion(newSignature))
                {
                    verdicts[nodeId] = new HotReloadNodeVerdict(
                        HotReloadNodeVerdictCategory.IncompatibleRestart,
                        "Type or version changed (" + oldSignature.TypeId + "@v" + oldSignature.TypeVersion
                        + " -> " + newSignature.TypeId + "@v" + newSignature.TypeVersion + ").");
                    continue;
                }

                if (!oldSignature.HasCompatibleLayout(newSignature))
                {
                    verdicts[nodeId] = new HotReloadNodeVerdict(
                        HotReloadNodeVerdictCategory.IncompatibleRestart,
                        "Same type and version but a different instance-memory layout; treated as incompatible per ADR-P5-001.");
                    continue;
                }

                verdicts[nodeId] = new HotReloadNodeVerdict(
                    HotReloadNodeVerdictCategory.Migrate,
                    "Unchanged in type, version, and instance-memory layout.");
            }

            foreach (var nodeId in newMap.NodeIds)
            {
                if (!oldMap.TryGetSignature(nodeId, out _))
                {
                    verdicts[nodeId] = new HotReloadNodeVerdict(
                        HotReloadNodeVerdictCategory.New,
                        "Node is new in this compiled program.");
                }
            }

            return verdicts;
        }

        // A node's own direct children, in order, as stable NodeIds -- used only to detect
        // whether that order/count changed, never as a migration key itself.
        private static Dictionary<NodeId, List<NodeId>> BuildDirectChildIdOrder(CompiledProgram program)
        {
            var indexToId = new Dictionary<uint, NodeId>();
            foreach (var entry in program.DebugMap) indexToId[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;

            var result = new Dictionary<NodeId, List<NodeId>>();
            for (var nodeIndex = 0; nodeIndex < program.Nodes.Count; nodeIndex++)
            {
                if (!indexToId.TryGetValue((uint)nodeIndex, out var nodeId)) continue;

                var range = program.Nodes[nodeIndex].Children;
                var children = new List<NodeId>((int)range.Count);
                for (var offset = 0; offset < range.Count; offset++)
                {
                    var childIndex = program.ChildIndices[(int)(range.Offset + offset)];
                    if (indexToId.TryGetValue(childIndex, out var childId)) children.Add(childId);
                }

                result[nodeId] = children;
            }

            return result;
        }

        private static HashSet<NodeId> FindStructuralChildChanges(
            CompiledProgram oldProgram, CompiledProgram newProgram, IReadOnlyDictionary<NodeId, HotReloadNodeVerdict> verdicts)
        {
            var oldChildren = BuildDirectChildIdOrder(oldProgram);
            var newChildren = BuildDirectChildIdOrder(newProgram);
            var changed = new HashSet<NodeId>();
            foreach (var pair in newChildren)
            {
                if (!verdicts.TryGetValue(pair.Key, out var verdict) || verdict.Category != HotReloadNodeVerdictCategory.Migrate)
                {
                    continue; // only meaningful for a node whose own state is otherwise migrating
                }

                if (!oldChildren.TryGetValue(pair.Key, out var previousChildren) || !previousChildren.SequenceEqual(pair.Value))
                {
                    changed.Add(pair.Key);
                }
            }

            return changed;
        }

        // Every descendant (inclusive) of `rootIndex`, walking the new program's own children table.
        private static void CollectSubtree(CompiledProgram program, uint rootIndex, HashSet<uint> into)
        {
            if (!into.Add(rootIndex)) return;

            var range = program.Nodes[(int)rootIndex].Children;
            for (var offset = 0; offset < range.Count; offset++)
            {
                CollectSubtree(program, program.ChildIndices[(int)(range.Offset + offset)], into);
            }
        }

        private static bool WritesSharedBlackboardSlot(CompiledProgram program, uint nodeIndex)
        {
            var record = program.Nodes[(int)nodeIndex];
            var range = record.WriteSlots;
            for (var offset = 0; offset < range.Count; offset++)
            {
                var slotIndex = program.WriteSlotIndices[(int)(range.Offset + offset)];
                if (program.BlackboardSlots[(int)slotIndex].Scope == BlackboardScope.Shared) return true;
            }

            return false;
        }

        private static IReadOnlyCollection<NodeId> LocalizeRestartSubtrees(
            CompiledProgram newProgram, HotReloadProgramIdentityMap newMap, IReadOnlyCollection<NodeId> incompatibleNodeIds)
        {
            if (incompatibleNodeIds.Count == 0) return Array.Empty<NodeId>();

            var indexToId = new Dictionary<uint, NodeId>();
            foreach (var entry in newProgram.DebugMap) indexToId[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;

            var restartRegion = new HashSet<uint>();
            foreach (var nodeId in incompatibleNodeIds)
            {
                if (newMap.TryGetRuntimeIndex(nodeId, out var index)) CollectSubtree(newProgram, index, restartRegion);
            }

            // Conservative safety check (ADR-P5-001): a shared write inside the candidate region
            // could be observed from outside it. This does not trace whether it actually is --
            // any Shared-scope write anywhere in the region disqualifies localization entirely,
            // an intentional over-approximation, not a full data-flow analysis.
            if (restartRegion.Any(index => WritesSharedBlackboardSlot(newProgram, index)))
            {
                var rootId = ResolveRootNodeId(newProgram);
                return rootId == null ? (IReadOnlyCollection<NodeId>)Array.Empty<NodeId>() : new[] { rootId.Value };
            }

            // Report only the topmost incompatible nodes -- one already nested inside another's
            // restarting subtree adds nothing new.
            var roots = new List<NodeId>();
            foreach (var nodeId in incompatibleNodeIds)
            {
                if (!newMap.TryGetRuntimeIndex(nodeId, out var index)) continue;

                var isNested = incompatibleNodeIds.Any(other =>
                {
                    if (other.Equals(nodeId)) return false;
                    if (!newMap.TryGetRuntimeIndex(other, out var otherIndex)) return false;
                    var otherSubtree = new HashSet<uint>();
                    CollectSubtree(newProgram, otherIndex, otherSubtree);
                    return otherSubtree.Contains(index) && otherIndex != index;
                });

                if (!isNested) roots.Add(nodeId);
            }

            return roots;
        }

        private static NodeId? ResolveRootNodeId(CompiledProgram program)
        {
            foreach (var entry in program.DebugMap)
            {
                if (entry.RuntimeNodeIndex == program.Header.RootNodeIndex) return entry.AuthoringNodeId;
            }

            return null;
        }
    }
}
