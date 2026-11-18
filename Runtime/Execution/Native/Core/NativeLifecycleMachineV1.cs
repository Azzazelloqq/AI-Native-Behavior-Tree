using System;
using AIBT.Burst;
using Unity.Collections;
using Unity.Profiling;

namespace AIBT
{
    internal enum NativeLifecycleNodeKindV1 : byte
    {
        GeneratedLeaf = 0,
        MemorySequence = 1,
        MemorySelector = 2,
        ReactiveSequence = 3,
        ReactiveSelector = 4,
        Inverter = 5,
        Succeeder = 6,
        Failer = 7,
        Repeater = 8,
        Timeout = 9,
        Cooldown = 10,
        Parallel = 11,

        // ADR-P10-013: reactive composites that keep their running child (guard pass).
        RetainingReactiveSequence = 12,
        RetainingReactiveSelector = 13,
    }

    internal readonly struct NativeLifecycleNodeBindingV1
    {
        internal NativeLifecycleNodeBindingV1(uint nodeIndex, NativeLifecycleNodeKindV1 kind)
        {
            NodeIndex = nodeIndex;
            Kind = kind;
        }

        internal uint NodeIndex { get; }
        internal NativeLifecycleNodeKindV1 Kind { get; }
    }

    internal enum NativeLifecycleStepKindV1 : byte
    {
        CompositeEntered = 0,
        ChildSelected = 1,
        ChildAccepted = 2,
        CompositeExited = 3,
        CompositeAborted = 4,
        DispatchRequired = 5,
        Waiting = 6,
        Completed = 7,
        ReactiveReset = 8,
        ParallelBranchSuspended = 9,
    }

    internal readonly struct NativeLifecycleDispatchTokenV1 : IEquatable<NativeLifecycleDispatchTokenV1>
    {
        internal NativeLifecycleDispatchTokenV1(ulong ownerId, uint generation, ulong dispatchId)
        {
            OwnerId = ownerId;
            Generation = generation;
            DispatchId = dispatchId;
        }

        internal ulong OwnerId { get; }
        internal uint Generation { get; }
        internal ulong DispatchId { get; }
        internal bool IsValid => OwnerId != 0 && Generation != 0 && DispatchId != 0;
        public bool Equals(NativeLifecycleDispatchTokenV1 other)
            => OwnerId == other.OwnerId && Generation == other.Generation && DispatchId == other.DispatchId;
        public override bool Equals(object obj) => obj is NativeLifecycleDispatchTokenV1 other && Equals(other);
        public override int GetHashCode() => OwnerId.GetHashCode() ^ (int)Generation ^ DispatchId.GetHashCode();
    }

    internal readonly struct NativeLifecycleStepResultV1
    {
        internal NativeLifecycleStepResultV1(
            NativeLifecycleStepKindV1 kind,
            uint nodeIndex,
            BurstCallbackPhase phase = default,
            NativeLifecycleDispatchTokenV1 dispatchToken = default,
            bool hasRootStatus = false,
            NodeStatus rootStatus = default,
            BurstNodeExitReason exitReason = default,
            BurstNodeAbortReason abortReason = default,
            bool hasDispatchReasons = false)
        {
            Kind = kind;
            NodeIndex = nodeIndex;
            Phase = phase;
            DispatchToken = dispatchToken;
            HasRootStatus = hasRootStatus;
            RootStatus = rootStatus;
            ExitReason = exitReason;
            AbortReason = abortReason;
            HasDispatchReasons = hasDispatchReasons;
        }

        internal NativeLifecycleStepKindV1 Kind { get; }
        internal uint NodeIndex { get; }
        internal BurstCallbackPhase Phase { get; }
        internal NativeLifecycleDispatchTokenV1 DispatchToken { get; }
        internal bool HasRootStatus { get; }
        internal NodeStatus RootStatus { get; }
        internal BurstNodeExitReason ExitReason { get; }
        internal BurstNodeAbortReason AbortReason { get; }
        internal bool HasDispatchReasons { get; }
    }

    internal struct NativeLifecycleControlV1
    {
        internal ulong OwnerId;
        internal uint Generation;
        internal ulong UpdateId;
        internal ulong SemanticSteps;
        internal ulong NextDispatchId;
        internal ulong ActiveDispatchId;
        internal uint Depth;
        internal uint ActiveDispatchNode;
        internal BurstCallbackPhase ActiveDispatchPhase;
        internal NodeStatus RootStatus;
        internal byte HasRootStatus;
        internal byte UpdateOpen;
        internal byte Waiting;
        internal byte Faulted;
        internal byte AbortRequested;
        internal byte RootAborted;
        internal BurstNodeAbortReason AbortReason;
        internal uint AbortTargetDepth;
        internal byte ReactiveResetPending;
        internal byte TimeoutPending;
        internal long TimeMicroseconds;
        internal uint SuspendedFrameStart;
        internal byte ParallelAbortPending;
        internal uint ParallelAbortOwnerDepth;

        // The source node of the abort in progress, where the executor knows one (a timeout, a v1
        // reactive reset, a retaining composite's guard). Diagnostic only; no step reads it.
        internal uint AbortSourceNode;
        internal byte HasAbortSource;

        // ADR-P10-013: the one retaining reactive composite that holds parked frames (its frame
        // index + 1, 0 = none), its guard-pass phase, and the two frame segments it parks in the
        // suspended-frame region: the retained branch during the pass, the held guard branch
        // during a takeover abort.
        internal uint RetainOwnerDepth;
        internal byte RetainPhase;
        internal byte RetainRest;
        internal byte RetainVisitRestPending;
        internal NodeStatus RetainEndStatus;
        internal uint RetainedFirstFrame;
        internal uint RetainedFrameCount;
        internal uint HeldFirstFrame;
        internal uint HeldFrameCount;
    }

    internal struct NativeLifecycleMachineV1
    {
        private const ulong MemorySequenceTypeId = 0x5b7ceab8e6ee5d73UL;
        private const ulong MemorySelectorTypeId = 0x728850afcebac87fUL;
        private const ulong ParallelTypeId = 0xc178d10e173662cfUL;

        private const byte RetainNone = 0;
        private const byte RetainPassPending = 1;
        private const byte RetainPassActive = 2;
        private const byte RetainAbortEnd = 3;
        private const byte RetainAbortTakeover = 4;
        private const byte RetainAbortSuperseded = 5;
        private const byte RetainRestLeaf = 1;
        private const byte RetainRestVisit = 2;

        private static readonly ProfilerMarker s_TryAdvanceMarker =
            new ProfilerMarker("AIBT.Native.LifecycleTick");
        private static readonly ProfilerMarker s_AdvanceLeafMarker =
            new ProfilerMarker("AIBT.Native.Dispatch.Leaf");
        private static readonly ProfilerMarker s_AdvanceAbortMarker =
            new ProfilerMarker("AIBT.Native.Dispatch.Abort");
        private static readonly ProfilerMarker s_AdvanceParallelMarker =
            new ProfilerMarker("AIBT.Native.Dispatch.Parallel");
        private static readonly ProfilerMarker s_AdvanceCompositeMarker =
            new ProfilerMarker("AIBT.Native.Dispatch.Composite");
        private static readonly ProfilerMarker s_AdvanceDecoratorMarker =
            new ProfilerMarker("AIBT.Native.Dispatch.Decorator");

        private NativeArray<NativeCompiledNodeRecordV1> _nodes;
        private NativeArray<uint> _children;
        private NativeArray<NativeLifecycleNodeBindingV1> _bindings;
        private NativeArray<byte> _memory;
        private NativeArray<byte> _configuration;
        private NativeArray<NativeFrameStateV1> _frames;
        private NativeArray<uint> _generations;
        private NativeArray<byte> _cooldownInitialized;
        private NativeArray<NativeParallelBranchStateV1> _parallelBranches;
        private NativeArray<NativeLifecycleControlV1> _control;

        // P10-014 (review finding D1): flags of the abort a Parallel interrupted to abort its
        // suspended branches, kept on its first branch record (NativeParallelBranchStateV1).
        private const byte ResumeActive = 1;
        private const byte ResumeAbortRequested = 2;
        private const byte ResumeReactiveReset = 4;
        private const byte ResumeTimeout = 8;
        private const byte ResumeParallelAbort = 16;
        private const byte ResumeHasSource = 32;

        internal static bool TryCreate(
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> children,
            NativeArray<NativeLifecycleNodeBindingV1> bindings,
            NativeArray<byte> memory,
            NativeArray<NativeFrameStateV1> frames,
            NativeArray<uint> generations,
            NativeArray<NativeLifecycleControlV1> control,
            out NativeLifecycleMachineV1 machine,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(nodes, children, bindings, memory, frames, generations, control, default,
                out machine, out failure);

        internal static bool TryCreate(
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> children,
            NativeArray<NativeLifecycleNodeBindingV1> bindings,
            NativeArray<byte> memory,
            NativeArray<NativeFrameStateV1> frames,
            NativeArray<uint> generations,
            NativeArray<NativeLifecycleControlV1> control,
            NativeArray<byte> configuration,
            out NativeLifecycleMachineV1 machine,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(nodes, children, bindings, memory, frames, generations, control, configuration, default,
                out machine, out failure);

        internal static bool TryCreate(
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> children,
            NativeArray<NativeLifecycleNodeBindingV1> bindings,
            NativeArray<byte> memory,
            NativeArray<NativeFrameStateV1> frames,
            NativeArray<uint> generations,
            NativeArray<NativeLifecycleControlV1> control,
            NativeArray<byte> configuration,
            NativeArray<byte> cooldownInitialized,
            out NativeLifecycleMachineV1 machine,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(nodes, children, bindings, memory, frames, generations, control, configuration,
                cooldownInitialized, default, out machine, out failure);

        internal static bool TryCreate(
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> children,
            NativeArray<NativeLifecycleNodeBindingV1> bindings,
            NativeArray<byte> memory,
            NativeArray<NativeFrameStateV1> frames,
            NativeArray<uint> generations,
            NativeArray<NativeLifecycleControlV1> control,
            NativeArray<byte> configuration,
            NativeArray<byte> cooldownInitialized,
            NativeArray<NativeParallelBranchStateV1> parallelBranches,
            out NativeLifecycleMachineV1 machine,
            out NativeRuntimeFailureV1 failure)
        {
            machine = default;
            if (!nodes.IsCreated || nodes.Length == 0
                || !children.IsCreated || !bindings.IsCreated || bindings.Length != nodes.Length
                || !memory.IsCreated || !frames.IsCreated || frames.Length < nodes.Length
                || !generations.IsCreated || generations.Length < nodes.Length
                || !control.IsCreated || control.Length != 1)
            {
                failure = CapacityFailure();
                return false;
            }

            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                var binding = bindings[index];
                if (binding.NodeIndex != (uint)index
                    || node.NodeTypeVersion != 1u
                    || (node.Flags & CompiledNodeFlags.BurstDomain) == 0
                    || node.ChildOffset > children.Length
                    || node.ChildCount > children.Length - node.ChildOffset
                    || node.InstanceMemoryOffset > memory.Length
                    || node.InstanceMemorySize > memory.Length - node.InstanceMemoryOffset
                    || !ValidBinding(node, binding.Kind, configuration))
                {
                    failure = CapacityFailure();
                    return false;
                }
                for (uint child = 0; child < node.ChildCount; child++)
                    if (children[checked((int)(node.ChildOffset + child))] >= nodes.Length)
                    {
                        failure = CapacityFailure();
                        return false;
                    }
            }

            for (var index = 0; index < bindings.Length; index++)
                if (bindings[index].Kind == NativeLifecycleNodeKindV1.Cooldown
                    && (!cooldownInitialized.IsCreated || cooldownInitialized.Length < nodes.Length))
                {
                    failure = CapacityFailure();
                    return false;
                }

            uint requiredParallelBranches = 0;
            for (var index = 0; index < bindings.Length; index++)
                if (bindings[index].Kind == NativeLifecycleNodeKindV1.Parallel)
                {
                    if (uint.MaxValue - requiredParallelBranches < nodes[index].ChildCount)
                    {
                        failure = CapacityFailure();
                        return false;
                    }
                    requiredParallelBranches += nodes[index].ChildCount;
                }
            if (requiredParallelBranches != 0
                && (!parallelBranches.IsCreated || parallelBranches.Length != requiredParallelBranches))
            {
                failure = CapacityFailure();
                return false;
            }
            for (var index = 0; index < parallelBranches.Length; index++)
                parallelBranches[index] = new NativeParallelBranchStateV1 { CapacityOrdinal = (uint)index };

            if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter);
                return false;
            }

            for (var index = 0; index < frames.Length; index++) frames[index] = default;
            for (var index = 0; index < generations.Length; index++) generations[index] = 0u;
            control[0] = new NativeLifecycleControlV1
            {
                OwnerId = ownerId,
                Generation = 1u,
                SuspendedFrameStart = (uint)frames.Length,
            };
            machine = new NativeLifecycleMachineV1
            {
                _nodes = nodes,
                _children = children,
                _bindings = bindings,
                _memory = memory,
                _configuration = configuration,
                _frames = frames,
                _generations = generations,
                _cooldownInitialized = cooldownInitialized,
                _parallelBranches = parallelBranches,
                _control = control,
            };
            failure = default;
            return true;
        }

