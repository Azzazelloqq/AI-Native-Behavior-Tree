using System;
using System.Collections.Generic;

namespace AIBT.Simulation
{
    /// <summary>
    /// One simulation world owned by the game: groups of agents, each group running one
    /// <see cref="ProductionTreeProgram"/>, advanced only by <see cref="Step"/>. No GameObject, component
    /// or Unity <c>Update</c> is involved, so it runs the same in EditMode, Play Mode and a server.
    /// <para>
    /// Threading: call every member from one thread at a time and never while <see cref="Step"/> runs
    /// (such calls return <see cref="SimulationResult.Busy"/>). <see cref="Step"/> completes every job it
    /// schedules before returning; nothing of the trees runs between steps. Several containers may exist
    /// side by side; they share no state that affects results.
    /// </para>
    /// <para>
    /// Determinism: results depend only on the programs, the settings, the sequence of calls with their
    /// arguments and the ticks passed, not on wall-clock time, job worker count, <see cref="TreeSimulationSettings.UseJobs"/>,
    /// <see cref="TreeSimulationSettings.MinimumJobBatch"/>, other containers, or what ran before in the process.
    /// Tree instance ids come from a container counter in the order agents are added or restarted.
    /// </para>
    /// <para>
    /// Experimental (ADR-P10-008): stage G1 runs trees whose generated nodes use blackboard keys only;
    /// game inputs, commands and external actions arrive in later stages. Effects a node emits are
    /// discarded. Names and members may change until stage G5.
    /// </para>
    /// </summary>
    public sealed class TreeSimulation : IDisposable
    {
        private readonly List<TreeGroup> _groups = new List<TreeGroup>();
        private readonly TreeSimulationSettings _settings;
        private ulong _nextTreeInstanceId;
        private ushort _nextGroupId;
        private uint _lastTick;
        private bool _hasStepped;
        private bool _stepping;
        private bool _disposed;
        private bool _disposeAfterStep;

        /// <summary>Test hook: runs inside <see cref="Step"/> after teardown, before any think.</summary>
        internal Action StepProbeForTests;

        /// <exception cref="ArgumentOutOfRangeException">TickMicroseconds, MinimumJobBatch or MaximumStepsPerThink is 0.</exception>
        public TreeSimulation(in TreeSimulationSettings settings)
        {
            if (settings.TickMicroseconds == 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "TickMicroseconds must be at least 1.");
            if (settings.MinimumJobBatch < 1)
                throw new ArgumentOutOfRangeException(nameof(settings), "MinimumJobBatch must be at least 1.");
            if (settings.MaximumStepsPerThink == 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "MaximumStepsPerThink must be at least 1.");
            _settings = settings;
        }

        public TreeSimulationSettings Settings => _settings;
        /// <summary>The tick of the last successful <see cref="Step"/>; 0 before the first.</summary>
        public uint LastTick => _lastTick;
        public bool IsStepping => _stepping;
        /// <summary>Groups not yet released (a removed group counts until the step after its teardown).</summary>
        public int GroupCount => _groups.Count;
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Adds a group of agents that all run <paramref name="program"/> and allocates its
        /// <see cref="TreeGroupSettings.Capacity"/> agent slots now (never during <see cref="Step"/>).
        /// The program must outlive the group; it refuses disposal while the group holds it.
        /// Returns <see cref="SimulationResult.InvalidArgument"/> for a capacity below 1, an interval of
        /// 0, or a program the container cannot run (a generated node with a binding other than
        /// Tree/Agent-scope blackboard keys or effects); <see cref="SimulationResult.CapacityExceeded"/>
        /// after 65 535 groups.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="program"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The container is disposed.</exception>
        public SimulationResult TryAddGroup(ProductionTreeProgram program, in TreeGroupSettings settings, out TreeGroup group)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (_disposed) throw new ObjectDisposedException(nameof(TreeSimulation));
            group = null;
            if (_stepping) return SimulationResult.Busy;
            if (settings.Capacity < 1 || settings.DefaultThinkInterval == 0
                || (settings.RootCompletion != RootCompletionPolicy.Restart && settings.RootCompletion != RootCompletionPolicy.Stop))
                return SimulationResult.InvalidArgument;
            if (settings.CommandQuotaPerAgent < 0 || settings.ActionCapacityPerAgent < 0) return SimulationResult.InvalidArgument;
            if (_nextGroupId == ushort.MaxValue) return SimulationResult.CapacityExceeded;
            var mapped = SimulationProgramMapV1.TryBuild(program, out var map);
            if (mapped != SimulationResult.Success) return mapped;
            var created = new TreeGroup(this, (ushort)(_nextGroupId + 1), program, settings, map);
            var result = created.TryReserveCore(settings.Capacity);
            if (result != SimulationResult.Success)
            {
                created.Release();
                return result;
            }
            _nextGroupId++;
            _groups.Add(created);
            group = created;
            return SimulationResult.Success;
        }

