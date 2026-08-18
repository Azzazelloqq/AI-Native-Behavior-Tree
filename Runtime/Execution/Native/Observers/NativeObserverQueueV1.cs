using System;
using AIBT.Burst;
using Unity.Collections;

namespace AIBT
{
    internal readonly struct NativeObserverTransitionV1
    {
        internal NativeObserverTransitionV1(
            uint observerNodeIndex,
            uint ownerNodeIndex,
            CompiledObserverMode mode,
            BurstNodeAbortReason reason)
        {
            ObserverNodeIndex = observerNodeIndex;
            OwnerNodeIndex = ownerNodeIndex;
            Mode = mode;
            Reason = reason;
        }
        internal uint ObserverNodeIndex { get; }
        internal uint OwnerNodeIndex { get; }
        internal CompiledObserverMode Mode { get; }
        internal BurstNodeAbortReason Reason { get; }
    }

    internal struct NativeObserverQueueV1
    {
        private NativeArray<NativeCompiledObserverRecordV1> _observers;
        private NativeArray<uint> _adjacencyOffsets;
        private NativeArray<uint> _adjacency;
        private NativeArray<uint> _queue;
        private NativeArray<byte> _queued;
        private NativeArray<NativeObserverStateV1> _states;
        private NativeArray<uint> _control;

        internal static bool TryCreate(
            NativeArray<NativeCompiledObserverRecordV1> observers,
            NativeArray<uint> watchedSlots,
            uint slotCount,
            Allocator allocator,
            out NativeObserverQueueV1 queue)
        {
            queue = default;
            if (!observers.IsCreated || !watchedSlots.IsCreated || slotCount > int.MaxValue
                || allocator != Allocator.Persistent) return false;
            var offsets = default(NativeArray<uint>);
            var adjacency = default(NativeArray<uint>);
            var ordered = default(NativeArray<uint>);
            var queued = default(NativeArray<byte>);
            var states = default(NativeArray<NativeObserverStateV1>);
            var control = default(NativeArray<uint>);
            try
            {
                for (var ordinal = 0; ordinal < observers.Length; ordinal++)
                {
                    var observer = observers[ordinal];
                    if (observer.ObserverNodeIndex == CompiledIndex.Invalid
                        || observer.OwningReactiveCompositeIndex == CompiledIndex.Invalid
                        || observer.Mode < CompiledObserverMode.Self || observer.Mode > CompiledObserverMode.Both
                        || observer.WatchedSlotOffset > watchedSlots.Length
                        || observer.WatchedSlotCount > watchedSlots.Length - observer.WatchedSlotOffset
                        || ordinal != 0 && observers[ordinal - 1].ObserverNodeIndex >= observer.ObserverNodeIndex)
                        return false;
                    for (uint index = 0; index < observer.WatchedSlotCount; index++)
                        if (watchedSlots[(int)(observer.WatchedSlotOffset + index)] >= slotCount) return false;
                }
                offsets = new NativeArray<uint>(checked((int)slotCount + 1), allocator, NativeArrayOptions.ClearMemory);
                adjacency = new NativeArray<uint>(watchedSlots.Length, allocator, NativeArrayOptions.ClearMemory);
                ordered = new NativeArray<uint>(observers.Length, allocator, NativeArrayOptions.ClearMemory);
                queued = new NativeArray<byte>(observers.Length, allocator, NativeArrayOptions.ClearMemory);
                states = new NativeArray<NativeObserverStateV1>(observers.Length, allocator, NativeArrayOptions.ClearMemory);
                control = new NativeArray<uint>(1, allocator, NativeArrayOptions.ClearMemory);
                for (var ordinal = 0; ordinal < observers.Length; ordinal++)
                {
                    var observer = observers[ordinal];
                    for (uint index = 0; index < observer.WatchedSlotCount; index++)
                        offsets[(int)watchedSlots[(int)(observer.WatchedSlotOffset + index)] + 1]++;
                    states[ordinal] = new NativeObserverStateV1
                    {
                        ObserverNodeIndex = observer.ObserverNodeIndex,
                        OwningReactiveCompositeIndex = observer.OwningReactiveCompositeIndex,
                    };
                }
                for (var slot = 1; slot < offsets.Length; slot++) offsets[slot] += offsets[slot - 1];
                var cursors = new uint[offsets.Length];
                for (var slot = 0; slot < offsets.Length; slot++) cursors[slot] = offsets[slot];
                for (var ordinal = 0; ordinal < observers.Length; ordinal++)
                {
                    var observer = observers[ordinal];
                    for (uint index = 0; index < observer.WatchedSlotCount; index++)
                    {
                        var slot = watchedSlots[(int)(observer.WatchedSlotOffset + index)];
                        adjacency[(int)cursors[slot]++] = (uint)ordinal;
                    }
                }
                queue = new NativeObserverQueueV1
                {
                    _observers = observers,
                    _adjacencyOffsets = offsets,
                    _adjacency = adjacency,
                    _queue = ordered,
                    _queued = queued,
                    _states = states,
                    _control = control,
                };
                return true;
            }
            catch (Exception)
            {
                Dispose(ref control); Dispose(ref states); Dispose(ref queued); Dispose(ref ordered); Dispose(ref adjacency); Dispose(ref offsets);
                return false;
            }
        }

