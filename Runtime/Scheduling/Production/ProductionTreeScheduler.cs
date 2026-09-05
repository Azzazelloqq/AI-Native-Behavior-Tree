using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AIBT
{
    /// <summary>Why <see cref="ProductionTreeScheduler.TryRegister(ProductionTreeHost,SchedulingProfile,out SchedulerRegistrationError)"/> refused a host.</summary>
    public enum SchedulerRegistrationError : byte
    {
        None = 0,
        AlreadyRegisteredWithThisScheduler,
        AlreadyRegisteredWithAnotherScheduler,
    }

    /// <summary>
    /// One population-level production coordinator per independently scheduled AI world -- see
    /// <c>Documentation~/decisions/ADR-P7-033-global-scheduler-and-profiles.md</c> (AIBT-037). A
    /// <see cref="ProductionTreeHost"/> registers with exactly one scheduler; while registered, the
    /// coordinator drives it instead of the host's own per-frame <c>Update</c> message, in
    /// deterministic due order, admitted against one optional global time allowance. The host still
    /// owns its machine, trace channel, terminal result, failure and disposal -- registration never
    /// creates a second tree instance or transfers native ownership. Multiple independent worlds
    /// require separate explicit scheduler instances; there is no hidden global singleton.
    /// </summary>
    /// <remarks>
    /// This step (P7-033 phases 3-4) covers registration, deterministic due ordering and budget
    /// admission. Deterministic policy selection (Immediate/Budgeted/Jobs) and full explainability
    /// are added in later steps of the same card; every registered host still only ever runs through
    /// its own existing <see cref="ProductionTreeHost.DriveOneUpdate"/> immediate/legacy dispatch
    /// until then.
    /// </remarks>
    public sealed class ProductionTreeScheduler : MonoBehaviour
    {
        private sealed class Registration
        {
            internal ProductionTreeHost Host;
            internal SchedulingProfile Profile;
            internal uint NextEligibleFrame;
            internal uint EligibleSinceFrame;
            internal bool Eligible;
            internal bool Urgent;
            internal double LastMeasuredMicroseconds;
        }

        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly Dictionary<ProductionTreeHost, Registration> _byHost = new Dictionary<ProductionTreeHost, Registration>();
        private readonly List<Registration> _due = new List<Registration>();
        private readonly Dictionary<SchedulingProfile, double> _consumedByProfileThisFrame = new Dictionary<SchedulingProfile, double>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly Comparison<Registration> _compareDue;
        private uint _frame;
        private SchedulerBudgetMode _budgetMode = SchedulerBudgetMode.Unbounded;
        private double _fixedBudgetMicroseconds;
        private Func<double> _budgetProvider;

        public ProductionTreeScheduler() => _compareDue = CompareDue;

        /// <summary>Registered host count, for inspection/tests -- not itself a scheduling input.</summary>
        public int RegisteredCount => _registrations.Count;

        /// <summary>The active budget source. <see cref="SchedulerBudgetMode.Unbounded"/> by default -- see <see cref="SetUnboundedBudget"/>.</summary>
        public SchedulerBudgetMode BudgetMode => _budgetMode;

        /// <summary>
        /// True when the most recently completed frame consumed more than its resolved allowance,
        /// or admitted at least one host with no prior measurement under a bounded budget (a real,
        /// disclosed cold-start characteristic -- an unmeasured cost is never guessed, so its host
        /// is always admitted on its first-ever drive regardless of remaining allowance). A callback
        /// or scheduled Job is never aborted to enforce the budget; this only reports that it happened.
        /// </summary>
        public bool LastFrameOverran { get; private set; }

        public bool IsRegistered(ProductionTreeHost host) => host != null && _byHost.ContainsKey(host);

        /// <summary>Looks up the profile a registered host was assigned. Returns false, not a default profile, when the host is not registered with this scheduler.</summary>
        public bool TryGetProfile(ProductionTreeHost host, out SchedulingProfile profile)
        {
            if (host != null && _byHost.TryGetValue(host, out var registration))
            {
                profile = registration.Profile;
                return true;
            }
            profile = null;
            return false;
        }

        /// <summary>Registers with <see cref="SchedulingProfile.Normal"/> -- the zero-configuration default: no per-tree policy or step-budget choice.</summary>
        public bool TryRegister(ProductionTreeHost host, out SchedulerRegistrationError error)
            => TryRegister(host, SchedulingProfile.Normal, out error);

        /// <summary>Registers with an explicit profile. Fails, never silently replaces, if already registered anywhere.</summary>
        public bool TryRegister(ProductionTreeHost host, SchedulingProfile profile, out SchedulerRegistrationError error)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (_byHost.ContainsKey(host))
            {
                error = SchedulerRegistrationError.AlreadyRegisteredWithThisScheduler;
                return false;
            }
            if (!host.TryRegisterOwner(this))
            {
                error = SchedulerRegistrationError.AlreadyRegisteredWithAnotherScheduler;
                return false;
            }
            var registration = new Registration
            {
                Host = host,
                Profile = profile,
                NextEligibleFrame = _frame,
            };
            _registrations.Add(registration);
            _byHost.Add(host, registration);
            error = SchedulerRegistrationError.None;
            return true;
        }

        /// <summary>
        /// Unregisters a host, returning it to standalone (self-driven) operation. Safe to call from
        /// within a dispatch callback this same scheduler is currently driving -- the drive loop
        /// re-reads its own bound every iteration and tolerates the list shrinking under it.
        /// </summary>
        public bool TryUnregister(ProductionTreeHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!_byHost.TryGetValue(host, out var registration)) return false;
            _registrations.Remove(registration);
            _byHost.Remove(host);
            host.ClearOwner(this);
            return true;
        }

        /// <summary>
        /// Marks a registered host as due for its next logical update regardless of cadence, ranked
        /// ahead of ordinary profile priority (but never ahead of an earlier deadline or older
        /// eligible-since frame). One-shot: consumed the moment that host is actually driven. Never
        /// mutates the shared profile and cannot grant pipelined permission or override a group cap.
        /// Returns false if the host is not registered with this scheduler.
        /// </summary>
        public bool RequestUrgentUpdate(ProductionTreeHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!_byHost.TryGetValue(host, out var registration)) return false;
            registration.Urgent = true;
            return true;
        }

        /// <summary>All eligible work may run. The default; also the correct choice for a game with no frame-time authority to hand AIBT.</summary>
        public void SetUnboundedBudget()
        {
            _budgetMode = SchedulerBudgetMode.Unbounded;
            _budgetProvider = null;
        }

        /// <summary>One fixed positive microsecond allowance admitted per scheduler frame.</summary>
        public void SetFixedBudget(double allowanceMicroseconds)
        {
            if (!(allowanceMicroseconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(allowanceMicroseconds), "The fixed budget must be a positive number of microseconds.");
            _budgetMode = SchedulerBudgetMode.Fixed;
            _fixedBudgetMicroseconds = allowanceMicroseconds;
            _budgetProvider = null;
        }

        /// <summary>
        /// A caller-owned, main-thread callback supplies the allowance for the current frame (e.g. a
        /// game's own frame-time/quality manager). Called at most once per scheduler frame. A
        /// negative return is clamped to zero -- a defensive guard against a buggy provider, not a
        /// user-authored value this scheduler silently reinterprets.
        /// </summary>
        public void SetBudgetProvider(Func<double> allowanceMicrosecondsProvider)
        {
            _budgetProvider = allowanceMicrosecondsProvider ?? throw new ArgumentNullException(nameof(allowanceMicrosecondsProvider));
            _budgetMode = SchedulerBudgetMode.Provider;
        }

        private void Update()
        {
            _frame++;
            LastFrameOverran = false;

            _due.Clear();
            for (var index = 0; index < _registrations.Count; index++)
            {
                var registration = _registrations[index];
                if (!registration.Eligible && registration.NextEligibleFrame <= _frame)
                {
                    registration.Eligible = true;
                    registration.EligibleSinceFrame = _frame;
                }
                if (registration.Eligible) _due.Add(registration);
            }
            if (_due.Count == 0) return;
            _due.Sort(_compareDue);

            var unbounded = _budgetMode == SchedulerBudgetMode.Unbounded;
            var remaining = 0.0;
            var frozenAllowance = 0.0;
            if (!unbounded)
            {
                frozenAllowance = _budgetMode == SchedulerBudgetMode.Fixed
                    ? _fixedBudgetMicroseconds
                    : Math.Max(0.0, _budgetProvider());
                remaining = frozenAllowance;
                _consumedByProfileThisFrame.Clear();
            }

            for (var index = 0; index < _due.Count; index++)
            {
                var registration = _due[index];
                // _due is a snapshot taken before this loop started; an earlier host's own dispatch
                // callback may have unregistered a not-yet-driven later entry in this same frame.
                // Honor that immediately rather than driving a host that just left this scheduler.
                if (!_byHost.ContainsKey(registration.Host)) continue;
                if (!unbounded)
                {
                    var share = registration.Profile.MaximumBudgetShare;
                    if (share.HasValue)
                    {
                        _consumedByProfileThisFrame.TryGetValue(registration.Profile, out var consumedForProfile);
                        // A profile's own share cap only ever limits that profile's own group; it is
                        // never redistributed back to it from an unused share elsewhere this frame.
                        if (consumedForProfile >= share.Value * frozenAllowance) continue;
                    }
                    // An unmeasured cost is never guessed -- always admitted on its first-ever
                    // drive, which is the disclosed cold-start case LastFrameOverran also reports.
                    if (registration.LastMeasuredMicroseconds > 0.0 && registration.LastMeasuredMicroseconds > remaining)
                    {
                        LastFrameOverran = true;
                        break; // due-sorted: nothing after this would outrank it, so stop admitting.
                    }
                    if (registration.LastMeasuredMicroseconds <= 0.0) LastFrameOverran = true;
                }

                _stopwatch.Restart();
                registration.Host.DriveOneUpdate();
                _stopwatch.Stop();
                var elapsedMicroseconds = _stopwatch.Elapsed.TotalMilliseconds * 1000.0;
                registration.LastMeasuredMicroseconds = elapsedMicroseconds;

                if (!unbounded)
                {
                    remaining -= elapsedMicroseconds;
                    if (remaining < 0.0) LastFrameOverran = true;
                    if (registration.Profile.MaximumBudgetShare.HasValue)
                    {
                        _consumedByProfileThisFrame.TryGetValue(registration.Profile, out var consumedSoFar);
                        _consumedByProfileThisFrame[registration.Profile] = consumedSoFar + elapsedMicroseconds;
                    }
                }

                registration.Urgent = false;
                registration.Eligible = false;
                registration.NextEligibleFrame = _frame + registration.Profile.UpdateCadence;
            }
        }

        private static int CompareDue(Registration a, Registration b)
        {
            var aDeadline = a.Profile.MaximumResponseLatencyFrames.HasValue
                ? a.EligibleSinceFrame + a.Profile.MaximumResponseLatencyFrames.Value : uint.MaxValue;
            var bDeadline = b.Profile.MaximumResponseLatencyFrames.HasValue
                ? b.EligibleSinceFrame + b.Profile.MaximumResponseLatencyFrames.Value : uint.MaxValue;
            if (aDeadline != bDeadline) return aDeadline < bDeadline ? -1 : 1;
            if (a.EligibleSinceFrame != b.EligibleSinceFrame) return a.EligibleSinceFrame < b.EligibleSinceFrame ? -1 : 1;
            if (a.Urgent != b.Urgent) return a.Urgent ? -1 : 1;
            if (a.Profile.Priority != b.Profile.Priority) return a.Profile.Priority > b.Profile.Priority ? -1 : 1;
            return a.Host.InstanceId.CompareTo(b.Host.InstanceId);
        }

        private void OnDestroy()
        {
            for (var index = 0; index < _registrations.Count; index++) _registrations[index].Host?.ClearOwner(this);
            _registrations.Clear();
            _byHost.Clear();
            _due.Clear();
            _consumedByProfileThisFrame.Clear();
        }
    }
}
