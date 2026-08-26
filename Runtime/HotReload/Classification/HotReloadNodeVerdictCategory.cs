namespace AIBT
{
    /// <summary>
    /// Per-node hot-reload classification, per <c>Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md</c>.
    /// </summary>
    public enum HotReloadNodeVerdictCategory
    {
        /// <summary>Present in both programs with the same type, version, and instance-memory layout. Live state copies across.</summary>
        Migrate,

        /// <summary>Present only in the new program. No prior state exists; initializes fresh.</summary>
        New,

        /// <summary>Present only in the old program. Any active operation is cancelled; state is discarded.</summary>
        Dropped,

        /// <summary>
        /// Present in both programs with a different type or version (or, defensively, an
        /// unexpected layout mismatch despite an unchanged type and version). The node's own
        /// subtree restarts; it is never migrated.
        /// </summary>
        IncompatibleRestart,
    }
}
