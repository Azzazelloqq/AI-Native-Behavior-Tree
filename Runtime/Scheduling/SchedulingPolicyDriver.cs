using System;
using AIBT.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Runtime.Scheduling
{
    /// <summary>
    /// Constructs N independent native tree-instance agents from one already-compiled
    /// <see cref="CompiledProgram"/> and drives them under each of the three accepted Phase 2
    /// fixed execution policies (Immediate, Budgeted, BatchedJobsSameFrame). This file is
    /// intentionally Runtime-only (no <c>AIBT.Authoring</c> dependency): it takes a compiled
    /// program as input rather than compiling one, so it can be shared unchanged between
    /// in-project EditMode harness tests (<c>Tests/Runtime/Benchmarking/</c>, this assembly) and
    /// the isolated Player benchmark project under <c>Benchmarks~/Phase4/Scheduling/</c> (which
    /// copies this file in alongside <c>Runtime/</c> and <c>Authoring/</c>, mirroring
    /// <c>Tools~/Verification/P2/Audit/Get-PublicApi.ps1</c>'s isolated-project technique).
    /// <para>
    /// Every leaf in a driven tree is a "generated leaf" whose Tick status is supplied by the
    /// caller per runtime node index -- this driver never interprets a node's type ID, so it
    /// carries no scenario-specific knowledge and cannot silently reimplement node semantics.
    /// </para>
    /// </summary>
    internal static class SchedulingPolicyDriver
    {
        internal static bool TryCreateAgents(
            CompiledProgram program,
            NativeLifecycleNodeKindV1[] nodeKinds,
            int agentCount,
            Allocator allocator,
            out SchedulingAgent[] agents,
            out NativeRuntimeFailureV1 failure)
        {
            agents = new SchedulingAgent[agentCount];
            for (var index = 0; index < agentCount; index++)
            {
                if (!TryCreateAgent(program, nodeKinds, allocator, out agents[index], out failure))
                {
                    for (var dispose = 0; dispose < index; dispose++) agents[dispose].Dispose();
                    agents = null;
                    return false;
                }
            }

            failure = default;
            return true;
        }

        private static bool TryCreateAgent(
            CompiledProgram program,
            NativeLifecycleNodeKindV1[] nodeKinds,
            Allocator allocator,
            out SchedulingAgent agent,
            out NativeRuntimeFailureV1 failure)
        {
            agent = default;
            var nodeCount = program.Nodes.Count;
            var nodes = new NativeArray<NativeCompiledNodeRecordV1>(nodeCount, allocator);
            var children = new NativeArray<uint>(program.ChildIndices.Count, allocator);
            var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(nodeCount, allocator);
            var memory = new NativeArray<byte>((int)program.Header.InstanceNodeMemorySize, allocator);
            var configuration = new NativeArray<byte>(program.ConfigBlob.Count, allocator);
            var frames = new NativeArray<NativeFrameStateV1>(nodeCount, allocator);
            var generations = new NativeArray<uint>(nodeCount, allocator);
            var control = new NativeArray<NativeLifecycleControlV1>(1, allocator);

            for (var index = 0; index < nodeCount; index++) nodes[index] = new NativeCompiledNodeRecordV1(program.Nodes[index]);
            for (var index = 0; index < program.ChildIndices.Count; index++) children[index] = program.ChildIndices[index];
            for (var index = 0; index < program.ConfigBlob.Count; index++) configuration[index] = program.ConfigBlob[index];

            var parallelCount = 0u;
            var hasCooldown = false;
            for (var index = 0; index < nodeCount; index++)
            {
                bindings[index] = new NativeLifecycleNodeBindingV1((uint)index, nodeKinds[index]);
                if (nodeKinds[index] == NativeLifecycleNodeKindV1.Parallel) parallelCount += (uint)program.Nodes[index].Children.Count;
                if (nodeKinds[index] == NativeLifecycleNodeKindV1.Cooldown) hasCooldown = true;
            }

            var cooldown = new NativeArray<byte>(hasCooldown ? nodeCount : 0, allocator);
            var parallel = new NativeArray<NativeParallelBranchStateV1>((int)parallelCount, allocator);

            if (!NativeLifecycleMachineV1.TryCreate(
                    nodes, children, bindings, memory, frames, generations, control, configuration, cooldown, parallel,
                    out var machine, out failure))
            {
                nodes.Dispose(); children.Dispose(); bindings.Dispose(); memory.Dispose(); configuration.Dispose();
                frames.Dispose(); generations.Dispose(); control.Dispose(); cooldown.Dispose(); parallel.Dispose();
                return false;
            }

            agent = new SchedulingAgent(nodes, children, bindings, memory, configuration, frames, generations, control, cooldown, parallel, machine);
            failure = default;
            return true;
        }

        /// <summary>Drives every agent one full tick (Immediate): begin update, advance to Completed/Waiting, dispatching leaves as they occur.</summary>
        internal static bool TryRunImmediate(
            SchedulingAgent[] agents,
            ulong updateId,
            NodeStatus[] leafStatusByRuntimeIndex,
            out ulong totalSteps,
            out NativeRuntimeFailureV1 failure)
        {
            totalSteps = 0;
            for (var index = 0; index < agents.Length; index++)
            {
                var machine = agents[index].Machine;
                if (!machine.TryBeginUpdate(updateId, out failure)) return false;
                while (true)
                {
                    if (!machine.TryAdvance(out var step, out failure)) return false;
                    totalSteps++;
                    if (!TryHandleStep(ref machine, step, leafStatusByRuntimeIndex, out var done, out var terminal, out failure)) return false;
                    if (terminal.HasValue) agents[index].TerminalResult = terminal;
                    if (done) break;
                }

                agents[index].Machine = machine;
            }

            failure = default;
            return true;
        }

        /// <summary>Drives every agent under a fixed per-tick step budget (Budgeted), resuming a suspended segment on the next call.</summary>
        internal static bool TryRunBudgeted(
            SchedulingAgent[] agents,
            NativeBudgetStateV1[] budgetStates,
            ulong updateId,
            uint stepLimit,
            NodeStatus[] leafStatusByRuntimeIndex,
            out ulong totalSteps,
            out NativeRuntimeFailureV1 failure)
        {
            totalSteps = 0;
            for (var index = 0; index < agents.Length; index++)
            {
                var machine = agents[index].Machine;
                var budgetState = budgetStates[index];
                if (!machine.TryBeginUpdate(updateId, out failure)) return false;
                var tickComplete = false;
                while (!tickComplete)
                {
                    if (!NativeLifecycleBudgetDriverV1.TryBeginSegment(stepLimit, ref budgetState))
                    {
                        failure = default;
                        break;
                    }

                    var segmentActive = true;
                    while (segmentActive)
                    {
                        if (!NativeLifecycleBudgetDriverV1.TryAdvance(ref machine, ref budgetState, out var kind, out var step, out failure)) return false;
                        if (kind == NativeBudgetAdvanceKindV1.Suspended) { segmentActive = false; continue; }
                        totalSteps++;
                        if (!TryHandleStep(ref machine, step, leafStatusByRuntimeIndex, out var done, out var terminal, out failure)) return false;
                        if (terminal.HasValue) agents[index].TerminalResult = terminal;
                        if (done) { tickComplete = true; segmentActive = false; }
                    }
                }

                agents[index].Machine = machine;
                budgetStates[index] = budgetState;
            }

            failure = default;
            return true;
        }

        /// <summary>Drives every agent together as one batch under BatchedJobsSameFrame, completing dispatches on the main thread between batch segments.</summary>
        internal static bool TryRunBatchedJobsSameFrame(
            SchedulingAgent[] agents,
            ulong updateId,
            uint batchSize,
            NodeStatus[] leafStatusByRuntimeIndex,
            Allocator allocator,
            out ulong totalSteps,
            out NativeRuntimeFailureV1 failure)
        {
            totalSteps = 0;
            var machines = new NativeLifecycleMachineV1[agents.Length];
            var laneDone = new bool[agents.Length];
            for (var index = 0; index < agents.Length; index++)
            {
                machines[index] = agents[index].Machine;
                if (!machines[index].TryBeginUpdate(updateId, out failure)) return false;
            }

            if (!NativeBatchedLifecycleOwnerV1.TryCreate(machines, Allocator.Persistent, out var owner, out failure)) return false;

            var results = new NativeArray<NativeLifecycleStepResultV1>(machines.Length, allocator);
            var failures = new NativeArray<NativeRuntimeFailureV1>(machines.Length, allocator);
            try
            {
                var openCount = machines.Length;
                while (openCount > 0)
                {
                    if (!owner.TrySchedule(batchSize, default(JobHandle), out var handle, out failure)) return false;
                    handle.Complete();
                    if (!owner.TryComplete(results, failures, out failure)) return false;

                    for (var index = 0; index < machines.Length; index++)
                    {
                        if (laneDone[index]) continue;
                        var machine = machines[index];
                        totalSteps++;
                        var step = results[index];
                        if (!TryHandleStep(ref machine, step, leafStatusByRuntimeIndex, out var done, out var terminal, out failure)) return false;
                        if (terminal.HasValue) agents[index].TerminalResult = terminal;
                        machines[index] = machine;
                        if (done) { laneDone[index] = true; openCount--; }
                    }
                }
            }
            finally
            {
                results.Dispose();
                failures.Dispose();
                owner.TryDispose(out _);
            }

            for (var index = 0; index < agents.Length; index++) agents[index].Machine = machines[index];
            failure = default;
            return true;
        }

        private static bool TryHandleStep(
            ref NativeLifecycleMachineV1 machine,
            NativeLifecycleStepResultV1 step,
            NodeStatus[] leafStatusByRuntimeIndex,
            out bool done,
            out NodeStatus? terminalResult,
            out NativeRuntimeFailureV1 failure)
        {
            done = false;
            terminalResult = null;
            switch (step.Kind)
            {
                case NativeLifecycleStepKindV1.DispatchRequired:
                    var status = step.Phase == BurstCallbackPhase.Tick ? leafStatusByRuntimeIndex[step.NodeIndex] : NodeStatus.Running;
                    return machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out failure);
                case NativeLifecycleStepKindV1.Completed:
                    done = true;
                    if (step.HasRootStatus) terminalResult = step.RootStatus;
                    failure = default;
                    return true;
                case NativeLifecycleStepKindV1.Waiting:
                    done = true;
                    failure = default;
                    return true;
                default:
                    failure = default;
                    return true;
            }
        }
    }

    /// <summary>One native tree-instance agent's owned allocations, disposed as a unit.</summary>
    internal struct SchedulingAgent
    {
        internal SchedulingAgent(
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> children,
            NativeArray<NativeLifecycleNodeBindingV1> bindings,
            NativeArray<byte> memory,
            NativeArray<byte> configuration,
            NativeArray<NativeFrameStateV1> frames,
            NativeArray<uint> generations,
            NativeArray<NativeLifecycleControlV1> control,
            NativeArray<byte> cooldown,
            NativeArray<NativeParallelBranchStateV1> parallel,
            NativeLifecycleMachineV1 machine)
        {
            _nodes = nodes;
            _children = children;
            _bindings = bindings;
            _memory = memory;
            _configuration = configuration;
            _frames = frames;
            _generations = generations;
            _control = control;
            _cooldown = cooldown;
            _parallel = parallel;
            Machine = machine;
            TerminalResult = null;
        }

        private NativeArray<NativeCompiledNodeRecordV1> _nodes;
        private NativeArray<uint> _children;
        private NativeArray<NativeLifecycleNodeBindingV1> _bindings;
        private NativeArray<byte> _memory;
        private NativeArray<byte> _configuration;
        private NativeArray<NativeFrameStateV1> _frames;
        private NativeArray<uint> _generations;
        private NativeArray<NativeLifecycleControlV1> _control;
        private NativeArray<byte> _cooldown;
        private NativeArray<NativeParallelBranchStateV1> _parallel;

        internal NativeLifecycleMachineV1 Machine;
        internal NodeStatus? TerminalResult;

        internal void Dispose()
        {
            if (_nodes.IsCreated) _nodes.Dispose();
            if (_children.IsCreated) _children.Dispose();
            if (_bindings.IsCreated) _bindings.Dispose();
            if (_memory.IsCreated) _memory.Dispose();
            if (_configuration.IsCreated) _configuration.Dispose();
            if (_frames.IsCreated) _frames.Dispose();
            if (_generations.IsCreated) _generations.Dispose();
            if (_control.IsCreated) _control.Dispose();
            if (_cooldown.IsCreated) _cooldown.Dispose();
            if (_parallel.IsCreated) _parallel.Dispose();
        }
    }
}
