using System;
using AIBT.Burst;

namespace AIBT.Simulation
{
    /// <summary>
    /// Result of every <see cref="TreeSimulation"/> and <see cref="TreeGroup"/> call that can fail at
    /// runtime. No hot-path call throws. Experimental (ADR-P10-008) until stage G5: names and members
    /// may still change.
    /// </summary>
    public enum SimulationResult : byte
    {
        Success = 0,
        /// <summary>Called while <see cref="TreeSimulation.Step"/> runs. Nothing changed.</summary>
        Busy = 1,
        /// <summary>The container or the group was disposed (or the group was removed). Nothing changed.</summary>
        Disposed = 2,
        /// <summary>The handle's slot is free, being torn down, or holds a newer generation; or it belongs to another group.</summary>
        StaleHandle = 3,
        /// <summary>The group's capacity (or the container's group count) would be exceeded. Nothing changed.</summary>
        CapacityExceeded = 4,
        /// <summary><c>Step(tick)</c> with a tick not greater than the previous one. Nothing ran.</summary>
        TickNotIncreasing = 5,
        /// <summary>
        /// An argument is out of range: an interval of 0, a capacity below the current one, a tick whose
        /// time overflows, or a program the container cannot run (see <see cref="TreeSimulation.TryAddGroup"/>).
        /// </summary>
        InvalidArgument = 6,
    }

    /// <summary>
    /// Settings of one <see cref="TreeSimulation"/>, fixed for its lifetime. Experimental (ADR-P10-008)
    /// until stage G5.
    /// </summary>
    public struct TreeSimulationSettings
    {
        /// <summary>
        /// Microseconds per game tick (33 333 for 30 Hz). An agent's update time is
        /// <c>tick × TickMicroseconds</c>, so durations authored in microseconds (Cooldown, Timeout,
        /// wait nodes) take effect at tick granularity. At least 1.
        /// </summary>
        public uint TickMicroseconds;

        /// <summary>
        /// Root seed of every agent's random streams (<c>time-and-random-v1</c>). Each random-capable
        /// node of an agent derives its stream from this seed, the program's semantic hash, the agent
        /// instance's container-local tree instance id and the node index. Same seed and same calls give
        /// the same results.
        /// </summary>
        public ulong RandomSeed;

        /// <summary>
        /// Runs a group's due agents in the catalog's Burst agent-frame job when at least
        /// <see cref="MinimumJobBatch"/> of them are due in one step; otherwise on the calling thread.
        /// Results are identical either way; only time differs. Catalogs generated without the
        /// agent-frame entry always run on the calling thread.
        /// </summary>
        public bool UseJobs;

        /// <summary>Fewest due agents of one group that are worth a job (the P10-007 threshold is 64). At least 1.</summary>
        public int MinimumJobBatch;

        /// <summary>
        /// Lifecycle steps one agent may take in one think. An agent that needs more stops with
        /// <see cref="NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded"/> (reported as a fault).
        /// At least 1.
        /// </summary>
        public uint MaximumStepsPerThink;

        /// <summary>
        /// Jobs on, minimum job batch 64, step limit <see cref="ProductionTreeHost.DefaultMaximumStepsPerUpdate"/>,
        /// random seed 0.
        /// </summary>
        public static TreeSimulationSettings Default(uint tickMicroseconds)
            => new TreeSimulationSettings
            {
                TickMicroseconds = tickMicroseconds,
                RandomSeed = 0,
                UseJobs = true,
                MinimumJobBatch = 64,
                MaximumStepsPerThink = ProductionTreeHost.DefaultMaximumStepsPerUpdate,
            };
    }

    /// <summary>What a <see cref="TreeGroup"/> does when an agent's root finishes. Experimental until G5.</summary>
    public enum RootCompletionPolicy : byte
    {
        /// <summary>Default. The agent restarts as a fresh instance (new instance id, blackboard defaults) at its next think; same handle.</summary>
        Restart = 0,
        /// <summary>The agent keeps its root result and stops thinking until <see cref="TreeGroup.TryRestart"/>.</summary>
        Stop = 1,
    }

    /// <summary>Settings of one <see cref="TreeGroup"/>. Experimental (ADR-P10-008) until G5.</summary>
    public struct TreeGroupSettings
    {
        /// <summary>Agent slots allocated when the group is added. At least 1. Grow with <see cref="TreeGroup.TryReserve"/>.</summary>
        public int Capacity;

