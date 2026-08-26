namespace AIBT
{
    /// <summary>
    /// One node's classification result, with an inspectable reason -- the same explainability
    /// discipline <c>execution-and-scheduling.md</c> requires of scheduler decisions applies to
    /// reload decisions (<c>Documentation~/hot-reload.md</c>'s "Editor workflows" section).
    /// </summary>
    public readonly struct HotReloadNodeVerdict
    {
        public HotReloadNodeVerdict(HotReloadNodeVerdictCategory category, string reason)
        {
            Category = category;
            Reason = reason;
        }

        public HotReloadNodeVerdictCategory Category { get; }

        /// <summary>A short, human-readable explanation -- meaningful outside a test-assertion context, not just a debug enum name.</summary>
        public string Reason { get; }
    }
}