        /// <summary>
        /// Removes every agent of <paramref name="group"/>. Their teardown (abort with
        /// <c>TreeStopped</c>) runs in the next <see cref="Step"/>; the step after that releases the
        /// group's storage and disposes it. Calls on the group return <see cref="SimulationResult.Disposed"/>
        /// from now on.
        /// </summary>
        public SimulationResult TryRemoveGroup(TreeGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (_disposed) return SimulationResult.Disposed;
            if (_stepping) return SimulationResult.Busy;
            if (group.Simulation != this || !_groups.Contains(group)) return SimulationResult.InvalidArgument;
            if (group.RemovalState != TreeGroup.Removal.None) return SimulationResult.Disposed;
            group.MarkRemoved();
            return SimulationResult.Success;
        }

        /// <summary>
        /// Advances the world to <paramref name="tick"/>. Order inside one call:
        /// 1. every group (creation order) tears down agents removed or restarted since the last step
        ///    (abort with <c>TreeStopped</c>), and groups removed one step earlier are released;
        /// 2. per group in creation order, the due agents (schedule due on this tick, or woken) think in
        ///    ascending slot index, each one update at time <c>tick × TickMicroseconds</c>, in a job or
        ///    on this thread;
        /// 3. root completions and faults are recorded in slot order and stay readable until the next step;
        /// 4. (P10-011) every group's action events of this step are collected in slot order (teardown
        ///    events of removed groups included) and stay readable until the next step.
        /// Completions the game gave with <see cref="TreeGroup.TryComplete(ActionHandle, ActionOutcome)"/>
        /// since the last step were applied when called; each woke its agent for this step.
        /// Returns <see cref="SimulationResult.TickNotIncreasing"/> (tick not greater than the last one),
        /// <see cref="SimulationResult.InvalidArgument"/> (time overflows a signed 64-bit microsecond
        /// count), <see cref="SimulationResult.Busy"/> or <see cref="SimulationResult.Disposed"/> without
        /// running anything. A failing agent never fails the step: it is reported in
        /// <see cref="TreeGroup.GetFaults"/>. Allocates no managed memory after warm-up and logs nothing.
        /// </summary>
        public SimulationResult Step(uint tick)
        {
            if (_disposed) return SimulationResult.Disposed;
            if (_stepping) return SimulationResult.Busy;
            if (_hasStepped && tick <= _lastTick) return SimulationResult.TickNotIncreasing;
            var time = (ulong)tick * _settings.TickMicroseconds;
            if (time > long.MaxValue) return SimulationResult.InvalidArgument;
            for (var index = 0; index < _groups.Count; index++)
            {
                var group = _groups[index];
                if (group.RemovalState != TreeGroup.Removal.None) continue;
                var inputs = group.ValidateInputs();
                if (inputs != SimulationResult.Success) return inputs;
            }
            _stepping = true;
            try
            {
                for (var index = 0; index < _groups.Count; index++)
                    if (_groups[index].RemovalState != TreeGroup.Removal.TornDown) _groups[index].BeginStep();
                for (var index = 0; index < _groups.Count; index++)
                {
                    var group = _groups[index];
                    if (group.RemovalState == TreeGroup.Removal.TornDown)
                    {
                        group.Release();
                        _groups.RemoveAt(index);
                        index--;
                        continue;
                    }
                    group.RunTeardown();
                }
                StepProbeForTests?.Invoke();
                for (var index = 0; index < _groups.Count; index++)
                {
                    var group = _groups[index];
                    if (group.RemovalState != TreeGroup.Removal.None) continue;
                    group.Think(tick, (long)time);
                    group.CollectCommands();
                }
                // P10-011: action events of every group this step touched, removed groups' teardowns included.
                for (var index = 0; index < _groups.Count; index++) _groups[index].CollectActionEvents();
                _lastTick = tick;
                _hasStepped = true;
                return SimulationResult.Success;
            }
            finally
            {
                _stepping = false;
                if (_disposeAfterStep) Dispose();
            }
        }

        /// <summary>
        /// The next container-local tree instance id: 1, 2, ... in the order agents are added or
        /// restarted. Seeds random streams and names instances; never a process-wide value.
        /// </summary>
        internal TreeInstanceId NextTreeInstanceId()
        {
            _nextTreeInstanceId++;
            return new TreeInstanceId(_nextTreeInstanceId);
        }

        internal bool IsUsable => !_disposed && !_stepping;

        /// <summary>
        /// Releases every group's native storage. Agents are not torn down (no abort runs, since
        /// nothing runs outside <see cref="Step"/>); remove groups and step once first when aborts matter.
        /// Programs are not disposed (the game owns them). Called during a step (from a test hook),
        /// disposal happens when the step returns.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            if (_stepping)
            {
                _disposeAfterStep = true;
                return;
            }
            _disposed = true;
            for (var index = 0; index < _groups.Count; index++) _groups[index].Release();
            _groups.Clear();
        }
    }
}
