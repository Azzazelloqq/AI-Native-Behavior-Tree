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

        // ---- P10-010: inputs and commands ---------------------------------------------------------
        private sealed class InputState
        {
            internal NativeArray<byte> Game;       // the bound game array as bytes (not owned)
            internal Type RowType;
            internal int Stride;
            internal bool Bound;
            internal NativeArray<byte> Staging;    // owned copy of the game rows, made once per step
            internal bool Wake;
            internal NativeArray<byte> LastSeen;   // owned: canonical row each agent saw at its last think
            internal NativeArray<byte> Seen;       // owned: 1 once the agent thought with wake on
        }

        private abstract class CommandCacheBase : IDisposable
        {
            internal bool Decoded;
            internal uint Stamp;
            public abstract void Dispose();
        }

        private sealed class CommandCache<T> : CommandCacheBase where T : unmanaged
        {
            internal NativeList<Command<T>> List;
            internal NativeArray<SimulationLeafV1> Leaves;
            internal NativeArray<byte> Scratch;

            public override void Dispose()
            {
                if (List.IsCreated) List.Dispose();
                if (Leaves.IsCreated) Leaves.Dispose();
                if (Scratch.IsCreated) Scratch.Dispose();
            }
        }

        private readonly SimulationProgramMapV1 _map;
        private readonly int _quota;
        private readonly InputState[] _inputStates;
        private readonly CommandCacheBase[] _commandCaches;
        private readonly System.Collections.Generic.List<SimulationLeafV1> _leafScratch = new System.Collections.Generic.List<SimulationLeafV1>();
        // Fixed for the group's life: one entry per input channel, and every channel's leaf table.
        private UnsafeList<SimulationInputChannelV1> _inputs;
        private UnsafeList<SimulationLeafV1> _leaves;
        private UnsafeList<SimulationAgentNativeV1> _simAgents;
        private NativeList<SimulationEmitterV1> _emitters;
        private NativeArray<byte> _wakeScratch;
        private bool _anyWake;
        private uint _stepStamp;

        internal TreeGroup(
            TreeSimulation simulation, ushort id, ProductionTreeProgram program, in TreeGroupSettings settings,
            SimulationProgramMapV1 map)
        {
            _simulation = simulation;
            Id = id;
            _program = program;
            _settings = settings;
            _map = map;
            _quota = settings.CommandQuotaPerAgent == 0 ? TreeGroupSettings.DefaultCommandQuotaPerAgent : settings.CommandQuotaPerAgent;
            _semanticHash = new NativeHash256V1(program.Program.Header.CanonicalSemanticHash);
            _agentFrameExecutor = program.Catalog.Executor as IGeneratedAgentFrameExecutorV1;
            _rootCompletions = new NativeList<RootCompletion>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _faults = new NativeList<AgentFault>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _table = new UnsafeList<AgentFrameAgentV1>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _jobAgents = new UnsafeList<AgentFrameAgentV1>(Math.Max(1, settings.Capacity), Allocator.Persistent);

            _inputStates = new InputState[map.Inputs.Count];
            _commandCaches = new CommandCacheBase[map.Commands.Count];
            _inputs = new UnsafeList<SimulationInputChannelV1>(Math.Max(1, map.Inputs.Count), Allocator.Persistent);
            var leafCount = 0;
            var largest = 1u;
            for (var channel = 0; channel < map.Inputs.Count; channel++)
            {
                var input = map.Inputs[channel];
                _inputStates[channel] = new InputState();
                _inputs.Add(new SimulationInputChannelV1
                {
                    LeafStart = leafCount, LeafCount = input.LeafCount, CanonicalSize = input.CanonicalSize,
                });
                leafCount += input.LeafCount;
                if (input.CanonicalSize > largest) largest = input.CanonicalSize;
            }
            _leaves = new UnsafeList<SimulationLeafV1>(Math.Max(1, leafCount), Allocator.Persistent);
            _leaves.Resize(leafCount, NativeArrayOptions.ClearMemory);
            _simAgents = new UnsafeList<SimulationAgentNativeV1>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _emitters = new NativeList<SimulationEmitterV1>(Math.Max(1, settings.Capacity), Allocator.Persistent);
            _wakeScratch = new NativeArray<byte>((int)largest, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        /// <summary>The container state of slot <paramref name="index"/> (its in-place dispatch reaches it through the bridge).</summary>
        internal ref SimulationAgentNativeV1 SimulationAgent(int index) => ref _simAgents.ElementAt(index);

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
                            new TreeInstanceId(ulong.MaxValue - (ulong)created), false,
                            _map, new SimulationAgentBridgeV1(this, created), out _))
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
            if (_emitters.Capacity < capacity) _emitters.Capacity = capacity;
            GrowSimulationStorage(capacity);
            return SimulationResult.Success;
        }

        /// <summary>
        /// P10-010: per-slot container entries (command quota counters and a command log sized for the
        /// quota of one think plus one teardown, so a step never grows them) and per-channel row
        /// buffers for the new capacity.
        /// </summary>
        private void GrowSimulationStorage(int capacity)
        {
            var commandChannels = _map.Commands.Count;
            var payloadBytes = 0;
            for (var channel = 0; channel < commandChannels; channel++)
                payloadBytes += (int)_map.Commands[channel].CanonicalSize;
            if (_simAgents.Capacity < capacity) _simAgents.Capacity = capacity;
            while (_simAgents.Length < capacity)
            {
                var entry = new SimulationAgentNativeV1
                {
                    Inputs = _inputs,
                    Leaves = _leaves,
                    Row = _simAgents.Length,
                    CommandFree = new UnsafeList<int>(Math.Max(1, commandChannels), Allocator.Persistent),
                    Commands = new UnsafeList<SimulationCommandRecordV1>(Math.Max(1, 2 * _quota * commandChannels), Allocator.Persistent),
                    Payload = new UnsafeList<byte>(Math.Max(1, 2 * _quota * payloadBytes), Allocator.Persistent),
                };
                entry.CommandFree.Resize(commandChannels, NativeArrayOptions.ClearMemory);
                _simAgents.Add(entry);
            }
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (state.Bound) EnsureStaging(channel, state.Stride);
                if (state.Wake) EnsureWakeStorage(channel);
            }
        }

        private void EnsureStaging(int channel, int stride)
        {
            var state = _inputStates[channel];
            var bytes = checked(_slots.Length * stride);
            if (!state.Staging.IsCreated || state.Staging.Length != bytes)
            {
                var staging = new NativeArray<byte>(bytes, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                if (state.Staging.IsCreated) state.Staging.Dispose();
                state.Staging = staging;
            }
            ref var input = ref _inputs.ElementAt(channel);
            input.Rows = state.Staging;
            input.RowStride = stride;
            input.RowCount = _slots.Length;
        }

        private void EnsureWakeStorage(int channel)
        {
            var state = _inputStates[channel];
            var size = (int)_map.Inputs[channel].CanonicalSize;
            var rows = _slots.Length;
            if (state.Seen.IsCreated && state.Seen.Length == rows) return;
            var lastSeen = new NativeArray<byte>(rows * size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var seen = new NativeArray<byte>(rows, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (state.Seen.IsCreated)
            {
                NativeArray<byte>.Copy(state.LastSeen, lastSeen, Math.Min(state.LastSeen.Length, lastSeen.Length));
                NativeArray<byte>.Copy(state.Seen, seen, Math.Min(state.Seen.Length, seen.Length));
                state.LastSeen.Dispose();
                state.Seen.Dispose();
            }
            state.LastSeen = lastSeen;
            state.Seen = seen;
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
                ForgetSeenInputs(index);
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
                    ResetCommandQuota(index);
                    TearDown(slot.Core);
                    slot.State = SlotState.Free;
                    slot.Woken = false;
                    slot.ResetAtNextThink = false;
                    slot.RestartRequested = false;
                }
                else if (slot.State == SlotState.Alive && slot.RestartRequested)
                {
                    ResetCommandQuota(index);
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
                if (!slot.Woken && tick % slot.Interval != slot.Phase)
                {
                    if (!_anyWake || !InputChanged(index)) continue;
                    WokenByInputForTests++;
                }
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
                ResetCommandQuota(index);
                if (_anyWake) RecordSeenInputs(index);
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
                    RecordFault(index, ref slot, core.LastFailure, core.LastRejectedResult);
                    continue;
                }
                if (core.LastRootResult.HasValue)
                {
                    _rootCompletions.Add(new RootCompletion(new AgentHandle(index, slot.Generation, Id), core.LastRootResult.Value));
                    if (_settings.RootCompletion == RootCompletionPolicy.Restart) slot.ResetAtNextThink = true;
                }
            }
        }

        private void RecordFault(
            int index, ref Slot slot, NativeRuntimeFailureV1 failure, BurstContextResult cause = BurstContextResult.Success)
        {
            // The owner id is a process-wide counter value; it must not reach an output.
            var reported = new NativeRuntimeFailureV1(
                failure.Code, failure.ResourceKind, failure.Requested, failure.Capacity, failure.Alignment,
                0, failure.Generation, failure.LeaseId);
            _faults.Add(new AgentFault(new AgentHandle(index, slot.Generation, Id), reported, cause));
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
                entry.Simulation = _simAgents[index];
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
                // The job appended to copies of the agent's command lists: keep their lengths.
                _simAgents.ElementAt(index) = agent.Simulation;
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
            if (_simAgents.IsCreated)
            {
                for (var index = 0; index < _simAgents.Length; index++)
                {
                    ref var entry = ref _simAgents.ElementAt(index);
                    if (entry.CommandFree.IsCreated) entry.CommandFree.Dispose();
                    if (entry.Commands.IsCreated) entry.Commands.Dispose();
                    if (entry.Payload.IsCreated) entry.Payload.Dispose();
                }
                _simAgents.Dispose();
            }
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (state.Staging.IsCreated) state.Staging.Dispose();
                if (state.LastSeen.IsCreated) state.LastSeen.Dispose();
                if (state.Seen.IsCreated) state.Seen.Dispose();
                state.Game = default;
                state.Bound = false;
            }
            for (var channel = 0; channel < _commandCaches.Length; channel++)
            {
                _commandCaches[channel]?.Dispose();
                _commandCaches[channel] = null;
            }
            if (_inputs.IsCreated) _inputs.Dispose();
            if (_leaves.IsCreated) _leaves.Dispose();
            if (_emitters.IsCreated) _emitters.Dispose();
            if (_wakeScratch.IsCreated) _wakeScratch.Dispose();
        }

        private static void ReleaseCore(ProductionTreeAgentCore core)
        {
            if (core == null || !core.BeginDispose()) return;
            core.ReleaseStorage();
        }

        // ---- P10-010: inputs ----------------------------------------------------------------------

        /// <summary>
        /// Binds the game array <paramref name="rows"/> to the input channel: row <c>i</c> is what the
        /// agent in slot <c>i</c> (<see cref="AgentHandle.Index"/>) reads through its
        /// <c>SnapshotReadHandle&lt;T&gt;</c>. The group keeps the array (it does not copy or own it): write
        /// it between steps; every <see cref="TreeSimulation.Step"/> copies its first
        /// <see cref="HighWaterIndex"/> rows once, before any agent runs, so every agent of a step sees the
        /// same values and the game may write the array again as soon as Step returns. Keep it alive and
        /// at least <see cref="Capacity"/> rows long while bound; rebind after growing the group.
        /// Binding again replaces the array. Returns <see cref="SimulationResult.UnknownChannel"/> when no
        /// node of the program reads the channel, <see cref="SimulationResult.TypeMismatch"/> when
        /// <typeparamref name="T"/> is not the value type its nodes declare (type id, version and every
        /// field), <see cref="SimulationResult.InvalidArgument"/> for an array that is not created or
        /// shorter than <see cref="Capacity"/>.
        /// </summary>
        public SimulationResult TryBindInput<T>(InputChannel<T> channel, NativeArray<T> rows) where T : unmanaged
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!channel.IsValid) return SimulationResult.InvalidArgument;
            var index = _map.FindInput(channel.Id);
            if (index < 0) return SimulationResult.UnknownChannel;
            if (!rows.IsCreated || rows.Length < _slots.Length) return SimulationResult.InvalidArgument;
            var state = _inputStates[index];
            var stride = UnsafeUtility.SizeOf<T>();
            if (state.RowType != typeof(T))
            {
                var layout = SimulationValueLayoutV1.TryBuild<T>(_program.Catalog, _map.Inputs[index].Binding, _leafScratch);
                if (layout != SimulationResult.Success) return layout;
                var input = _inputs[index];
                if (_leafScratch.Count != input.LeafCount) return SimulationResult.TypeMismatch;
                for (var leaf = 0; leaf < _leafScratch.Count; leaf++) _leaves[input.LeafStart + leaf] = _leafScratch[leaf];
                state.RowType = typeof(T);
            }
            state.Stride = stride;
            state.Game = rows.Reinterpret<byte>(stride);
            state.Bound = true;
            EnsureStaging(index, stride);
            _inputs.ElementAt(index).Bound = 1;
            return SimulationResult.Success;
        }

        /// <summary>Forgets the bound array. Steps return <see cref="SimulationResult.InputMissing"/> until the channel is bound again.</summary>
        public SimulationResult TryUnbindInput<T>(InputChannel<T> channel) where T : unmanaged
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!channel.IsValid) return SimulationResult.InvalidArgument;
            var index = _map.FindInput(channel.Id);
            if (index < 0) return SimulationResult.UnknownChannel;
            var state = _inputStates[index];
            state.Game = default;
            state.Bound = false;
            _inputs.ElementAt(index).Bound = 0;
            return SimulationResult.Success;
        }

        /// <summary>
        /// Off by default. When on, an agent that is not due in a step thinks anyway when its row of this
        /// channel differs from the row it read at its last think (byte compare of the canonical value:
        /// -0.0 equals +0.0, padding ignored). Agents that have not thought since this was turned on, or
        /// since they were added, are not woken. Allocates the per-agent last-seen rows now.
        /// </summary>
        public SimulationResult TrySetWakeOnChange<T>(InputChannel<T> channel, bool enabled) where T : unmanaged
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!channel.IsValid) return SimulationResult.InvalidArgument;
            var index = _map.FindInput(channel.Id);
            if (index < 0) return SimulationResult.UnknownChannel;
            var state = _inputStates[index];
            if (enabled && !state.Wake)
            {
                EnsureWakeStorage(index);
                for (var slot = 0; slot < state.Seen.Length; slot++) state.Seen[slot] = 0;
            }
            state.Wake = enabled;
            _anyWake = false;
            for (var other = 0; other < _inputStates.Length; other++) _anyWake |= _inputStates[other].Wake;
            return SimulationResult.Success;
        }

        /// <summary>
        /// Step phase 0 (before anything runs, for every active group): every input channel bound to a
        /// readable array of at least <see cref="Capacity"/> rows.
        /// </summary>
        internal SimulationResult ValidateInputs()
        {
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (!state.Bound) return SimulationResult.InputMissing;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                try
                {
                    AtomicSafetyHandle.CheckReadAndThrow(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(state.Game));
                }
                catch (Exception)
                {
                    return SimulationResult.InputMissing;
                }
#endif
                if (state.Game.Length < _slots.Length * state.Stride) return SimulationResult.InvalidArgument;
            }
            return SimulationResult.Success;
        }

        /// <summary>
        /// Step phase 1 start: forgets last step's commands and copies every bound input's rows into the
        /// group's own buffer (the only copy a step makes; see <see cref="TryBindInput{T}"/>).
        /// </summary>
        internal void BeginStep()
        {
            unchecked { _stepStamp++; }
            for (var position = 0; position < _emitters.Length; position++)
            {
                ref var entry = ref _simAgents.ElementAt(_emitters[position].Index);
                entry.Commands.Clear();
                entry.Payload.Clear();
            }
            _emitters.Clear();
            if (_removal != Removal.None) return;
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (!state.Bound || _highWater == 0) continue;
                NativeArray<byte>.Copy(state.Game, 0, state.Staging, 0, _highWater * state.Stride);
            }
        }

        private void ResetCommandQuota(int index)
        {
            ref var entry = ref _simAgents.ElementAt(index);
            for (var channel = 0; channel < entry.CommandFree.Length; channel++) entry.CommandFree[channel] = _quota;
        }

        private void ForgetSeenInputs(int index)
        {
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (state.Seen.IsCreated && index < state.Seen.Length) state.Seen[index] = 0;
            }
        }

        private void RecordSeenInputs(int index)
        {
            ref var entry = ref _simAgents.ElementAt(index);
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (!state.Wake) continue;
                var size = _inputs[channel].CanonicalSize;
                SimulationAgentRoutinesV1.TryStage(ref entry, channel, state.LastSeen, (uint)index * size, size);
                state.Seen[index] = 1;
            }
        }

        private bool InputChanged(int index)
        {
            ref var entry = ref _simAgents.ElementAt(index);
            for (var channel = 0; channel < _inputStates.Length; channel++)
            {
                var state = _inputStates[channel];
                if (!state.Wake || state.Seen[index] == 0) continue;
                var size = _inputs[channel].CanonicalSize;
                SimulationAgentRoutinesV1.TryStage(ref entry, channel, _wakeScratch, 0u, size);
                var start = (int)((uint)index * size);
                for (var octet = 0; octet < (int)size; octet++)
                    if (_wakeScratch[octet] != state.LastSeen[start + octet]) return true;
            }
            return false;
        }

        // ---- P10-010: commands --------------------------------------------------------------------

        /// <summary>Step phase 3: the agents that emitted commands in this step, in slot order.</summary>
        internal void CollectCommands()
        {
            for (var index = 0; index < _highWater; index++)
                if (_simAgents[index].Commands.Length != 0)
                    _emitters.Add(new SimulationEmitterV1 { Index = index, Generation = _slots[index].Generation });
        }

        /// <summary>
        /// The commands of <paramref name="channel"/> agents emitted in the last step: agents in slot order,
        /// each agent's commands in emission order (a torn-down agent's teardown commands before a
        /// restarted agent's think). Valid until the next step, the group's release or the container's
        /// disposal. Returns <see cref="SimulationResult.UnknownChannel"/> when no node emits the channel,
        /// <see cref="SimulationResult.TypeMismatch"/> when <typeparamref name="T"/> is not its payload type,
        /// <see cref="SimulationResult.CatalogTooOld"/> when the catalog has no binding ids (commands are
        /// discarded). The first call per channel builds its decoder (allocates); later calls allocate nothing.
        /// </summary>
        public SimulationResult TryGetCommands<T>(CommandChannel<T> channel, out NativeArray<Command<T>>.ReadOnly commands)
            where T : unmanaged
        {
            commands = default;
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!channel.IsValid) return SimulationResult.InvalidArgument;
            var index = _map.FindCommand(channel.Id);
            if (index < 0) return _map.CommandsWithoutIds ? SimulationResult.CatalogTooOld : SimulationResult.UnknownChannel;
            var cache = _commandCaches[index] as CommandCache<T>;
            if (cache == null)
            {
                var layout = SimulationValueLayoutV1.TryBuild<T>(_program.Catalog, _map.Commands[index].Binding, _leafScratch);
                if (layout != SimulationResult.Success) return layout;
                if (_leafScratch.Count != _map.Commands[index].LeafCount) return SimulationResult.TypeMismatch;
                cache = new CommandCache<T>
                {
                    List = new NativeList<Command<T>>(Math.Max(1, _slots.Length), Allocator.Persistent),
                    Leaves = new NativeArray<SimulationLeafV1>(_leafScratch.ToArray(), Allocator.Persistent),
                    Scratch = new NativeArray<byte>(UnsafeUtility.SizeOf<T>(), Allocator.Persistent),
                };
                _commandCaches[index]?.Dispose();
                _commandCaches[index] = cache;
            }
            if (!cache.Decoded || cache.Stamp != _stepStamp) Decode(index, cache);
            commands = cache.List.AsReadOnly();
            return SimulationResult.Success;
        }

        /// <summary>Same as <see cref="TryGetCommands{T}"/>; empty when that call does not succeed.</summary>
        public NativeArray<Command<T>>.ReadOnly GetCommands<T>(CommandChannel<T> channel) where T : unmanaged
            => TryGetCommands(channel, out var commands) == SimulationResult.Success ? commands : default;

        private void Decode<T>(int channel, CommandCache<T> cache) where T : unmanaged
        {
            cache.List.Clear();
            var expected = _map.Commands[channel].CanonicalSize;
            for (var position = 0; position < _emitters.Length; position++)
            {
                var emitter = _emitters[position];
                ref var entry = ref _simAgents.ElementAt(emitter.Index);
                var handle = new AgentHandle(emitter.Index, emitter.Generation, Id);
                for (var record = 0; record < entry.Commands.Length; record++)
                {
                    var command = entry.Commands[record];
                    if (command.Channel != channel || command.PayloadSize != expected) continue;
                    for (var octet = 0; octet < cache.Scratch.Length; octet++) cache.Scratch[octet] = 0;
                    for (var leafIndex = 0; leafIndex < cache.Leaves.Length; leafIndex++)
                    {
                        var leaf = cache.Leaves[leafIndex];
                        for (var octet = 0; octet < (int)leaf.Size; octet++)
                            cache.Scratch[(int)leaf.RawOffset + octet] = entry.Payload[(int)(command.PayloadOffset + leaf.CanonicalOffset) + octet];
                    }
                    cache.List.Add(new Command<T>(handle, cache.Scratch.Reinterpret<T>(1)[0]));
                }
            }
            cache.Stamp = _stepStamp;
            cache.Decoded = true;
        }

        // ---- test access ------------------------------------------------------------------------

        internal ProductionTreeAgentCore CoreForTests(int index) => _slots[index].Core;
        internal uint GenerationForTests(int index) => _slots[index].Generation;
        internal bool IsSlotAliveForTests(int index) => (uint)index < (uint)_slots.Length && _slots[index].State == SlotState.Alive;
        internal bool JobPathAvailable => _jobPathAvailable;
        /// <summary>Test diagnostics: agent-frame jobs this group completed.</summary>
        internal long JobBatchesForTests;
        /// <summary>Test diagnostics: thinks caused by wake on changed input.</summary>
        internal long WokenByInputForTests;
    }
}
