using System;
using System.Collections.Generic;
using AIBT.Authoring;

namespace AIBT.Agents
{
    /// <summary>How one managed tick ended.</summary>
    internal enum ManagedTickOutcome : byte
    {
        /// <summary>A leaf is still running; tick again.</summary>
        Running,

        /// <summary>The root finished with <see cref="ManagedTreeInstance.LastResult"/>.</summary>
        Completed,

        /// <summary>A node threw or the tree hit an invalid state; see <see cref="ManagedTreeInstance.FaultMessage"/>.</summary>
        Faulted,

        /// <summary>The instance was stopped or already finished and was not ticked.</summary>
        Idle,
    }

    /// <summary>Last observed state of a node, for live debugging views.</summary>
    internal enum ManagedNodeState : byte
    {
        Idle = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Aborted = 4,
    }

    /// <summary>
    /// One running tree: a <see cref="ReferenceExecutionMachine"/> plus the managed node objects
    /// its leaves dispatch to. Owns no Unity object, so tests drive it without a scene.
    /// </summary>
    internal sealed class ManagedTreeInstance : IReferenceTraceSink, IReferenceLeafHandler
    {
        private const uint None = RuntimeNodeIndex.InvalidValue;

        private static readonly ulong MemorySequenceTypeId = StableHash.Fnv1A64(BuiltInNodeManifests.MemorySequenceTypeId);
        private static readonly ulong MemorySelectorTypeId = StableHash.Fnv1A64(BuiltInNodeManifests.MemorySelectorTypeId);
        private static long s_nextInstanceId;

        private readonly ManagedTreeProgram _program;
        private readonly ReferenceExecutionMachine _machine;
        private readonly BtNode[] _nodes;
        private readonly BtCondition[] _conditions;
        private readonly uint[] _parents;
        private readonly bool[] _isComposite;
        private readonly bool[] _isMemoryComposite;
        private readonly bool[] _isMemorySelector;
        private readonly Watch[] _watches;
        private readonly List<uint> _watched = new List<uint>();
        private readonly Dictionary<string, KeySlot> _keys;
        private readonly string[] _keyNames;
        private readonly BlackboardValueType[] _slotTypes;
        private readonly long[] _slotChangedAt;
        private readonly byte[] _nodeStates;
        private ulong _updateId;
        private long _timeMicroseconds;
        private uint _currentNode = None;
        private Exception _nodeException;
        private uint _nodeExceptionIndex = None;
        private bool _stopped;

        internal ManagedTreeInstance(ManagedTreeProgram program, BehaviorTreeAgent owner)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            Owner = owner;
            var compiled = program.Program;
            var count = compiled.Nodes.Count;
            _nodes = new BtNode[count];
            _conditions = new BtCondition[count];
            _nodeStates = new byte[count];
            _watches = new Watch[count];
            _parents = new uint[count];
            _isComposite = new bool[count];
            _isMemoryComposite = new bool[count];
            _isMemorySelector = new bool[count];
            NodeIds = new string[count];
            for (var index = 0; index < compiled.DebugMap.Count; index++)
                NodeIds[compiled.DebugMap[index].RuntimeNodeIndex] = compiled.DebugMap[index].AuthoringNodeId.Value;

            for (var index = 0; index < count; index++) _parents[index] = None;
            for (var index = 0; index < count; index++)
            {
                var record = compiled.Nodes[index];
                _isMemorySelector[index] = record.NodeTypeId == MemorySelectorTypeId;
                _isMemoryComposite[index] = _isMemorySelector[index] || record.NodeTypeId == MemorySequenceTypeId;
                _isComposite[index] = program.Nodes.Registry.TryGet(record.NodeTypeId, out var entry)
                    && entry.Manifest.Kind == NodeBehaviorKind.Composite;
                for (var child = 0u; child < record.Children.Count; child++)
                    _parents[compiled.ChildIndices[(int)(record.Children.Offset + child)]] = (uint)index;
            }

