using System;
using System.Collections.Generic;
using UnityEngine;

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
        private readonly List<IProductionTreeWorldParticipant> _participants =
            new List<IProductionTreeWorldParticipant>();
        private ProductionTreeScheduler _scheduler;

        /// <summary>The latest participant input/delivery failure; None until a participant rejects a phase.</summary>
        public NativeRuntimeFailureV1 LastWorldFailure { get; private set; }
        public int RegisteredParticipantCount => _participants.Count;

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
            if (_scheduler == null || host == null || _participants.Contains(participant))
            {
                error = SchedulerRegistrationError.AlreadyRegisteredWithThisScheduler;
                return false;
            }
            if (!_scheduler.TryRegister(host, profile, out error)) return false;
            _participants.Add(participant);
            return true;
        }

        /// <summary>Unregisters the participant and returns its host to standalone scheduling.</summary>
        public bool TryUnregister(IProductionTreeWorldParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (!_participants.Remove(participant)) return false;
            var host = participant.Host;
            return host != null && _scheduler != null && _scheduler.TryUnregister(host);
        }

        internal void CaptureInputs()
        {
            for (var index = 0; index < _participants.Count; index++)
                _participants[index].SampleInputs();
        }

        internal void FlushIdleInputs()
        {
            for (var index = 0; index < _participants.Count; index++)
            {
                var participant = _participants[index];
                var host = participant.Host;
                if (host == null || !host.CanAcceptExternalTreeWrites) continue;
                if (!participant.TryFlushInputs(out var failure))
                    RecordFailure(failure);
            }
        }

        internal void DeliverEffects()
        {
            for (var index = 0; index < _participants.Count; index++)
            {
                var participant = _participants[index];
                var host = participant.Host;
                // A PipelinedJobs host intentionally holds its drive lock between scheduler
                // frames. Its effects remain in the bounded queue until the next idle barrier.
                if (host == null || !host.CanDrainEffects) continue;
                if (!host.TryDrainEffects(participant, out var failure)) RecordFailure(failure);
            }
        }

        private void RecordFailure(NativeRuntimeFailureV1 failure)
        {
            if (failure.Code == NativeRuntimeDiagnosticCodeV1.None) return;
            LastWorldFailure = failure;
            Debug.LogError("AIBT ProductionTreeWorld: " + failure.Code + ".", this);
        }
    }
}