        internal bool TryEnqueueChangedSlot(uint slotIndex)
        {
            if (!_control.IsCreated || slotIndex + 1u >= _adjacencyOffsets.Length) return false;
            var start = _adjacencyOffsets[(int)slotIndex];
            var end = _adjacencyOffsets[(int)slotIndex + 1];
            for (var index = start; index < end; index++)
            {
                var ordinal = _adjacency[(int)index];
                if (_queued[(int)ordinal] != 0) continue;
                var count = _control[0];
                if (count >= _queue.Length) return false;
                var insert = count;
                var nodeIndex = _observers[(int)ordinal].ObserverNodeIndex;
                while (insert != 0 && _observers[(int)_queue[(int)insert - 1]].ObserverNodeIndex > nodeIndex)
                {
                    _queue[(int)insert] = _queue[(int)insert - 1];
                    insert--;
                }
                _queue[(int)insert] = ordinal;
                _queued[(int)ordinal] = 1;
                _control[0] = count + 1;
            }
            return true;
        }

        internal bool TryEnqueueSharedReport(NativeSharedCommitReportV1 report, ulong updateId)
        {
            if (!report.IsValid || updateId == 0 || report.EligibleUpdateId != updateId) return false;
            for (uint index = 0; index < report.ChangedSlotCount; index++)
                if (!TryEnqueueChangedSlot(report.ChangedScopeSlots[checked((int)index)])) return false;
            return true;
        }

        internal bool TryDequeue(out uint observerOrdinal)
        {
            observerOrdinal = 0;
            if (!_control.IsCreated || _control[0] == 0) return false;
            var count = _control[0];
            observerOrdinal = _queue[0];
            for (uint index = 1; index < count; index++) _queue[(int)index - 1] = _queue[(int)index];
            _queue[(int)count - 1] = 0;
            _queued[(int)observerOrdinal] = 0;
            _control[0] = count - 1;
            return true;
        }

        internal bool TryAcceptEvaluation(
            uint observerOrdinal,
            ConditionResult result,
            out bool changed,
            out NativeObserverTransitionV1 transition)
        {
            changed = false;
            transition = default;
            if (!_states.IsCreated || observerOrdinal >= _states.Length || result > ConditionResult.Failure) return false;
            var state = _states[(int)observerOrdinal];
            if (state.HasLastConditionResult == 0)
            {
                state.LastConditionResult = (byte)result;
                state.HasLastConditionResult = 1;
                _states[(int)observerOrdinal] = state;
                return true;
            }
            if (state.LastConditionResult == (byte)result) return true;
            var previous = (ConditionResult)state.LastConditionResult;
            state.LastConditionResult = (byte)result;
            _states[(int)observerOrdinal] = state;
            var observer = _observers[(int)observerOrdinal];
            var selfTriggered = (observer.Mode == CompiledObserverMode.Self || observer.Mode == CompiledObserverMode.Both)
                && previous == ConditionResult.Success && result == ConditionResult.Failure;
            var lowerTriggered = (observer.Mode == CompiledObserverMode.LowerPriority || observer.Mode == CompiledObserverMode.Both)
                && previous == ConditionResult.Failure && result == ConditionResult.Success;
            if (!selfTriggered && !lowerTriggered) return true;
            changed = true;
            transition = new NativeObserverTransitionV1(
                observer.ObserverNodeIndex,
                observer.OwningReactiveCompositeIndex,
                observer.Mode,
                selfTriggered ? BurstNodeAbortReason.ObserverSelf : BurstNodeAbortReason.ObserverLowerPriority);
            return true;
        }

        internal uint Count => _control.IsCreated ? _control[0] : 0;

        internal void Dispose()
        {
            Dispose(ref _control); Dispose(ref _states); Dispose(ref _queued); Dispose(ref _queue);
            Dispose(ref _adjacency); Dispose(ref _adjacencyOffsets);
            _observers = default;
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        { if (value.IsCreated) { value.Dispose(); value = default; } }
    }
}
