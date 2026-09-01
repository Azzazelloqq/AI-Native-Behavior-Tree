namespace AIBT
{
    // Mirrors HotReloadMigrationReport's own explainability shape for the native backend (P7-012).
    internal readonly struct NativeHotReloadMigrationReport
    {
        private NativeHotReloadMigrationReport(uint migratedNodeCount, uint resetNodeCount, uint droppedNodeCount, uint cursorResetNodeCount)
        {
            MigratedNodeCount = migratedNodeCount;
            ResetNodeCount = resetNodeCount;
            DroppedNodeCount = droppedNodeCount;
            CursorResetNodeCount = cursorResetNodeCount;
        }

        /// <summary>How many nodes had their persisted instance state (Frame, Generation, NodeMemory) copied from the old instance.</summary>
        internal uint MigratedNodeCount { get; }

        /// <summary>How many nodes kept the fresh instance's default state -- new nodes, incompatible nodes, and anything nested under a restarting subtree.</summary>
        internal uint ResetNodeCount { get; }

        /// <summary>How many nodes existed in the old instance but not the new one.</summary>
        internal uint DroppedNodeCount { get; }

        /// <summary>
        /// How many migrated composite nodes had their own leading 4 NodeMemory cursor bytes reset
        /// (not copied) because their direct children's order changed -- ADR-P5-001 item 2 /
        /// ADR-P7-011 decision 5's own composite-cursor-reset rule, applied for the first time in
        /// this codebase (neither backend had it built before this card).
        /// </summary>
        internal uint CursorResetNodeCount { get; }

        internal static NativeHotReloadMigrationReport Migrated(uint migratedNodeCount, uint resetNodeCount, uint droppedNodeCount, uint cursorResetNodeCount)
            => new NativeHotReloadMigrationReport(migratedNodeCount, resetNodeCount, droppedNodeCount, cursorResetNodeCount);
    }
}
