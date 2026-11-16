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
        /// <summary>
        /// P10-004, opt-in only (never chosen automatically): one Burst job work item runs one
        /// agent's whole update, node bodies included; the main thread only begins updates,
        /// schedules and completes one job per group, and drains traces and effects in agent order.
        /// Results equal <see cref="Immediate"/> frame by frame. A host the job cannot run (no
        /// generated catalog, a catalog generated before P10-004, a step budget, shared-scope writes
        /// or async/managed leaves) runs as <see cref="Immediate"/> and its frame entry reports an
        /// <see cref="AgentFrameFallbackReason"/>. Not part of the internal Auto-selection set.
        /// </summary>
        JobsAgentFrame = 4,
    }

    /// <summary>
    /// P10-004: why a host whose profile forces <see cref="SchedulingPolicy.JobsAgentFrame"/> ran as
    /// <see cref="SchedulingPolicy.Immediate"/> instead (<see cref="SchedulerFrameEntry.AgentFrameFallback"/>).
    /// </summary>
    public enum AgentFrameFallbackReason : byte
    {
        /// <summary>The host ran in the agent-frame job (or the policy was not requested).</summary>
        None = 0,
        /// <summary>The host dispatches through a caller-supplied managed delegate, not a generated catalog.</summary>
        ManagedDispatch = 1,
        /// <summary>The catalog was generated before P10-004 and has no agent-frame entry; regenerate it.</summary>
        CatalogWithoutAgentFrameEntry = 2,
        /// <summary>The tree writes Shared or world-scope blackboard values (their same-frame visibility needs agent order).</summary>
        SharedOrWorldScopeWrite = 3,
        /// <summary>The tree has an async-operation, completion or other non-job binding.</summary>
        AsyncOrManagedLeaf = 4,
        /// <summary>The host has a <see cref="ProductionTreeHost.StepBudget"/>, whose suspension needs the main thread.</summary>
        StepBudgetConfigured = 5,
    }
}