            var bindings = new List<ReferenceLeafBinding>();
            var bound = new HashSet<ulong>();
            for (var index = 0; index < BuiltInLeafManifests.All.Count; index++)
            {
                var provider = BuiltInLeafManifests.All[index];
                bindings.Add(new ReferenceLeafBinding(
                    StableHash.Fnv1A64(provider.Manifest.TypeId),
                    provider.Manifest.Version,
                    new ReferencePreviewLeafFactoryHandler(provider, null)));
            }

            for (var index = 0; index < program.Leaves.Length; index++)
            {
                var leaf = program.Leaves[index];
                if (leaf == null || !bound.Add(leaf.Type.NumericTypeId)) continue;
                bindings.Add(new ReferenceLeafBinding(leaf.Type.NumericTypeId, leaf.Type.Manifest.Version, this));
            }

            _machine = new ReferenceExecutionMachine(
                compiled,
                new TreeInstanceId((ulong)System.Threading.Interlocked.Increment(ref s_nextInstanceId)),
                new ReferenceLeafRegistry(bindings),
                this,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);

            _keys = new Dictionary<string, KeySlot>(StringComparer.Ordinal);
            _slotTypes = new BlackboardValueType[compiled.BlackboardSlots.Count];
            _slotChangedAt = new long[compiled.BlackboardSlots.Count];
            var names = new List<string>(program.Document.Blackboard.Count);
            for (var index = 0; index < program.Document.Blackboard.Count; index++)
            {
                var key = program.Document.Blackboard[index];
                if (_machine.TryFindBlackboardSlot(StableHash.Fnv1A64(key.Id), out var slot, out var type))
                {
                    _keys[key.Id] = new KeySlot(slot, type.ValueType);
                    _slotTypes[slot] = type.ValueType;
                    names.Add(key.Id);
                }
            }

            _keyNames = names.ToArray();
            Blackboard = new AgentBlackboard(this);

            // Nodes are created after the blackboard is resolvable so key parameters bind at once.
            for (var index = 0; index < program.Leaves.Length; index++)
            {
                var leaf = program.Leaves[index];
                if (leaf == null) continue;
                var node = leaf.Type.CreateNode();
                node.Attach(this, leaf.Document.Id.Value, (uint)index);
                leaf.Type.Bind(node, leaf.Document, this);
                _nodes[index] = node;
                _conditions[index] = node as BtCondition;
            }

            for (var index = 0; index < _nodes.Length; index++) _nodes[index]?.Initialize();
        }

        internal BehaviorTreeAgent Owner { get; }

        internal ManagedTreeProgram Program => _program;

        /// <summary>The reference machine this instance drives; exposed for its own tests.</summary>
        internal ReferenceExecutionMachine Machine => _machine;

        internal AgentBlackboard Blackboard { get; }

        internal IReadOnlyList<string> KeyNames => _keyNames;

        /// <summary>Authoring node ID per runtime node index.</summary>
        internal string[] NodeIds { get; }

        internal float DeltaTime { get; private set; }

        internal float TimeSeconds => _timeMicroseconds / 1000000f;

        internal NodeStatus? LastResult { get; private set; }

        internal bool IsFaulted { get; private set; }

        internal string FaultMessage { get; private set; }

        /// <summary>The exception a node threw, with its original stack trace, when that caused the fault.</summary>
        internal Exception FaultException { get; private set; }

        internal string FaultNodeId { get; private set; }

        internal ulong TickCount => _updateId;

        /// <summary>Incremented whenever any node's live state changes; lets a debugger skip unchanged frames.</summary>
        internal uint StateVersion { get; private set; }

        internal bool IsActive => !_stopped && !IsFaulted && !LastResult.HasValue;

        /// <summary>Number of conditions currently re-checked every tick because of their interrupt mode.</summary>
        internal int WatchedConditionCount => _watched.Count;

        /// <summary>Live state of one node for debugging views.</summary>
        internal ManagedNodeState GetNodeState(uint runtimeIndex) => (ManagedNodeState)_nodeStates[runtimeIndex];

