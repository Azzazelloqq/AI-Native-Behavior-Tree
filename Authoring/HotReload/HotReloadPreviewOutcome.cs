using System.Collections.Generic;

namespace AIBT.Authoring
{
    /// <summary>
    /// What one <see cref="HotReloadPreviewDriver.TryReload"/> call actually did -- the public,
    /// explainable shape <c>AIBT.Editor</c> (no internals visibility into <c>AIBT.Runtime</c>)
    /// displays to the user, per <c>Documentation~/hot-reload.md</c>'s "Editor workflows" section.
    /// </summary>
    public sealed class HotReloadPreviewOutcome
    {
        internal HotReloadPreviewOutcome(
            bool fellBackToFullRestart,
            bool requiredFullRestart,
            IReadOnlyDictionary<NodeId, string> nodeVerdicts,
            IReadOnlyCollection<NodeId> restartSubtreeRootNodeIds,
            uint migratedNodeCount,
            uint resetNodeCount,
            uint droppedNodeCount)
        {
            FellBackToFullRestart = fellBackToFullRestart;
            RequiredFullRestart = requiredFullRestart;
            NodeVerdicts = nodeVerdicts;
            RestartSubtreeRootNodeIds = restartSubtreeRootNodeIds;
            MigratedNodeCount = migratedNodeCount;
            ResetNodeCount = resetNodeCount;
            DroppedNodeCount = droppedNodeCount;
        }

        /// <summary>
        /// <c>true</c> when the old instance was still active (not idle), so the reload fell back
        /// to a full restart entirely rather than attempt any state copy -- per
        /// <c>ADR-P5-001</c>'s implementation addendum, migration only ever runs between rounds.
        /// </summary>
        public bool FellBackToFullRestart { get; }

        /// <summary>
        /// <c>true</c> when the classifier itself required a whole-tree restart (an incompatible
        /// change at the root, or a localization safety escalation) -- distinct from
        /// <see cref="FellBackToFullRestart"/>, which is about the old instance's activity, not the
        /// change's own compatibility.
        /// </summary>
        public bool RequiredFullRestart { get; }

        /// <summary>Every node's classification, by stable node ID, as a human-readable category name (never a raw enum value the UI would need its own mapping for).</summary>
        public IReadOnlyDictionary<NodeId, string> NodeVerdicts { get; }

        /// <summary>The topmost node IDs whose subtree restarted fresh -- empty for a full migration or a full restart.</summary>
        public IReadOnlyCollection<NodeId> RestartSubtreeRootNodeIds { get; }

        /// <summary>How many nodes had their persisted state copied from the old instance.</summary>
        public uint MigratedNodeCount { get; }

        /// <summary>How many nodes kept the fresh instance's default state.</summary>
        public uint ResetNodeCount { get; }

        /// <summary>How many nodes existed in the old instance but not the new one.</summary>
        public uint DroppedNodeCount { get; }
    }
}
