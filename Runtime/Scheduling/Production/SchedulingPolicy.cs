namespace AIBT
{
    /// <summary>
    /// The concrete execution policies a caller may force through
    /// <see cref="SchedulingProfile.ForcedPolicy"/>. Mirrors the internal Auto-selection policy set
    /// 1:1 (<c>Documentation~/execution-and-scheduling.md</c>'s policy table). There is no
    /// <c>Auto</c> value here -- Auto is expressed by leaving <see cref="SchedulingProfile.ForcedPolicy"/>
    /// unset, matching how the native selector already treats "Auto" as the absence of a forced
    /// policy rather than a fifth policy value.
    /// </summary>
    public enum SchedulingPolicy : byte
    {
        Immediate = 0,
        Budgeted = 1,
        BatchedJobsSameFrame = 2,
        PipelinedJobs = 3,
    }
}
