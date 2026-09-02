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
            NodeStatus rootStatus = default)
        {
            Kind = kind;
            NodeIndex = nodeIndex;
            Phase = phase;
            DispatchToken = dispatchToken;
            HasRootStatus = hasRootStatus;
            RootStatus = rootStatus;
        }

        internal NativeLifecycleStepKindV1 Kind { get; }
        internal uint NodeIndex { get; }
        internal BurstCallbackPhase Phase { get; }
        internal NativeLifecycleDispatchTokenV1 DispatchToken { get; }
        internal bool HasRootStatus { get; }
        internal NodeStatus RootStatus { get; }
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
    }

    internal struct NativeLifecycleMachineV1
    {
        private const ulong MemorySequenceTypeId = 0x5b7ceab8e6ee5d73UL;
        private const ulong MemorySelectorTypeId = 0x728850afcebac87fUL;
        private const ulong ParallelTypeId = 0xc178d10e173662cfUL;

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
            if (!ScheduleExpiredTimeout(ref control)) ScheduleReactiveReplacement(ref control);
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
                control.ParallelAbortPending = 0;
                if (ownerFrame.LifecycleState == NativeFrameLifecycleStateV1.Aborting)
                {
                    control.AbortTargetDepth = 0;
                    ownerFrame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    _frames[ownerIndex] = ownerFrame;
                    control.SemanticSteps++;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.CompositeAborted, ownerFrame.NodeIndex);
                    failure = default;
                    return true;
                }
                control.AbortRequested = 0;
                control.AbortTargetDepth = 0;
                ownerFrame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                _frames[ownerIndex] = ownerFrame;
                control.SemanticSteps++;
                _control[0] = control;
                result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, ownerFrame.NodeIndex);
                failure = default;
                return true;
            }

            var frameIndex = checked((int)control.Depth - 1);
            var frame = _frames[frameIndex];
            var node = _nodes[checked((int)frame.NodeIndex)];
            var kind = _bindings[checked((int)frame.NodeIndex)].Kind;
            if (control.AbortRequested != 0 && frame.LifecycleState != NativeFrameLifecycleStateV1.Inactive)
                return AdvanceAbort(frameIndex, ref frame, node, kind, ref control, out result, out failure);
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

        internal bool TryRequestAbort(BurstNodeAbortReason reason, out NativeRuntimeFailureV1 failure)
        {
            if (!IsCreated || (byte)reason > (byte)BurstNodeAbortReason.Timeout)
            {
                failure = LifetimeFailure();
                return false;
            }
            var control = _control[0];
            if (control.UpdateOpen == 0 || control.Depth == 0 || control.ActiveDispatchId != 0
                || control.AbortRequested != 0 || control.HasRootStatus != 0)
            {
                failure = LifetimeFailure(control);
                return false;
            }
            control.AbortRequested = 1;
            control.AbortReason = reason;
            control.AbortTargetDepth = 0;
            control.ReactiveResetPending = 0;
            control.TimeoutPending = 0;
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
                control.ParallelAbortPending = 1;
                control.ParallelAbortOwnerDepth = checked((uint)frameIndex + 1u);
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
                        control.ParallelAbortPending = 1;
                        control.ParallelAbortOwnerDepth = checked((uint)frameIndex + 1u);
                        if (!TryBeginNextParallelBranchAbort(frameIndex, ref frame, ref control))
                        {
                            result = default;
                            failure = LifetimeFailure(control);
                            return false;
                        }
                    }
                    else frame.LifecycleState = NativeFrameLifecycleStateV1.Exiting;
                    _frames[frameIndex] = frame;
                    _control[0] = control;
                    result = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.ChildAccepted, frame.NodeIndex);
                    failure = default;
                    return true;
                }
                _frames[frameIndex] = frame;
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
                if (!ScheduleExpiredTimeout(ref control)) ScheduleReactiveReplacement(ref control);
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
            result = new NativeLifecycleStepResultV1(
                NativeLifecycleStepKindV1.DispatchRequired, nodeIndex, phase, token);
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
            if (branch.SuspendedFrameCount == 0 || branch.FirstSuspendedFrame < control.SuspendedFrameStart
                || branch.FirstSuspendedFrame + branch.SuspendedFrameCount > _frames.Length
                || control.Depth > branch.FirstSuspendedFrame)
                return false;
            var destination = control.Depth;
            for (uint offset = 0; offset < branch.SuspendedFrameCount; offset++)
                _frames[checked((int)(destination + offset))] = _frames[checked((int)(branch.FirstSuspendedFrame + offset))];
            var removedStart = branch.FirstSuspendedFrame;
            var removedCount = branch.SuspendedFrameCount;
            for (var source = checked((int)removedStart) - 1; source >= control.SuspendedFrameStart; source--)
                _frames[source + checked((int)removedCount)] = _frames[source];
            for (var index = 0; index < _parallelBranches.Length; index++)
            {
                var other = _parallelBranches[index];
                if (other.SuspendedFrameCount != 0 && other.FirstSuspendedFrame < removedStart)
                {
                    other.FirstSuspendedFrame += removedCount;
                    _parallelBranches[index] = other;
                }
            }
            branch.FirstSuspendedFrame = 0;
            branch.SuspendedFrameCount = 0;
            _parallelBranches[branchIndex] = branch;
            control.Depth += removedCount;
            control.SuspendedFrameStart += removedCount;
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
                control.AbortRequested = 1;
                control.AbortReason = ownerFrame.LifecycleState == NativeFrameLifecycleStateV1.Aborting
                    ? control.AbortReason : BurstNodeAbortReason.Explicit;
                control.AbortTargetDepth = checked((uint)ownerFrameIndex + 1u);
                control.SemanticSteps++;
                return true;
            }
            return false;
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
            => kind == NativeLifecycleNodeKindV1.MemorySequence || kind == NativeLifecycleNodeKindV1.ReactiveSequence;

        private static bool IsDecorator(NativeLifecycleNodeKindV1 kind)
            => kind >= NativeLifecycleNodeKindV1.Inverter && kind <= NativeLifecycleNodeKindV1.Cooldown;

        private static NativeDecoratorKindV1 ToDecoratorKind(NativeLifecycleNodeKindV1 kind)
            => (NativeDecoratorKindV1)((byte)kind - (byte)NativeLifecycleNodeKindV1.Inverter);

        private void ScheduleReactiveReplacement(ref NativeLifecycleControlV1 control)
        {
            if (control.Depth < 2 || control.AbortRequested != 0) return;
            for (var index = 0; index < control.Depth - 1; index++)
            {
                var frame = _frames[index];
                var kind = _bindings[checked((int)frame.NodeIndex)].Kind;
                if (!NativeReactivePolicyV1.IsReactive(kind) || frame.LifecycleState != NativeFrameLifecycleStateV1.Running) continue;
                control.AbortRequested = 1;
                control.AbortReason = BurstNodeAbortReason.Explicit;
                control.AbortTargetDepth = (uint)index + 1u;
                control.ReactiveResetPending = 1;
                return;
            }
        }

        private bool ScheduleExpiredTimeout(ref NativeLifecycleControlV1 control)
        {
            if (control.Depth < 2 || control.AbortRequested != 0) return false;
            for (var depth = 0; depth < control.Depth - 1; depth++)
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
                return true;
            }
            return false;
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
