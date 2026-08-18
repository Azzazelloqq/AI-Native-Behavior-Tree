using System;
using Unity.Collections;

namespace AIBT
{
    public enum NativeFrameLifecycleStateV1 : byte
    {
        Inactive = 0,
        Entering = 1,
        Running = 2,
        Exiting = 3,
        Aborting = 4,
    }

    public struct NativeFrameStateV1
    {
        public uint NodeIndex;
        public uint ParentFrameIndex;
        public uint ChildCursor;
        public uint ActivationGeneration;
        public ulong LastUpdateId;
        public NativeFrameLifecycleStateV1 LifecycleState;
        public NodeStatus PendingStatus;
        public byte HasPendingChildResult;
        internal byte CooldownDeadlinePending;
        internal uint ParallelFirstBranch;
        internal int ParallelActiveBranch;
        internal uint ParallelVisitCursor;
    }

    public struct NativeParallelBranchStateV1
    {
        public uint CapacityOrdinal;
        public uint NodeIndex;
        public byte State;
        internal uint FirstSuspendedFrame;
        internal uint SuspendedFrameCount;
    }

    public struct NativeObserverStateV1
    {
        public uint ObserverNodeIndex;
        public uint OwningReactiveCompositeIndex;
        public byte LastConditionResult;
        public byte HasLastConditionResult;
    }

    public struct NativeUpdateStateV1
    {
        public ulong UpdateId;
        public uint Phase;
        public uint WorkCursor;
    }

    public struct NativeBudgetStateV1
    {
        public uint StepLimit;
        public uint StepsConsumed;
        public uint ResumeCursor;
        public byte Exhausted;
    }

    public readonly struct NativeInstanceArenaCapacityV1
    {
        public NativeInstanceArenaCapacityV1(
            uint nodeMemoryBytes,
            uint treeBlackboardBytes,
            uint frameCount,
            uint generationCount,
            uint parallelBranchCapacity,
            uint observerCount,
            uint updateStateCount,
            uint budgetStateCount,
            uint maximumAlignment)
        {
            NodeMemoryBytes = nodeMemoryBytes;
            TreeBlackboardBytes = treeBlackboardBytes;
            FrameCount = frameCount;
            GenerationCount = generationCount;
            ParallelBranchCapacity = parallelBranchCapacity;
            ObserverCount = observerCount;
            UpdateStateCount = updateStateCount;
            BudgetStateCount = budgetStateCount;
            MaximumAlignment = maximumAlignment;
        }

        public uint NodeMemoryBytes { get; }
        public uint TreeBlackboardBytes { get; }
        public uint FrameCount { get; }
        public uint GenerationCount { get; }
        public uint ParallelBranchCapacity { get; }
        public uint ObserverCount { get; }
        public uint UpdateStateCount { get; }
        public uint BudgetStateCount { get; }
        public uint MaximumAlignment { get; }

        public static bool TryDerive(
            NativeProgramImageViewV1 program,
            out NativeInstanceArenaCapacityV1 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            uint treeBytes = 0;
            uint treeAlignment = 1;
            for (var index = 0; index < program.BlackboardSlots.Length; index++)
            {
                var slot = program.BlackboardSlots[index];
                if (slot.Scope != BlackboardScope.Tree)
                {
                    continue;
                }

                if (!NativeCheckedMathV1.TryAdd(
                    slot.Offset,
                    slot.Size,
                    NativeResourceKindV1.InstanceTreeBlackboard,
                    out var end,
                    out failure))
                {
                    capacity = default;
                    return false;
                }

                if (end > treeBytes)
                {
                    treeBytes = end;
                }

                if (slot.Alignment > treeAlignment)
                {
                    treeAlignment = slot.Alignment;
                }
            }

            if (!NativeCheckedMathV1.TryAlignUp(
                treeBytes,
                treeAlignment,
                NativeResourceKindV1.InstanceTreeBlackboard,
                out treeBytes,
                out failure))
            {
                capacity = default;
                return false;
            }

            capacity = new NativeInstanceArenaCapacityV1(
                program.Header.InstanceNodeMemorySize,
                treeBytes,
                program.Header.NodeCount,
                program.Header.NodeCount,
                program.Header.ChildIndexCount,
                (uint)program.Observers.Length,
                1,
                1,
                program.Header.RequiredMaximumAlignment);
            failure = default;
            return true;
        }
    }