        /// <summary>
        /// Main-thread, read-only copy of the current activation stack (root first). Diagnostic
        /// only: callers must not hold a drive lock or have a job in flight on this machine.
        /// </summary>
        internal void CopyActivePath(System.Collections.Generic.List<uint> into)
        {
            into.Clear();
            if (!IsCreated) return;
            var depth = _control[0].Depth;
            for (var index = 0; index < depth && index < _frames.Length; index++)
                into.Add(_frames[index].NodeIndex);
        }

        /// <summary>
        /// P10-012: the depth of the activation stack (0 when not created), for an allocation-free
        /// read of the same path as <see cref="CopyActivePath"/> with <see cref="ActiveNodeAt"/>.
        /// Diagnostic only, same rules as <see cref="CopyActivePath"/>.
        /// </summary>
        internal int ActiveDepth => IsCreated ? (int)Math.Min(_control[0].Depth, (uint)_frames.Length) : 0;

        /// <summary>P10-012: the node index of activation frame <paramref name="depth"/> (0 = root), below <see cref="ActiveDepth"/>.</summary>
        internal uint ActiveNodeAt(int depth) => _frames[depth].NodeIndex;

        internal bool TryBeginUpdate(ulong updateId, out NativeRuntimeFailureV1 failure)
            => TryBeginUpdate(updateId, 0, out failure);

        internal bool TryBeginUpdate(ulong updateId, long timeMicroseconds, out NativeRuntimeFailureV1 failure)
        {
            if (!IsCreated || updateId == 0)
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (control.Faulted != 0 || control.UpdateOpen != 0 || control.ActiveDispatchId != 0
                || control.HasRootStatus != 0 || updateId <= control.UpdateId)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            if (control.Depth == 0)
            {
                control.Depth = 1;
                _frames[0] = new NativeFrameStateV1
                {
                    NodeIndex = 0u,
                    ParentFrameIndex = uint.MaxValue,
                    LifecycleState = NativeFrameLifecycleStateV1.Inactive,
                };
            }
            control.UpdateId = updateId;
            control.TimeMicroseconds = timeMicroseconds;
            control.UpdateOpen = 1;
            control.Waiting = 0;
            if (!PrepareUpdateStart(ref control, 0, 0))
            {
                failure = FrameCapacityFailure(control);
                return false;
            }
            _control[0] = control;
            failure = default;
            return true;
        }

        internal bool TryAdvance(out NativeLifecycleStepResultV1 result, out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_TryAdvanceMarker.Auto();
            result = default;
            if (!IsCreated)
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (control.UpdateOpen == 0 || control.ActiveDispatchId != 0 || control.Faulted != 0)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            if (control.Depth == 0)
            {
                control.UpdateOpen = 0;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(
                    NativeLifecycleStepKindV1.Completed, 0u,
                    hasRootStatus: control.HasRootStatus != 0,
                    rootStatus: control.RootStatus);
                failure = default;
                return true;
            }

            if (control.AbortRequested != 0 && control.ReactiveResetPending != 0
                && control.Depth == control.AbortTargetDepth)
            {
                var ownerIndex = checked((int)control.Depth - 1);
                var ownerFrame = _frames[ownerIndex];
                var ownerNode = _nodes[checked((int)ownerFrame.NodeIndex)];
                WriteCursor(ownerNode, 0);
                ownerFrame.HasPendingChildResult = 0;
                ownerFrame.PendingStatus = NodeStatus.Running;
                _frames[ownerIndex] = ownerFrame;
                control.AbortRequested = 0;
                control.ReactiveResetPending = 0;
                control.AbortTargetDepth = 0;
                control.SemanticSteps++;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ReactiveReset, ownerFrame.NodeIndex);
                failure = default;
                return true;
            }

            if (control.AbortRequested != 0 && control.TimeoutPending != 0
                && control.Depth == control.AbortTargetDepth)
            {
                var ownerIndex = checked((int)control.Depth - 1);
                var ownerFrame = _frames[ownerIndex];
                var ownerNode = _nodes[checked((int)ownerFrame.NodeIndex)];
                NativeDecoratorPolicyV1.TryDecodeTimeout(
                    _configuration.AsReadOnly(), ownerNode.ConfigOffset, ownerNode.ConfigSize, out var timeout);
                ownerFrame.PendingStatus = timeout.Result;
                ownerFrame.HasPendingChildResult = 0;
                ownerFrame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[ownerIndex] = ownerFrame;
                control.AbortRequested = 0;
                control.TimeoutPending = 0;
                control.AbortTargetDepth = 0;
                control.SemanticSteps++;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, ownerFrame.NodeIndex);
                failure = default;
                return true;
            }

