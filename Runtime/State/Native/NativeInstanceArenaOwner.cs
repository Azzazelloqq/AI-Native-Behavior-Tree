using System;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    public sealed class NativeInstanceArenaOwnerV1
    {
        private NativeArray<byte> _nodeMemory;
        private NativeArray<byte> _treeBlackboard;
        private NativeArray<NativeFrameStateV1> _frames;
        private NativeArray<uint> _generations;
        private NativeArray<NativeParallelBranchStateV1> _parallelBranches;
        private NativeArray<NativeObserverStateV1> _observers;
        private NativeArray<NativeUpdateStateV1> _updateState;
        private NativeArray<NativeBudgetStateV1> _budgetState;
        private NativeArray<ulong> _treeSlotVersionsV2;
        private NativeArray<ulong> _treeRevisionV2;
        private NativeArray<ulong> _randomStatesV2;
        private NativeArray<ulong> _randomIncrementsV2;
        private NativeArray<uint> _randomNodeIndicesV2;
        private NativeHash256V1 _randomSemanticHash;
        private ulong _randomRootSeed;
        private ulong _randomTreeInstanceId;
        private bool _randomInitialized;
        private NativeNodeMemoryRegionV1[] _memoryRegions;
        private NativeObserverBindingV1[] _observerBindings;
        private NativeLeaseTokenV1 _activeLease;
        private JobHandle _activeDependency;
        private ulong _nextLeaseId;

        private NativeInstanceArenaOwnerV1()
        {
        }

        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public ulong ProgramOwnerId { get; private set; }
        public uint ProgramGeneration { get; private set; }
        public NativeOwnerStateV1 State { get; private set; }
        public NativeInstanceArenaCapacityV1 Capacity { get; private set; }
        public bool HasBlackboardV2 => _treeSlotVersionsV2.IsCreated;

        public static bool TryCreate(
            NativeProgramReadLeaseV1 programLease,
            NativeInstanceArenaCapacityV1 capacity,
            Allocator allocator,
            out NativeInstanceArenaOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(programLease, capacity, allocator, -1, out owner, out failure);

        internal static bool TryCreate(
            NativeProgramReadLeaseV1 programLease,
            NativeInstanceArenaCapacityV1 capacity,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeInstanceArenaOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
        {
            owner = null;
            if (allocator != Allocator.Persistent)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid,
                    NativeResourceKindV1.None,
                    (ulong)allocator,
                    (ulong)Allocator.Persistent);
                return false;
            }

            if (programLease.Owner == null || !programLease.Owner.IsLeaseActive(programLease))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    ownerId: programLease.Token.OwnerId,
                    generation: programLease.Token.Generation,
                    leaseId: programLease.Token.LeaseId);
                return false;
            }

            if (!NativeInstanceArenaCapacityV1.TryDerive(programLease.View, out var required, out failure)
                || !TryValidateCapacity(required, capacity, out failure))
            {
                return false;
            }

            var nodeMemory = default(NativeArray<byte>);
            var treeBlackboard = default(NativeArray<byte>);
            var frames = default(NativeArray<NativeFrameStateV1>);
            var generations = default(NativeArray<uint>);
            var parallelBranches = default(NativeArray<NativeParallelBranchStateV1>);
            var observers = default(NativeArray<NativeObserverStateV1>);
            var updateState = default(NativeArray<NativeUpdateStateV1>);
            var budgetState = default(NativeArray<NativeBudgetStateV1>);
            var allocations = 0;
            var currentResource = NativeResourceKindV1.InstanceNodeMemory;
            try
            {
                nodeMemory = Allocate<byte>(capacity.NodeMemoryBytes, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceTreeBlackboard;
                treeBlackboard = Allocate<byte>(capacity.TreeBlackboardBytes, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceFrames;
                frames = Allocate<NativeFrameStateV1>(capacity.FrameCount, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceGenerations;
                generations = Allocate<uint>(capacity.GenerationCount, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceParallelBranches;
                parallelBranches = Allocate<NativeParallelBranchStateV1>(capacity.ParallelBranchCapacity, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceObservers;
                observers = Allocate<NativeObserverStateV1>(capacity.ObserverCount, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceUpdateState;
                updateState = Allocate<NativeUpdateStateV1>(capacity.UpdateStateCount, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.InstanceBudgetState;
                budgetState = Allocate<NativeBudgetStateV1>(capacity.BudgetStateCount, allocator, failAfterSuccessfulAllocations, ref allocations);

                if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
                {
                    throw new OverflowException("The native owner ID counter overflowed.");
                }

                var memoryRegions = new NativeNodeMemoryRegionV1[programLease.View.Nodes.Length];
                for (var index = 0; index < memoryRegions.Length; index++)
                {
                    var node = programLease.View.Nodes[index];
                    memoryRegions[index] = new NativeNodeMemoryRegionV1(
                        node.InstanceMemoryOffset,
                        node.InstanceMemorySize,
                        node.MemoryLifetime);
                }

                var observerBindings = new NativeObserverBindingV1[programLease.View.Observers.Length];
                for (var index = 0; index < observerBindings.Length; index++)
                {
                    var observer = programLease.View.Observers[index];
                    observerBindings[index] = new NativeObserverBindingV1(
                        observer.ObserverNodeIndex,
                        observer.OwningReactiveCompositeIndex);
                }

                owner = new NativeInstanceArenaOwnerV1
                {
                    OwnerId = ownerId,
                    Generation = 1,
                    ProgramOwnerId = programLease.Token.OwnerId,
                    ProgramGeneration = programLease.Token.Generation,
                    State = NativeOwnerStateV1.Initialized,
                    Capacity = capacity,
                    _nodeMemory = nodeMemory,
                    _treeBlackboard = treeBlackboard,
                    _frames = frames,
                    _generations = generations,
                    _parallelBranches = parallelBranches,
                    _observers = observers,
                    _updateState = updateState,
                    _budgetState = budgetState,
                    _memoryRegions = memoryRegions,
                    _observerBindings = observerBindings,
                };
                owner.InitializeSentinels();
                failure = default;
                return true;
            }
            catch (OverflowException)
            {
                DisposeReverse(ref budgetState, ref updateState, ref observers, ref parallelBranches, ref generations,
                    ref frames, ref treeBlackboard, ref nodeMemory);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter);
                return false;
            }
            catch (Exception)
            {
                DisposeReverse(ref budgetState, ref updateState, ref observers, ref parallelBranches, ref generations,
                    ref frames, ref treeBlackboard, ref nodeMemory);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    currentResource,
                    (ulong)allocations + 1,
                    (ulong)allocations);
                return false;
            }
        }

        public static bool TryCreateV2(
            NativeProgramReadLeaseV2 programLease,
            NativeInstanceArenaCapacityV2 capacity,
            Allocator allocator,
            out NativeInstanceArenaOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
        {
            owner = null;
            if (programLease.SemanticLease.Owner == null
                || !programLease.SemanticLease.Owner.IsLeaseActive(programLease.SemanticLease))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    ownerId: programLease.Token.OwnerId,
                    generation: programLease.Token.Generation,
                    leaseId: programLease.Token.LeaseId);
                return false;
            }
            if (capacity.TreeSlotVersions < programLease.View.Slots.Length || capacity.TreeRevisionCount < 1
                || capacity.TreeSlotVersions > int.MaxValue || capacity.TreeRevisionCount > int.MaxValue
                || capacity.RandomStreamCount > int.MaxValue || capacity.RandomStreamCount > programLease.View.Semantic.Nodes.Length)
            {
                failure = new NativeRuntimeFailureV1(
                    capacity.TreeSlotVersions > int.MaxValue || capacity.TreeRevisionCount > int.MaxValue
                        ? NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow
                        : NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    capacity.TreeSlotVersions < programLease.View.Slots.Length
                        ? NativeResourceKindV1.InstanceTreeSlotVersions
                        : NativeResourceKindV1.InstanceTreeRevision,
                    capacity.TreeSlotVersions < programLease.View.Slots.Length
                        ? (uint)programLease.View.Slots.Length : 1,
                    capacity.TreeSlotVersions < programLease.View.Slots.Length
                        ? capacity.TreeSlotVersions : capacity.TreeRevisionCount);
                return false;
            }
            if (!TryCreate(programLease.SemanticLease, capacity.Semantic, allocator, out owner, out failure)) return false;

            var versions = default(NativeArray<ulong>);
            var revision = default(NativeArray<ulong>);
            var randomStates = default(NativeArray<ulong>);
            var randomIncrements = default(NativeArray<ulong>);
            var randomNodeIndices = default(NativeArray<uint>);
            var currentResource = NativeResourceKindV1.InstanceTreeSlotVersions;
            try
            {
                versions = new NativeArray<ulong>(programLease.View.Slots.Length, allocator, NativeArrayOptions.ClearMemory);
                currentResource = NativeResourceKindV1.InstanceTreeRevision;
                revision = new NativeArray<ulong>(1, allocator, NativeArrayOptions.ClearMemory);
                currentResource = NativeResourceKindV1.InstanceRandomStates;
                randomStates = new NativeArray<ulong>((int)capacity.RandomStreamCount, allocator, NativeArrayOptions.ClearMemory);
                currentResource = NativeResourceKindV1.InstanceRandomIncrements;
                randomIncrements = new NativeArray<ulong>((int)capacity.RandomStreamCount, allocator, NativeArrayOptions.ClearMemory);
                currentResource = NativeResourceKindV1.InstanceRandomNodeIndices;
                randomNodeIndices = new NativeArray<uint>((int)capacity.RandomStreamCount, allocator, NativeArrayOptions.ClearMemory);
                if (!TryInitializeTreeDefaults(programLease.View, owner._treeBlackboard, out failure))
                {
                    Dispose(ref revision); Dispose(ref versions); owner.TryDispose(out _); owner = null; return false;
                }
                owner._treeSlotVersionsV2 = versions;
                owner._treeRevisionV2 = revision;
                owner._randomStatesV2 = randomStates;
                owner._randomIncrementsV2 = randomIncrements;
                owner._randomNodeIndicesV2 = randomNodeIndices;
                return true;
            }
            catch (Exception)
            {
                Dispose(ref randomNodeIndices); Dispose(ref randomIncrements); Dispose(ref randomStates);
                Dispose(ref revision); Dispose(ref versions); owner.TryDispose(out _); owner = null;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    currentResource);
                return false;
            }
        }

        public bool TryInitializeRandomStreams(
            NativeProgramReadLeaseV2 programLease,
            ulong rootSeed,
            ulong treeInstanceId,
            NativeArray<uint> randomNodeIndices,
            out NativeRuntimeFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized || !HasBlackboardV2
                || programLease.SemanticLease.Owner == null
                || !programLease.SemanticLease.Owner.IsLeaseActive(programLease.SemanticLease)
                || programLease.Token.OwnerId != ProgramOwnerId || programLease.Token.Generation != ProgramGeneration
                || treeInstanceId == 0 || !randomNodeIndices.IsCreated
                || randomNodeIndices.Length != _randomStatesV2.Length)
            {
                failure = LifetimeFailure(programLease.Token.LeaseId);
                return false;
            }
            uint previous = 0;
            for (var index = 0; index < randomNodeIndices.Length; index++)
            {
                var nodeIndex = randomNodeIndices[index];
                if (nodeIndex >= programLease.View.Semantic.Nodes.Length || (index != 0 && nodeIndex <= previous))
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.InstanceRandomNodeIndices,
                        nodeIndex,
                        (uint)programLease.View.Semantic.Nodes.Length);
                    return false;
                }
                previous = nodeIndex;
            }
            _randomRootSeed = rootSeed;
            _randomTreeInstanceId = treeInstanceId;
            _randomSemanticHash = programLease.View.Semantic.Header.CanonicalSemanticHash;
            for (var index = 0; index < randomNodeIndices.Length; index++)
            {
                var nodeIndex = randomNodeIndices[index];
                NativeRandomStreamDerivationV1.TryDerive(rootSeed, _randomSemanticHash, treeInstanceId, nodeIndex, out var stream);
                _randomNodeIndicesV2[index] = nodeIndex;
                _randomStatesV2[index] = stream.State;
                _randomIncrementsV2[index] = stream.Increment;
            }
            _randomInitialized = true;
            failure = default;
            return true;
        }

        public bool TryAcquireExecutionLease(
            NativeProgramReadLeaseV1 programLease,
            out NativeInstanceExecutionLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing && _activeLease.IsValid)
            {
                lease = default;
                failure = LiveJobFailure(_activeLease.LeaseId);
                return false;
            }

            if (State != NativeOwnerStateV1.Initialized
                || programLease.Owner == null
                || !programLease.Owner.IsLeaseActive(programLease)
                || programLease.Token.OwnerId != ProgramOwnerId
                || programLease.Token.Generation != ProgramGeneration)
            {
                lease = default;
                failure = LifetimeFailure(programLease.Token.LeaseId);
                return false;
            }

            if (_nextLeaseId == ulong.MaxValue)
            {
                lease = default;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter,
                    _nextLeaseId,
                    ulong.MaxValue,
                    ownerId: OwnerId,
                    generation: Generation);
                return false;
            }

            _activeLease = new NativeLeaseTokenV1(OwnerId, Generation, ++_nextLeaseId);
            _activeDependency = default;
            State = NativeOwnerStateV1.Executing;
            lease = new NativeInstanceExecutionLeaseV1(this, _activeLease, CreateView());
            failure = default;
            return true;
        }

        public bool TryAcquireExecutionLeaseV2(
            NativeProgramReadLeaseV2 programLease,
            out NativeInstanceExecutionLeaseV2 lease,
            out NativeRuntimeFailureV1 failure)
        {
            if (!HasBlackboardV2)
            {
                lease = default;
                failure = LifetimeFailure(0);
                return false;
            }
            if (!TryAcquireExecutionLease(programLease.SemanticLease, out var semanticLease, out failure))
            {
                lease = default;
                return false;
            }
            lease = new NativeInstanceExecutionLeaseV2(semanticLease, programLease.View, CreateViewV2());
            return true;
        }

        public bool TryRegisterDependency(
            NativeInstanceExecutionLeaseV1 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsActiveLease(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }

            _activeDependency = JobHandle.CombineDependencies(_activeDependency, dependency);
            failure = default;
            return true;
        }

        public bool TryRegisterDependency(
            NativeInstanceExecutionLeaseV2 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
            => TryRegisterDependency(lease.SemanticLease, dependency, out failure);

        public bool TryReleaseExecutionLease(
            NativeInstanceExecutionLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsActiveLease(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }

            if (!_activeDependency.IsCompleted)
            {
                failure = LiveJobFailure(lease.Token.LeaseId);
                return false;
            }

            _activeLease = default;
            _activeDependency = default;
            State = NativeOwnerStateV1.Initialized;
            failure = default;
            return true;
        }

        public bool TryReleaseExecutionLease(
            NativeInstanceExecutionLeaseV2 lease,
            out NativeRuntimeFailureV1 failure)
            => TryReleaseExecutionLease(lease.SemanticLease, out failure);

        public bool TryResetTreeBlackboard(
            NativeProgramReadLeaseV2 programLease,
            out NativeRuntimeFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing && _activeLease.IsValid)
            {
                failure = LiveJobFailure(_activeLease.LeaseId);
                return false;
            }
            if (State != NativeOwnerStateV1.Initialized || !HasBlackboardV2
                || programLease.SemanticLease.Owner == null
                || !programLease.SemanticLease.Owner.IsLeaseActive(programLease.SemanticLease)
                || programLease.Token.OwnerId != ProgramOwnerId || programLease.Token.Generation != ProgramGeneration)
            {
                failure = LifetimeFailure(programLease.Token.LeaseId);
                return false;
            }

            var changed = false;
            for (var index = 0; index < programLease.View.Slots.Length; index++)
            {
                var slot = programLease.View.Slots[index];
                if (slot.Scope != BlackboardScope.Tree) continue;
                if (!TreeBytesEqual(programLease.View, _treeBlackboard, slot))
                {
                    if (_treeSlotVersionsV2[index] == ulong.MaxValue || _treeRevisionV2[0] == ulong.MaxValue)
                    {
                        failure = new NativeRuntimeFailureV1(
                            NativeRuntimeDiagnosticCodeV1.BlackboardVersionOverflow,
                            NativeResourceKindV1.InstanceTreeSlotVersions,
                            ownerId: OwnerId,
                            generation: Generation);
                        return false;
                    }
                    changed = true;
                }
            }
            if (!changed) { failure = default; return true; }

            for (var index = 0; index < programLease.View.Slots.Length; index++)
            {
                var slot = programLease.View.Slots[index];
                if (slot.Scope != BlackboardScope.Tree || TreeBytesEqual(programLease.View, _treeBlackboard, slot)) continue;
                CopyDefault(programLease.View, _treeBlackboard, slot);
                _treeSlotVersionsV2[index]++;
            }
            _treeRevisionV2[0]++;
            failure = default;
            return true;
        }

        public bool TryCompleteTerminalExit(uint nodeIndex, out NativeRuntimeFailureV1 failure)
            => TryClearActivationExit(nodeIndex, out failure);

        public bool TryCompleteAbortedExit(uint nodeIndex, out NativeRuntimeFailureV1 failure)
            => TryClearActivationExit(nodeIndex, out failure);

        public bool TryRestart(out NativeRuntimeFailureV1 failure) => TryResetAll(out failure);

        public bool TryReset(out NativeRuntimeFailureV1 failure) => TryResetAll(out failure);

        public bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing && _activeLease.IsValid)
            {
                failure = LiveJobFailure(_activeLease.LeaseId);
                return false;
            }

            if (State != NativeOwnerStateV1.Initialized)
            {
                failure = LifetimeFailure(0);
                return false;
            }

            Dispose(ref _treeRevisionV2);
            Dispose(ref _treeSlotVersionsV2);
            Dispose(ref _randomNodeIndicesV2);
            Dispose(ref _randomIncrementsV2);
            Dispose(ref _randomStatesV2);
            DisposeReverse(ref _budgetState, ref _updateState, ref _observers, ref _parallelBranches, ref _generations,
                ref _frames, ref _treeBlackboard, ref _nodeMemory);
            _memoryRegions = null;
            _observerBindings = null;
            State = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        private bool TryClearActivationExit(uint nodeIndex, out NativeRuntimeFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing && _activeLease.IsValid)
            {
                failure = LiveJobFailure(_activeLease.LeaseId);
                return false;
            }

            if (State != NativeOwnerStateV1.Initialized || nodeIndex >= _memoryRegions.Length)
            {
                failure = LifetimeFailure(0);
                return false;
            }

            if (_generations[(int)nodeIndex] == uint.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceGenerations,
                    uint.MaxValue,
                    uint.MaxValue,
                    ownerId: OwnerId,
                    generation: Generation);
                return false;
            }

            var region = _memoryRegions[nodeIndex];
            if (region.Lifetime == NodeMemoryLifetime.Activation)
            {
                Clear(_nodeMemory, region.Offset, region.Size);
            }

            _generations[(int)nodeIndex]++;
            if (nodeIndex < _frames.Length)
            {
                var frame = _frames[(int)nodeIndex];
                frame.NodeIndex = CompiledIndex.Invalid;
                frame.ParentFrameIndex = CompiledIndex.Invalid;
                frame.ChildCursor = 0;
                frame.ActivationGeneration = _generations[(int)nodeIndex];
                frame.LastUpdateId = 0;
                frame.LifecycleState = NativeFrameLifecycleStateV1.Inactive;
                frame.PendingStatus = NodeStatus.Running;
                _frames[(int)nodeIndex] = frame;
            }

            failure = default;
            return true;
        }

        private bool TryResetAll(out NativeRuntimeFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing && _activeLease.IsValid)
            {
                failure = LiveJobFailure(_activeLease.LeaseId);
                return false;
            }

            if (State != NativeOwnerStateV1.Initialized)
            {
                failure = LifetimeFailure(0);
                return false;
            }

            if (Generation == uint.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceGenerations,
                    Generation,
                    uint.MaxValue,
                    ownerId: OwnerId,
                    generation: Generation);
                return false;
            }

            Generation++;
            Clear(_nodeMemory);
            Clear(_treeBlackboard);
            Clear(_frames);
            Clear(_generations);
            Clear(_parallelBranches);
            Clear(_observers);
            Clear(_updateState);
            Clear(_budgetState);
            if (_randomInitialized)
            {
                for (var index = 0; index < _randomStatesV2.Length; index++)
                {
                    NativeRandomStreamDerivationV1.TryDerive(
                        _randomRootSeed, _randomSemanticHash, _randomTreeInstanceId, _randomNodeIndicesV2[index], out var stream);
                    _randomStatesV2[index] = stream.State;
                    _randomIncrementsV2[index] = stream.Increment;
                }
            }
            InitializeSentinels();
            failure = default;
            return true;
        }

        private void InitializeSentinels()
        {
            for (var index = 0; index < _frames.Length; index++)
            {
                var frame = _frames[index];
                frame.NodeIndex = CompiledIndex.Invalid;
                frame.ParentFrameIndex = CompiledIndex.Invalid;
                frame.PendingStatus = NodeStatus.Running;
                _frames[index] = frame;
            }

            for (var index = 0; index < _parallelBranches.Length; index++)
            {
                var branch = _parallelBranches[index];
                branch.CapacityOrdinal = (uint)index;
                branch.NodeIndex = CompiledIndex.Invalid;
                _parallelBranches[index] = branch;
            }

            for (var index = 0; index < _observers.Length; index++)
            {
                var observer = _observers[index];
                if (index < _observerBindings.Length)
                {
                    observer.ObserverNodeIndex = _observerBindings[index].ObserverNodeIndex;
                    observer.OwningReactiveCompositeIndex = _observerBindings[index].OwningReactiveCompositeIndex;
                }
                else
                {
                    observer.ObserverNodeIndex = CompiledIndex.Invalid;
                    observer.OwningReactiveCompositeIndex = CompiledIndex.Invalid;
                }

                _observers[index] = observer;
            }
        }

        private NativeInstanceArenaViewV1 CreateView()
            => new NativeInstanceArenaViewV1(
                _nodeMemory,
                _treeBlackboard,
                _frames,
                _generations,
                _parallelBranches,
                _observers,
                _updateState,
                _budgetState);

        private NativeInstanceArenaViewV2 CreateViewV2()
            => new NativeInstanceArenaViewV2(
                CreateView(), _treeSlotVersionsV2, _treeRevisionV2,
                _randomStatesV2, _randomIncrementsV2, _randomNodeIndicesV2);

        private static bool TryInitializeTreeDefaults(
            NativeProgramImageViewV2 program,
            NativeArray<byte> destination,
            out NativeRuntimeFailureV1 failure)
        {
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope != BlackboardScope.Tree) continue;
                if ((ulong)slot.Offset + slot.Size > (uint)destination.Length
                    || (ulong)slot.DefaultOffset + slot.DefaultSize > (uint)program.Semantic.DefaultValueBlob.Length
                    || slot.Size != slot.DefaultSize
                    || !NativeBlackboardCanonicalV1.IsCanonical(
                        program, slot, program.Semantic.DefaultValueBlob, slot.DefaultOffset))
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.BlackboardInvalidSlot,
                        NativeResourceKindV1.InstanceTreeBlackboard,
                        (ulong)slot.Offset + slot.Size,
                        (uint)destination.Length);
                    return false;
                }
            }
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope == BlackboardScope.Tree) CopyDefault(program, destination, slot);
            }
            failure = default;
            return true;
        }

        private static bool TreeBytesEqual(
            NativeProgramImageViewV2 program,
            NativeArray<byte> destination,
            NativeBlackboardSlotBindingV2 slot)
        {
            for (var index = 0; index < slot.Size; index++)
                if (destination[(int)slot.Offset + index] != program.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + index]) return false;
            return true;
        }

        private static void CopyDefault(
            NativeProgramImageViewV2 program,
            NativeArray<byte> destination,
            NativeBlackboardSlotBindingV2 slot)
        {
            for (var index = 0; index < slot.Size; index++)
                destination[(int)slot.Offset + index] = program.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + index];
        }

        private bool IsActiveLease(NativeInstanceExecutionLeaseV1 lease)
            => ReferenceEquals(lease.Owner, this)
                && State == NativeOwnerStateV1.Executing
                && lease.Token == _activeLease;

        internal bool IsExecutionLeaseActive(NativeInstanceExecutionLeaseV2 lease)
            => lease.IsValid && IsActiveLease(lease.SemanticLease);

        private NativeRuntimeFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private NativeRuntimeFailureV1 LiveJobFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private static bool TryValidateCapacity(
            NativeInstanceArenaCapacityV1 required,
            NativeInstanceArenaCapacityV1 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            if (!NativeCheckedMathV1.IsPowerOfTwo(capacity.MaximumAlignment))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.MaximumAlignment,
                    required.MaximumAlignment,
                    capacity.MaximumAlignment,
                    capacity.MaximumAlignment);
                return false;
            }

            if (!RequireCapacity(required.NodeMemoryBytes, capacity.NodeMemoryBytes, NativeResourceKindV1.InstanceNodeMemory, out failure)
                || !RequireCapacity(required.TreeBlackboardBytes, capacity.TreeBlackboardBytes, NativeResourceKindV1.InstanceTreeBlackboard, out failure)
                || !RequireCapacity(required.FrameCount, capacity.FrameCount, NativeResourceKindV1.InstanceFrames, out failure)
                || !RequireCapacity(required.GenerationCount, capacity.GenerationCount, NativeResourceKindV1.InstanceGenerations, out failure)
                || !RequireCapacity(required.ParallelBranchCapacity, capacity.ParallelBranchCapacity, NativeResourceKindV1.InstanceParallelBranches, out failure)
                || !RequireCapacity(required.ObserverCount, capacity.ObserverCount, NativeResourceKindV1.InstanceObservers, out failure)
                || !RequireCapacity(required.UpdateStateCount, capacity.UpdateStateCount, NativeResourceKindV1.InstanceUpdateState, out failure)
                || !RequireCapacity(required.BudgetStateCount, capacity.BudgetStateCount, NativeResourceKindV1.InstanceBudgetState, out failure)
                || !RequireCapacity(required.MaximumAlignment, capacity.MaximumAlignment, NativeResourceKindV1.MaximumAlignment, out failure))
            {
                return false;
            }

            failure = default;
            return true;
        }

        private static bool RequireCapacity(uint requested, uint capacity, NativeResourceKindV1 resource, out NativeRuntimeFailureV1 failure)
        {
            if (capacity > int.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    resource,
                    capacity,
                    int.MaxValue);
                return false;
            }

            if (requested > capacity)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    resource,
                    requested,
                    capacity);
                return false;
            }

            failure = default;
            return true;
        }

        private static NativeArray<T> Allocate<T>(uint length, Allocator allocator, int failAfter, ref int allocations)
            where T : struct
        {
            if (failAfter >= 0 && allocations == failAfter)
            {
                throw new InvalidOperationException("Injected native allocation failure.");
            }

            var result = new NativeArray<T>((int)length, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return result;
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            for (var index = 0; index < array.Length; index++)
            {
                array[index] = default;
            }
        }

        private static void Clear(NativeArray<byte> array, uint offset, uint size)
        {
            var end = offset + size;
            for (var index = offset; index < end; index++)
            {
                array[(int)index] = 0;
            }
        }

        private static void DisposeReverse(
            ref NativeArray<NativeBudgetStateV1> budgetState,
            ref NativeArray<NativeUpdateStateV1> updateState,
            ref NativeArray<NativeObserverStateV1> observers,
            ref NativeArray<NativeParallelBranchStateV1> parallelBranches,
            ref NativeArray<uint> generations,
            ref NativeArray<NativeFrameStateV1> frames,
            ref NativeArray<byte> treeBlackboard,
            ref NativeArray<byte> nodeMemory)
        {
            Dispose(ref budgetState);
            Dispose(ref updateState);
            Dispose(ref observers);
            Dispose(ref parallelBranches);
            Dispose(ref generations);
            Dispose(ref frames);
            Dispose(ref treeBlackboard);
            Dispose(ref nodeMemory);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }

        private readonly struct NativeNodeMemoryRegionV1
        {
            internal NativeNodeMemoryRegionV1(uint offset, uint size, NodeMemoryLifetime lifetime)
            {
                Offset = offset;
                Size = size;
                Lifetime = lifetime;
            }

            internal uint Offset { get; }
            internal uint Size { get; }
            internal NodeMemoryLifetime Lifetime { get; }
        }

        private readonly struct NativeObserverBindingV1
        {
            internal NativeObserverBindingV1(uint observerNodeIndex, uint owningReactiveCompositeIndex)
            {
                ObserverNodeIndex = observerNodeIndex;
                OwningReactiveCompositeIndex = owningReactiveCompositeIndex;
            }

            internal uint ObserverNodeIndex { get; }
            internal uint OwningReactiveCompositeIndex { get; }
        }
    }

    public readonly struct NativeInstanceExecutionLeaseV1
    {
        internal NativeInstanceExecutionLeaseV1(
            NativeInstanceArenaOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeInstanceArenaViewV1 view)
        {
            Owner = owner;
            Token = token;
            View = view;
        }

        internal NativeInstanceArenaOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeInstanceArenaViewV1 View { get; }
        public bool IsValid => Owner != null && Token.IsValid;
    }
}
