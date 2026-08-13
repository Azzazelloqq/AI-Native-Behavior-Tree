using System;
using System.Collections.Generic;

namespace AIBT
{
    internal enum ReferenceOperationState : byte
    {
        Active,
        Consumed,
        Cancelled,
    }

    internal enum ReferenceOperationTransition : byte
    {
        Applied,
        AlreadyApplied,
        InvalidState,
        Unknown,
    }

    internal sealed class ReferenceOperationLedger
    {
        private readonly TreeInstanceId _treeInstanceId;
        private readonly Dictionary<OperationId, ReferenceOperationState> _states;
        private ulong _nextSequence;
        private bool _sequenceExhausted;

        internal ReferenceOperationLedger(TreeInstanceId treeInstanceId, ulong firstSequence = 1)
        {
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            if (firstSequence == 0) throw new ArgumentOutOfRangeException(nameof(firstSequence));
            _treeInstanceId = treeInstanceId;
            _nextSequence = firstSequence;
            _states = new Dictionary<OperationId, ReferenceOperationState>();
        }

        internal TreeInstanceId TreeInstanceId => _treeInstanceId;
        internal int Count => _states.Count;
        internal int ActiveCount
        {
            get
            {
                var count = 0;
                foreach (var pair in _states)
                    if (pair.Value == ReferenceOperationState.Active) count++;
                return count;
            }
        }
        internal bool CanAllocate => !_sequenceExhausted;

        internal bool TryAllocate(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            out OperationId operationId,
            out Diagnostic diagnostic)
        {
            operationId = default;
            diagnostic = null;
            if (!nodeIndex.IsValid) throw new ArgumentException("A runtime node index is required.", nameof(nodeIndex));
            if (activationGeneration == 0) throw new ArgumentOutOfRangeException(nameof(activationGeneration));
            if (_sequenceExhausted)
            {
                diagnostic = CommandAsyncDiagnostics.Create(
                    CommandAsyncDiagnosticCodes.OperationSequenceOverflow,
                    "The per-instance operation sequence is exhausted and cannot advance without wrapping.",
                    _treeInstanceId);
                return false;
            }

            operationId = new OperationId(_treeInstanceId, nodeIndex, activationGeneration, _nextSequence);
            _states.Add(operationId, ReferenceOperationState.Active);
            if (_nextSequence == ulong.MaxValue)
            {
                _sequenceExhausted = true;
            }
            else
            {
                _nextSequence++;
            }

            return true;
        }

        internal bool TryGetState(OperationId operationId, out ReferenceOperationState state)
        {
            return _states.TryGetValue(operationId, out state);
        }

        internal ReferenceOperationTransition MarkConsumed(OperationId operationId)
        {
            if (!_states.TryGetValue(operationId, out var state)) return ReferenceOperationTransition.Unknown;
            if (state == ReferenceOperationState.Consumed) return ReferenceOperationTransition.AlreadyApplied;
            if (state != ReferenceOperationState.Active) return ReferenceOperationTransition.InvalidState;
            _states[operationId] = ReferenceOperationState.Consumed;
            return ReferenceOperationTransition.Applied;
        }

        internal ReferenceOperationTransition MarkCancelled(OperationId operationId)
        {
            if (!_states.TryGetValue(operationId, out var state)) return ReferenceOperationTransition.Unknown;
            if (state == ReferenceOperationState.Cancelled) return ReferenceOperationTransition.AlreadyApplied;
            if (state != ReferenceOperationState.Active) return ReferenceOperationTransition.InvalidState;
            _states[operationId] = ReferenceOperationState.Cancelled;
            return ReferenceOperationTransition.Applied;
        }

        internal void CancelAllActive()
        {
            if (_states.Count == 0) return;
            var active = new List<OperationId>();
            foreach (var pair in _states)
            {
                if (pair.Value == ReferenceOperationState.Active) active.Add(pair.Key);
            }

            for (var index = 0; index < active.Count; index++)
            {
                _states[active[index]] = ReferenceOperationState.Cancelled;
            }
        }

        internal OperationId[] CopyActiveOperations()
        {
            var active = new List<OperationId>();
            foreach (var pair in _states)
            {
                if (pair.Value == ReferenceOperationState.Active) active.Add(pair.Key);
            }

            active.Sort(OperationComparer.Instance);
            return active.ToArray();
        }

        private sealed class OperationComparer : IComparer<OperationId>
        {
            internal static readonly OperationComparer Instance = new OperationComparer();
            public int Compare(OperationId left, OperationId right)
            {
                var result = left.TreeInstanceId.Value.CompareTo(right.TreeInstanceId.Value);
                if (result != 0) return result;
                result = left.NodeIndex.Value.CompareTo(right.NodeIndex.Value);
                if (result != 0) return result;
                result = left.ActivationGeneration.CompareTo(right.ActivationGeneration);
                return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
            }
        }
    }
}
