using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace AIBT
{
    /// <summary>
    /// One agent binding owned by a <see cref="ProductionTreeWorld"/>. Implementations stage Unity
    /// facts in <see cref="SampleInputs"/>, flush them only when called at the safe scheduler
    /// boundary, and receive committed effects on the main thread.
    /// </summary>
    public interface IProductionTreeWorldParticipant : IProductionTreeEffectReceiver
    {
        ProductionTreeHost Host { get; }
        void SampleInputs();
        bool TryFlushInputs(out NativeRuntimeFailureV1 failure);
    }

    /// <summary>
    /// Optional Unity-facing frame bridge for one <see cref="ProductionTreeScheduler"/>. It adds
    /// no tree state and no per-agent Update: the scheduler invokes its three phase callbacks from
    /// its own frame, so inputs, pipelined completion and public effect delivery stay ordered.
    /// </summary>
    [RequireComponent(typeof(ProductionTreeScheduler))]
    public sealed class ProductionTreeWorld : MonoBehaviour
    {
        private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("AIBT.Production.Inputs.Sample");
        private static readonly ProfilerMarker FlushMarker = new ProfilerMarker("AIBT.Production.Inputs.Flush");
        private static readonly ProfilerMarker EffectsMarker = new ProfilerMarker("AIBT.Production.Effects.Deliver");
        // Registration order, with null for a removed participant until the next compaction
        // (P10-001): register/unregister are O(1) amortized and the survivors keep their order.
        private readonly List<IProductionTreeWorldParticipant> _participants =
            new List<IProductionTreeWorldParticipant>();
        private readonly Dictionary<IProductionTreeWorldParticipant, int> _slots =
            new Dictionary<IProductionTreeWorldParticipant, int>(ParticipantIdentity.Instance);
        private int _vacantSlots;
        private int _iterating;
        private ProductionTreeScheduler _scheduler;

        /// <summary>The latest participant input/delivery failure; None until a participant rejects a phase.</summary>
        public NativeRuntimeFailureV1 LastWorldFailure { get; private set; }
        public int RegisteredParticipantCount => _slots.Count;

        internal ProductionTreeScheduler Scheduler => _scheduler;

        private void Awake()
        {
            _scheduler = GetComponent<ProductionTreeScheduler>();
        }

        private void OnEnable()
        {
            if (_scheduler == null) _scheduler = GetComponent<ProductionTreeScheduler>();
            if (_scheduler == null || !_scheduler.TryAttachWorldBridge(this))
                Debug.LogError("AIBT ProductionTreeWorld requires one unshared ProductionTreeScheduler.", this);
        }

        private void OnDisable()
        {
            _scheduler?.DetachWorldBridge(this);
        }

        /// <summary>Registers one black-box host/binding pair with this world's scheduler.</summary>
        public bool TryRegister(
            IProductionTreeWorldParticipant participant,
            SchedulingProfile profile,
            out SchedulerRegistrationError error)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var host = participant.Host;
            if (_scheduler == null || host == null || _slots.ContainsKey(participant))
            {
                error = SchedulerRegistrationError.AlreadyRegisteredWithThisScheduler;
                return false;
            }
            if (!_scheduler.TryRegister(host, profile, out error)) return false;
            _slots.Add(participant, _participants.Count);
            _participants.Add(participant);
            return true;
        }

        /// <summary>Unregisters the participant and returns its host to standalone scheduling.</summary>
        public bool TryUnregister(IProductionTreeWorldParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (!_slots.TryGetValue(participant, out var slot)) return false;
            _slots.Remove(participant);
            _participants[slot] = null;
            _vacantSlots++;
            CompactIfSparse();
            var host = participant.Host;
            return host != null && _scheduler != null && _scheduler.TryUnregister(host);
        }

        internal void CaptureInputs()
        {
            using var scope = SampleMarker.Auto();
            _iterating++;
            try
            {
                for (var index = 0; index < _participants.Count; index++)
                    _participants[index]?.SampleInputs();
            }
            finally
            {
                _iterating--;
                CompactIfSparse();
            }
        }

        internal void FlushIdleInputs()
        {
            using var scope = FlushMarker.Auto();
            _iterating++;
            try
            {
                for (var index = 0; index < _participants.Count; index++)
                {
                    var participant = _participants[index];
                    var host = participant?.Host;
                    if (host == null || !host.CanAcceptExternalTreeWrites) continue;
                    if (!participant.TryFlushInputs(out var failure))
                        RecordFailure(failure);
                }
            }
            finally
            {
                _iterating--;
                CompactIfSparse();
            }
        }

        internal void DeliverEffects()
        {
            using var scope = EffectsMarker.Auto();
            _iterating++;
            try
            {
                for (var index = 0; index < _participants.Count; index++)
                {
                    var participant = _participants[index];
                    var host = participant?.Host;
                    // A PipelinedJobs host intentionally holds its drive lock between scheduler
                    // frames. Its effects remain in the bounded queue until the next idle barrier.
                    if (host == null || !host.CanDrainEffects) continue;
                    if (!host.TryDrainEffects(participant, out var failure)) RecordFailure(failure);
                }
            }
            finally
            {
                _iterating--;
                CompactIfSparse();
            }
        }

        /// <summary>
        /// Drops vacant slots once they are at least half of the list, keeping the survivors'
        /// order. Never while a phase loop runs: a participant's callback may unregister (an
        /// effect handler that restarts or stops its agent) and the loop's indices must not move.
        /// </summary>
        private void CompactIfSparse()
        {
            if (_iterating != 0 || _vacantSlots == 0 || _vacantSlots * 2 < _participants.Count) return;
            var write = 0;
            for (var read = 0; read < _participants.Count; read++)
            {
                var participant = _participants[read];
                if (participant == null) continue;
                _slots[participant] = write;
                _participants[write++] = participant;
            }
            _participants.RemoveRange(write, _participants.Count - write);
            _vacantSlots = 0;
        }

        /// <summary>Participants are matched by reference, never by a Unity "destroyed object" equality.</summary>
        private sealed class ParticipantIdentity : IEqualityComparer<IProductionTreeWorldParticipant>
        {
            internal static readonly ParticipantIdentity Instance = new ParticipantIdentity();

            public bool Equals(IProductionTreeWorldParticipant x, IProductionTreeWorldParticipant y) => ReferenceEquals(x, y);

            public int GetHashCode(IProductionTreeWorldParticipant obj)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private void RecordFailure(NativeRuntimeFailureV1 failure)
        {
            if (failure.Code == NativeRuntimeDiagnosticCodeV1.None) return;
            LastWorldFailure = failure;
            Debug.LogError("AIBT ProductionTreeWorld: " + failure.Code + ".", this);
        }
    }
}