        internal ManagedTickOutcome Tick(float deltaSeconds)
        {
            if (_stopped || IsFaulted || LastResult.HasValue) return ManagedTickOutcome.Idle;
            if (deltaSeconds < 0f || float.IsNaN(deltaSeconds)) deltaSeconds = 0f;
            DeltaTime = deltaSeconds;
            _timeMicroseconds = checked(_timeMicroseconds + (long)(deltaSeconds * 1000000.0));
            if (_watched.Count != 0 && !CheckWatchedConditions()) return ManagedTickOutcome.Faulted;

            _updateId++;
            var envelope = _machine.Update(new ReferenceUpdateContext(_updateId, new Revision(_updateId), _timeMicroseconds));
            switch (envelope.Progress)
            {
                case ReferenceExecutionProgress.Waiting:
                    return ManagedTickOutcome.Running;
                case ReferenceExecutionProgress.Completed:
                    LastResult = envelope.RootResult ?? _machine.TerminalResult ?? NodeStatus.Failure;
                    ClearWatches();
                    return ManagedTickOutcome.Completed;
                default:
                    Fault(envelope.Diagnostics);
                    return ManagedTickOutcome.Faulted;
            }
        }

        /// <summary>
        /// Starts the tree again from its root. A running tree is aborted first so its active
        /// nodes exit cleanly. Blackboard values are kept unless <paramref name="resetBlackboard"/>.
        /// </summary>
        internal bool Restart(bool resetBlackboard)
        {
            AbortActive();
            BlackboardValue[] kept = null;
            if (!resetBlackboard)
            {
                kept = new BlackboardValue[_keyNames.Length];
                for (var index = 0; index < _keyNames.Length; index++)
                    _machine.TryReadBlackboardSlot(_keys[_keyNames[index]].Slot, out kept[index]);
            }

            var envelope = _machine.Restart();
            if (envelope.Progress != ReferenceExecutionProgress.Completed)
            {
                Fault(envelope.Diagnostics);
                return false;
            }

            if (kept == null)
            {
                for (var index = 0; index < _slotChangedAt.Length; index++) _slotChangedAt[index] = _timeMicroseconds;
            }
            else
            {
                for (var index = 0; index < _keyNames.Length; index++)
                {
                    if (kept[index].IsValid)
                        _machine.TryWriteBlackboardSlot(_keys[_keyNames[index]].Slot, kept[index], RuntimeNodeIndex.Invalid);
                }
            }

            Array.Clear(_nodeStates, 0, _nodeStates.Length);
            ClearWatches();
            StateVersion++;
            LastResult = null;
            IsFaulted = false;
            FaultMessage = null;
            FaultException = null;
            FaultNodeId = null;
            _stopped = false;
            return true;
        }

        /// <summary>Aborts any running node and stops ticking until <see cref="Restart"/>.</summary>
        internal void Stop()
        {
            AbortActive();
            ClearWatches();
            _stopped = true;
        }

        internal bool TryResolveKey(string name, out uint slot, out BlackboardValueType type)
        {
            if (name != null && _keys.TryGetValue(name, out var key))
            {
                slot = key.Slot;
                type = key.Type;
                return true;
            }

            slot = CompiledIndex.Invalid;
            type = BlackboardValueType.Invalid;
            return false;
        }

        internal bool TryRead(uint slot, out BlackboardValue value) => _machine.TryReadBlackboardSlot(slot, out value);

        internal bool TryWrite(uint slot, BlackboardValue value)
        {
            var known = _machine.TryReadBlackboardSlot(slot, out var previous);
            if (!_machine.TryWriteBlackboardSlot(slot, value, new RuntimeNodeIndex(_currentNode))) return false;
            if ((!known || !previous.Equals(value)) && slot < (uint)_slotChangedAt.Length) _slotChangedAt[slot] = _timeMicroseconds;
            return true;
        }

        /// <summary>
        /// Seconds of tree time since a slot last changed value through a node or gameplay write.
        /// A key that never changed counts from the tree's start or its last blackboard reset.
        /// </summary>
        internal float SecondsSinceChanged(uint slot)
            => slot < (uint)_slotChangedAt.Length ? (_timeMicroseconds - _slotChangedAt[slot]) / 1000000f : 0f;

        internal BlackboardValueType SlotType(uint slot)
            => slot < (uint)_slotTypes.Length ? _slotTypes[slot] : BlackboardValueType.Invalid;

