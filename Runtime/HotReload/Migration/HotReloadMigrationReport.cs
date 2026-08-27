namespace AIBT
{
    /// <summary>
    /// What <see cref="HotReloadStateMigration.Migrate"/> actually did -- inspectable, not silent,
    /// per the same explainability discipline <c>Documentation~/hot-reload.md</c>'s "Editor
    /// workflows" section requires.
    /// </summary>
    internal readonly struct HotReloadMigrationReport
    {
        private HotReloadMigrationReport(
            bool fellBackToFullRestart,
            HotReloadFullRestartReport? fullRestartReport,
            uint migratedNodeCount,
            uint resetNodeCount,
            uint droppedNodeCount)
        {
            FellBackToFullRestart = fellBackToFullRestart;
            FullRestartReport = fullRestartReport;
            MigratedNodeCount = migratedNodeCount;
            ResetNodeCount = resetNodeCount;
            DroppedNodeCount = droppedNodeCount;
        }

        /// <summary>
        /// <c>true</c> when the old instance was not idle (had active frames), so this call fell
        /// back to <see cref="HotReloadFullRestart"/> entirely rather than guess at a mid-flight
        /// copy -- <see cref="FullRestartReport"/> carries that mechanism's own report.
        /// </summary>
        internal bool FellBackToFullRestart { get; }

        internal HotReloadFullRestartReport? FullRestartReport { get; }

        /// <summary>How many nodes had their persisted instance state (memory, activation generation) copied from the old instance.</summary>
        internal uint MigratedNodeCount { get; }

        /// <summary>How many nodes kept the fresh instance's default state -- new nodes, incompatible nodes, and anything nested under a restarting subtree.</summary>
        internal uint ResetNodeCount { get; }

        /// <summary>How many nodes existed in the old instance but not the new one.</summary>
        internal uint DroppedNodeCount { get; }

        internal static HotReloadMigrationReport FellBack(HotReloadFullRestartReport fullRestartReport)
            => new HotReloadMigrationReport(true, fullRestartReport, 0, 0, 0);

        internal static HotReloadMigrationReport Migrated(uint migratedNodeCount, uint resetNodeCount, uint droppedNodeCount)
            => new HotReloadMigrationReport(false, null, migratedNodeCount, resetNodeCount, droppedNodeCount);
    }
}
