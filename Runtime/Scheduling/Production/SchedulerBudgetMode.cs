namespace AIBT
{
    /// <summary>
    /// The three budget-source modes <c>Documentation~/decisions/ADR-P7-033-global-scheduler-and-profiles.md</c>
    /// (AIBT-037) defines. There is no zero-input automatic millisecond/percentage guess -- AIBT
    /// cannot know how much of a game's frame belongs to AI, so the honest default is
    /// <see cref="Unbounded"/>, not an invented number.
    /// </summary>
    public enum SchedulerBudgetMode : byte
    {
        /// <summary>Backward-compatible default: all eligible work may run. No hidden time claim.</summary>
        Unbounded = 0,

        /// <summary>One caller-supplied positive microsecond allowance per scheduler frame.</summary>
        Fixed = 1,

        /// <summary>A caller-owned main-thread callback returns the allowance for the current frame.</summary>
        Provider = 2,
    }
}