        void IReferenceLeafHandler.Enter(ref ReferenceNodeContext context)
        {
            var index = context.NodeIndex.Value;
            var previous = _currentNode;
            _currentNode = index;
            try
            {
                _nodes[index].EnterNode();
            }
            catch (Exception exception)
            {
                Capture(exception, index);
                throw;
            }
            finally
            {
                _currentNode = previous;
            }
        }

        NodeStatus IReferenceLeafHandler.Tick(ref ReferenceNodeContext context)
        {
            var index = context.NodeIndex.Value;
            var previous = _currentNode;
            _currentNode = index;
            try
            {
                var status = _nodes[index].TickNode();
                var condition = _conditions[index];
                if (condition != null && condition.interrupt != ConditionInterrupt.None)
                    RecordWatch(index, condition.interrupt, status == NodeStatus.Success);
                return status;
            }
            catch (Exception exception)
            {
                Capture(exception, index);
                throw;
            }
            finally
            {
                _currentNode = previous;
            }
        }

        void IReferenceLeafHandler.Abort(ref ReferenceNodeContext context, NodeAbortReason reason)
        {
            var index = context.NodeIndex.Value;
            var previous = _currentNode;
            _currentNode = index;
            try
            {
                _nodes[index].AbortNode();
            }
            catch (Exception exception)
            {
                Capture(exception, index);
                throw;
            }
            finally
            {
                _currentNode = previous;
            }
        }

        void IReferenceLeafHandler.Exit(ref ReferenceNodeContext context, NodeExitReason reason)
        {
            var index = context.NodeIndex.Value;
            var previous = _currentNode;
            _currentNode = index;
            try
            {
                _nodes[index].ExitNode();
            }
            catch (Exception exception)
            {
                Capture(exception, index);
                throw;
            }
            finally
            {
                _currentNode = previous;
            }
        }

        void IReferenceTraceSink.Record(in ReferenceTraceRecord record)
        {
            if (!record.NodeIndex.IsValid || record.NodeIndex.Value >= (uint)_nodeStates.Length) return;
            var index = record.NodeIndex.Value;
            switch (record.Kind)
            {
                case ReferenceTraceEventKind.NodeEntered:
                    _nodeStates[index] = (byte)ManagedNodeState.Running;
                    StateVersion++;
                    break;
                case ReferenceTraceEventKind.NodeExited:
                    _nodeStates[index] = record.ExitReason == NodeExitReason.Success ? (byte)ManagedNodeState.Succeeded
                        : record.ExitReason == NodeExitReason.Failure ? (byte)ManagedNodeState.Failed
                        : (byte)ManagedNodeState.Aborted;
                    StateVersion++;
                    // A composite that finishes ends the scope of every condition it was guarding.
                    if (_isMemoryComposite[index] && _watched.Count != 0) DropWatchesOwnedBy(index);
                    break;
            }
        }

        // ---- Condition interrupts -------------------------------------------------------------
        //
        // A memory composite keeps the child it chose until that child finishes. A condition with
        // an interrupt mode stays under watch while its answer still explains that choice: it is
        // re-checked before every tick, and a changed answer asks the owning composite to abandon
        // its running child and choose again (ReferenceExecutionMachine.TryRequestReevaluation).
        // Reactive composites need none of this: they reevaluate on every update by definition.

        private void RecordWatch(uint index, ConditionInterrupt mode, bool result)
        {
            var wanted = result
                ? mode == ConditionInterrupt.Self || mode == ConditionInterrupt.Both
                : mode == ConditionInterrupt.LowerPriority || mode == ConditionInterrupt.Both;
            var owner = None;
            var branch = index;
            if (wanted)
            {
                // Self: the nearest composite, which goes on to run what this condition allowed.
                // LowerPriority: the nearest selector, which moves on to a later child.
                for (var parent = _parents[index]; parent != None; branch = parent, parent = _parents[parent])
                {
                    if (result ? _isComposite[parent] : _isMemorySelector[parent])
                    {
                        // Only a memory composite can be asked to choose again; a reactive one
                        // already reevaluates every update and a parallel has no single choice.
                        if (_isMemoryComposite[parent]) owner = parent;
                        break;
                    }
                }
            }

            ref var watch = ref _watches[index];
            if (owner == None)
            {
                if (watch.Active) RemoveWatch(index);
                return;
            }

            if (!watch.Active) _watched.Add(index);
            watch.Active = true;
            watch.Expected = result;
            watch.Owner = owner;
            watch.Branch = branch;
        }

