using System;
using System.Threading;
using AIBT.Burst;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Applies <c>ADR-P7-010</c> to production: a real component driving one compiled tree
    /// instance's lifecycle every frame during actual Play mode -- the missing piece every prior
    /// debugger/preview tool (<c>P3-009</c>, <c>P3-010</c>, <c>P3-011</c>, <c>P6-008</c>, <c>P6-012</c>)
    /// disclosed and worked around with a self-driven or benchmark-driven substitute.
    /// <para>
    /// Reuses <see cref="SchedulingPolicyDriver"/>'s own already-tested agent construction
    /// (<c>TryCreateAgents</c>) and disposal, but does not reuse its <c>TryRunImmediate</c>/
    /// <c>TryRunBudgeted</c> driving loops -- those require every leaf's status to be supplied by
    /// the caller *in advance* (a benchmark-only shape; see their own doc comments), which cannot
    /// drive a real running tree whose leaves compute their own outcome. This host instead drives
    /// <see cref="NativeLifecycleMachineV1.TryAdvance"/>/<c>TryCompleteDispatch</c> directly,
    /// mirroring <c>SchedulingPolicyDriver.TryHandleStep</c>'s own exact per-step handling but
    /// resolving a real Tick's status through <see cref="DispatchLeaf"/> -- a delegate supplied by
    /// whoever builds this host, on demand, at the exact moment a dispatch is due. This keeps the
    /// host itself free of any <c>AIBT.Authoring</c> dependency (matching the ADR's own reasoning
    /// that a shipped game needs only <c>AIBT.Runtime</c> plus an already-compiled program): the
    /// real per-project leaf dispatch table (a generated <c>GenericNativeDispatchTranslatorV1</c>,
    /// <c>P7-009</c>) is resolved by the caller that constructs this host, never by the host itself.
    /// </para>
    /// <para>
    /// Scope, per the ADR's own decision 3: <c>Immediate</c>/<c>Budgeted</c> only. A single
    /// per-<see cref="GameObject"/> host cannot itself perform <c>BatchedJobsSameFrame</c>/
    /// <c>PipelinedJobs</c>'s population-wide batch dispatch -- a population-level coordinator for
    /// those policies is explicit, disclosed future work, not attempted here.
    /// </para>
    /// </summary>
    public sealed class ProductionTreeHost : MonoBehaviour
    {
        /// <summary>
        /// Resolves one leaf's real Tick status. Called only when a real Tick is due
        /// (<see cref="BurstCallbackPhase.Tick"/>), never for Enter/Exit/Abort callbacks, which this
        /// host always answers with <see cref="NodeStatus.Running"/> -- matching
        /// <c>SchedulingPolicyDriver.TryHandleStep</c>'s own exact behavior for those phases.
        /// </summary>
        public delegate NodeStatus DispatchLeaf(uint runtimeNodeIndex);

        private static long s_nextTreeInstanceId;

        private SchedulingAgent[] _agents;
        private DispatchLeaf _dispatch;
        private NativeTraceRecorderV1 _recorder;
        private ulong _updateId;
        private bool _ready;

        /// <summary>The host's own trace channel, owned for this tree instance's whole lifetime -- the exact "caller-owned session" shape <c>NativeExecutionDebuggerSession.Attach</c> already expects, unmodified.</summary>
        public NativeTraceChannelOwnerV1 TraceChannelOwner { get; private set; }

        /// <summary>The most recently observed root status, or <c>null</c> if the tree has never reached a terminal status.</summary>
        public NodeStatus? LastRootResult { get; private set; }

        public ulong TotalUpdates => _updateId;

        /// <summary>
        /// Constructs the native agent and trace channel for <paramref name="program"/>. Must be
        /// called once, before this component starts ticking (typically from <c>Awake()</c> on a
        /// caller-owned subclass, or immediately after <c>AddComponent</c>) -- mirrors the ADR's own
        /// spike-proven construct-in-<c>Awake</c>/dispose-in-<c>OnDestroy</c> lifecycle.
        /// </summary>
        public bool TryBootstrap(
            CompiledProgram program,
            DispatchLeaf dispatch,
            NativeTraceChannelCapacityV1 traceCapacity,
            out NativeRuntimeFailureV1 failure)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

            // NativeLifecycleNodeKindV1 is internal to AIBT.Runtime, so it cannot appear in this
            // public method's own signature -- derived here instead, mirroring
            // NativeHotReloadInstance.TryBuild's own exact pattern (ClassifyKind per compiled node).
            var nodeKinds = new NativeLifecycleNodeKindV1[program.Nodes.Count];
            for (var index = 0; index < nodeKinds.Length; index++)
            {
                nodeKinds[index] = NativeHotReloadInstance.ClassifyKind(program.Nodes[index].NodeTypeId);
            }

            if (!SchedulingPolicyDriver.TryCreateAgents(program, nodeKinds, agentCount: 1, Allocator.Persistent, out _agents, out failure))
            {
                return false;
            }

            var treeInstanceId = (ulong)Interlocked.Increment(ref s_nextTreeInstanceId);
            if (!NativeTraceChannelOwnerV1.TryCreate(traceCapacity, NativeTraceLevelV1.Lifecycle, new TreeInstanceId(treeInstanceId), workerOrdinal: 0, Allocator.Persistent, out var owner, out var traceFailure))
            {
                failure = new NativeRuntimeFailureV1(traceFailure.Code);
                _agents[0].Dispose();
                _agents = null;
                return false;
            }

            TraceChannelOwner = owner;
            _recorder = new NativeTraceRecorderV1(owner, new NativeHash256V1(program.Header.CompiledContentHash), treeInstanceId);
            _ready = true;
            failure = default;
            return true;
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            _updateId++;
            var machine = _agents[0].Machine;
            if (!machine.TryBeginUpdate(_updateId, out var failure))
            {
                Debug.LogError("AIBT ProductionTreeHost: TryBeginUpdate failed: " + failure.Code);
                _agents[0].Machine = machine;
                return;
            }

            _recorder.RecordUpdateStarted(_updateId);
            var lastStep = default(NativeLifecycleStepResultV1);
            var running = true;
            while (running)
            {
                if (!machine.TryAdvance(out var step, out failure))
                {
                    Debug.LogError("AIBT ProductionTreeHost: TryAdvance failed: " + failure.Code);
                    break;
                }

                _recorder.RecordStep(_updateId, step);

                switch (step.Kind)
                {
                    case NativeLifecycleStepKindV1.DispatchRequired:
                        var status = step.Phase == BurstCallbackPhase.Tick ? _dispatch(step.NodeIndex) : NodeStatus.Running;
                        if (!machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out failure))
                        {
                            Debug.LogError("AIBT ProductionTreeHost: TryCompleteDispatch failed: " + failure.Code);
                            running = false;
                            break;
                        }

                        _recorder.RecordDispatchCompletion(_updateId, step, status);
                        break;
                    case NativeLifecycleStepKindV1.Completed:
                        if (step.HasRootStatus)
                        {
                            LastRootResult = step.RootStatus;
                        }

                        running = false;
                        break;
                    case NativeLifecycleStepKindV1.Waiting:
                        running = false;
                        break;
                }

                lastStep = step;
            }

            _recorder.RecordUpdateEnded(
                _updateId,
                lastStep.Kind == NativeLifecycleStepKindV1.Completed && lastStep.HasRootStatus,
                lastStep.HasRootStatus ? lastStep.RootStatus : default);

            _agents[0].Machine = machine;
        }

        private void OnDestroy()
        {
            _ready = false;
            if (TraceChannelOwner != null && TraceChannelOwner.State != NativeOwnerStateV1.Disposed)
            {
                TraceChannelOwner.TryDispose(out _);
            }

            if (_agents != null)
            {
                _agents[0].Dispose();
                _agents = null;
            }
        }
    }
}
