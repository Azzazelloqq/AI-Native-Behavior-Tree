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
    /// Registration, deterministic due ordering and budget admission (P7-033 phases 3-4) drive
    /// Immediate/Budgeted through <see cref="ProductionTreeHost.DriveOneUpdate"/> unchanged. A
    /// profile that forces <see cref="SchedulingPolicy.BatchedJobsSameFrame"/> or
    /// <see cref="SchedulingPolicy.PipelinedJobs"/> (step 5) routes through
    /// <see cref="NativeAutoSelectionV1.TrySelect"/> and, for BatchedJobsSameFrame with
    /// <see cref="SetJobsCapabilities"/> configured, a real <see cref="ProductionBatchedGroupDriverV1"/>
    /// batch across adjacent same-catalog due hosts; PipelinedJobs is not yet supported by any
    /// configuration (its own cross-frame stage semantics are a distinct, later integration). Full
    /// explainability (step 6) is not yet built.
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
        private readonly Dictionary<GeneratedBurstCatalogV2, NativeWorkEstimatorV1> _jobsWorkEstimators = new Dictionary<GeneratedBurstCatalogV2, NativeWorkEstimatorV1>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly Comparison<Registration> _compareDue;
        private uint _frame;
        private SchedulerBudgetMode _budgetMode = SchedulerBudgetMode.Unbounded;
        private double _fixedBudgetMicroseconds;
        private Func<double> _budgetProvider;
        private SchedulerJobsCapabilities? _jobsCapabilities;

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

        /// <summary>
        /// Enables real <see cref="SchedulingPolicy.BatchedJobsSameFrame"/> selection for hosts
        /// whose profile forces it: hosts sharing one profile and one generated catalog, adjacent
        /// in this frame's own due order, are driven together through
        /// <see cref="NativeAutoSelectionV1.TrySelect"/> and a real
        /// <c>NativeBatchedLifecycleOwnerV1</c>-batched drive
        /// (<see cref="ProductionBatchedGroupDriverV1"/>) instead of one host at a time. Until this
        /// is called, a forced <see cref="SchedulingPolicy.BatchedJobsSameFrame"/> or
        /// <see cref="SchedulingPolicy.PipelinedJobs"/> fails that host with a structured
        /// diagnostic rather than guessing a batch-work target -- <see cref="SchedulingPolicy.PipelinedJobs"/>
        /// specifically is not yet supported by any configuration (its own cross-frame stage
        /// semantics are a distinct, not-yet-built integration; see <c>Planning~/Evidence/P7-033/</c>).
        /// </summary>
        public void SetJobsCapabilities(SchedulerJobsCapabilities capabilities) => _jobsCapabilities = capabilities;

        /// <summary>Reverts to the default: a forced Jobs policy fails with a structured diagnostic instead of batching.</summary>
        public void ClearJobsCapabilities()
        {
            _jobsCapabilities = null;
            _jobsWorkEstimators.Clear();
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

            var dueIndex = 0;
            while (dueIndex < _due.Count)
            {
                var registration = _due[dueIndex];
                // _due is a snapshot taken before this loop started; an earlier host's own dispatch
                // callback may have unregistered a not-yet-driven later entry in this same frame.
                // Honor that immediately rather than driving a host that just left this scheduler.
                if (!_byHost.ContainsKey(registration.Host)) { dueIndex++; continue; }

                var forcedJobs = registration.Profile.ForcedPolicy == SchedulingPolicy.BatchedJobsSameFrame
                    || registration.Profile.ForcedPolicy == SchedulingPolicy.PipelinedJobs;
                if (!forcedJobs)
                {
                    if (!unbounded)
                    {
                        var share = registration.Profile.MaximumBudgetShare;
                        if (share.HasValue)
                        {
                            _consumedByProfileThisFrame.TryGetValue(registration.Profile, out var consumedForProfile);
                            // A profile's own share cap only ever limits that profile's own group; it is
                            // never redistributed back to it from an unused share elsewhere this frame.
                            if (consumedForProfile >= share.Value * frozenAllowance) { dueIndex++; continue; }
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
                    dueIndex++;
                    continue;
                }

                dueIndex = DriveForcedJobsGroup(dueIndex, unbounded, frozenAllowance, ref remaining);
            }
        }

        /// <summary>
        /// Handles one run of consecutive due entries (starting at <paramref name="startIndex"/>)
        /// that share one profile forcing <see cref="SchedulingPolicy.BatchedJobsSameFrame"/> or
        /// <see cref="SchedulingPolicy.PipelinedJobs"/> -- grouped only while adjacent in this
        /// frame's own due order, so due-order/deadline guarantees are never reordered around a
        /// grouping opportunity. Returns the index just past the run, for the caller to resume from.
        /// </summary>
        private int DriveForcedJobsGroup(int startIndex, bool unbounded, double frozenAllowance, ref double remaining)
        {
            var registration = _due[startIndex];
            var catalog = registration.Host.GeneratedDispatchAdapter?.Catalog;
            var groupEnd = startIndex + 1;
            while (groupEnd < _due.Count)
            {
                var candidate = _due[groupEnd];
                if (!_byHost.ContainsKey(candidate.Host)) break;
                if (candidate.Profile != registration.Profile) break;
                if (!ReferenceEquals(candidate.Host.GeneratedDispatchAdapter?.Catalog, catalog)) break;
                groupEnd++;
            }
            var groupCount = groupEnd - startIndex;

            if (catalog == null || _jobsCapabilities == null)
            {
                // No generated catalog to batch, or this coordinator was never given Jobs tuning --
                // an honest, structured rejection rather than a silent Immediate substitution.
                for (var member = startIndex; member < groupEnd; member++) _due[member].Host.FailUnsupportedForcedPolicy();
                return groupEnd;
            }

            if (!unbounded)
            {
                for (var member = startIndex; member < groupEnd; member++)
                {
                    var share = _due[member].Profile.MaximumBudgetShare;
                    if (!share.HasValue) continue;
                    _consumedByProfileThisFrame.TryGetValue(_due[member].Profile, out var consumedForProfile);
                    if (consumedForProfile >= share.Value * frozenAllowance) return groupEnd; // this group's own cap is exhausted; later entries may still run.
                }
                if (_jobsWorkEstimators.TryGetValue(catalog, out var admissionEstimator) && admissionEstimator.HasEstimate
                    && admissionEstimator.TryEstimateWorkPerAgentNanoseconds(out var perAgentNanoseconds, out _))
                {
                    var estimatedMicroseconds = perAgentNanoseconds * groupCount / 1000.0;
                    if (estimatedMicroseconds > remaining)
                    {
                        LastFrameOverran = true;
                        return _due.Count; // due-sorted: nothing after this would outrank it, so stop admitting entirely.
                    }
                }
                // An unestimated catalog is never guessed -- admitted on its first-ever group drive,
                // the same disclosed cold-start LastFrameOverran already reports for single hosts.
            }

            _jobsWorkEstimators.TryGetValue(catalog, out var workloadEstimator);
            NativeAutoWorkloadV1 workload;
            if (workloadEstimator.HasEstimate && workloadEstimator.TryEstimateWorkPerAgentNanoseconds(out var estimatedPerAgent, out _))
            {
                workload = new NativeAutoWorkloadV1(workloadEstimator.SmoothedStepsPerAgent, estimatedPerAgent, 1);
            }
            else
            {
                // Cold start, never guessed: this call always carries an explicit ForcedPolicy, so
                // TrySelect's forced branch never consults the workload magnitude for its decision --
                // only its own positivity guard needs a value, so 1.0 here influences nothing real.
                workload = new NativeAutoWorkloadV1(0.0, 1.0, 0);
            }

            var configuration = new NativeAutoConfigurationV1(
                NativeAutoSupportedPoliciesV1.Immediate | NativeAutoSupportedPoliciesV1.Budgeted | NativeAutoSupportedPoliciesV1.BatchedJobsSameFrame,
                NativeAutoLatencyModeV1.SameFrame,
                (NativeAutoPolicyV1)registration.Profile.ForcedPolicy.Value,
                _jobsCapabilities.Value.MinimumJobWorkloadNanoseconds,
                _jobsCapabilities.Value.TargetBatchWorkNanoseconds,
                _jobsCapabilities.Value.PolicyMinBatchSize,
                _jobsCapabilities.Value.PolicyMaxBatchSize,
                _jobsCapabilities.Value.MemoryLimitBatchSize,
                (uint)Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
                null, registration.Profile.UpdateCadence);

            if (!NativeAutoSelectionV1.TrySelect(configuration, workload, (uint)groupCount, out var explanation, out _)
                || explanation.ChosenPolicy != NativeAutoPolicyV1.BatchedJobsSameFrame)
            {
                for (var member = startIndex; member < groupEnd; member++) _due[member].Host.FailUnsupportedForcedPolicy();
                return groupEnd;
            }

            var groupHosts = new List<ProductionTreeHost>(groupCount);
            for (var member = startIndex; member < groupEnd; member++) groupHosts.Add(_due[member].Host);

            _stopwatch.Restart();
            var groupOk = ProductionBatchedGroupDriverV1.TryRun(groupHosts, explanation.BatchSize, out var totalSteps, out _);
            _stopwatch.Stop();
            var elapsedMicroseconds = _stopwatch.Elapsed.TotalMilliseconds * 1000.0;

            if (groupOk && totalSteps > 0)
            {
                var estimator = workloadEstimator;
                if (estimator.TryObserve((uint)groupCount, totalSteps, out _)) _jobsWorkEstimators[catalog] = estimator;
            }

            if (!unbounded)
            {
                remaining -= elapsedMicroseconds;
                if (remaining < 0.0) LastFrameOverran = true;
            }
            var perMemberMicroseconds = elapsedMicroseconds / groupCount;
            for (var member = startIndex; member < groupEnd; member++)
            {
                var memberRegistration = _due[member];
                memberRegistration.LastMeasuredMicroseconds = perMemberMicroseconds;
                if (!unbounded && memberRegistration.Profile.MaximumBudgetShare.HasValue)
                {
                    _consumedByProfileThisFrame.TryGetValue(memberRegistration.Profile, out var consumedSoFar);
                    _consumedByProfileThisFrame[memberRegistration.Profile] = consumedSoFar + perMemberMicroseconds;
                }
                memberRegistration.Urgent = false;
                memberRegistration.Eligible = false;
                memberRegistration.NextEligibleFrame = _frame + memberRegistration.Profile.UpdateCadence;
            }

            return groupEnd;
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
            _jobsWorkEstimators.Clear();
        }
    }
}