    public readonly struct NativeInstanceArenaViewV1
    {
        internal NativeInstanceArenaViewV1(
            NativeArray<byte> nodeMemory,
            NativeArray<byte> treeBlackboard,
            NativeArray<NativeFrameStateV1> frames,
            NativeArray<uint> generations,
            NativeArray<NativeParallelBranchStateV1> parallelBranches,
            NativeArray<NativeObserverStateV1> observers,
            NativeArray<NativeUpdateStateV1> updateState,
            NativeArray<NativeBudgetStateV1> budgetState)
        {
            NodeMemory = nodeMemory;
            TreeBlackboard = treeBlackboard;
            Frames = frames;
            Generations = generations;
            ParallelBranches = parallelBranches;
            Observers = observers;
            UpdateState = updateState;
            BudgetState = budgetState;
        }

        public NativeArray<byte> NodeMemory { get; }
        public NativeArray<byte> TreeBlackboard { get; }
        public NativeArray<NativeFrameStateV1> Frames { get; }
        public NativeArray<uint> Generations { get; }
        public NativeArray<NativeParallelBranchStateV1> ParallelBranches { get; }
        public NativeArray<NativeObserverStateV1> Observers { get; }
        public NativeArray<NativeUpdateStateV1> UpdateState { get; }
        public NativeArray<NativeBudgetStateV1> BudgetState { get; }
    }

    public readonly struct NativeInstanceArenaCapacityV2
    {
        public NativeInstanceArenaCapacityV2(
            NativeInstanceArenaCapacityV1 semantic,
            uint treeSlotVersions,
            uint treeRevisionCount)
            : this(semantic, treeSlotVersions, treeRevisionCount, 0)
        {
        }

        public NativeInstanceArenaCapacityV2(
            NativeInstanceArenaCapacityV1 semantic,
            uint treeSlotVersions,
            uint treeRevisionCount,
            uint randomStreamCount)
        {
            Semantic = semantic;
            TreeSlotVersions = treeSlotVersions;
            TreeRevisionCount = treeRevisionCount;
            RandomStreamCount = randomStreamCount;
        }

        public NativeInstanceArenaCapacityV1 Semantic { get; }
        public uint TreeSlotVersions { get; }
        public uint TreeRevisionCount { get; }
        public uint RandomStreamCount { get; }

        public static bool TryDerive(
            NativeProgramImageViewV2 program,
            out NativeInstanceArenaCapacityV2 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            if (!NativeInstanceArenaCapacityV1.TryDerive(program.Semantic, out var semantic, out failure))
            {
                capacity = default;
                return false;
            }

            capacity = new NativeInstanceArenaCapacityV2(semantic, (uint)program.Slots.Length, 1);
            return true;
        }
    }

    public readonly struct NativeInstanceArenaViewV2
    {
        internal NativeInstanceArenaViewV2(
            NativeInstanceArenaViewV1 semantic,
            NativeArray<ulong> treeSlotVersions,
            NativeArray<ulong> treeRevision,
            NativeArray<ulong> randomStates,
            NativeArray<ulong> randomIncrements,
            NativeArray<uint> randomNodeIndices)
        {
            Semantic = semantic;
            TreeSlotVersions = treeSlotVersions;
            TreeRevision = treeRevision;
            RandomStates = randomStates;
            RandomIncrements = randomIncrements.AsReadOnly();
            RandomNodeIndices = randomNodeIndices.AsReadOnly();
        }

        public NativeInstanceArenaViewV1 Semantic { get; }
        public NativeArray<ulong> TreeSlotVersions { get; }
        public NativeArray<ulong> TreeRevision { get; }
        public NativeArray<ulong> RandomStates { get; }
        public NativeArray<ulong>.ReadOnly RandomIncrements { get; }
        public NativeArray<uint>.ReadOnly RandomNodeIndices { get; }
    }

    public readonly struct NativeInstanceExecutionLeaseV2
    {
        internal NativeInstanceExecutionLeaseV2(
            NativeInstanceExecutionLeaseV1 semanticLease,
            NativeProgramImageViewV2 program,
            NativeInstanceArenaViewV2 view)
        {
            SemanticLease = semanticLease;
            Program = program;
            View = view;
        }

        internal NativeInstanceExecutionLeaseV1 SemanticLease { get; }
        public NativeLeaseTokenV1 Token => SemanticLease.Token;
        public NativeProgramImageViewV2 Program { get; }
        public NativeInstanceArenaViewV2 View { get; }
        public bool IsValid => SemanticLease.IsValid;
    }
}
