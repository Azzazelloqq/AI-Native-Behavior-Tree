using System.Collections.Generic;

namespace AIBT
{
    /// <summary>
    /// The full result of classifying one compiled-program transition, per <c>ADR-P5-001</c>.
    /// </summary>
    public sealed class HotReloadClassificationResult
    {
        internal HotReloadClassificationResult(
            IReadOnlyDictionary<NodeId, HotReloadNodeVerdict> nodeVerdicts,
            IReadOnlyCollection<NodeId> structuralChildChangeNodeIds,
            IReadOnlyCollection<NodeId> restartSubtreeRootNodeIds,
            bool requiresFullRestart)
        {
            NodeVerdicts = nodeVerdicts;
            StructuralChildChangeNodeIds = structuralChildChangeNodeIds;
            RestartSubtreeRootNodeIds = restartSubtreeRootNodeIds;
            RequiresFullRestart = requiresFullRestart;
        }

        /// <summary>Every node's individual verdict, keyed by its stable authoring node ID.</summary>
        public IReadOnlyDictionary<NodeId, HotReloadNodeVerdict> NodeVerdicts { get; }

        /// <summary>
        /// Nodes present in both programs whose own direct children changed in count or order
        /// (insertion, removal, or reordering among direct children) -- a purely structural fact.
        /// A node here still classifies <see cref="HotReloadNodeVerdictCategory.Migrate"/> for its
        /// own state; whether that structural change requires resetting a composite's own running
        /// cursor is a node-type-semantics decision for the caller (the composite handler
        /// registries), not this classifier -- it does not know which compiled type IDs are
        /// Memory composites.
        /// </summary>
        public IReadOnlyCollection<NodeId> StructuralChildChangeNodeIds { get; }

        /// <summary>
        /// The topmost node IDs (in the new program) whose entire subtree must restart fresh --
        /// every <see cref="HotReloadNodeVerdictCategory.IncompatibleRestart"/> node's full
        /// descendant subtree, localized as tightly as possible, or escalated to the whole tree
        /// when localization cannot be proven safe (a node in the candidate region writes a
        /// Shared-scope blackboard slot). Empty when every node classifies
        /// <see cref="HotReloadNodeVerdictCategory.Migrate"/>/<see cref="HotReloadNodeVerdictCategory.New"/>/
        /// <see cref="HotReloadNodeVerdictCategory.Dropped"/>.
        /// </summary>
        public IReadOnlyCollection<NodeId> RestartSubtreeRootNodeIds { get; }

        /// <summary>
        /// <c>true</c> when the whole tree must restart -- either the root itself is in
        /// <see cref="RestartSubtreeRootNodeIds"/>, or the root's own stable identity could not be
        /// resolved at all (a pathological compiled program with no debug entry for its root),
        /// which this classifier conservatively treats as unsafe to reason about further.
        /// </summary>
        public bool RequiresFullRestart { get; }
    }
}
