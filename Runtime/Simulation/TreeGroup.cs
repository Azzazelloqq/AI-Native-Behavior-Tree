using System;
using AIBT.AgentFrame;
using AIBT.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace AIBT.Simulation
{
    /// <summary>
    /// N agents of one <see cref="ProductionTreeProgram"/> inside one <see cref="TreeSimulation"/>.
    /// Agents live in pooled slots allocated up front (<see cref="Capacity"/>), so a steady population
    /// allocates nothing after warm-up; an agent is a slot index plus a generation
    /// (<see cref="AgentHandle"/>). Owned by its container; same threading rules (one thread, never
    /// during <see cref="TreeSimulation.Step"/>). Experimental (ADR-P10-008) until stage G5.
    /// </summary>
    public sealed class TreeGroup
    {
        internal enum Removal : byte { None = 0, Pending = 1, TornDown = 2 }

        private enum SlotState : byte { Free = 0, Alive = 1, PendingRemoval = 2, Retired = 3 }

        private struct Slot
        {
            internal ProductionTreeAgentCore Core;
            internal SlotState State;
            internal uint Generation;
            internal uint Interval;
            internal uint Phase;
            internal bool Woken;
            // Root completed under RootCompletionPolicy.Restart: reset to a fresh instance at the next think.
            internal bool ResetAtNextThink;
            // TryRestart was called: tear down (if running) at the next step, then reset at the next think.
            internal bool RestartRequested;
            // The persistent agent-frame table entry holds this slot's storage handles.
            internal bool TableReady;
        }

        private readonly TreeSimulation _simulation;
        private readonly ProductionTreeProgram _program;
        private readonly TreeGroupSettings _settings;
        private readonly NativeHash256V1 _semanticHash;
        private readonly IGeneratedAgentFrameExecutorV1 _agentFrameExecutor;
        private Slot[] _slots = Array.Empty<Slot>();
        private int[] _due = Array.Empty<int>();
        // Persistent JobsAgentFrame table: one entry per slot, storage handles written once per slot.
        private UnsafeList<AgentFrameAgentV1> _table;
        // The due agents of one step, copied from the table so the job sees them contiguously.
        private UnsafeList<AgentFrameAgentV1> _jobAgents;
        private NativeList<RootCompletion> _rootCompletions;
        private NativeList<AgentFault> _faults;
        private bool _jobPathAvailable;
        private uint _spreadCounter;
        private int _count;
        private int _highWater;
        private Removal _removal;
        private bool _released;

        internal TreeGroup(TreeSimulation simulation, ushort id, ProductionTreeProgram program, in TreeGroupSettings settings)
        {
            _simulation = simulation;
            Id = id;
            _program = program;
            _settings = settings;
            _semanticHash = new NativeHash256V1(program.Program.Header.CanonicalSemanticHash);
            _agentFrameExecutor = program.Catalog.Executor as IGeneratedAgentFrameExecutorV1;
            _rootCompletions = new NativeList<RootCompletion>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _faults = new NativeList<AgentFault>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _table = new UnsafeList<AgentFrameAgentV1>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _jobAgents = new UnsafeList<AgentFrameAgentV1>(Math.Max(1, settings.Capacity), Allocator.Persistent);
        }

        internal TreeSimulation Simulation => _simulation;
        internal Removal RemovalState => _removal;

        /// <summary>Container-local id, the <see cref="AgentHandle.Group"/> of this group's handles.</summary>
        public ushort Id { get; }
        public ProductionTreeProgram Program => _program;
        public TreeGroupSettings Settings => _settings;
        /// <summary>Allocated agent slots.</summary>
        public int Capacity => _slots.Length;
        /// <summary>Live agents (removed agents leave the count at once).</summary>
        public int Count => _count;
        /// <summary>One past the highest slot index ever used.</summary>
        public int HighWaterIndex => _highWater;
        /// <summary>False once the group was removed from its container or the container was disposed.</summary>
        public bool IsValid => !_released && _removal == Removal.None && !_simulation.IsDisposed;

        private SimulationResult Usable()
        {
            if (_released || _removal != Removal.None || _simulation.IsDisposed) return SimulationResult.Disposed;
            if (_simulation.IsStepping) return SimulationResult.Busy;
            return SimulationResult.Success;
        }

        /// <summary>
        /// Grows the group to <paramref name="capacity"/> slots, allocating every new agent's native
        /// storage now (never during a step). Existing handles and indices are kept. A capacity at or
        /// below the current one changes nothing and succeeds.
        /// </summary>
        public SimulationResult TryReserve(int capacity)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (capacity < 1) return SimulationResult.InvalidArgument;
            return TryReserveCore(capacity);
        }

        internal SimulationResult TryReserveCore(int capacity)
        {
            if (capacity <= _slots.Length) return SimulationResult.Success;
            var settings = _simulation.Settings;
            var slots = new Slot[capacity];
            Array.Copy(_slots, slots, _slots.Length);
            var created = _slots.Length;
            try
            {
                for (; created < capacity; created++)
                {
                    var core = new ProductionTreeAgentCore(null) { MaximumStepsPerUpdate = settings.MaximumStepsPerThink };
                    // Placeholder identity until the slot is first used: TryAdd resets every agent to a
                    // fresh instance with a container-local id.
                    if (!core.TryBootstrapGenerated(
                            _program.Definition, _program.Catalog, null,
                            new TreeInstanceId(ulong.MaxValue - (ulong)created), false, out _))
                        return SimulationResult.InvalidArgument;
                    slots[created].Core = core;
                }
            }
            finally
            {
                if (created < capacity)
                {
                    for (var index = _slots.Length; index < created; index++) ReleaseCore(slots[index].Core);
                }
            }
            if (_slots.Length == 0)
                _jobPathAvailable = _agentFrameExecutor != null
                    && slots[0].Core.GetAgentFrameFallback() == AgentFrameFallbackReason.None;
            _slots = slots;
            if (_due.Length < capacity) _due = new int[capacity];
            if (_table.Capacity < capacity) _table.Capacity = capacity;
            while (_table.Length < capacity) _table.Add(default);
            if (_jobAgents.Capacity < capacity) _jobAgents.Capacity = capacity;
            if (_rootCompletions.Capacity < capacity) _rootCompletions.Capacity = capacity;
            if (_faults.Capacity < capacity) _faults.Capacity = capacity;
            return SimulationResult.Success;
        }

        /// <summary>Adds an agent with <see cref="TreeGroupSettings.DefaultThinkInterval"/> and phase 0.</summary>
        public SimulationResult TryAdd(out AgentHandle agent)
            => TryAdd(ThinkSchedule.Every(_settings.DefaultThinkInterval, 0), out agent);

        /// <summary>
        /// Adds an agent in the lowest free slot as a fresh instance (next container instance id,
        /// blackboard defaults, random streams seeded). Its first think is the first step whose tick
        /// matches its schedule (or the next step if woken). Nothing of the tree runs in this call.
        /// </summary>
        public SimulationResult TryAdd(in ThinkSchedule schedule, out AgentHandle agent)
        {
            agent = default;
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (schedule.Interval == 0) return SimulationResult.InvalidArgument;
            for (var index = 0; index < _slots.Length; index++)
            {
                ref var slot = ref _slots[index];
                if (slot.State != SlotState.Free) continue;
                if (slot.Generation == uint.MaxValue)
                {
                    // Generations are never reissued: a slot that used them all is retired.
                    slot.State = SlotState.Retired;
                    continue;
                }
                if (!TryResetToFreshInstance(ref slot)) return SimulationResult.InvalidArgument;
                slot.Generation++;
                slot.State = SlotState.Alive;
                ApplySchedule(ref slot, schedule);
                slot.Woken = false;
                slot.ResetAtNextThink = false;
                slot.RestartRequested = false;
                _count++;
                if (index + 1 > _highWater) _highWater = index + 1;
                agent = new AgentHandle(index, slot.Generation, Id);
                return SimulationResult.Success;
            }
            return SimulationResult.CapacityExceeded;
        }

        /// <summary>
        /// Removes the agent: <see cref="IsAlive"/> is false at once and the handle is stale. Its
        /// teardown (abort with <c>TreeStopped</c>) runs at the start of the next step; the slot is
        /// reusable after that step.
        /// </summary>
        public SimulationResult TryRemove(AgentHandle agent)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            _slots[index].State = SlotState.PendingRemoval;
            _count--;
            return SimulationResult.Success;
        }

        public bool IsAlive(AgentHandle agent) => !_released && TryResolve(agent, out _);

        /// <summary>
        /// Restarts a stopped, faulted or running agent as a fresh instance at its next think (a
        /// running agent is torn down at the start of the next step first). The handle is kept.
        /// </summary>
        public SimulationResult TryRestart(AgentHandle agent)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            _slots[index].RestartRequested = true;
            return SimulationResult.Success;
        }

        /// <summary>Changes when the agent thinks, from the next step on. A spread schedule takes the group's next spread phase.</summary>
        public SimulationResult TrySetThinkSchedule(AgentHandle agent, in ThinkSchedule schedule)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (schedule.Interval == 0) return SimulationResult.InvalidArgument;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            ApplySchedule(ref _slots[index], schedule);
            return SimulationResult.Success;
        }

        /// <summary>The agent thinks in the next step even if not due (once; its phase does not move).</summary>
        public SimulationResult TryWake(AgentHandle agent)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            _slots[index].Woken = true;
            return SimulationResult.Success;
        }

        /// <summary>
        /// Wakes agents by slot index for the next step. Generations are not checked: indices outside
        /// the group or of slots without a live agent are ignored.
        /// </summary>
        public SimulationResult TryWakeMany(NativeArray<int>.ReadOnly indices)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            for (var position = 0; position < indices.Length; position++)
            {
                var index = indices[position];
                if ((uint)index < (uint)_slots.Length && _slots[index].State == SlotState.Alive)
                    _slots[index].Woken = true;
            }
            return SimulationResult.Success;
        }

        /// <summary>
        /// Agents whose root finished in the last step, in slot order. Valid until the next step, the
        /// group's release or the container's disposal; read it before then. Empty when none.
        /// </summary>
        public NativeArray<RootCompletion>.ReadOnly GetRootCompletions()
            => _rootCompletions.IsCreated ? _rootCompletions.AsReadOnly() : default;

        /// <summary>Agents that faulted in the last step, in slot order. Same validity as <see cref="GetRootCompletions"/>.</summary>
        public NativeArray<AgentFault>.ReadOnly GetFaults()
            => _faults.IsCreated ? _faults.AsReadOnly() : default;

        private bool TryResolve(AgentHandle agent, out int index)
        {
            index = agent.Index;
            return agent.Generation != 0 && agent.Group == Id
                && (uint)index < (uint)_slots.Length
                && _slots[index].State == SlotState.Alive
                && _slots[index].Generation == agent.Generation;
        }

        private void ApplySchedule(ref Slot slot, in ThinkSchedule schedule)
        {
            slot.Interval = schedule.Interval;
            slot.Phase = schedule.IsSpread ? _spreadCounter++ % schedule.Interval : schedule.Phase % schedule.Interval;
        }

        private bool TryResetToFreshInstance(ref Slot slot)
        {
            var id = _simulation.NextTreeInstanceId();
            if (!slot.Core.TryResetInstance(id, out _)) return false;
            slot.Core.GeneratedDispatch.SeedRandomStreams(_simulation.Settings.RandomSeed, _semanticHash);
            return true;
        }

        internal void MarkRemoved()
        {
            _removal = Removal.Pending;
            for (var index = 0; index < _slots.Length; index++)
                if (_slots[index].State == SlotState.Alive) _slots[index].State = SlotState.PendingRemoval;
            _count = 0;
        }

        /// <summary>Step phase 1: clears last step's outputs and tears down removed and restarting agents.</summary>
        internal void RunTeardown()
        {
            _rootCompletions.Clear();
            _faults.Clear();
            for (var index = 0; index < _slots.Length; index++)
            {
                ref var slot = ref _slots[index];
                if (slot.State == SlotState.PendingRemoval)
                {
                    TearDown(slot.Core);
                    slot.State = SlotState.Free;
                    slot.Woken = false;
                    slot.ResetAtNextThink = false;
                    slot.RestartRequested = false;
                }
                else if (slot.State == SlotState.Alive && slot.RestartRequested)
                {
                    TearDown(slot.Core);
                    slot.RestartRequested = false;
                    slot.ResetAtNextThink = true;
                }
            }
            if (_removal == Removal.Pending) _removal = Removal.TornDown;
        }

        private static void TearDown(ProductionTreeAgentCore core)
        {
            try
            {
                core.RunTeardown();
            }
            catch (Exception)
            {
                core.Fail(ProductionTreeAgentCore.InvalidLifetime(), ProductionTreeAgentFailureDetail.Text);
            }
        }

        /// <summary>Step phase 2: every due agent of this group thinks once at <paramref name="time"/>.</summary>
        internal void Think(uint tick, long time)
        {
            var dueCount = 0;
            for (var index = 0; index < _highWater; index++)
            {
                ref var slot = ref _slots[index];
                if (slot.State != SlotState.Alive) continue;
                if (!slot.Woken && tick % slot.Interval != slot.Phase) continue;
                slot.Woken = false;
                if (slot.ResetAtNextThink)
                {
                    slot.ResetAtNextThink = false;
                    if (!TryResetToFreshInstance(ref slot))
                    {
                        RecordFault(index, ref slot, ProductionTreeAgentCore.InvalidLifetime());
                        continue;
                    }
                }
                var core = slot.Core;
                if (!core.Ready) continue; // stopped: faulted, or finished under RootCompletionPolicy.Stop
                if (!core.UpdateOpen && !core.TryBeginUpdateAt(time))
                {
                    RecordFault(index, ref slot, core.LastFailure);
                    continue;
                }
                _due[dueCount++] = index;
            }
            if (dueCount == 0) return;

            var settings = _simulation.Settings;
            if (settings.UseJobs && _jobPathAvailable && dueCount >= settings.MinimumJobBatch)
                RunJob(dueCount);
            else
                RunInPlace(dueCount);

            for (var position = 0; position < dueCount; position++)
            {
                var index = _due[position];
                ref var slot = ref _slots[index];
                var core = slot.Core;
                if (core.Ready) continue; // Waiting: thinks again on its schedule.
                if (core.LastFailure.Code != NativeRuntimeDiagnosticCodeV1.None)
                {
                    RecordFault(index, ref slot, core.LastFailure);
                    continue;
                }
                if (core.LastRootResult.HasValue)
                {
                    _rootCompletions.Add(new RootCompletion(new AgentHandle(index, slot.Generation, Id), core.LastRootResult.Value));
                    if (_settings.RootCompletion == RootCompletionPolicy.Restart) slot.ResetAtNextThink = true;
                }
            }
        }

        private void RecordFault(int index, ref Slot slot, NativeRuntimeFailureV1 failure)
        {
            // The owner id is a process-wide counter value; it must not reach an output.
            var reported = new NativeRuntimeFailureV1(
                failure.Code, failure.ResourceKind, failure.Requested, failure.Capacity, failure.Alignment,
                0, failure.Generation, failure.LeaseId);
            _faults.Add(new AgentFault(new AgentHandle(index, slot.Generation, Id), reported));
        }

        private void RunInPlace(int dueCount)
        {
            for (var position = 0; position < dueCount; position++)
            {
                var core = _slots[_due[position]].Core;
                try
                {
                    core.RunSegment(null, false);
                }
                catch (Exception)
                {
                    core.Fail(ProductionTreeAgentCore.InvalidLifetime(), ProductionTreeAgentFailureDetail.Text);
                }
            }
        }

        private void RunJob(int dueCount)
        {
            _jobAgents.Clear();
            for (var position = 0; position < dueCount; position++)
            {
                var index = _due[position];
                ref var slot = ref _slots[index];
                ref var entry = ref _table.ElementAt(index);
                if (!slot.TableReady)
                {
                    slot.Core.FillAgentFrame(ref entry);
                    slot.TableReady = true;
                }
                else slot.Core.PrepareAgentFrame(ref entry);
                _jobAgents.Add(entry);
            }
            try
            {
                var workers = JobsUtility.JobWorkerCount;
                var innerBatch = Math.Max(1, Math.Min(64, dueCount / (4 * (Math.Max(0, workers) + 1))));
                _agentFrameExecutor.ScheduleAgentFrame(new AgentFrameJobDataV1 { Agents = _jobAgents }, dueCount, innerBatch, default)
                    .Complete();
                JobBatchesForTests++;
            }
            catch (Exception)
            {
                // Nothing ran: every agent of this batch stops with a structured failure.
                for (var position = 0; position < dueCount; position++)
                    _slots[_due[position]].Core.Fail(ProductionTreeAgentCore.InvalidLifetime(), ProductionTreeAgentFailureDetail.AgentFrameNotRun);
                return;
            }
            for (var position = 0; position < dueCount; position++)
            {
                var index = _due[position];
                ref var agent = ref _jobAgents.ElementAt(position);
                var core = _slots[index].Core;
                try
                {
                    core.FinishAgentFrame(ref agent);
                }
                catch (Exception)
                {
                    core.Fail(ProductionTreeAgentCore.InvalidLifetime(), ProductionTreeAgentFailureDetail.Text);
                }
                agent.Outcome = AgentFrameOutcomeV1.None;
                // Keep the (possibly grown) log lists in the persistent entry.
                _table.ElementAt(index) = agent;
            }
        }

        /// <summary>Releases every agent's native storage (no teardown runs) and the output buffers.</summary>
        internal void Release()
        {
            if (_released) return;
            _released = true;
            for (var index = 0; index < _slots.Length; index++) ReleaseCore(_slots[index].Core);
            _slots = Array.Empty<Slot>();
            _count = 0;
            if (_table.IsCreated) _table.Dispose();
            if (_jobAgents.IsCreated) _jobAgents.Dispose();
            if (_rootCompletions.IsCreated) _rootCompletions.Dispose();
            if (_faults.IsCreated) _faults.Dispose();
        }

        private static void ReleaseCore(ProductionTreeAgentCore core)
        {
            if (core == null || !core.BeginDispose()) return;
            core.ReleaseStorage();
        }

        // ---- test access ------------------------------------------------------------------------

        internal ProductionTreeAgentCore CoreForTests(int index) => _slots[index].Core;
        internal uint GenerationForTests(int index) => _slots[index].Generation;
        internal bool IsSlotAliveForTests(int index) => (uint)index < (uint)_slots.Length && _slots[index].State == SlotState.Alive;
        internal bool JobPathAvailable => _jobPathAvailable;
        /// <summary>Test diagnostics: agent-frame jobs this group completed.</summary>
        internal long JobBatchesForTests;
    }
}
