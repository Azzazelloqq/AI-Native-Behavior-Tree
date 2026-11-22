using System;
using System.Threading;
using AIBT.Burst;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Drives one native tree in Unity Update with an optional per-frame step budget.
    /// Completion stops execution; disabling pauses; destruction cancels active work.
    /// Generated-node dispatch adapters remain caller-owned.
    /// </summary>
    public sealed class ProductionTreeHost : MonoBehaviour
    {
        /// <summary>Legacy Tick-only adapter. Use DispatchLifecycle for Actions with lifecycle effects.</summary>
        public delegate NodeStatus DispatchLeaf(uint runtimeNodeIndex);
        /// <summary>Executes a complete callback; status is consumed only for Tick.</summary>
        public delegate BurstContextResult DispatchLifecycle(in DispatchRequest request, out NodeStatus status);

        /// <summary>Callback identity and frozen update inputs. Reasons apply to their matching phase.</summary>
        public readonly struct DispatchRequest
        {
            internal DispatchRequest(NativeLifecycleStepResultV1 step, ulong updateId, long timeMicroseconds)
            {
                NodeIndex = step.NodeIndex;
                Phase = step.Phase;
                UpdateId = updateId;
                TimeMicroseconds = timeMicroseconds;
                ExitReason = step.ExitReason;
                AbortReason = step.AbortReason;
            }
            public uint NodeIndex { get; }
            public BurstCallbackPhase Phase { get; }
            public ulong UpdateId { get; }
            public long TimeMicroseconds { get; }
            public BurstNodeExitReason ExitReason { get; }
            public BurstNodeAbortReason AbortReason { get; }
        }

        private static long s_nextTreeInstanceId;
        // P10-009: the MonoBehaviour-free native instance and its update bookkeeping. The host keeps
        // Unity's object model, the clock, the drive lock and the scheduler registration.
        private readonly ProductionTreeAgentCore _core;
        private Func<long> _clock;
        private bool _driving;
        private ProductionTreeScheduler _owner;

        public ProductionTreeHost() => _core = new ProductionTreeAgentCore(this);

        /// <summary>
        /// P10-004: default for <see cref="MaximumStepsPerUpdate"/>. The largest tree measured in the
        /// package's tests and benchmarks needs a few hundred lifecycle steps per update (see
        /// <c>Documentation~/performance.md</c>), so one million only stops a tree that never yields.
        /// </summary>
        public const uint DefaultMaximumStepsPerUpdate = 1000000u;

        /// <summary>
        /// P10-004: the most lifecycle steps one logical update may take under Immediate,
        /// BatchedJobsSameFrame and JobsAgentFrame execution. A tree that needs more (for example a
        /// repeater over a subtree that completes in the same update) stops this host with
        /// <see cref="NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded"/> instead of
        /// looping forever. Budgeted hosts (<see cref="StepBudget"/>) and PipelinedJobs are not
        /// limited by it: they already yield between frames. Must be at least 1.
        /// </summary>
        public uint MaximumStepsPerUpdate
        {
            get => _core.MaximumStepsPerUpdate;
            set
            {
                if (value == 0u) throw new ArgumentOutOfRangeException(nameof(value), "At least one step per update is required.");
                _core.MaximumStepsPerUpdate = value;
            }
        }

        /// <summary>P10-004: set once the scheduler has logged this host's JobsAgentFrame fallback.</summary>
        internal bool AgentFrameFallbackReported { get; set; }

        /// <summary>Lifecycle steps taken by the current (or last) logical update.</summary>
        internal ulong StepsThisUpdate => _core.StepsThisUpdate;

        internal void FailStepLimit() => _core.FailStepLimit();

        /// <summary>The instance-owned trace channel, available until destruction.</summary>
        public NativeTraceChannelOwnerV1 TraceChannelOwner => _core.TraceChannelOwner;
        /// <summary>The exact immutable program used by this host, including its source debug map.</summary>
        public CompiledProgram Program => _core.Program;
        /// <summary>Terminal root result, retained without automatic restart.</summary>
        public NodeStatus? LastRootResult => _core.LastRootResult;
        /// <summary>The failure that stopped this host; None before any failure.</summary>
        public NativeRuntimeFailureV1 LastFailure => _core.LastFailure;
        /// <summary>Logical updates started, excluding budget-resume frames.</summary>
        public ulong TotalUpdates => _core.TotalUpdates;
        /// <summary>Stable per-instance identity assigned at bootstrap; 0 before bootstrap. Used for deterministic coordinator ordering (<see cref="ProductionTreeScheduler"/>) -- never reused across instances within a process.</summary>
        public ulong InstanceId => _core.InstanceId;

        /// <summary>
        /// P11-002: root seed used when a bootstrap does not pass one (0, as
        /// <c>TreeSimulationSettings.Default</c>).
        /// </summary>
        public const ulong DefaultRandomSeed = 0UL;

        /// <summary>
        /// P11-002: root seed of this host's random streams, fixed at generated bootstrap
        /// (<see cref="DefaultRandomSeed"/> unless the bootstrap passed one). Every node that declares
        /// a random stream derives it from this seed, the program's semantic hash,
        /// <see cref="InstanceId"/> and its runtime node index (<c>time-and-random-v1</c>).
        /// </summary>
        public ulong RandomSeed { get; private set; }

        /// <summary>Null selects Immediate; otherwise limits native steps per frame. Zero pauses progress.</summary>
        public uint? StepBudget
        {
            get => _core.StepBudget;
            set => _core.StepBudget = value;
        }

        /// <summary>
        /// Configures bounded public effect delivery before generated bootstrap. Omit this call to
        /// preserve the existing production behavior where generated effect commands have no Unity
        /// receiver. Capacity is fixed after bootstrap and must cover the largest command burst a
        /// consumer intends to leave undrained.
        /// </summary>
        public bool TryConfigureEffectDelivery(int capacity, out NativeRuntimeFailureV1 failure)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            return _core.TryConfigureEffectDelivery(capacity, _driving, out failure);
        }

        /// <summary>True only at the P7-039-safe boundary where external Tree facts may be flushed.</summary>
        public bool CanAcceptExternalTreeWrites
            => _core.Bootstrapped && _core.GeneratedDispatch != null && _core.Ready && !_core.Disposed && !_core.DestroyRequested && !_driving;

        // The scheduler-owned world bridge must not turn a normal cross-frame PipelinedJobs lock
        // into a host failure. Unlike external writes, a terminal host may still have committed
        // effects to drain, so this deliberately does not require _ready.
        internal bool CanDrainEffects
            => _core.Bootstrapped && _core.GeneratedDispatch != null && !_core.Disposed && !_core.DestroyRequested && !_driving;

        /// <summary>
        /// Delivers buffered generated effects in deterministic command order. This main-thread
        /// operation is refused while the host is executing; receiver exceptions remain caller
        /// failures and leave the current effect pending for explicit recovery.
        /// </summary>
        public bool TryDrainEffects(IProductionTreeEffectReceiver receiver, out NativeRuntimeFailureV1 failure)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (!CanDrainEffects)
            {
                failure = InvalidLifetime();
                return false;
            }
            var effectQueue = _core.EffectQueue;
            if (effectQueue == null)
            {
                failure = default;
                return true;
            }
            try
            {
                while (effectQueue.TryPeek(out var effect))
                {
                    receiver.Receive(in effect);
                    effectQueue.RemoveHead();
                }
                failure = default;
                return true;
            }
            catch (Exception)
            {
                failure = InvalidLifetime();
                return false;
            }
        }

        /// <summary>
        /// This host's own generated-catalog dispatch bridge, if bootstrapped through the generated
        /// overload; null for the legacy delegate-based overloads. A <see cref="ProductionTreeScheduler"/>
        /// group wave uses this to fold this host's own pending request into a shared batch -- never
        /// exposed for any other purpose.
        /// </summary>
        internal GeneratedTreeDispatchAdapterV2 GeneratedDispatchAdapter => _core.GeneratedDispatch;

        /// <summary>
        /// This host's own machine value -- a bundle of <c>NativeArray</c> handles, not a snapshot;
        /// any copy observes the same underlying native state. For a
        /// <see cref="ProductionTreeScheduler"/> group driver to schedule this host's own
        /// <c>TryAdvance</c> as part of a shared <c>NativeBatchedLifecycleOwnerV1</c> Job, between
        /// a <see cref="TryBeginBatchedRound"/> and this host's next barrier.
        /// </summary>
        internal NativeLifecycleMachineV1 Machine => _core.Machine;

        /// <summary>
        /// Editor live-debug read of this host's active node path (runtime node indices, root
        /// first). Returns false, leaving <paramref name="into"/> empty, while the host is driving,
        /// pipelined, stopped or not bootstrapped. Never changes execution state.
        /// </summary>
        internal bool TryCopyActiveNodes(System.Collections.Generic.List<uint> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (!_core.Bootstrapped || _core.Disposed || _driving || !_core.HasAgents) return false;
            _core.Machine.CopyActivePath(into);
            return true;
        }

        /// <summary>Bootstraps a Tick-only integration with scaled Unity time and no-op lifecycle callbacks.</summary>
        public bool TryBootstrap(CompiledProgram program, DispatchLeaf dispatch,
            NativeTraceChannelCapacityV1 traceCapacity, out NativeRuntimeFailureV1 failure)
        {
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            BurstContextResult Adapter(in DispatchRequest request, out NodeStatus status)
            {
                status = request.Phase == BurstCallbackPhase.Tick ? dispatch(request.NodeIndex) : NodeStatus.Running;
                return BurstContextResult.Success;
            }
            return TryBootstrap(program, Adapter, traceCapacity, null, out failure);
        }

        /// <summary>
        /// Bootstraps once with full lifecycle dispatch. A null clock uses scaled Unity time.
        /// Custom clocks return nonnegative, nondecreasing microseconds, read once per logical
        /// update. Clock and dispatch adapters execute synchronously on the main thread.
        /// </summary>
        public bool TryBootstrap(CompiledProgram program, DispatchLifecycle dispatch,
            NativeTraceChannelCapacityV1 traceCapacity, Func<long> clock, out NativeRuntimeFailureV1 failure)
        {
            var treeInstanceId = new TreeInstanceId((ulong)Interlocked.Increment(ref s_nextTreeInstanceId));
            if (!_core.TryBootstrapCore(program, dispatch, traceCapacity, treeInstanceId, _driving, out failure))
                return false;
            _clock = clock ?? ReadScaledTime;
            return true;
        }

        /// <summary>
        /// Bootstraps production-owned generated dispatch. The catalog supplies every callback
        /// and layout; no caller-authored dispatch delegate or byte offset is accepted.
        /// </summary>
        public bool TryBootstrap(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            NativeTraceChannelCapacityV1 traceCapacity,
            Func<long> clock,
            out NativeRuntimeFailureV1 failure)
            => TryBootstrap(definition, catalog, traceCapacity, clock, DefaultRandomSeed, out failure);

        /// <summary>
        /// P11-002: the generated bootstrap with a root seed for the random streams of nodes that
        /// declare one (<c>[AibtRandomStream]</c>). Each such node's stream is derived before the first
        /// update from <paramref name="randomSeed"/>, the program's semantic hash, this host's
        /// <see cref="InstanceId"/> and the node's runtime index, as <c>TreeSimulation</c> does. A tree
        /// without such nodes has no stream and is not affected.
        /// </summary>
        public bool TryBootstrap(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            NativeTraceChannelCapacityV1 traceCapacity,
            Func<long> clock,
            ulong randomSeed,
            out NativeRuntimeFailureV1 failure)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (_core.Bootstrapped || _core.Disposed || _driving)
            {
                failure = InvalidLifetime();
                return false;
            }
            var treeInstanceId = new TreeInstanceId((ulong)Interlocked.Increment(ref s_nextTreeInstanceId));
            if (!_core.TryBootstrapGenerated(definition, catalog, traceCapacity, treeInstanceId, _driving, out failure))
                return false;
            _core.GeneratedDispatch.SeedRandomStreams(
                randomSeed, new NativeHash256V1(definition.Binding.SemanticProgram.Header.CanonicalSemanticHash));
            RandomSeed = randomSeed;
            _clock = clock ?? ReadScaledTime;
            return true;
        }

        private static long ReadScaledTime() => checked((long)(Time.timeAsDouble * 1000000.0));

        /// <summary>
        /// True once a <see cref="ProductionTreeScheduler"/> owns this host's drive loop. While
        /// owned, Unity's own per-frame <c>Update</c> message no-ops; the coordinator calls
        /// <see cref="DriveOneUpdate"/> itself. Ownership never duplicates or transfers native
        /// state -- this host remains the sole owner of its machine, trace channel and disposal.
        /// </summary>
        public bool IsCoordinatorOwned => _owner != null;

        /// <summary>
        /// Opaque, per-host handle for one Tree-scope blackboard slot resolved for external write
        /// (ADR-P7-039). Resolve once via <see cref="TryResolveExternalTreeWrite{T}"/>, typically at
        /// spawn time, then reuse every frame with <see cref="TryWriteExternalTreeValue{T}"/>. Valid
        /// only for the exact host it was resolved against.
        /// </summary>
        public readonly struct ExternalTreeWriteHandle<T> where T : unmanaged
        {
            private readonly ulong _instanceId;
            private readonly uint _slotIndex;
            private readonly NativeBlackboardSlotBindingV2 _slot;

            internal ExternalTreeWriteHandle(ulong instanceId, uint slotIndex, NativeBlackboardSlotBindingV2 slot)
            {
                _instanceId = instanceId;
                _slotIndex = slotIndex;
                _slot = slot;
            }

            internal ulong InstanceId => _instanceId;
            internal uint SlotIndex => _slotIndex;
            internal NativeBlackboardSlotBindingV2 Slot => _slot;
            internal bool IsValid => _instanceId != 0;
        }

        /// <summary>
        /// Resolves a Tree-scope blackboard slot, addressed by <paramref name="stableKey"/>, for
        /// repeated external writes from ordinary C# code (ADR-P7-039) -- an agent's own live
        /// transform, a moving target's position, and similar per-frame inputs a compiled node
        /// itself never writes. Succeeds only when no node in this tree declares Write access to the
        /// key; reusing an eligible key that some node does write is refused, never a silent race.
        /// Available only for a host bootstrapped through the generated-catalog
        /// <see cref="TryBootstrap(GeneratedTreeRuntimeDefinitionV2, GeneratedBurstCatalogV2, NativeTraceChannelCapacityV1, Func{long}, out NativeRuntimeFailureV1)"/>
        /// overload -- a delegate-based host has no blackboard storage to write into and is refused
        /// structurally. Only built-in (non-registered) blackboard value types are supported.
        /// </summary>
        public bool TryResolveExternalTreeWrite<T>(
            string stableKey,
            NativeBlackboardTypeIdV2 expectedType,
            out ExternalTreeWriteHandle<T> handle,
            out NativeRuntimeFailureV1 failure)
            where T : unmanaged
        {
            handle = default;
            if (string.IsNullOrEmpty(stableKey)) throw new ArgumentException("A stable blackboard key is required.", nameof(stableKey));
            if (!_core.Bootstrapped || _core.Disposed)
            {
                failure = InvalidLifetime();
                return false;
            }
            var generatedDispatch = _core.GeneratedDispatch;
            if (generatedDispatch == null)
            {
                failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                return false;
            }
            var stableKeyId = StableHash.Fnv1A64(stableKey);
            if (!generatedDispatch.TryResolveExternalTreeWrite(
                    stableKeyId, expectedType, out var slotIndex, out var slot, out var burstFailure))
            {
                failure = new NativeRuntimeFailureV1(MapExternalWriteFailure(burstFailure));
                return false;
            }
            handle = new ExternalTreeWriteHandle<T>(InstanceId, slotIndex, slot);
            failure = default;
            return true;
        }

        /// <summary>
        /// Writes one external value through a handle already resolved by
        /// <see cref="TryResolveExternalTreeWrite{T}"/> -- the per-frame hot path, no string lookup
        /// or managed allocation. Refuses, touching no memory, when this host is mid-round (the same
        /// reentrancy invariant every other entry point already enforces via <see cref="_driving"/>
        /// -- refused rather than faulting the host, since writing at the wrong moment is an
        /// expected, recoverable caller situation, not a reentrancy bug) or when
        /// <paramref name="handle"/> was resolved against a different (or since-destroyed) host.
        /// </summary>
        public bool TryWriteExternalTreeValue<T>(
            ExternalTreeWriteHandle<T> handle,
            T value,
            out bool changed,
            out NativeRuntimeFailureV1 failure)
            where T : unmanaged
        {
            changed = false;
            if (!handle.IsValid || handle.InstanceId != InstanceId || !CanAcceptExternalTreeWrites)
            {
                failure = InvalidLifetime();
                return false;
            }
            var candidate = new NativeArray<T>(1, Allocator.Temp);
            try
            {
                candidate[0] = value;
                if (!_core.GeneratedDispatch.TryWriteExternalTreeValue(
                        handle.SlotIndex, handle.Slot, candidate, out changed, out var burstFailure))
                {
                    failure = new NativeRuntimeFailureV1(MapExternalWriteFailure(burstFailure));
                    return false;
                }
                failure = default;
                return true;
            }
            finally
            {
                candidate.Dispose();
            }
        }

        private static NativeRuntimeDiagnosticCodeV1 MapExternalWriteFailure(BurstContextResult failure)
        {
            switch (failure)
            {
                case BurstContextResult.TypeMismatch: return NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch;
                case BurstContextResult.PhaseViolation: return NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch;
                case BurstContextResult.InvalidEncoding: return NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue;
                case BurstContextResult.Overflow: return NativeRuntimeDiagnosticCodeV1.BlackboardVersionOverflow;
                default: return NativeRuntimeDiagnosticCodeV1.BlackboardUndeclaredAccess;
            }
        }

        /// <summary>
        /// True when a Tree-scope value of this host may be read (ADR-P11-004): bootstrapped through
        /// the generated catalog, not stopped, and not executing. Unlike
        /// <see cref="CanAcceptExternalTreeWrites"/> it stays true after the root finished or the
        /// host faulted. A <c>PipelinedJobs</c> host is executing between scheduler frames.
        /// </summary>
        public bool CanReadTreeValues
            => _core.Bootstrapped && _core.GeneratedDispatch != null && !_core.Disposed && !_core.DestroyRequested && !_driving;

        /// <summary>P11-004: true while this host runs a round or holds its drive lock (a PipelinedJobs job in flight).</summary>
        internal bool IsExecuting => _driving && !_core.Disposed;

        /// <summary>
        /// Opaque, per-host handle for one Tree-scope blackboard slot resolved for external read
        /// (ADR-P11-004). Resolve once via <see cref="TryResolveExternalTreeRead{T}"/>, then reuse every
        /// frame with <see cref="TryReadExternalTreeValue{T}"/>. Valid only for the exact host it was
        /// resolved against.
        /// </summary>
        public readonly struct ExternalTreeReadHandle<T> where T : unmanaged
        {
            private readonly ulong _instanceId;
            private readonly NativeBlackboardSlotBindingV2 _slot;

            internal ExternalTreeReadHandle(ulong instanceId, NativeBlackboardSlotBindingV2 slot)
            {
                _instanceId = instanceId;
                _slot = slot;
            }

            internal ulong InstanceId => _instanceId;
            internal NativeBlackboardSlotBindingV2 Slot => _slot;
            internal bool IsValid => _instanceId != 0;
        }

        /// <summary>
        /// Resolves a Tree-scope blackboard slot, addressed by <paramref name="stableKey"/>, for
        /// repeated reads from ordinary C# code (ADR-P11-004): any Tree-scope key with a built-in type,
        /// whether nodes write it, a fact sets it or nobody writes it. Refuses an unknown key
        /// (<c>BlackboardUndeclaredAccess</c>), an Agent- or Shared-scope key (<c>BlackboardUnsupportedScope</c>),
        /// a different type or a <typeparamref name="T"/> that is not the managed type of <paramref name="expectedType"/>
        /// (<c>int</c> for Int32, <c>float</c> for Float32, <see cref="Float3Value"/> for Float3 and so on; a type of
        /// the same size such as <c>uint</c> is refused) (<c>BlackboardTypeMismatch</c>), a
        /// host bootstrapped through a delegate overload (<c>NativeCapacityPlanInvalid</c>) and a host not
        /// bootstrapped or stopped (<c>NativeLifetimeStateInvalid</c>). Legal while the host executes.
        /// </summary>
        public bool TryResolveExternalTreeRead<T>(
            string stableKey,
            NativeBlackboardTypeIdV2 expectedType,
            out ExternalTreeReadHandle<T> handle,
            out NativeRuntimeFailureV1 failure)
            where T : unmanaged
        {
            handle = default;
            if (string.IsNullOrEmpty(stableKey)) throw new ArgumentException("A stable blackboard key is required.", nameof(stableKey));
            if (!_core.Bootstrapped || _core.Disposed)
            {
                failure = InvalidLifetime();
                return false;
            }
            var generatedDispatch = _core.GeneratedDispatch;
            if (generatedDispatch == null)
            {
                failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                return false;
            }
            if (!generatedDispatch.TryResolveExternalTreeRead<T>(
                    StableHash.Fnv1A64(stableKey), expectedType, out _, out var slot, out var code))
            {
                failure = new NativeRuntimeFailureV1(code);
                return false;
            }
            handle = new ExternalTreeReadHandle<T>(InstanceId, slot);
            failure = default;
            return true;
        }

        /// <summary>
        /// Reads one value through a handle resolved by <see cref="TryResolveExternalTreeRead{T}"/> --
        /// the per-frame path: no string lookup, no managed allocation. Refuses with
        /// <c>NativeLifetimeStateInvalid</c>, touching no memory, while <see cref="CanReadTreeValues"/> is
        /// false (executing, stopped) or when <paramref name="handle"/> belongs to another host. After a
        /// fault the value is whatever the storage held when the fault happened and may be mid-update.
        /// </summary>
        public bool TryReadExternalTreeValue<T>(
            ExternalTreeReadHandle<T> handle,
            out T value,
            out NativeRuntimeFailureV1 failure)
            where T : unmanaged
        {
            value = default;
            if (!handle.IsValid || handle.InstanceId != InstanceId || !CanReadTreeValues
                || !_core.GeneratedDispatch.TryReadExternalTreeValue(handle.Slot, out value))
            {
                failure = InvalidLifetime();
                return false;
            }
            failure = default;
            return true;
        }

        /// <summary>
        /// Editor-only type-erased read of a Tree-scope key by name (ADR-P11-004 D7), for the graph's
        /// live view of a native agent. Allocates; same gate and failure codes as the typed read.
        /// </summary>
        internal bool TryReadTreeValue(string stableKey, out BlackboardValue value, out NativeRuntimeFailureV1 failure)
        {
            value = default;
            if (string.IsNullOrEmpty(stableKey)) throw new ArgumentException("A stable blackboard key is required.", nameof(stableKey));
            if (_core.Bootstrapped && !_core.Disposed && _core.GeneratedDispatch == null)
            {
                failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                return false;
            }
            if (!CanReadTreeValues)
            {
                failure = InvalidLifetime();
                return false;
            }
            if (!_core.GeneratedDispatch.TryReadTreeValue(StableHash.Fnv1A64(stableKey), out value, out var code))
            {
                failure = new NativeRuntimeFailureV1(code);
                return false;
            }
            failure = default;
            return true;
        }

        internal bool TryRegisterOwner(ProductionTreeScheduler owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (_owner != null && _owner != owner) return false;
            _owner = owner;
            return true;
        }

        internal void ClearOwner(ProductionTreeScheduler owner)
        {
            if (_owner == owner) _owner = null;
        }

        private void Update()
        {
            if (_owner != null) return; // driven by the coordinator instead, see DriveOneUpdate
            DriveOneUpdate();
        }

        /// <summary>
        /// Reads the clock and opens the next logical update, or resumes a budget-suspended one.
        /// Returns false when nothing may run this call (the clock callback stopped the host, or the
        /// clock or update begin failed and the host is now stopped).
        /// </summary>
        private bool TryOpenOrResumeUpdate()
        {
            if (!_core.UpdateOpen)
            {
                var now = _clock();
                if (!_core.Ready || _core.DestroyRequested) return false;
                return _core.TryBeginUpdateAt(now);
            }
            _core.ResumeIfSuspended();
            return true;
        }

        /// <summary>
        /// One drive step -- identical body to the standalone per-frame <c>Update</c> message, and
        /// the only entry point <see cref="ProductionTreeScheduler"/> uses once this host is
        /// registered. Never called directly by application code; use a coordinator or let this
        /// host drive itself standalone.
        /// </summary>
        internal void DriveOneUpdate()
        {
            if (!_core.Ready || !isActiveAndEnabled || _core.Disposed) return;
            if (_driving)
            {
                _core.Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.Reentrant);
                return;
            }
            _driving = true;
            try
            {
                if (!TryOpenOrResumeUpdate()) return;
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.HostBegin);
                _core.RunSegment(StepBudget, false);
            }
            catch (Exception exception)
            {
                _core.Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                _driving = false;
                if (_core.DestroyRequested) DisposeHost();
            }
        }

        /// <summary>
        /// True while this host has advanced to a dispatch requirement not yet resolved via
        /// <see cref="CompletePendingDispatch"/> -- a <see cref="ProductionTreeScheduler"/> wave in
        /// flight. A second overlapping wave step is refused, matching the existing single-driver
        /// invariant this host has always held (<see cref="_driving"/>).
        /// </summary>
        internal bool HasPendingDispatch => _core.HasPendingDispatch;

        /// <summary>
        /// Advances until the next dispatch requirement -- returned unresolved for a caller-owned
        /// wave batch, never auto-resolved -- or the update segment reaches Completed/Waiting.
        /// Reserved for <see cref="ProductionTreeScheduler"/>'s own generated-catalog group
        /// dispatch; unlike <see cref="DriveOneUpdate"/>, the caller alone decides how the returned
        /// request is resolved and must call <see cref="CompletePendingDispatch"/> exactly once
        /// before advancing this host again. Never mixed with <see cref="DriveOneUpdate"/> on the
        /// same host in the same update -- both share the identical reentrancy guard.
        /// </summary>
        internal bool TryAdvanceToNextDispatch(out DispatchRequest request)
        {
            request = default;
            if (!_core.Ready || !isActiveAndEnabled || _core.Disposed || _core.DestroyRequested || _core.HasPendingDispatch) return false;
            if (_driving)
            {
                _core.Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.Reentrant);
                return false;
            }
            _driving = true;
            var resolvedWithinThisCall = false;
            try
            {
                if (!TryOpenOrResumeUpdate()) return false;
                var found = _core.AdvanceOneStepForGroupedDispatch(out request);
                resolvedWithinThisCall = !_core.HasPendingDispatch;
                return found;
            }
            catch (Exception exception)
            {
                _core.Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
                resolvedWithinThisCall = true;
                return false;
            }
            finally
            {
                // A pending step deliberately keeps _driving held across this call's own return --
                // released only once CompletePendingDispatch resolves it, mirroring DriveOneUpdate's
                // own hold for the whole (synchronous, here caller-paced) duration of one dispatch.
                if (resolvedWithinThisCall)
                {
                    _driving = false;
                    if (_core.DestroyRequested) DisposeHost();
                }
            }
        }

        /// <summary>What one externally-supplied lifecycle step meant for a <see cref="ProductionTreeScheduler"/>'s own BatchedJobsSameFrame/PipelinedJobs group round.</summary>
        internal enum BatchedStepOutcome : byte
        {
            /// <summary>An internal-progress step; this host stays open for the group driver's next round.</summary>
            Continue = 0,
            /// <summary>A dispatch requirement was reached; resolve it via <see cref="TryResolveSinglePendingDispatch"/> or a <see cref="GeneratedDispatchGroupExecutorV2"/> batch, then this host is open again.</summary>
            DispatchRequired = 1,
            /// <summary>The update reached Completed/Waiting; call <see cref="ReleaseBatchedDrive"/> and drop this host from the round.</summary>
            Terminal = 2,
        }

        /// <summary>
        /// Opens this host's next logical update (or resumes a suspended one) and holds its drive
        /// lock across every subsequent <see cref="TryHandleBatchedStepResult"/> call until a
        /// barrier is reached -- for a <see cref="ProductionTreeScheduler"/> group driver that
        /// advances this host's own machine externally through a <c>NativeBatchedLifecycleOwnerV1</c>
        /// Job shared with other agents, rather than this host calling <c>TryAdvance</c> itself.
        /// <paramref name="included"/> is false, with no failure, when this host is simply not
        /// eligible right now (already terminal, disabled, destroying); a genuine misuse (already
        /// driving, a bad clock) fails and returns false. The caller must schedule the returned
        /// <paramref name="machine"/> for exactly one <c>TryAdvance</c> per round until a barrier.
        /// </summary>
        internal bool TryBeginBatchedRound(out bool included, out NativeLifecycleMachineV1 machine)
        {
            included = false;
            machine = default;
            if (_core.Disposed || _core.DestroyRequested || !_core.Ready || !isActiveAndEnabled || _core.HasPendingDispatch)
                return true; // not eligible this round -- not a caller error.
            if (_driving)
            {
                _core.Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.Reentrant);
                return false;
            }
            _driving = true;
            try
            {
                if (!_core.UpdateOpen)
                {
                    var now = _clock();
                    if (!_core.Ready || _core.DestroyRequested) { included = false; return true; }
                    if (!_core.TryBeginUpdateAt(now)) return false;
                }
                else _core.ResumeIfSuspended();
                machine = _core.Machine;
                included = true;
                return true;
            }
            catch (Exception exception)
            {
                _core.Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
                return false;
            }
            finally
            {
                if (!included) _driving = false;
            }
        }

        /// <summary>
        /// Applies one externally-advanced lifecycle step (already produced by the group driver's
        /// own batched Job) to this host's own recorder/terminal bookkeeping -- the exact same
        /// per-step handling the grouped-dispatch advance already applies, minus
        /// the loop and the <c>TryAdvance</c> call itself, since the step was advanced elsewhere.
        /// Must only be called between a <see cref="TryBeginBatchedRound"/> that returned
        /// <c>included: true</c> (or a prior round's own <see cref="BatchedStepOutcome.Continue"/>)
        /// and this host's next barrier.
        /// </summary>
        internal BatchedStepOutcome TryHandleBatchedStepResult(NativeLifecycleStepResultV1 step, out DispatchRequest request)
            => TryHandleBatchedStepResult(step, false, out request);

        /// <summary>
        /// P10-004: <paramref name="enforceStepLimit"/> applies <see cref="MaximumStepsPerUpdate"/>
        /// (BatchedJobsSameFrame). A step past the limit is not handled: this host fails and the
        /// caller receives <see cref="BatchedStepOutcome.Terminal"/> to release its drive lock.
        /// </summary>
        internal BatchedStepOutcome TryHandleBatchedStepResult(
            NativeLifecycleStepResultV1 step, bool enforceStepLimit, out DispatchRequest request)
            => _core.TryHandleBatchedStepResult(step, enforceStepLimit, out request);

        /// <summary>
        /// Resolves this host's own currently-pending dispatch requirement through its normal
        /// single-instance callback (the same call <c>RunSegment</c> already makes) --
        /// for a group driver's round with no other same-catalog member to batch with this frame.
        /// </summary>
        internal bool TryResolveSinglePendingDispatch()
        {
            if (!_core.TryResolveSinglePendingDispatch(out var result, out var status)) return false;
            if (!_core.Ready) return false; // A reentrant call may already have faulted the host.
            return CompletePendingDispatch(result, status);
        }

        /// <summary>
        /// Reacquires the externally-driven lifecycle lock after a grouped dispatch completion
        /// released it. This makes the cross-round ownership invariant explicit for both the
        /// same-frame and pipelined production drivers.
        /// </summary>
        internal bool TryResumeBatchedDriveAfterDispatch()
        {
            if (_core.Disposed || _core.DestroyRequested || !_core.Ready || !isActiveAndEnabled || _core.HasPendingDispatch)
                return false;
            if (_driving)
            {
                _core.Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.Reentrant);
                return false;
            }
            _driving = true;
            return true;
        }

        /// <summary>Releases the drive lock <see cref="TryBeginBatchedRound"/> took for a host whose round ended at <see cref="BatchedStepOutcome.Terminal"/> (no dispatch was pending, so <see cref="CompletePendingDispatch"/> never runs to release it).</summary>
        internal void ReleaseBatchedDrive()
        {
            _driving = false;
            if (_core.DestroyRequested) DisposeHost();
        }

        /// <summary>
        /// P10-004: why this host cannot run inside a JobsAgentFrame job, or
        /// <see cref="AgentFrameFallbackReason.None"/> when it can.
        /// </summary>
        internal AgentFrameFallbackReason GetAgentFrameFallback() => _core.GetAgentFrameFallback();

        /// <summary>
        /// P10-004: the begin half of <see cref="DriveOneUpdate"/> for a JobsAgentFrame group: the same
        /// eligibility, clock and update-begin handling, then fills <paramref name="agent"/> for the
        /// job instead of calling <c>RunSegment</c>. <paramref name="included"/> is false when this
        /// update has nothing to run (exactly when <see cref="DriveOneUpdate"/> would return before
        /// advancing). When included, the drive lock stays held until <see cref="FinishAgentFrame"/>.
        /// </summary>
        internal void BeginAgentFrame(out bool included, ref AgentFrameAgentV1 agent)
        {
            included = false;
            if (!_core.Ready || !isActiveAndEnabled || _core.Disposed) return;
            if (_driving)
            {
                _core.Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.Reentrant);
                return;
            }
            _driving = true;
            try
            {
                if (!TryOpenOrResumeUpdate()) return;
                // RunSegment's own loop condition: nothing runs once the host stopped or is destroying.
                if (!_core.Ready || _core.DestroyRequested) return;
                _core.FillAgentFrame(ref agent);
                included = true;
            }
            catch (Exception exception)
            {
                _core.Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                if (!included)
                {
                    _driving = false;
                    if (_core.DestroyRequested) DisposeHost();
                }
            }
        }

        /// <summary>
        /// P10-004: the rest of <see cref="DriveOneUpdate"/> after the agent-frame job ran this
        /// host's update: replays its trace calls and effects in step order, then applies the same
        /// terminal or failure bookkeeping <c>RunSegment</c> applies, and releases the drive lock.
        /// </summary>
        internal void FinishAgentFrame(ref AgentFrameAgentV1 agent)
        {
            try
            {
                _core.FinishAgentFrame(ref agent);
            }
            catch (Exception exception)
            {
                _core.Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                agent.Outcome = AgentFrameOutcomeV1.None;
                _driving = false;
                if (_core.DestroyRequested) DisposeHost();
            }
        }

        /// <summary>
        /// P10-004: releases a begun agent frame whose job never ran (the scheduler was destroyed or
        /// scheduling failed) with the given failure, never leaving the drive lock held.
        /// </summary>
        internal void AbandonAgentFrame(NativeRuntimeFailureV1 failure, string detail)
        {
            try
            {
                _core.Fail(failure, detail);
            }
            finally
            {
                _driving = false;
                if (_core.DestroyRequested) DisposeHost();
            }
        }

        /// <summary>
        /// Structured, never-silent rejection for a <see cref="SchedulingProfile.ForcedPolicy"/> the
        /// coordinator cannot honor this frame (no generated catalog to batch, no
        /// <see cref="SchedulerJobsCapabilities"/> configured, or <c>NativeAutoSelectionV1.TrySelect</c>
        /// itself rejected the forced policy) -- never a silent fallback to a different policy.
        /// </summary>
        internal void FailUnsupportedForcedPolicy()
        {
            _core.Fail(
                new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid),
                "Forced scheduling policy is not supported by this coordinator's own current configuration.");
        }

        internal void FailExternalScheduling(NativeRuntimeFailureV1 failure)
        {
            _core.Fail(failure, "The coordinator's externally-driven scheduling round failed.");
        }

        internal bool TryReserveGeneratedEffectCapacity(uint nodeIndex, out BurstContextResult failure)
            => _core.TryReserveGeneratedEffectCapacity(nodeIndex, out failure);

        /// <summary>
        /// Resolves the single dispatch requirement <see cref="TryAdvanceToNextDispatch"/> most
        /// recently returned. Returns false, without mutating the machine, if no wave step is
        /// currently pending (a caller error) or the host already faulted reentrantly.
        /// </summary>
        internal bool CompletePendingDispatch(BurstContextResult result, NodeStatus status)
        {
            if (!_core.HasPendingDispatch) return false;
            try
            {
                return _core.CompletePendingDispatch(result, status);
            }
            finally
            {
                _driving = false;
                if (_core.DestroyRequested) DisposeHost();
            }
        }

        private static NativeRuntimeFailureV1 InvalidLifetime() => ProductionTreeAgentCore.InvalidLifetime();

        /// <summary>Permanently stops this host, cancels active work and releases its storage. Reentrant calls finish cleanup after the current callback.</summary>
        public void Stop()
        {
            if (!_core.RequestDestroy()) return;
            _owner?.TryUnregister(this);
            if (!_driving) DisposeHost();
        }

        private void OnDestroy() => Stop();

        private void DisposeHost()
        {
            if (!_core.BeginDispose()) return;
            _driving = true;
            try
            {
                _core.RunTeardown();
            }
            catch (Exception exception)
            {
                _core.Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                _driving = false;
                _core.ReleaseStorage();
                _clock = null;
            }
        }
    }
}
