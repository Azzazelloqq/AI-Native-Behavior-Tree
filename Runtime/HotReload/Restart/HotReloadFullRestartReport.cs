namespace AIBT
{
    /// <summary>
    /// What <see cref="HotReloadFullRestart.Restart"/> actually did to the old instance before
    /// building the fresh one -- inspectable, not silent, per the same explainability discipline
    /// <c>Documentation~/hot-reload.md</c>'s "Editor workflows" section requires.
    /// </summary>
    internal readonly struct HotReloadFullRestartReport
    {
        internal HotReloadFullRestartReport(bool oldInstanceWasAborted, uint activeNodeCountBeforeRestart, uint activeOperationCountBeforeRestart)
        {
            OldInstanceWasAborted = oldInstanceWasAborted;
            ActiveNodeCountBeforeRestart = activeNodeCountBeforeRestart;
            ActiveOperationCountBeforeRestart = activeOperationCountBeforeRestart;
        }

        /// <summary><c>true</c> when the old instance actually had active state to tear down (an abort ran); <c>false</c> when it was already idle/terminal/faulted.</summary>
        internal bool OldInstanceWasAborted { get; }

        /// <summary>How many nodes were active in the old instance immediately before restart, if inspectable.</summary>
        internal uint ActiveNodeCountBeforeRestart { get; }

        /// <summary>How many async operations were active in the old instance immediately before restart, if inspectable.</summary>
        internal uint ActiveOperationCountBeforeRestart { get; }
    }
}