            if (control.AbortRequested != 0 && control.ParallelAbortPending != 0
                && control.Depth == control.AbortTargetDepth)
            {
                var ownerIndex = checked((int)control.Depth - 1);
                var ownerFrame = _frames[ownerIndex];
                if (ownerFrame.ParallelActiveBranch >= 0)
                {
                    var completedIndex = checked((int)(ownerFrame.ParallelFirstBranch + (uint)ownerFrame.ParallelActiveBranch));
                    var completedBranch = _parallelBranches[completedIndex];
                    completedBranch.State = (byte)NativeParallelChildStateV1.NotStarted;
                    completedBranch.FirstSuspendedFrame = 0;
                    completedBranch.SuspendedFrameCount = 0;
                    _parallelBranches[completedIndex] = completedBranch;
                    ownerFrame.ParallelActiveBranch = -1;
                    _frames[ownerIndex] = ownerFrame;
                }
                if (TryBeginNextParallelBranchAbort(ownerIndex, ref ownerFrame, ref control))
                {
                    _frames[ownerIndex] = ownerFrame;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, ownerFrame.NodeIndex);
                    failure = default;
                    return true;
                }
                // The last branch has exited: the abort the Parallel interrupted (a timeout, a
                // reset, an outer Parallel's branch abort, a retained abort, a tree stop) resumes
                // toward its own target, or, after the Parallel's terminal decision, none remains.
                var aborted = ownerFrame.LifecycleState == NativeFrameLifecycleStateV1.Aborting;
                RestoreOuterAbort(ownerFrame, aborted, ref control);
                ownerFrame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[ownerIndex] = ownerFrame;
                control.SemanticSteps++;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(
                    aborted ? NativeLifecycleStepKindV1.CompositeAborted : NativeLifecycleStepKindV1.ChildAccepted,
                    ownerFrame.NodeIndex);
                failure = default;
                return true;
            }

            var frameIndex = checked((int)control.Depth - 1);
            var frame = _frames[frameIndex];
            var node = _nodes[checked((int)frame.NodeIndex)];
            var kind = _bindings[checked((int)frame.NodeIndex)].Kind;
            if (control.AbortRequested != 0 && frame.LifecycleState != NativeFrameLifecycleStateV1.Inactive)
            {
                if (control.RetainPhase != RetainNone && control.RetainOwnerDepth == (uint)frameIndex + 1u)
                {
                    // ADR-P10-013 common rule 3: an abort that reaches a retaining composite with
                    // parked frames aborts them first, in the same deepest-first traversal and
                    // without an extra step.
                    if (!TryUnparkForAbort(ref control))
                    {
                        result = default;
                        failure = FrameCapacityFailure(control);
                        return false;
                    }
                    frameIndex = checked((int)control.Depth - 1);
                    frame = _frames[frameIndex];
                    node = _nodes[checked((int)frame.NodeIndex)];
                    kind = _bindings[checked((int)frame.NodeIndex)].Kind;
                }
                return AdvanceAbort(frameIndex, ref frame, node, kind, ref control, out result, out failure);
            }
            if (control.RetainVisitRestPending != 0)
            {
                // A Parallel guard branch completed its visit with Running and took over; the
                // update now continues exactly as it would have at that point.
                control.RetainVisitRestPending = 0;
                if (kind != NativeLifecycleNodeKindV1.Parallel)
                {
                    result = default;
                    failure = LifetimeFailure(control);
                    return false;
                }
                return RestAfterParallelVisit(frameIndex, ref frame, ref control, out result, out failure);
            }
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Inactive)
            {
                if (!TryActivate(ref frame, node, ref control, out failure)) return false;
                if (kind == NativeLifecycleNodeKindV1.Timeout)
                {
                    NativeDecoratorPolicyV1.TryDecodeTimeout(
                        _configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out var timeout);
                    if (!NativeDecoratorPolicyV1.TryDeadline(control.TimeMicroseconds, timeout.DurationMicroseconds, out var deadline))
                    {
                        failure = TimeOverflowFailure(control);
                        return false;
                    }
                    WriteInt64(node, deadline);
                }
                // The update in which a retaining composite is entered has no guard pass.
                if (NativeReactivePolicyV1.IsRetaining(kind)) frame.LastUpdateId = control.UpdateId;
                _frames[frameIndex] = frame;
                if (kind == NativeLifecycleNodeKindV1.GeneratedLeaf)
                    return PublishDispatch(ref control, frame.NodeIndex, BurstCallbackPhase.Enter, out result, out failure);
                control.SemanticSteps++;
                frame.LifecycleState = NativeFrameLifecycleStateV1.Running;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeEntered, frame.NodeIndex);
                failure = default;
                return true;
            }

            if (kind == NativeLifecycleNodeKindV1.GeneratedLeaf)
                return AdvanceLeaf(frameIndex, ref frame, ref control, out result, out failure);
            return AdvanceComposite(frameIndex, ref frame, node, kind, ref control, out result, out failure);
        }

        // Queues the same descendant-cancellation/cursor-reset transition used by reactive
        // reevaluation, on a freshly seeded migration instance before its first update.
        internal bool TryQueueHotReloadChildReset(uint ownerDepth, out NativeRuntimeFailureV1 failure)
        {
            if (!IsCreated || ownerDepth == 0)
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (ownerDepth > control.Depth || control.UpdateOpen != 0 || control.ActiveDispatchId != 0
                || control.AbortRequested != 0 || control.HasRootStatus != 0)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            control.AbortRequested = 1;
            control.AbortReason = BurstNodeAbortReason.HotReload;
            control.AbortTargetDepth = ownerDepth;
            control.ReactiveResetPending = 1;
            control.HasAbortSource = 0;
            _control[0] = control;
            failure = default;
            return true;
        }

        internal bool TryRequestAbort(BurstNodeAbortReason reason, out NativeRuntimeFailureV1 failure,
            bool replacePendingForTreeStop = false)
        {
            // replacePendingForTreeStop: a whole-tree abort that replaces an abort already pending
            // or in progress -- the host's tree stop, or (P10-014, finding F-S2-2) the HotReload
            // abort of NativeHotReloadFullRestart after update start scheduled a v1 reset or timeout.
            if (!IsCreated || (byte)reason > (byte)BurstNodeAbortReason.Timeout
                || (replacePendingForTreeStop && reason != BurstNodeAbortReason.TreeStopped
                    && reason != BurstNodeAbortReason.HotReload))
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (control.UpdateOpen == 0 || control.Depth == 0 || control.ActiveDispatchId != 0
                || (control.AbortRequested != 0 && !replacePendingForTreeStop) || control.HasRootStatus != 0)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            control.AbortRequested = 1;
            control.AbortReason = reason;
            control.AbortTargetDepth = 0;
            control.ReactiveResetPending = 0;
            control.TimeoutPending = 0;
            control.HasAbortSource = 0;
            control.AbortSourceNode = 0;
            if (control.RetainPhase == RetainAbortEnd || control.RetainPhase == RetainAbortTakeover)
                control.RetainPhase = RetainAbortSuperseded;
            // P10-014 (review finding D3): a takeover completed by a Parallel guard's visit leaves
            // the rest of that visit pending; an abort replaces it.
            control.RetainVisitRestPending = 0;
            if (replacePendingForTreeStop) ReplaceSavedOuterAborts();
            _control[0] = control;
            failure = default;
            return true;
        }

        internal bool TryApplyObserverTransition(
            NativeObserverTransitionV1 transition,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsCreated || transition.Reason != BurstNodeAbortReason.ObserverSelf
                && transition.Reason != BurstNodeAbortReason.ObserverLowerPriority)
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (control.UpdateOpen == 0 || control.ActiveDispatchId != 0 || control.AbortRequested != 0)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            if (transition.OwnerNodeIndex >= _bindings.Length
                || !NativeReactivePolicyV1.IsReactive(_bindings[checked((int)transition.OwnerNodeIndex)].Kind))
            {
                failure = default;
                return true;
            }
            var ownerDepth = FindActiveFrame(transition.OwnerNodeIndex, control.Depth);
            if (ownerDepth < 0 && !TryRestoreRetainedPathToNode(
                    transition.OwnerNodeIndex, ref control, out ownerDepth, out failure))
                return false;
            if (ownerDepth < 0 || ownerDepth + 1 >= control.Depth)
            {
                failure = default;
                return true;
            }
            if (transition.Reason == BurstNodeAbortReason.ObserverLowerPriority)
            {
                var owner = _nodes[checked((int)transition.OwnerNodeIndex)];
                var observerOrdinal = -1;
                var activeOrdinal = -1;
                var activeChild = _frames[ownerDepth + 1].NodeIndex;
                for (uint ordinal = 0; ordinal < owner.ChildCount; ordinal++)
                {
                    var child = _children[checked((int)(owner.ChildOffset + ordinal))];
                    if (child == transition.ObserverNodeIndex) observerOrdinal = checked((int)ordinal);
                    if (child == activeChild) activeOrdinal = checked((int)ordinal);
                }
                if (observerOrdinal < 0 || activeOrdinal <= observerOrdinal)
                {
                    failure = default;
                    return true;
                }
            }
            control.AbortRequested = 1;
            control.AbortReason = transition.Reason;
            control.AbortTargetDepth = checked((uint)ownerDepth + 1u);
            control.ReactiveResetPending = 1;
            control.HasAbortSource = 1;
            control.AbortSourceNode = transition.ObserverNodeIndex;
            control.RetainVisitRestPending = 0; // P10-014, finding D3: the reset replaces the rest of a guard's visit.
            _control[0] = control;
            failure = default;
            return true;
        }

        private bool TryRestoreRetainedPathToNode(
            uint nodeIndex,
            ref NativeLifecycleControlV1 control,
            out int activeDepth,
            out NativeRuntimeFailureV1 failure)
        {
            activeDepth = -1;
            if (!_parallelBranches.IsCreated)
            {
                failure = default;
                return true;
            }

            for (var restoreCount = 0; restoreCount < _frames.Length; restoreCount++)
            {
                activeDepth = FindActiveFrame(nodeIndex, control.Depth);
                if (activeDepth >= 0)
                {
                    failure = default;
                    return true;
                }

                var targetNode = nodeIndex;
                var restored = false;
                for (var ancestorCount = 0; ancestorCount < _frames.Length; ancestorCount++)
                {
                    if (!TryFindSuspendedBranchContaining(targetNode, control, out var branchIndex))
                    {
                        failure = default;
                        return true;
                    }
                    if (!TryFindParallelOwnerNode(branchIndex, out var ownerNode))
                    {
                        failure = LifetimeFailure(control);
                        return false;
                    }
                    var ownerDepth = FindActiveFrame(ownerNode, control.Depth);
                    if (ownerDepth < 0)
                    {
                        targetNode = ownerNode;
                        continue;
                    }

                    var ownerFrame = _frames[ownerDepth];
                    var ordinal = branchIndex - checked((int)ownerFrame.ParallelFirstBranch);
                    var ownerNodeRecord = _nodes[checked((int)ownerFrame.NodeIndex)];
                    if (ordinal < 0 || ordinal >= checked((int)ownerNodeRecord.ChildCount)
                        || ownerFrame.ParallelActiveBranch >= 0)
                    {
                        failure = LifetimeFailure(control);
                        return false;
                    }
                    ownerFrame.ParallelActiveBranch = ordinal;
                    _frames[ownerDepth] = ownerFrame;
                    if (!RestoreSuspendedBranch(ownerDepth, branchIndex, ref control))
                    {
                        failure = new NativeRuntimeFailureV1(
                            NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                            NativeResourceKindV1.InstanceFrames);
                        return false;
                    }
                    restored = true;
                    break;
                }
                if (!restored)
                {
                    failure = LifetimeFailure(control);
                    return false;
                }
            }

            failure = LifetimeFailure(control);
            return false;
        }

        private int FindActiveFrame(uint nodeIndex, uint depth)
        {
            for (var index = 0; index < depth; index++)
                if (_frames[index].NodeIndex == nodeIndex) return index;
            return -1;
        }

        private bool TryFindSuspendedBranchContaining(
            uint nodeIndex,
            NativeLifecycleControlV1 control,
            out int branchIndex)
        {
            branchIndex = -1;
            for (var frameIndex = checked((int)control.SuspendedFrameStart); frameIndex < _frames.Length; frameIndex++)
            {
                if (_frames[frameIndex].NodeIndex != nodeIndex) continue;
                for (var index = 0; index < _parallelBranches.Length; index++)
                {
                    var branch = _parallelBranches[index];
                    var first = checked((int)branch.FirstSuspendedFrame);
                    var end = checked(first + (int)branch.SuspendedFrameCount);
                    if (branch.SuspendedFrameCount == 0
                        || frameIndex < first
                        || frameIndex >= end) continue;
                    branchIndex = index;
                    return true;
                }
            }
            return false;
        }

        private bool TryFindParallelOwnerNode(int branchIndex, out uint ownerNode)
        {
            ownerNode = 0;
            uint first = 0;
            for (var nodeIndex = 0; nodeIndex < _bindings.Length; nodeIndex++)
            {
                if (_bindings[nodeIndex].Kind != NativeLifecycleNodeKindV1.Parallel) continue;
                var count = _nodes[nodeIndex].ChildCount;
                var firstIndex = checked((int)first);
                if (branchIndex >= firstIndex && branchIndex - firstIndex < checked((int)count))
                {
                    ownerNode = checked((uint)nodeIndex);
                    return true;
                }
                first += count;
            }
            return false;
        }

        internal bool TryCompleteDispatch(
            NativeLifecycleDispatchTokenV1 token,
            BurstContextResult contextResult,
            NodeStatus status,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsCreated)
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (!token.IsValid || token.OwnerId != control.OwnerId || token.Generation != control.Generation
                || token.DispatchId != control.ActiveDispatchId || control.ActiveDispatchId == 0
                || control.Depth == 0)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            var frameIndex = checked((int)control.Depth - 1);
            var frame = _frames[frameIndex];
            if (frame.NodeIndex != control.ActiveDispatchNode)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            control.ActiveDispatchId = 0;
            if (contextResult != BurstContextResult.Success)
            {
                control.Faulted = 1;
                control.UpdateOpen = 0;
                _control[0] = control;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.InstanceFrames,
                    ownerId: control.OwnerId,
                    generation: control.Generation,
                    leaseId: token.DispatchId);
                return false;
            }

            switch (control.ActiveDispatchPhase)
            {
                case BurstCallbackPhase.Enter:
                    if (frame.LifecycleState != NativeFrameLifecycleStateV1.Entering)
                    {
                        failure = LifetimeFailure(control);
                        return false;
                    }
                    frame.LifecycleState = NativeFrameLifecycleStateV1.Running;
                    break;
                case BurstCallbackPhase.Tick:
                    if (frame.LifecycleState != NativeFrameLifecycleStateV1.Running
                        || status != NodeStatus.Success && status != NodeStatus.Failure && status != NodeStatus.Running)
                    {
                        failure = LifetimeFailure(control);
                        return false;
                    }
                    if (status != NodeStatus.Running)
                    {
                        frame.PendingStatus = status;
                        frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    }
                    break;
                case BurstCallbackPhase.Exit:
                    if (frame.LifecycleState != NativeFrameLifecycleStateV1.Exiting)
                    {
                        failure = LifetimeFailure(control);
                        return false;
                    }
                    ClearActivationMemory(_nodes[checked((int)frame.NodeIndex)]);
                    if (control.AbortRequested != 0) PopAbortedFrame(ref control);
                    else PopFrame(ref control, frame.PendingStatus);
                    break;
                case BurstCallbackPhase.Abort:
                    if (frame.LifecycleState != NativeFrameLifecycleStateV1.Aborting
                        || control.AbortRequested == 0)
                    {
                        failure = LifetimeFailure(control);
                        return false;
                    }
                    frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    // Distinguish an aborted Exit from a terminal Tick awaiting Exit when
                    // cancellation arrives. The latter retains its original terminal reason.
                    frame.PendingStatus = NodeStatus.Running;
                    break;
                default:
                    failure = LifetimeFailure(control);
                    return false;
            }
            if (control.Depth != 0 && control.ActiveDispatchPhase != BurstCallbackPhase.Exit)
                _frames[frameIndex] = frame;
            _control[0] = control;
            failure = default;
            return true;
        }

        private bool AdvanceLeaf(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_AdvanceLeafMarker.Auto();
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Running)
            {
                if (frame.LastUpdateId == control.UpdateId)
                {
                    if (control.RetainPhase == RetainPassActive)
                    {
                        if (!TryBeginGuardTakeover(frameIndex, RetainRestLeaf, ref control, out var tookOver))
                        {
                            result = default;
                            failure = FrameCapacityFailure(control);
                            return false;
                        }
                        if (tookOver)
                        {
                            // The tick that returned Running already counted; the first abort
                            // step of the retained branch is this step.
                            var topIndex = checked((int)control.Depth - 1);
                            var top = _frames[topIndex];
                            return AdvanceAbort(topIndex, ref top, _nodes[checked((int)top.NodeIndex)],
                                _bindings[checked((int)top.NodeIndex)].Kind, ref control, out result, out failure);
                        }
                    }
                    if (TrySuspendParallelBranch(frameIndex, ref control, out var ownerNode))
                    {
                        control.SemanticSteps++;
                        _control[0] = control;
                        result = new NativeLifecycleStepResultV1(
                            NativeLifecycleStepKindV1.ParallelBranchSuspended, ownerNode);
                        failure = default;
                        return true;
                    }
                    control.UpdateOpen = 0;
                    control.Waiting = 1;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.Waiting, frame.NodeIndex);
                    failure = default;
                    return true;
                }
                frame.LastUpdateId = control.UpdateId;
                _frames[frameIndex] = frame;
                return PublishDispatch(ref control, frame.NodeIndex, BurstCallbackPhase.Tick, out result, out failure);
            }
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Exiting)
                return PublishDispatch(ref control, frame.NodeIndex, BurstCallbackPhase.Exit, out result, out failure);
            result = default;
            failure = LifetimeFailure(control);
            return false;
        }

        private bool AdvanceAbort(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            NativeLifecycleNodeKindV1 kind,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_AdvanceAbortMarker.Auto();
            if (kind == NativeLifecycleNodeKindV1.Parallel
                && frame.LifecycleState != NativeFrameLifecycleStateV1.Exiting
                && HasRunningParallelBranch(frame, node))
            {
                if (frame.ParallelActiveBranch >= 0)
                {
                    var activeIndex = checked((int)(frame.ParallelFirstBranch + (uint)frame.ParallelActiveBranch));
                    var active = _parallelBranches[activeIndex];
                    active.State = (byte)NativeParallelChildStateV1.NotStarted;
                    _parallelBranches[activeIndex] = active;
                    frame.ParallelActiveBranch = -1;
                }
                frame.LifecycleState = NativeFrameLifecycleStateV1.Aborting;
                if (TryBeginNextParallelBranchAbort(frameIndex, ref frame, ref control))
                {
                    _frames[frameIndex] = frame;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, frame.NodeIndex);
                    failure = default;
                    return true;
                }
            }
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Exiting)
            {
                if (kind == NativeLifecycleNodeKindV1.GeneratedLeaf)
                    return PublishDispatch(ref control, frame.NodeIndex, BurstCallbackPhase.Exit, out result, out failure);
                control.SemanticSteps++;
                ClearActivationMemory(node);
                if (kind == NativeLifecycleNodeKindV1.Parallel) ClearSavedOuterAbort(frame);
                PopAbortedFrame(ref control);
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeExited, frame.NodeIndex);
                failure = default;
                return true;
            }
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Aborting)
            {
                if (kind == NativeLifecycleNodeKindV1.GeneratedLeaf)
                    return PublishDispatch(ref control, frame.NodeIndex, BurstCallbackPhase.Abort, out result, out failure);
                control.SemanticSteps++;
                frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeAborted, frame.NodeIndex);
                failure = default;
                return true;
            }
            frame.LifecycleState = NativeFrameLifecycleStateV1.Aborting;
            _frames[frameIndex] = frame;
            if (kind == NativeLifecycleNodeKindV1.GeneratedLeaf)
                return PublishDispatch(ref control, frame.NodeIndex, BurstCallbackPhase.Abort, out result, out failure);
            control.SemanticSteps++;
            frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
            _frames[frameIndex] = frame;
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeAborted, frame.NodeIndex);
            failure = default;
            return true;
        }

        private bool AdvanceParallel(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_AdvanceParallelMarker.Auto();
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Exiting)
            {
                control.SemanticSteps++;
                var status = frame.PendingStatus;
                ClearParallelBranches(frame, node);
                ClearActivationMemory(node);
                PopFrame(ref control, status);
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeExited, frame.NodeIndex);
                failure = default;
                return true;
            }
            if (frame.LifecycleState != NativeFrameLifecycleStateV1.Running)
            {
                result = default;
                failure = LifetimeFailure(control);
                return false;
            }
            if (frame.ParallelFirstBranch == 0 && ParallelFirstBranch(frame.NodeIndex) != 0)
                frame.ParallelFirstBranch = ParallelFirstBranch(frame.NodeIndex);
            if (frame.ParallelActiveBranch == 0 && frame.ParallelVisitCursor == 0 && frame.HasPendingChildResult == 0)
                frame.ParallelActiveBranch = -1;

            if (frame.HasPendingChildResult != 0)
            {
                if (frame.ParallelActiveBranch < 0 || frame.ParallelActiveBranch >= node.ChildCount)
                {
                    result = default;
                    failure = LifetimeFailure(control);
                    return false;
                }
                var branchIndex = checked((int)(frame.ParallelFirstBranch + (uint)frame.ParallelActiveBranch));
                var branch = _parallelBranches[branchIndex];
                branch.State = (byte)(frame.PendingStatus == NodeStatus.Success
                    ? NativeParallelChildStateV1.Success : NativeParallelChildStateV1.Failure);
                branch.SuspendedFrameCount = 0;
                _parallelBranches[branchIndex] = branch;
                frame.ParallelActiveBranch = -1;
                frame.HasPendingChildResult = 0;
                control.SemanticSteps++;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                failure = default;
                return true;
            }

            if (frame.ParallelVisitCursor >= node.ChildCount)
            {
                NativeParallelPolicyEvaluatorV1.TryDecode(
                    _configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, node.ChildCount, out var configuration);
                if (!NativeParallelPolicyEvaluatorV1.TryEvaluate(
                    configuration, _parallelBranches.AsReadOnly(), frame.ParallelFirstBranch, node.ChildCount, out var decision))
                {
                    result = default;
                    failure = LifetimeFailure(control);
                    return false;
                }
                frame.ParallelVisitCursor = 0;
                control.SemanticSteps++;
                if (decision.IsTerminal)
                {
                    frame.PendingStatus = decision.Status;
                    if (HasRunningParallelBranch(frame, node))
                    {
                        if (!TryBeginNextParallelBranchAbort(frameIndex, ref frame, ref control))
                        {
                            result = default;
                            failure = LifetimeFailure(control);
                            return false;
                        }
                        control.HasAbortSource = 1;
                        control.AbortSourceNode = frame.NodeIndex;
                    }
                    else frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    _frames[frameIndex] = frame;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                    failure = default;
                    return true;
                }
                _frames[frameIndex] = frame;
                if (control.RetainPhase == RetainPassActive)
                {
                    if (!TryBeginGuardTakeover(frameIndex, RetainRestVisit, ref control, out var tookOver))
                    {
                        result = default;
                        failure = FrameCapacityFailure(control);
                        return false;
                    }
                    if (tookOver)
                    {
                        _control[0] = control;
                        result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                        failure = default;
                        return true;
                    }
                }
                return RestAfterParallelVisit(frameIndex, ref frame, ref control, out result, out failure);
            }


            var ordinal = frame.ParallelVisitCursor++;
            var selectedIndex = checked((int)(frame.ParallelFirstBranch + ordinal));
            var selected = _parallelBranches[selectedIndex];
            frame.ParallelActiveBranch = checked((int)ordinal);
            _frames[frameIndex] = frame;
            control.SemanticSteps++;
            if ((NativeParallelChildStateV1)selected.State == NativeParallelChildStateV1.Running)
            {
                if (!RestoreSuspendedBranch(frameIndex, selectedIndex, ref control))
                {
                    result = default;
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                        NativeResourceKindV1.InstanceFrames);
                    return false;
                }
                if (!PrepareUpdateStart(ref control, 0, frameIndex + 1))
                {
                    result = default;
                    failure = FrameCapacityFailure(control);
                    return false;
                }
            }
            else if ((NativeParallelChildStateV1)selected.State == NativeParallelChildStateV1.NotStarted)
            {
                if (control.Depth >= control.SuspendedFrameStart)
                {
                    result = default;
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                        NativeResourceKindV1.InstanceFrames);
                    return false;
                }
                _frames[checked((int)control.Depth)] = new NativeFrameStateV1
                {
                    NodeIndex = _children[checked((int)(node.ChildOffset + ordinal))],
                    ParentFrameIndex = checked((uint)frameIndex),
                    LifecycleState = NativeFrameLifecycleStateV1.Inactive,
                };
                control.Depth++;
            }
            else
            {
                frame.ParallelActiveBranch = -1;
                _frames[frameIndex] = frame;
            }
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, frame.NodeIndex);
            failure = default;
            return true;
        }

        private bool AdvanceComposite(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            NativeLifecycleNodeKindV1 kind,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_AdvanceCompositeMarker.Auto();
            if (kind == NativeLifecycleNodeKindV1.Parallel)
                return AdvanceParallel(frameIndex, ref frame, node, ref control, out result, out failure);
            if (IsDecorator(kind))
                return AdvanceDecorator(frameIndex, ref frame, node, kind, ref control, out result, out failure);
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Exiting)
            {
                control.SemanticSteps++;
                var status = frame.PendingStatus;
                ClearActivationMemory(node);
                PopFrame(ref control, status);
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeExited, frame.NodeIndex);
                failure = default;
                return true;
            }
            if (frame.LifecycleState != NativeFrameLifecycleStateV1.Running)
            {
                result = default;
                failure = LifetimeFailure(control);
                return false;
            }

            if (control.RetainPhase != RetainNone && control.RetainOwnerDepth == (uint)frameIndex + 1u)
            {
                if (control.RetainPhase == RetainPassPending)
                    return StartGuardPass(frameIndex, ref frame, node, ref control, out result, out failure);
                if (control.RetainPhase != RetainPassActive)
                {
                    result = default;
                    failure = LifetimeFailure(control);
                    return false;
                }
                if (frame.HasPendingChildResult != 0)
                    return AcceptGuard(frameIndex, ref frame, node, kind, ref control, out result, out failure);
                if (ReadCursor(node) == ReadUInt32(node, 4) - 1u)
                    return ResumeRetainedBranch(frameIndex, ref frame, node, ref control, out result, out failure);
            }

            if (frame.HasPendingChildResult != 0)
            {
                control.SemanticSteps++;
                var cursor = ReadCursor(node);
                var sequence = IsSequence(kind);
                var terminal = sequence
                    ? frame.PendingStatus == NodeStatus.Failure
                    : frame.PendingStatus == NodeStatus.Success;
                if (terminal)
                {
                    frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                }
                else
                {
                    cursor++;
                    WriteCursor(node, cursor);
                    if (cursor >= node.ChildCount)
                    {
                        frame.PendingStatus = sequence
                            ? NodeStatus.Success : NodeStatus.Failure;
                        frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    }
                }
                frame.HasPendingChildResult = 0;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                failure = default;
                return true;
            }

            var childCursor = ReadCursor(node);
            if (childCursor >= node.ChildCount)
            {
                control.SemanticSteps++;
                frame.PendingStatus = IsSequence(kind)
                    ? NodeStatus.Success : NodeStatus.Failure;
                frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                failure = default;
                return true;
            }

            if (control.Depth >= _frames.Length)
            {
                result = default;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.InstanceFrames,
                    control.Depth + 1u,
                    (uint)_frames.Length);
                return false;
            }
            var childNodeIndex = _children[checked((int)(node.ChildOffset + childCursor))];
            control.SemanticSteps++;
            _frames[checked((int)control.Depth)] = new NativeFrameStateV1
            {
                NodeIndex = childNodeIndex,
                ParentFrameIndex = checked((uint)frameIndex),
                LifecycleState = NativeFrameLifecycleStateV1.Inactive,
            };
            control.Depth++;
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, frame.NodeIndex);
            failure = default;
            return true;
        }

        private bool AdvanceDecorator(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            NativeLifecycleNodeKindV1 kind,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_AdvanceDecoratorMarker.Auto();
            if (frame.LifecycleState == NativeFrameLifecycleStateV1.Exiting)
            {
                control.SemanticSteps++;
                var status = frame.PendingStatus;
                ClearActivationMemory(node);
                PopFrame(ref control, status);
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeExited, frame.NodeIndex);
                failure = default;
                return true;
            }
            if (frame.LifecycleState != NativeFrameLifecycleStateV1.Running)
            {
                result = default;
                failure = LifetimeFailure(control);
                return false;
            }
            if (frame.HasPendingChildResult != 0)
            {
                control.SemanticSteps++;
                if (kind == NativeLifecycleNodeKindV1.Repeater)
                {
                    NativeDecoratorPolicyV1.TryDecodeRepeater(
                        _configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out var configuration);
                    var completed = ReadCursor(node);
                    if (frame.PendingStatus == NodeStatus.Failure && configuration.StopOnFailure)
                    {
                        frame.PendingStatus = NodeStatus.Failure;
                        frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    }
                    else
                    {
                        completed++;
                        WriteCursor(node, completed);
                        if (completed >= configuration.Count)
                        {
                            frame.PendingStatus = NodeStatus.Success;
                            frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                        }
                    }
                }
                else
                {
                    if (kind == NativeLifecycleNodeKindV1.Cooldown)
                    {
                        NativeDecoratorPolicyV1.TryDecodeCooldown(
                            _configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out var cooldown);
                        if (frame.PendingStatus == NodeStatus.Success
                            && cooldown.StartPolicy == NativeCooldownStartPolicyV1.OnSuccessfulExit)
                        {
                            if (!NativeDecoratorPolicyV1.TryDeadline(control.TimeMicroseconds, cooldown.DurationMicroseconds, out var deadline))
                            {
                                result = default;
                                failure = TimeOverflowFailure(control);
                                return false;
                            }
                            WriteInt64(node, deadline);
                            _cooldownInitialized[checked((int)frame.NodeIndex)] = 1;
                        }
                    }
                    frame.PendingStatus = NativeDecoratorPolicyV1.Transform(ToDecoratorKind(kind), frame.PendingStatus);
                    frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                }
                frame.HasPendingChildResult = 0;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                failure = default;
                return true;
            }

            if (kind == NativeLifecycleNodeKindV1.Timeout
                && control.TimeMicroseconds >= ReadInt64(node))
            {
                NativeDecoratorPolicyV1.TryDecodeTimeout(
                    _configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out var timeout);
                control.SemanticSteps++;
                frame.PendingStatus = timeout.Result;
                frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[frameIndex] = frame;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                failure = default;
                return true;
            }
            if (kind == NativeLifecycleNodeKindV1.Cooldown)
            {
                NativeDecoratorPolicyV1.TryDecodeCooldown(
                    _configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out var cooldown);
                if (_cooldownInitialized[checked((int)frame.NodeIndex)] != 0
                    && control.TimeMicroseconds < ReadInt64(node))
                {
                    control.SemanticSteps++;
                    frame.PendingStatus = cooldown.Result;
                    frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    _frames[frameIndex] = frame;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                    failure = default;
                    return true;
                }
                if (cooldown.StartPolicy == NativeCooldownStartPolicyV1.OnEnter)
                {
                    frame.CooldownDeadlinePending = 1;
                    _frames[frameIndex] = frame;
                }
            }
            if (control.Depth >= _frames.Length)
            {
                result = default;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.InstanceFrames,
                    control.Depth + 1u,
                    (uint)_frames.Length);
                return false;
            }
            control.SemanticSteps++;
            _frames[(int)control.Depth] = new NativeFrameStateV1
            {
                NodeIndex = _children[(int)node.ChildOffset],
                ParentFrameIndex = (uint)frameIndex,
                LifecycleState = NativeFrameLifecycleStateV1.Inactive,
            };
            control.Depth++;
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, frame.NodeIndex);
            failure = default;
            return true;
        }

        private bool TryActivate(
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            ref NativeLifecycleControlV1 control,
            out NativeRuntimeFailureV1 failure)
        {
            var nodeIndex = checked((int)frame.NodeIndex);
            if (_generations[nodeIndex] == uint.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceGenerations,
                    uint.MaxValue,
                    uint.MaxValue);
                return false;
            }
            ClearActivationMemory(node);
            if (control.Depth >= 2)
            {
                var parentIndex = checked((int)control.Depth - 2);
                var parent = _frames[parentIndex];
                if (parent.CooldownDeadlinePending != 0)
                {
                    var parentNode = _nodes[checked((int)parent.NodeIndex)];
                    NativeDecoratorPolicyV1.TryDecodeCooldown(
                        _configuration.AsReadOnly(), parentNode.ConfigOffset, parentNode.ConfigSize, out var cooldown);
                    if (!NativeDecoratorPolicyV1.TryDeadline(control.TimeMicroseconds, cooldown.DurationMicroseconds, out var deadline))
                    {
                        failure = TimeOverflowFailure(control);
                        return false;
                    }
                    WriteInt64(parentNode, deadline);
                    _cooldownInitialized[checked((int)parent.NodeIndex)] = 1;
                    parent.CooldownDeadlinePending = 0;
                    _frames[parentIndex] = parent;
                }
            }
            var generation = _generations[nodeIndex] + 1u;
            _generations[nodeIndex] = generation;
            frame.ActivationGeneration = generation;
            frame.LifecycleState = NativeFrameLifecycleStateV1.Entering;
            frame.ChildCursor = 0u;
            frame.HasPendingChildResult = 0;
            failure = default;
            return true;
        }

        private bool PublishDispatch(
            ref NativeLifecycleControlV1 control,
            uint nodeIndex,
            BurstCallbackPhase phase,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            if (control.NextDispatchId == ulong.MaxValue)
            {
                result = default;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter);
                return false;
            }
            var dispatchId = ++control.NextDispatchId;
            control.ActiveDispatchId = dispatchId;
            control.ActiveDispatchNode = nodeIndex;
            control.ActiveDispatchPhase = phase;
            control.SemanticSteps++;
            _control[0] = control;
            var token = new NativeLifecycleDispatchTokenV1(control.OwnerId, control.Generation, dispatchId);
            var pendingStatus = _frames[checked((int)control.Depth - 1)].PendingStatus;
            var exitReason = control.AbortRequested != 0 && pendingStatus == NodeStatus.Running
                ? BurstNodeExitReason.Aborted
                : pendingStatus == NodeStatus.Success ? BurstNodeExitReason.Success : BurstNodeExitReason.Failure;
            result = new NativeLifecycleStepResultV1(
                NativeLifecycleStepKindV1.DispatchRequired, nodeIndex, phase, token,
                exitReason: exitReason, abortReason: control.AbortReason, hasDispatchReasons: true);
            failure = default;
            return true;
        }

        private void PopFrame(ref NativeLifecycleControlV1 control, NodeStatus status)
        {
            var poppedIndex = checked((int)control.Depth - 1);
            _frames[poppedIndex] = default;
            control.Depth--;
            if (control.Depth == 0)
            {
                control.RootStatus = status;
                control.HasRootStatus = 1;
                return;
            }
            var parentIndex = checked((int)control.Depth - 1);
            var parent = _frames[parentIndex];
            parent.PendingStatus = status;
            parent.HasPendingChildResult = 1;
            _frames[parentIndex] = parent;
        }

        private void PopAbortedFrame(ref NativeLifecycleControlV1 control)
        {
            var poppedIndex = checked((int)control.Depth - 1);
            _frames[poppedIndex] = default;
            control.Depth--;
            if (control.ReactiveResetPending != 0 && control.Depth == control.AbortTargetDepth) return;
            if ((control.RetainPhase == RetainAbortEnd || control.RetainPhase == RetainAbortTakeover)
                && control.Depth == control.RetainOwnerDepth)
            {
                CompleteRetainedAbortBoundary(ref control);
                return;
            }
            if (control.Depth != 0) return;
            control.AbortRequested = 0;
            control.RootAborted = 1;
            control.HasRootStatus = 0;
        }

        private uint ReadCursor(NativeCompiledNodeRecordV1 node)
        {
            if (node.InstanceMemorySize < 4u) return 0u;
            var offset = checked((int)node.InstanceMemoryOffset);
            return (uint)(_memory[offset]
                | _memory[offset + 1] << 8
                | _memory[offset + 2] << 16
                | _memory[offset + 3] << 24);
        }

        private void WriteCursor(NativeCompiledNodeRecordV1 node, uint value)
        {
            if (node.InstanceMemorySize < 4u) return;
            var offset = checked((int)node.InstanceMemoryOffset);
            _memory[offset] = (byte)value;
            _memory[offset + 1] = (byte)(value >> 8);
            _memory[offset + 2] = (byte)(value >> 16);
            _memory[offset + 3] = (byte)(value >> 24);
        }

        private uint ParallelFirstBranch(uint nodeIndex)
        {
            uint first = 0;
            for (var index = 0; index < nodeIndex; index++)
                if (_bindings[index].Kind == NativeLifecycleNodeKindV1.Parallel)
                    first += _nodes[index].ChildCount;
            return first;
        }

        private bool TrySuspendParallelBranch(
            int leafFrameIndex,
            ref NativeLifecycleControlV1 control,
            out uint ownerNodeIndex)
        {
            ownerNodeIndex = 0;
            for (var ownerIndex = leafFrameIndex - 1; ownerIndex >= 0; ownerIndex--)
            {
                var owner = _frames[ownerIndex];
                if (_bindings[checked((int)owner.NodeIndex)].Kind != NativeLifecycleNodeKindV1.Parallel
                    || owner.ParallelActiveBranch < 0)
                    continue;
                var count = checked((uint)(leafFrameIndex - ownerIndex));
                if (count > control.SuspendedFrameStart
                    || control.SuspendedFrameStart - count < ownerIndex + 1)
                    return false;
                var start = control.SuspendedFrameStart - count;
                for (var offset = count; offset != 0; offset--)
                    _frames[checked((int)(start + offset - 1))] = _frames[ownerIndex + checked((int)offset)];
                var branchIndex = checked((int)(owner.ParallelFirstBranch + (uint)owner.ParallelActiveBranch));
                var branch = _parallelBranches[branchIndex];
                branch.NodeIndex = _frames[ownerIndex + 1].NodeIndex;
                branch.State = (byte)NativeParallelChildStateV1.Running;
                branch.FirstSuspendedFrame = start;
                branch.SuspendedFrameCount = count;
                _parallelBranches[branchIndex] = branch;
                owner.ParallelActiveBranch = -1;
                _frames[ownerIndex] = owner;
                control.Depth = checked((uint)ownerIndex + 1u);
                control.SuspendedFrameStart = start;
                ownerNodeIndex = owner.NodeIndex;
                return true;
            }
            return false;
        }

        private bool RestoreSuspendedBranch(int ownerFrameIndex, int branchIndex, ref NativeLifecycleControlV1 control)
        {
            var branch = _parallelBranches[branchIndex];
            if (!TryRestoreSegment(branch.FirstSuspendedFrame, branch.SuspendedFrameCount, ref control))
                return false;
            branch = _parallelBranches[branchIndex];
            branch.FirstSuspendedFrame = 0;
            branch.SuspendedFrameCount = 0;
            _parallelBranches[branchIndex] = branch;
            return true;
        }

        /// <summary>
        /// Moves one segment of the suspended-frame region back onto the active stack and closes
        /// the gap: segments below it move up, and every segment record (parallel branches and the
        /// parked retaining segments) follows. The move is an in-place rotation for every program
        /// (P10-014, review finding D2): the original copy-then-shift overwrote a lower segment when
        /// the frame array had fewer free frames than the restored segment is long. Where the copy
        /// was correct, both leave the same active and suspended frames.
        /// </summary>
        private bool TryRestoreSegment(uint first, uint count, ref NativeLifecycleControlV1 control)
        {
            if (count == 0 || first < control.SuspendedFrameStart
                || first + count > _frames.Length
                || control.Depth > first)
                return false;
            var removedStart = first;
            var removedCount = count;
            MoveSegmentToStackTop(_frames, control.SuspendedFrameStart, first, count, control.Depth);
            for (var index = 0; index < _parallelBranches.Length; index++)
            {
                var other = _parallelBranches[index];
                if (other.SuspendedFrameCount != 0 && other.FirstSuspendedFrame < removedStart)
                {
                    other.FirstSuspendedFrame += removedCount;
                    _parallelBranches[index] = other;
                }
            }
            if (control.RetainedFrameCount != 0 && control.RetainedFirstFrame < removedStart)
                control.RetainedFirstFrame += removedCount;
            if (control.HeldFrameCount != 0 && control.HeldFirstFrame < removedStart)
                control.HeldFirstFrame += removedCount;
            control.Depth += removedCount;
            control.SuspendedFrameStart += removedCount;
            return true;
        }

        /// <summary>
        /// The frame move of <see cref="TryRestoreSegment"/>: [<paramref name="suspendedStart"/>,
        /// <paramref name="first"/>) holds the lower segments L and [first, first + count) the
        /// segment S. "L S" is rotated into "S L" in place, then S is copied down to
        /// <paramref name="destination"/> (the stack top). Internal for the P10-014 test that
        /// compares it with the original copy-then-shift.
        /// </summary>
        internal static void MoveSegmentToStackTop(
            NativeArray<NativeFrameStateV1> frames, uint suspendedStart, uint first, uint count, uint destination)
        {
            var lowStart = checked((int)suspendedStart);
            var segmentStart = checked((int)first);
            var segmentEnd = checked((int)(first + count));
            ReverseFrames(frames, lowStart, segmentStart);
            ReverseFrames(frames, segmentStart, segmentEnd);
            ReverseFrames(frames, lowStart, segmentEnd);
            for (uint offset = 0; offset < count; offset++)
                frames[checked((int)(destination + offset))] = frames[checked((int)(suspendedStart + offset))];
        }

        private static void ReverseFrames(NativeArray<NativeFrameStateV1> frames, int start, int end)
        {
            for (int low = start, high = end - 1; low < high; low++, high--)
            {
                var swap = frames[low];
                frames[low] = frames[high];
                frames[high] = swap;
            }
        }

        /// <summary>Moves the active frames from <paramref name="fromIndex"/> up into the suspended-frame region.</summary>
        private bool TryPushSegment(int fromIndex, ref NativeLifecycleControlV1 control, out uint first, out uint count)
        {
            first = 0;
            count = 0;
            if (fromIndex < 0 || (uint)fromIndex >= control.Depth) return false;
            count = control.Depth - (uint)fromIndex;
            if (count > control.SuspendedFrameStart
                || control.SuspendedFrameStart - count < (uint)fromIndex)
                return false;
            var start = control.SuspendedFrameStart - count;
            for (var offset = count; offset != 0; offset--)
                _frames[checked((int)(start + offset - 1))] = _frames[fromIndex + checked((int)offset) - 1];
            control.Depth = (uint)fromIndex;
            control.SuspendedFrameStart = start;
            first = start;
            return true;
        }

        private void ClearParallelBranches(NativeFrameStateV1 frame, NativeCompiledNodeRecordV1 node)
        {
            for (uint ordinal = 0; ordinal < node.ChildCount; ordinal++)
                _parallelBranches[checked((int)(frame.ParallelFirstBranch + ordinal))] =
                    new NativeParallelBranchStateV1 { CapacityOrdinal = frame.ParallelFirstBranch + ordinal };
        }

        private bool HasRunningParallelBranch(NativeFrameStateV1 frame, NativeCompiledNodeRecordV1 node)
        {
            for (uint ordinal = 0; ordinal < node.ChildCount; ordinal++)
                if ((NativeParallelChildStateV1)_parallelBranches[checked((int)(frame.ParallelFirstBranch + ordinal))].State
                    == NativeParallelChildStateV1.Running)
                    return true;
            return false;
        }

        private bool TryBeginNextParallelBranchAbort(
            int ownerFrameIndex,
            ref NativeFrameStateV1 ownerFrame,
            ref NativeLifecycleControlV1 control)
        {
            var node = _nodes[checked((int)ownerFrame.NodeIndex)];
            for (var ordinal = checked((int)node.ChildCount) - 1; ordinal >= 0; ordinal--)
            {
                var branchIndex = checked((int)ownerFrame.ParallelFirstBranch + ordinal);
                if ((NativeParallelChildStateV1)_parallelBranches[branchIndex].State != NativeParallelChildStateV1.Running)
                    continue;
                ownerFrame.ParallelActiveBranch = ordinal;
                if (!RestoreSuspendedBranch(ownerFrameIndex, branchIndex, ref control)) return false;
                SaveOuterAbort(ownerFrame, ref control);
                control.AbortRequested = 1;
                control.AbortReason = ownerFrame.LifecycleState == NativeFrameLifecycleStateV1.Aborting
                    ? control.AbortReason : BurstNodeAbortReason.Explicit;
                control.AbortTargetDepth = checked((uint)ownerFrameIndex + 1u);
                control.ReactiveResetPending = 0;
                control.TimeoutPending = 0;
                control.ParallelAbortPending = 1;
                control.ParallelAbortOwnerDepth = checked((uint)ownerFrameIndex + 1u);
                control.SemanticSteps++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Before a Parallel aborts its first suspended branch, keeps the abort state it interrupts
        /// on its first branch record; later branches of the same sequence keep the first save.
        /// </summary>
        private void SaveOuterAbort(NativeFrameStateV1 owner, ref NativeLifecycleControlV1 control)
        {
            var index = checked((int)owner.ParallelFirstBranch);
            var record = _parallelBranches[index];
            if ((record.AbortResumeFlags & ResumeActive) != 0) return;
            var flags = ResumeActive;
            if (control.AbortRequested != 0) flags |= ResumeAbortRequested;
            if (control.ReactiveResetPending != 0) flags |= ResumeReactiveReset;
            if (control.TimeoutPending != 0) flags |= ResumeTimeout;
            if (control.ParallelAbortPending != 0) flags |= ResumeParallelAbort;
            if (control.HasAbortSource != 0) flags |= ResumeHasSource;
            record.AbortResumeFlags = flags;
            record.AbortResumeTargetDepth = control.AbortTargetDepth;
            record.AbortResumeSourceNode = control.AbortSourceNode;
            _parallelBranches[index] = record;
        }

        /// <summary>
        /// After the last branch of a Parallel has exited, restores the abort state saved by
        /// <see cref="SaveOuterAbort"/>. Without an interrupted abort (a terminal decision) the
        /// Parallel's abort simply ends, as before P10-014.
        /// </summary>
        private void RestoreOuterAbort(NativeFrameStateV1 owner, bool aborted, ref NativeLifecycleControlV1 control)
        {
            var index = checked((int)owner.ParallelFirstBranch);
            var record = _parallelBranches[index];
            var flags = record.AbortResumeFlags;
            var targetDepth = record.AbortResumeTargetDepth;
            var sourceNode = record.AbortResumeSourceNode;
            record.AbortResumeFlags = 0;
            record.AbortResumeTargetDepth = 0;
            record.AbortResumeSourceNode = 0;
            _parallelBranches[index] = record;
            if ((flags & ResumeAbortRequested) == 0)
            {
                control.ParallelAbortPending = 0;
                control.AbortTargetDepth = 0;
                if (!aborted) control.AbortRequested = 0;
                return;
            }
            control.AbortRequested = 1;
            control.AbortTargetDepth = targetDepth;
            control.ReactiveResetPending = (byte)((flags & ResumeReactiveReset) != 0 ? 1 : 0);
            control.TimeoutPending = (byte)((flags & ResumeTimeout) != 0 ? 1 : 0);
            control.ParallelAbortPending = (byte)((flags & ResumeParallelAbort) != 0 ? 1 : 0);
            control.HasAbortSource = (byte)((flags & ResumeHasSource) != 0 ? 1 : 0);
            control.AbortSourceNode = sourceNode;
        }

        /// <summary>
        /// A Parallel that leaves through an abort without finishing its own branch-abort sequence
        /// (a tree stop reached it from below) drops what it had saved.
        /// </summary>
        private void ClearSavedOuterAbort(NativeFrameStateV1 owner)
        {
            if (!_parallelBranches.IsCreated || owner.ParallelFirstBranch >= _parallelBranches.Length) return;
            var index = checked((int)owner.ParallelFirstBranch);
            var record = _parallelBranches[index];
            if (record.AbortResumeFlags == 0) return;
            record.AbortResumeFlags = 0;
            record.AbortResumeTargetDepth = 0;
            record.AbortResumeSourceNode = 0;
            _parallelBranches[index] = record;
        }

        /// <summary>
        /// A tree stop that replaces a pending abort also replaces what every Parallel in the
        /// middle of its branch aborts will resume: the stop continues to the root.
        /// </summary>
        private void ReplaceSavedOuterAborts()
        {
            for (var index = 0; index < _parallelBranches.Length; index++)
            {
                var record = _parallelBranches[index];
                if ((record.AbortResumeFlags & ResumeActive) == 0) continue;
                record.AbortResumeFlags = ResumeActive | ResumeAbortRequested;
                record.AbortResumeTargetDepth = 0;
                record.AbortResumeSourceNode = 0;
                _parallelBranches[index] = record;
            }
        }

        private void ClearActivationMemory(NativeCompiledNodeRecordV1 node)
        {
            if (node.MemoryLifetime != NodeMemoryLifetime.Activation) return;
            for (uint index = 0; index < node.InstanceMemorySize; index++)
                _memory[checked((int)(node.InstanceMemoryOffset + index))] = 0;
        }

        private bool IsCreated => _control.IsCreated && _control.Length == 1 && _control[0].OwnerId != 0;

        internal ulong SchedulingOwnerId => IsCreated ? _control[0].OwnerId : 0;

        internal bool TryAttachCreatedEmptyJobStorage(
            NativeArray<byte> storage,
            NativeArray<NativeParallelBranchStateV1> parallelStorage)
        {
            if (!storage.IsCreated || storage.Length != 0
                || !parallelStorage.IsCreated || parallelStorage.Length != 0) return false;
            if (!_cooldownInitialized.IsCreated) _cooldownInitialized = storage;
            if (!_parallelBranches.IsCreated) _parallelBranches = parallelStorage;
            return true;
        }

        /// <summary>
        /// P10-009: returns this machine's mutable instance state to exactly what <see cref="TryCreate(NativeArray{NativeCompiledNodeRecordV1}, NativeArray{uint}, NativeArray{NativeLifecycleNodeBindingV1}, NativeArray{byte}, NativeArray{NativeFrameStateV1}, NativeArray{uint}, NativeArray{NativeLifecycleControlV1}, NativeArray{byte}, NativeArray{byte}, NativeArray{NativeParallelBranchStateV1}, out NativeLifecycleMachineV1, out NativeRuntimeFailureV1)"/>
        /// leaves over freshly allocated (zeroed) storage, with a new owner id: node memory, frames,
        /// generations and cooldown flags zero, parallel branch records re-numbered, one fresh
        /// control record. Immutable program data (nodes, children, bindings, configuration) is kept.
        /// The simulation container's restart in place; never used by hosts.
        /// </summary>
        internal bool TryResetInstance(out NativeRuntimeFailureV1 failure)
        {
            if (!IsCreated)
            {
                failure = LifetimeFailure();
                return false;
            }
            if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter);
                return false;
            }
            for (var index = 0; index < _memory.Length; index++) _memory[index] = 0;
            for (var index = 0; index < _frames.Length; index++) _frames[index] = default;
            for (var index = 0; index < _generations.Length; index++) _generations[index] = 0u;
            if (_cooldownInitialized.IsCreated)
                for (var index = 0; index < _cooldownInitialized.Length; index++) _cooldownInitialized[index] = 0;
            if (_parallelBranches.IsCreated)
                for (var index = 0; index < _parallelBranches.Length; index++)
                    _parallelBranches[index] = new NativeParallelBranchStateV1 { CapacityOrdinal = (uint)index };
            _control[0] = new NativeLifecycleControlV1
            {
                OwnerId = ownerId,
                Generation = 1u,
                SuspendedFrameStart = (uint)_frames.Length,
            };
            failure = default;
            return true;
        }

        /// <summary>P10-009: test-only read of the mutable instance state, owner id excluded, for the reset-equals-fresh proof.</summary>
        internal void CopyInstanceStateForTests(System.Collections.Generic.List<byte> into)
        {
            into.Clear();
            for (var index = 0; index < _memory.Length; index++) into.Add(_memory[index]);
            AppendBytes(into, _frames);
            AppendBytes(into, _generations);
            if (_cooldownInitialized.IsCreated) AppendBytes(into, _cooldownInitialized);
            if (_parallelBranches.IsCreated) AppendBytes(into, _parallelBranches);
            var control = _control[0];
            control.OwnerId = 0;
            var controls = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Temp);
            controls[0] = control;
            AppendBytes(into, controls);
            controls.Dispose();
        }

        private static void AppendBytes<T>(System.Collections.Generic.List<byte> into, NativeArray<T> array) where T : struct
        {
            var bytes = array.Reinterpret<byte>(Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>());
            for (var index = 0; index < bytes.Length; index++) into.Add(bytes[index]);
        }

        private static bool ValidBinding(
            NativeCompiledNodeRecordV1 node,
            NativeLifecycleNodeKindV1 kind,
            NativeArray<byte> configuration)
        {
            if (kind == NativeLifecycleNodeKindV1.GeneratedLeaf)
                return node.ChildCount == 0 && node.NodeTypeId != MemorySequenceTypeId && node.NodeTypeId != MemorySelectorTypeId
                    && node.NodeTypeId != ParallelTypeId
                    && !NativeReactivePolicyV1.Matches(node.NodeTypeId, NativeLifecycleNodeKindV1.ReactiveSequence)
                    && !NativeReactivePolicyV1.Matches(node.NodeTypeId, NativeLifecycleNodeKindV1.ReactiveSelector)
                    && !NativeReactivePolicyV1.IsRetainingTypeId(node.NodeTypeId)
                    && !NativeDecoratorPolicyV1.TryGetKind(node.NodeTypeId, out _);
            if (!IsDecorator(kind))
            {
                if (kind == NativeLifecycleNodeKindV1.Parallel)
                    return node.NodeTypeId == ParallelTypeId && node.ChildCount != 0
                        && node.InstanceMemorySize == 8u && node.InstanceMemoryAlignment == 4u
                        && node.MemoryLifetime == NodeMemoryLifetime.Activation
                        && configuration.IsCreated
                        && NativeParallelPolicyEvaluatorV1.TryDecode(
                            configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, node.ChildCount, out _);
                if (NativeReactivePolicyV1.IsRetaining(kind))
                    return node.InstanceMemorySize == 8u && node.InstanceMemoryAlignment == 4u
                        && node.InstanceMemoryOffset % 4u == 0u
                        && node.MemoryLifetime == NodeMemoryLifetime.Activation
                        && NativeReactivePolicyV1.Matches(node.NodeTypeId, kind);
                if (node.InstanceMemorySize != 4u || node.InstanceMemoryAlignment != 4u
                    || node.MemoryLifetime != NodeMemoryLifetime.Activation) return false;
                if (kind == NativeLifecycleNodeKindV1.MemorySequence) return node.NodeTypeId == MemorySequenceTypeId;
                if (kind == NativeLifecycleNodeKindV1.MemorySelector) return node.NodeTypeId == MemorySelectorTypeId;
                return NativeReactivePolicyV1.Matches(node.NodeTypeId, kind);
            }
            if (node.ChildCount != 1 || !NativeDecoratorPolicyV1.TryGetKind(node.NodeTypeId, out var decorator)
                || decorator != ToDecoratorKind(kind)) return false;
            if (kind == NativeLifecycleNodeKindV1.Inverter || kind == NativeLifecycleNodeKindV1.Succeeder || kind == NativeLifecycleNodeKindV1.Failer)
                return node.InstanceMemorySize == 0 && node.InstanceMemoryAlignment == 1 && node.ConfigSize == 0;
            if (!configuration.IsCreated || node.ConfigOffset > configuration.Length || node.ConfigSize > configuration.Length - node.ConfigOffset)
                return false;
            if (kind == NativeLifecycleNodeKindV1.Repeater)
                return node.InstanceMemorySize == 4 && node.InstanceMemoryAlignment == 4
                    && node.MemoryLifetime == NodeMemoryLifetime.Activation
                    && NativeDecoratorPolicyV1.TryDecodeRepeater(configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out _);
            if (kind == NativeLifecycleNodeKindV1.Timeout)
                return node.InstanceMemorySize == 8 && node.InstanceMemoryAlignment == 8
                    && node.MemoryLifetime == NodeMemoryLifetime.Activation
                    && NativeDecoratorPolicyV1.TryDecodeTimeout(configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out _);
            return node.InstanceMemorySize == 8 && node.InstanceMemoryAlignment == 8
                && node.MemoryLifetime == NodeMemoryLifetime.Instance
                && NativeDecoratorPolicyV1.TryDecodeCooldown(configuration.AsReadOnly(), node.ConfigOffset, node.ConfigSize, out _);
        }

        private static bool IsSequence(NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.MemorySequence || kind == NativeLifecycleNodeKindV1.ReactiveSequence
                || kind == NativeLifecycleNodeKindV1.RetainingReactiveSequence;

        private static bool IsDecorator(NativeLifecycleNodeKindV1 kind)
            => kind >= NativeLifecycleNodeKindV1.Inverter && kind <= NativeLifecycleNodeKindV1.Cooldown;

        private static NativeDecoratorKindV1 ToDecoratorKind(NativeLifecycleNodeKindV1 kind)
            => (NativeDecoratorKindV1)((byte)kind - (byte)NativeLifecycleNodeKindV1.Inverter);

        /// <summary>
        /// Update-start processing for the active frames from <paramref name="scanStart"/>
        /// (reference-executor-machine-v1.md, "Retaining reactive composites"): an expired
        /// timeout, else a v1 reactive replacement, each shallowest first, but not below the
        /// shallowest retaining composite at or after <paramref name="retainStart"/> that still
        /// holds a running child and has not had its guard pass in this update; if neither aborts
        /// that composite, its running branch is parked for the guard pass. Without retaining
        /// composites this is exactly the v1 processing. Returns false only on a frame capacity
        /// failure, which one frame per node makes unreachable.
        /// </summary>
        private bool PrepareUpdateStart(ref NativeLifecycleControlV1 control, int scanStart, int retainStart)
        {
            if (control.Depth < 2 || control.AbortRequested != 0) return true;
            var cut = FindRetainingCut(control, retainStart);
            var end = cut < 0 ? checked((int)control.Depth - 1) : cut;
            if (ScheduleExpiredTimeout(ref control, scanStart, end)) return true;
            if (ScheduleReactiveReplacement(ref control, scanStart, end)) return true;
            return cut < 0 || ParkRetainedBranch(cut, ref control);
        }

        private int FindRetainingCut(NativeLifecycleControlV1 control, int start)
        {
            for (var depth = start; depth < control.Depth - 1; depth++)
            {
                var frame = _frames[depth];
                if (NativeReactivePolicyV1.IsRetaining(_bindings[checked((int)frame.NodeIndex)].Kind)
                    && frame.LifecycleState == NativeFrameLifecycleStateV1.Running
                    && frame.LastUpdateId != control.UpdateId)
                    return depth;
            }
            return -1;
        }

        private bool ScheduleReactiveReplacement(ref NativeLifecycleControlV1 control, int start, int end)
        {
            if (control.Depth < 2 || control.AbortRequested != 0) return false;
            for (var index = start; index < end && index < control.Depth - 1; index++)
            {
                var frame = _frames[index];
                var kind = _bindings[checked((int)frame.NodeIndex)].Kind;
                if (!NativeReactivePolicyV1.IsReactive(kind) || frame.LifecycleState != NativeFrameLifecycleStateV1.Running) continue;
                control.AbortRequested = 1;
                control.AbortReason = BurstNodeAbortReason.Explicit;
                control.AbortTargetDepth = (uint)index + 1u;
                control.ReactiveResetPending = 1;
                control.HasAbortSource = 1;
                control.AbortSourceNode = frame.NodeIndex;
                return true;
            }
            return false;
        }

        private bool ScheduleExpiredTimeout(ref NativeLifecycleControlV1 control, int start, int end)
        {
            if (control.Depth < 2 || control.AbortRequested != 0) return false;
            for (var depth = start; depth < end && depth < control.Depth - 1; depth++)
            {
                var frame = _frames[depth];
                if (_bindings[checked((int)frame.NodeIndex)].Kind != NativeLifecycleNodeKindV1.Timeout
                    || frame.LifecycleState != NativeFrameLifecycleStateV1.Running)
                    continue;
                var node = _nodes[checked((int)frame.NodeIndex)];
                if (control.TimeMicroseconds < ReadInt64(node)) continue;
                control.AbortRequested = 1;
                control.AbortReason = BurstNodeAbortReason.Timeout;
                control.AbortTargetDepth = checked((uint)depth + 1u);
                control.TimeoutPending = 1;
                control.HasAbortSource = 1;
                control.AbortSourceNode = frame.NodeIndex;
                return true;
            }
            return false;
        }

        // ---- Retaining reactive composites (ADR-P10-013) -----------------------------------
        //
        // On the update in which it is entered the composite is a memory composite. On a later
        // update, update-start processing parks its running branch in the suspended-frame region
        // (ParkRetainedBranch) and the composite runs a guard pass over children 0 .. r-1 as new
        // activations on the ordinary stack, while the parked branch receives no callback. The
        // pass then resumes the parked branch, ends the composite after aborting it, or lets a
        // running guard take over after aborting it (the guard branch is held in the region
        // meanwhile). At most one composite holds parked frames at a time: a guard branch is a
        // new activation, and a resumed branch is restored before a deeper composite parks.
        // Activation memory: cursor at offset 0 (the guard cursor during the pass) and the
        // retained ordinal + 1 at offset 4 while a pass holds a parked branch.

        private bool ParkRetainedBranch(int ownerIndex, ref NativeLifecycleControlV1 control)
        {
            if (control.RetainPhase != RetainNone) return false;
            var owner = _frames[ownerIndex];
            if (!TryPushSegment(ownerIndex + 1, ref control, out var first, out var count)) return false;
            control.RetainOwnerDepth = checked((uint)ownerIndex + 1u);
            control.RetainPhase = RetainPassPending;
            control.RetainRest = 0;
            control.RetainedFirstFrame = first;
            control.RetainedFrameCount = count;
            control.HeldFirstFrame = 0;
            control.HeldFrameCount = 0;
            owner.LastUpdateId = control.UpdateId;
            owner.HasPendingChildResult = 0;
            _frames[ownerIndex] = owner;
            return true;
        }

        private bool StartGuardPass(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            var retained = ReadCursor(node);
            if (control.RetainedFrameCount == 0 || retained >= node.ChildCount || control.Depth != (uint)frameIndex + 1u)
            {
                result = default;
                failure = LifetimeFailure(control);
                return false;
            }
            WriteUInt32(node, 4, retained + 1u);
            WriteCursor(node, 0);
            control.RetainPhase = RetainPassActive;
            control.SemanticSteps++;
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, frame.NodeIndex);
            failure = default;
            return true;
        }

        private bool AcceptGuard(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            NativeLifecycleNodeKindV1 kind,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            var cursor = ReadCursor(node);
            var retained = ReadUInt32(node, 4) - 1u;
            if (cursor >= retained || retained >= node.ChildCount)
            {
                result = default;
                failure = LifetimeFailure(control);
                return false;
            }
            control.SemanticSteps++;
            frame.HasPendingChildResult = 0;
            var terminal = IsSequence(kind)
                ? frame.PendingStatus == NodeStatus.Failure
                : frame.PendingStatus == NodeStatus.Success;
            _frames[frameIndex] = frame;
            if (terminal)
            {
                // The guard ends the composite: abort the retained branch, then record the result.
                if (!TryRestoreSegment(control.RetainedFirstFrame, control.RetainedFrameCount, ref control))
                {
                    result = default;
                    failure = FrameCapacityFailure(control);
                    return false;
                }
                control.RetainedFrameCount = 0;
                control.RetainedFirstFrame = 0;
                control.RetainPhase = RetainAbortEnd;
                control.RetainEndStatus = frame.PendingStatus;
                BeginRetainedAbort(ref control, _children[checked((int)(node.ChildOffset + cursor))]);
            }
            else
            {
                WriteCursor(node, cursor + 1u);
            }
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
            failure = default;
            return true;
        }

        private bool ResumeRetainedBranch(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            NativeCompiledNodeRecordV1 node,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            if (control.Depth != (uint)frameIndex + 1u
                || !TryRestoreSegment(control.RetainedFirstFrame, control.RetainedFrameCount, ref control))
            {
                result = default;
                failure = FrameCapacityFailure(control);
                return false;
            }
            WriteUInt32(node, 4, 0u);
            ClearRetainState(ref control);
            control.SemanticSteps++;
            // The resumed branch now gets the update-start processing it skipped (timeouts, v1
            // reactive replacement, the next retaining composite's guard pass).
            if (!PrepareUpdateStart(ref control, frameIndex + 1, frameIndex + 1))
            {
                result = default;
                failure = FrameCapacityFailure(control);
                return false;
            }
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildSelected, frame.NodeIndex);
            failure = default;
            return true;
        }

        /// <summary>
        /// Called where a guard branch has come to rest with a running result (a leaf that already
        /// ticked in this update, or a Parallel whose visit completed non-terminally). If no
        /// Parallel with an active branch lies between it and the retaining composite in its guard
        /// pass, the running guard takes over: the guard branch is held aside, the retained branch
        /// is restored and aborted first, and the held branch comes back afterwards
        /// (reference-executor-machine-v1.md, "Takeover").
        /// </summary>
        private bool TryBeginGuardTakeover(int restFrameIndex, byte rest, ref NativeLifecycleControlV1 control, out bool tookOver)
        {
            tookOver = false;
            var ownerIndex = checked((int)control.RetainOwnerDepth) - 1;
            if (ownerIndex < 0 || ownerIndex >= restFrameIndex) return true;
            for (var index = restFrameIndex - 1; index > ownerIndex; index--)
            {
                var frame = _frames[index];
                if (_bindings[checked((int)frame.NodeIndex)].Kind == NativeLifecycleNodeKindV1.Parallel
                    && frame.ParallelActiveBranch >= 0)
                    return true;
            }
            var guardNode = _frames[ownerIndex + 1].NodeIndex;
            if (!TryPushSegment(ownerIndex + 1, ref control, out var heldFirst, out var heldCount)) return false;
            control.HeldFirstFrame = heldFirst;
            control.HeldFrameCount = heldCount;
            if (!TryRestoreSegment(control.RetainedFirstFrame, control.RetainedFrameCount, ref control)) return false;
            control.RetainedFrameCount = 0;
            control.RetainedFirstFrame = 0;
            control.RetainPhase = RetainAbortTakeover;
            control.RetainRest = rest;
            BeginRetainedAbort(ref control, guardNode);
            tookOver = true;
            return true;
        }

        private static void BeginRetainedAbort(ref NativeLifecycleControlV1 control, uint guardNode)
        {
            control.AbortRequested = 1;
            control.AbortReason = BurstNodeAbortReason.Explicit;
            control.AbortTargetDepth = control.RetainOwnerDepth;
            control.ReactiveResetPending = 0;
            control.TimeoutPending = 0;
            control.HasAbortSource = 1;
            control.AbortSourceNode = guardNode;
        }

        /// <summary>The last frame of the retained branch has exited after an end or a takeover.</summary>
        private void CompleteRetainedAbortBoundary(ref NativeLifecycleControlV1 control)
        {
            var ownerIndex = checked((int)control.RetainOwnerDepth - 1);
            var owner = _frames[ownerIndex];
            var node = _nodes[checked((int)owner.NodeIndex)];
            WriteUInt32(node, 4, 0u);
            control.AbortRequested = 0;
            control.AbortTargetDepth = 0;
            control.HasAbortSource = 0;
            if (control.RetainPhase == RetainAbortEnd)
            {
                owner.PendingStatus = control.RetainEndStatus;
                owner.HasPendingChildResult = 0;
                owner.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[ownerIndex] = owner;
                ClearRetainState(ref control);
                return;
            }
            // Takeover: the running guard is the composite's running child now; the cursor
            // already names it.
            var rest = control.RetainRest;
            if (!TryRestoreSegment(control.HeldFirstFrame, control.HeldFrameCount, ref control))
            {
                control.Faulted = 1;
                control.UpdateOpen = 0;
                return;
            }
            ClearRetainState(ref control);
            if (rest == RetainRestVisit) control.RetainVisitRestPending = 1;
        }

        private bool TryUnparkForAbort(ref NativeLifecycleControlV1 control)
        {
            if (control.RetainedFrameCount != 0)
            {
                if (!TryRestoreSegment(control.RetainedFirstFrame, control.RetainedFrameCount, ref control)) return false;
                control.RetainedFrameCount = 0;
                control.RetainedFirstFrame = 0;
            }
            if (control.HeldFrameCount != 0)
            {
                if (!TryRestoreSegment(control.HeldFirstFrame, control.HeldFrameCount, ref control)) return false;
                control.HeldFrameCount = 0;
                control.HeldFirstFrame = 0;
            }
            ClearRetainState(ref control);
            return true;
        }

        private static void ClearRetainState(ref NativeLifecycleControlV1 control)
        {
            control.RetainOwnerDepth = 0;
            control.RetainPhase = RetainNone;
            control.RetainRest = 0;
            control.RetainEndStatus = default;
            control.RetainedFirstFrame = 0;
            control.RetainedFrameCount = 0;
            control.HeldFirstFrame = 0;
            control.HeldFrameCount = 0;
        }

        private bool RestAfterParallelVisit(
            int frameIndex,
            ref NativeFrameStateV1 frame,
            ref NativeLifecycleControlV1 control,
            out NativeLifecycleStepResultV1 result,
            out NativeRuntimeFailureV1 failure)
        {
            if (TrySuspendParallelBranch(frameIndex, ref control, out var ownerNode))
            {
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(
                    NativeLifecycleStepKindV1.ParallelBranchSuspended, ownerNode);
                failure = default;
                return true;
            }
            control.UpdateOpen = 0;
            control.Waiting = 1;
            _frames[frameIndex] = frame;
            _control[0] = control;
            result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.Waiting, frame.NodeIndex);
            failure = default;
            return true;
        }

        private uint ReadUInt32(NativeCompiledNodeRecordV1 node, uint offset)
        {
            if (node.InstanceMemorySize < offset + 4u) return 0u;
            var start = checked((int)(node.InstanceMemoryOffset + offset));
            return (uint)(_memory[start]
                | _memory[start + 1] << 8
                | _memory[start + 2] << 16
                | _memory[start + 3] << 24);
        }

        private void WriteUInt32(NativeCompiledNodeRecordV1 node, uint offset, uint value)
        {
            if (node.InstanceMemorySize < offset + 4u) return;
            var start = checked((int)(node.InstanceMemoryOffset + offset));
            _memory[start] = (byte)value;
            _memory[start + 1] = (byte)(value >> 8);
            _memory[start + 2] = (byte)(value >> 16);
            _memory[start + 3] = (byte)(value >> 24);
        }

        private long ReadInt64(NativeCompiledNodeRecordV1 node)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            ulong value = 0;
            for (var index = 0; index < 8; index++) value |= (ulong)_memory[offset + index] << (index * 8);
            return unchecked((long)value);
        }

        private void WriteInt64(NativeCompiledNodeRecordV1 node, long value)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            var bits = unchecked((ulong)value);
            for (var index = 0; index < 8; index++) _memory[offset + index] = (byte)(bits >> (index * 8));
        }

        private static NativeRuntimeFailureV1 TimeOverflowFailure(NativeLifecycleControlV1 control)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                NativeResourceKindV1.InstanceNodeMemory,
                ownerId: control.OwnerId,
                generation: control.Generation);

        private NativeRuntimeFailureV1 FrameCapacityFailure(NativeLifecycleControlV1 control)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                NativeResourceKindV1.InstanceFrames,
                control.Depth,
                (uint)_frames.Length);

        private static NativeRuntimeFailureV1 CapacityFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                NativeResourceKindV1.InstanceFrames);

        private static NativeRuntimeFailureV1 LifetimeFailure(NativeLifecycleControlV1 control = default)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                NativeResourceKindV1.InstanceFrames,
                ownerId: control.OwnerId,
                generation: control.Generation,
                leaseId: control.ActiveDispatchId);
    }
}
