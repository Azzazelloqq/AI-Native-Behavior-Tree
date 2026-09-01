namespace AIBT
{
    // Mirrors HotReloadFullRestartReport's own explainability shape for the native backend (P7-012).
    internal readonly struct NativeHotReloadFullRestartReport
    {
        internal NativeHotReloadFullRestartReport(bool oldInstanceWasAborted)
        {
            OldInstanceWasAborted = oldInstanceWasAborted;
        }

        /// <summary>
        /// <c>true</c> when a fresh update could be opened on the old instance and an abort was
        /// requested against it -- true for essentially every valid instance, including one that
        /// never ticked at all (<c>TryBeginUpdate</c> succeeds unconditionally there too; it is not
        /// a distinct "nothing to resume" signal). <c>false</c> only when the old instance could not
        /// accept a fresh update at all (already faulted, mid-dispatch, or still holding an unread
        /// terminal root status from a prior completed run) -- restart still proceeds to construct
        /// the fresh instance in that case, simply without an abort step.
        /// </summary>
        internal bool OldInstanceWasAborted { get; }
    }
}