        /// <summary>Think interval in ticks (at least 1) of agents added without a schedule; their phase is 0.</summary>
        public uint DefaultThinkInterval;

        public RootCompletionPolicy RootCompletion;

        /// <summary>Capacity <paramref name="capacity"/>, interval 1, <see cref="RootCompletionPolicy.Restart"/>.</summary>
        public static TreeGroupSettings Default(int capacity)
            => new TreeGroupSettings { Capacity = capacity, DefaultThinkInterval = 1u, RootCompletion = RootCompletionPolicy.Restart };
    }

    /// <summary>
    /// Stable identity of one agent of a <see cref="TreeGroup"/>. <see cref="Index"/> is the agent's
    /// slot, stable for the agent's whole life. After removal the slot is reused with a newer
    /// <see cref="Generation"/>, so an old handle is refused (<see cref="SimulationResult.StaleHandle"/>)
    /// instead of addressing the new agent. Generation 0 is never issued: <c>default(AgentHandle)</c>
    /// is null. Experimental (ADR-P10-008) until G5.
    /// </summary>
    public readonly struct AgentHandle : IEquatable<AgentHandle>
    {
        internal AgentHandle(int index, uint generation, ushort group)
        {
            Index = index;
            Generation = generation;
            Group = group;
        }

        public int Index { get; }
        public uint Generation { get; }
        /// <summary>Container-local group id (1 for the first group a container adds); a handle used with another group is refused.</summary>
        public ushort Group { get; }
        public bool IsNull => Generation == 0;

        public bool Equals(AgentHandle other) => Index == other.Index && Generation == other.Generation && Group == other.Group;
        public override bool Equals(object obj) => obj is AgentHandle other && Equals(other);
        public override int GetHashCode() => unchecked((Index * 397) ^ (int)Generation ^ (Group << 20));
        public static bool operator ==(AgentHandle left, AgentHandle right) => left.Equals(right);
        public static bool operator !=(AgentHandle left, AgentHandle right) => !left.Equals(right);
        public override string ToString() => IsNull ? "AgentHandle(null)" : "AgentHandle(" + Group + ":" + Index + "#" + Generation + ")";
    }

    /// <summary>
    /// When an agent thinks: on ticks <c>t</c> with <c>t % Interval == Phase</c>, plus once after each
    /// wake. Interval 1 is every tick. <see cref="Spread"/> lets the group choose the phase
    /// <c>(n % Interval)</c> where <c>n</c> counts the group's spread schedules so far, so N agents
    /// added with one interval are spread evenly. Experimental (ADR-P10-008) until G5.
    /// </summary>
    public readonly struct ThinkSchedule
    {
        /// <summary><paramref name="phase"/> is taken modulo <paramref name="interval"/>; an interval of 0 is refused when used.</summary>
        public ThinkSchedule(uint interval, uint phase)
        {
            Interval = interval;
            Phase = interval == 0 ? 0 : phase % interval;
            IsSpread = false;
        }

        private ThinkSchedule(uint interval)
        {
            Interval = interval;
            Phase = 0;
            IsSpread = true;
        }

        public static ThinkSchedule Every(uint interval, uint phase) => new ThinkSchedule(interval, phase);
        public static ThinkSchedule Spread(uint interval) => new ThinkSchedule(interval);

        public uint Interval { get; }
        /// <summary>The phase; 0 for a spread schedule (the group assigns the real phase).</summary>
        public uint Phase { get; }
        public bool IsSpread { get; }
    }

    /// <summary>One agent whose root finished in the last step. Experimental until G5.</summary>
    public readonly struct RootCompletion
    {
        internal RootCompletion(AgentHandle agent, NodeStatus result)
        {
            Agent = agent;
            Result = result;
        }

        public AgentHandle Agent { get; }
        /// <summary>Success or Failure.</summary>
        public NodeStatus Result { get; }
    }

    /// <summary>
    /// One agent that stopped with a runtime failure in the last step. The agent stays alive but does
    /// not think until <see cref="TreeGroup.TryRestart"/> or <see cref="TreeGroup.TryRemove"/>.
    /// The failure's owner id is cleared: it comes from a process-wide counter and would make
    /// results depend on what ran earlier in the process. Experimental until G5.
    /// </summary>
    public readonly struct AgentFault
    {
        internal AgentFault(AgentHandle agent, NativeRuntimeFailureV1 failure)
        {
            Agent = agent;
            Failure = failure;
        }

        public AgentHandle Agent { get; }
        public NativeRuntimeFailureV1 Failure { get; }
    }
}