        private bool CheckWatchedConditions()
        {
            var needsFullRestart = false;
            for (var position = 0; position < _watched.Count; position++)
            {
                var index = _watched[position];
                ref var watch = ref _watches[index];
                // A failed condition matters only while its selector runs a lower-priority child.
                if (!watch.Expected && !IsBeforeRunningChild(watch.Owner, watch.Branch)) continue;

                bool answer;
                _currentNode = index;
                try
                {
                    answer = _conditions[index].Evaluate();
                }
                catch (Exception exception)
                {
                    Capture(exception, index);
                    Fault(null);
                    FaultMessage = "A condition threw while it was re-checked for its interrupt mode: " + exception.Message;
                    return false;
                }
                finally
                {
                    _currentNode = None;
                }

                if (answer == watch.Expected) continue;
                watch.Expected = answer;
                if (!_machine.TryRequestReevaluation(new RuntimeNodeIndex(watch.Owner))) needsFullRestart = true;
            }

            // The owner is not on the single active path (it sits inside a parallel branch), so
            // only a restart from the root can make it choose again.
            return !needsFullRestart || Restart(false);
        }

        private bool IsBeforeRunningChild(uint owner, uint branch)
        {
            var record = _program.Program.Nodes[(int)owner];
            for (var child = 0u; child < record.Children.Count; child++)
            {
                var childIndex = _program.Program.ChildIndices[(int)(record.Children.Offset + child)];
                if (childIndex == branch) return _nodeStates[childIndex] != (byte)ManagedNodeState.Running;
                if (_nodeStates[childIndex] == (byte)ManagedNodeState.Running) return false;
            }

            return false;
        }

        private void DropWatchesOwnedBy(uint owner)
        {
            for (var position = _watched.Count - 1; position >= 0; position--)
            {
                var index = _watched[position];
                if (_watches[index].Owner != owner) continue;
                _watches[index] = default;
                _watched.RemoveAt(position);
            }
        }

        private void RemoveWatch(uint index)
        {
            _watches[index] = default;
            _watched.Remove(index);
        }

        private void ClearWatches()
        {
            for (var position = 0; position < _watched.Count; position++) _watches[_watched[position]] = default;
            _watched.Clear();
        }

        private void AbortActive()
        {
            if (_stopped || IsFaulted || LastResult.HasValue || _updateId == 0) return;
            _updateId++;
            var update = new ReferenceUpdateContext(_updateId, new Revision(_updateId), _timeMicroseconds);
            // "Only an active tree instance can be aborted" is a normal outcome here (nothing was
            // running); a node that throws during its own abort still faults the instance.
            var envelope = _machine.Abort(update, NodeAbortReason.TreeStopped, new RuntimeNodeIndex(_program.Program.Header.RootNodeIndex));
            if (envelope.Progress == ReferenceExecutionProgress.Faulted) Fault(envelope.Diagnostics);
        }

        private void Capture(Exception exception, uint nodeIndex)
        {
            if (_nodeException != null) return;
            _nodeException = exception;
            _nodeExceptionIndex = nodeIndex;
        }

        private void Fault(DiagnosticCollection diagnostics)
        {
            IsFaulted = true;
            FaultException = _nodeException;
            FaultNodeId = _nodeExceptionIndex != None ? NodeIds[_nodeExceptionIndex] : null;
            FaultMessage = diagnostics != null && diagnostics.Count != 0
                ? ManagedTreeProgram.Describe(diagnostics)
                : "The behavior tree stopped in an invalid state.";
            _nodeException = null;
            _nodeExceptionIndex = None;
            ClearWatches();
        }

        private struct Watch
        {
            internal bool Active;
            internal bool Expected;
            internal uint Owner;
            internal uint Branch;
        }

        private readonly struct KeySlot
        {
            internal KeySlot(uint slot, BlackboardValueType type)
            {
                Slot = slot;
                Type = type;
            }

            internal uint Slot { get; }

            internal BlackboardValueType Type { get; }
        }
    }
}
