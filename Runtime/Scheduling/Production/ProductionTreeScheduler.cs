using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Profiling;
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
    /// Immediate/Budgeted preserve the host's direct drive. Forced Jobs policies pass through the
    /// deterministic native selector and real generated dispatch; PipelinedJobs additionally needs
    /// explicit permission from both the profile and <see cref="SchedulerJobsCapabilities"/> and
    /// retains each scheduled lifecycle round across at least one scheduler frame. The last
    /// completed frame is inspectable through <see cref="TryGetFrameEntry"/>.
    /// </remarks>
    public sealed class ProductionTreeScheduler : MonoBehaviour
    {
        private static readonly ProfilerMarker s_FrameMarker =
            new ProfilerMarker("AIBT.Production.Scheduler.Frame");
        private static readonly ProfilerMarker s_PipelinedAdvanceMarker =
            new ProfilerMarker("AIBT.Production.Scheduler.PipelinedAdvance");

        private sealed class Registration
        {
            internal ProductionTreeHost Host;
            internal SchedulingProfile Profile;
            internal uint NextEligibleFrame;
            internal uint EligibleSinceFrame;
            internal bool Eligible;
            internal bool Urgent;
            internal bool InFlight;
            internal double LastMeasuredMicroseconds;
        }

        private sealed class PipelinedRun
        {
            internal readonly List<Registration> Registrations;
            internal readonly GeneratedBurstCatalogV2 Catalog;
            internal readonly ProductionPipelinedGroupDriverV1 Driver;
            internal readonly NativeAutoExplanationV1 Explanation;
            internal readonly bool HasWorkEstimate;
            internal readonly uint StartedFrame;
            internal double ConsumedMicroseconds;

            internal PipelinedRun(
                List<Registration> registrations,
                GeneratedBurstCatalogV2 catalog,
                ProductionPipelinedGroupDriverV1 driver,
                NativeAutoExplanationV1 explanation,
                bool hasWorkEstimate,
                uint startedFrame,
                double consumedMicroseconds)
            {
                Registrations = registrations;
                Catalog = catalog;
                Driver = driver;
                Explanation = explanation;
                HasWorkEstimate = hasWorkEstimate;
                StartedFrame = startedFrame;
                ConsumedMicroseconds = consumedMicroseconds;
            }
        }

        private readonly struct GroupWorkspaceKey : IEquatable<GroupWorkspaceKey>
        {
            internal GroupWorkspaceKey(GeneratedBurstCatalogV2 catalog, SchedulingProfile profile)
            {
                Catalog = catalog;
                Profile = profile;
            }

            private GeneratedBurstCatalogV2 Catalog { get; }
            private SchedulingProfile Profile { get; }

            public bool Equals(GroupWorkspaceKey other)
                => ReferenceEquals(Catalog, other.Catalog) && ReferenceEquals(Profile, other.Profile);

            public override bool Equals(object obj)
                => obj is GroupWorkspaceKey other && Equals(other);

            public override int GetHashCode()
                => unchecked((RuntimeHelpers.GetHashCode(Catalog) * 397) ^ RuntimeHelpers.GetHashCode(Profile));
        }

        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly Dictionary<ProductionTreeHost, Registration> _byHost = new Dictionary<ProductionTreeHost, Registration>();
        private readonly List<Registration> _due = new List<Registration>();
        private readonly Dictionary<SchedulingProfile, double> _consumedByProfileThisFrame = new Dictionary<SchedulingProfile, double>();
        private readonly Dictionary<GeneratedBurstCatalogV2, NativeWorkEstimatorV1> _jobsWorkEstimators = new Dictionary<GeneratedBurstCatalogV2, NativeWorkEstimatorV1>();
        private readonly Dictionary<GroupWorkspaceKey, GeneratedDispatchGroupWorkspaceV2> _groupWorkspaces =
            new Dictionary<GroupWorkspaceKey, GeneratedDispatchGroupWorkspaceV2>();
        private readonly List<PipelinedRun> _pipelinedRuns = new List<PipelinedRun>();
        private readonly List<SchedulerFrameEntry> _frameEntries = new List<SchedulerFrameEntry>();
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

        /// <summary>The monotonically increasing scheduler frame represented by the current snapshot.</summary>
        public uint LastFrameIndex { get; private set; }

        /// <summary>The frozen global allowance for the current snapshot, or positive infinity in Unbounded mode.</summary>
        public double LastFrameAllocatedBudgetMicroseconds { get; private set; }

        /// <summary>Measured coordinator work consumed during the current snapshot frame.</summary>
        public double LastFrameConsumedMicroseconds { get; private set; }

        /// <summary>Number of bounded agent entries available through <see cref="TryGetFrameEntry"/>.</summary>
        public int FrameEntryCount => _frameEntries.Count;

        /// <summary>Copies one entry from the scheduler-owned reused frame buffer; returns false when the index is outside the snapshot.</summary>
        public bool TryGetFrameEntry(int index, out SchedulerFrameEntry entry)
        {
            if ((uint)index < (uint)_frameEntries.Count)
            {
                entry = _frameEntries[index];
                return true;
            }
            entry = default;
            return false;
        }

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
            var catalog = host.GeneratedDispatchAdapter?.Catalog;
            var cohortAfterRegistration = catalog == null ? 0 : CountCohort(catalog, profile) + 1;
            if (RequiresGroupWorkspace(profile) && catalog != null && cohortAfterRegistration > 1
                && !TryPrewarmGroupWorkspace(catalog, profile, cohortAfterRegistration))
            {
                host.ClearOwner(this);
                throw new InvalidOperationException("Unable to prewarm the fixed grouped-dispatch workspace.");
            }
            var registration = new Registration
            {
                Host = host,
                Profile = profile,
                NextEligibleFrame = _frame,
            };
            _registrations.Add(registration);
            _byHost.Add(host, registration);
            if (_frameEntries.Capacity < _registrations.Count)
                _frameEntries.Capacity = _registrations.Count;
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
            if (registration.InFlight) DrainPipelinedRunContaining(host);
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
        /// Enables real Jobs-policy selection with caller-authored workload and batch bounds.
        /// PipelinedJobs remains unavailable unless this value explicitly permits it and the
        /// assigned profile independently permits its latency. Unsupported forced policies fail
        /// with a structured diagnostic rather than silently substituting another policy.
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
            using var _ = s_FrameMarker.Auto();
            _frame++;
            LastFrameIndex = _frame;
            LastFrameOverran = false;
            LastFrameConsumedMicroseconds = 0.0;
            _frameEntries.Clear();

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
            LastFrameAllocatedBudgetMicroseconds = unbounded ? double.PositiveInfinity : frozenAllowance;

            AdvancePipelinedRuns(unbounded, ref remaining);

            _due.Clear();
            for (var index = 0; index < _registrations.Count; index++)
            {
                var registration = _registrations[index];
                if (!registration.InFlight && !registration.Eligible && registration.NextEligibleFrame <= _frame)
                {
                    registration.Eligible = true;
                    registration.EligibleSinceFrame = _frame;
                }
                if (!registration.InFlight && registration.Eligible) _due.Add(registration);
            }
            if (_due.Count == 0) return;
            _due.Sort(_compareDue);

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
                            if (consumedForProfile >= share.Value * frozenAllowance)
                            {
                                AddDeferredShareEntry(registration);
                                dueIndex++;
                                continue;
                            }
                        }
                        // An unmeasured cost is never guessed -- always admitted on its first-ever
                        // drive, which is the disclosed cold-start case LastFrameOverran also reports.
                        if (registration.LastMeasuredMicroseconds > 0.0 && registration.LastMeasuredMicroseconds > remaining)
                        {
                            LastFrameOverran = true;
                            AddDeferredBudgetEntries(dueIndex);
                            break; // due-sorted: nothing after this would outrank it, so stop admitting.
                        }
                        if (registration.LastMeasuredMicroseconds <= 0.0) LastFrameOverran = true;
                    }

                    _stopwatch.Restart();
                    registration.Host.DriveOneUpdate();
                    _stopwatch.Stop();
                    var elapsedMicroseconds = _stopwatch.Elapsed.TotalMilliseconds * 1000.0;
                    registration.LastMeasuredMicroseconds = elapsedMicroseconds;
                    LastFrameConsumedMicroseconds += elapsedMicroseconds;

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
                    AddDirectEntry(registration, elapsedMicroseconds);
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
                for (var member = startIndex; member < groupEnd; member++)
                {
                    _due[member].Host.FailUnsupportedForcedPolicy();
                    AddUnsupportedEntry(_due[member]);
                }
                return groupEnd;
            }

            _jobsWorkEstimators.TryGetValue(catalog, out var workloadEstimator);
            var hasWorkEstimate = workloadEstimator.HasEstimate;
            var estimatedPerAgent = 0.0;
            if (hasWorkEstimate
                && !workloadEstimator.TryEstimateWorkPerAgentNanoseconds(out estimatedPerAgent, out _))
                hasWorkEstimate = false;
            NativeAutoWorkloadV1 workload;
            if (hasWorkEstimate)
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

            var pipelined = registration.Profile.ForcedPolicy == SchedulingPolicy.PipelinedJobs;
            var pipelineSupported = _jobsCapabilities.Value.PipelinedJobsPermitted
                && registration.Profile.PipeliningPermitted;
            var supportedPolicies = NativeAutoSupportedPoliciesV1.Immediate
                | NativeAutoSupportedPoliciesV1.Budgeted
                | NativeAutoSupportedPoliciesV1.BatchedJobsSameFrame;
            if (pipelineSupported) supportedPolicies |= NativeAutoSupportedPoliciesV1.PipelinedJobs;
            var configuration = new NativeAutoConfigurationV1(
                supportedPolicies,
                pipelineSupported ? NativeAutoLatencyModeV1.PipelinedAllowed : NativeAutoLatencyModeV1.SameFrame,
                (NativeAutoPolicyV1)registration.Profile.ForcedPolicy.Value,
                _jobsCapabilities.Value.MinimumJobWorkloadNanoseconds,
                _jobsCapabilities.Value.TargetBatchWorkNanoseconds,
                _jobsCapabilities.Value.PolicyMinBatchSize,
                _jobsCapabilities.Value.PolicyMaxBatchSize,
                _jobsCapabilities.Value.MemoryLimitBatchSize,
                (uint)Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
                null, registration.Profile.UpdateCadence);

            if (!NativeAutoSelectionV1.TrySelect(configuration, workload, (uint)groupCount, out var explanation, out _)
                || explanation.ChosenPolicy != (NativeAutoPolicyV1)registration.Profile.ForcedPolicy.Value)
            {
                for (var member = startIndex; member < groupEnd; member++)
                {
                    _due[member].Host.FailUnsupportedForcedPolicy();
                    AddUnsupportedEntry(_due[member]);
                }
                return groupEnd;
            }

            GeneratedDispatchGroupWorkspaceV2 workspace = null;
            if (groupCount > 1
                && (!_groupWorkspaces.TryGetValue(
                        new GroupWorkspaceKey(catalog, registration.Profile), out workspace)
                    || workspace.MaximumParticipants < groupCount))
            {
                for (var member = startIndex; member < groupEnd; member++)
                {
                    _due[member].Host.FailUnsupportedForcedPolicy();
                    AddJobsEntry(_due[member], explanation, hasWorkEstimate,
                        SchedulerFrameDisposition.Failed, 0.0, 0, 0);
                }
                return groupEnd;
            }

            if (!unbounded)
            {
                for (var member = startIndex; member < groupEnd; member++)
                {
                    var share = _due[member].Profile.MaximumBudgetShare;
                    if (!share.HasValue) continue;
                    _consumedByProfileThisFrame.TryGetValue(_due[member].Profile, out var consumedForProfile);
                    if (consumedForProfile >= share.Value * frozenAllowance)
                    {
                        AddDeferredJobsEntries(
                            startIndex, groupEnd, explanation, hasWorkEstimate,
                            SchedulerFrameDisposition.DeferredProfileShare);
                        return groupEnd; // this group's own cap is exhausted; later entries may still run.
                    }
                }
                if (hasWorkEstimate)
                {
                    var estimatedMicroseconds = estimatedPerAgent * groupCount / 1000.0;
                    if (estimatedMicroseconds > remaining)
                    {
                        LastFrameOverran = true;
                        AddDeferredJobsEntries(
                            startIndex, groupEnd, explanation, hasWorkEstimate,
                            SchedulerFrameDisposition.DeferredGlobalBudget);
                        return _due.Count; // due-sorted: nothing after this would outrank it, so stop admitting entirely.
                    }
                }
                else
                {
                    // An unestimated catalog is never guessed and is admitted once. Surface that
                    // cold-start admission just as the direct-host path does.
                    LastFrameOverran = true;
                }
            }

            var groupHosts = new List<ProductionTreeHost>(groupCount);
            for (var member = startIndex; member < groupEnd; member++) groupHosts.Add(_due[member].Host);

            _stopwatch.Restart();
            if (pipelined)
            {
                var groupRegistrations = new List<Registration>(groupCount);
                for (var member = startIndex; member < groupEnd; member++)
                    groupRegistrations.Add(_due[member]);
                var started = ProductionPipelinedGroupDriverV1.TryStart(
                    groupHosts, explanation.BatchSize, workspace, out var driver, out _);
                _stopwatch.Stop();
                var schedulingMicroseconds = _stopwatch.Elapsed.TotalMilliseconds * 1000.0;
                if (!started || driver == null)
                {
                    for (var member = startIndex; member < groupEnd; member++)
                    {
                        _due[member].Host.FailUnsupportedForcedPolicy();
                        AddJobsEntry(_due[member], explanation, hasWorkEstimate,
                            SchedulerFrameDisposition.Failed, schedulingMicroseconds, 0, 0);
                    }
                    return groupEnd;
                }
                for (var member = 0; member < groupRegistrations.Count; member++)
                {
                    groupRegistrations[member].InFlight = true;
                    groupRegistrations[member].Urgent = false;
                }
                _pipelinedRuns.Add(new PipelinedRun(
                    groupRegistrations, catalog, driver, explanation, hasWorkEstimate, _frame, schedulingMicroseconds));
                LastFrameConsumedMicroseconds += schedulingMicroseconds;
                for (var member = 0; member < groupRegistrations.Count; member++)
                    AddJobsEntry(groupRegistrations[member], explanation, hasWorkEstimate,
                        SchedulerFrameDisposition.PipelinedScheduled, schedulingMicroseconds / groupCount, 0, 0);
                if (!unbounded)
                {
                    remaining -= schedulingMicroseconds;
                    if (remaining < 0.0) LastFrameOverran = true;
                    if (registration.Profile.MaximumBudgetShare.HasValue)
                    {
                        _consumedByProfileThisFrame.TryGetValue(registration.Profile, out var consumedSoFar);
                        _consumedByProfileThisFrame[registration.Profile] = consumedSoFar + schedulingMicroseconds;
                    }
                }
                return groupEnd;
            }

            var groupOk = ProductionBatchedGroupDriverV1.TryRun(
                groupHosts, explanation.BatchSize, out var totalSteps, out _, workspace);
            _stopwatch.Stop();
            var elapsedMicroseconds = _stopwatch.Elapsed.TotalMilliseconds * 1000.0;
            LastFrameConsumedMicroseconds += elapsedMicroseconds;

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
                AddJobsEntry(memberRegistration, explanation, hasWorkEstimate,
                    groupOk ? SchedulerFrameDisposition.Executed : SchedulerFrameDisposition.Failed,
                    perMemberMicroseconds, totalSteps / (ulong)groupCount, 0);
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
            while (_pipelinedRuns.Count > 0) DrainPipelinedRunAt(_pipelinedRuns.Count - 1);
            for (var index = 0; index < _registrations.Count; index++) _registrations[index].Host?.ClearOwner(this);
            _registrations.Clear();
            _byHost.Clear();
            _due.Clear();
            _consumedByProfileThisFrame.Clear();
            _jobsWorkEstimators.Clear();
            foreach (var workspace in _groupWorkspaces.Values) workspace.TryDispose(out _);
            _groupWorkspaces.Clear();
        }

        private static bool RequiresGroupWorkspace(SchedulingProfile profile)
            => profile.ForcedPolicy == SchedulingPolicy.BatchedJobsSameFrame
                || profile.ForcedPolicy == SchedulingPolicy.PipelinedJobs;

        private int CountCohort(GeneratedBurstCatalogV2 catalog, SchedulingProfile profile)
        {
            var count = 0;
            for (var index = 0; index < _registrations.Count; index++)
            {
                var registration = _registrations[index];
                if (ReferenceEquals(registration.Profile, profile)
                    && ReferenceEquals(registration.Host.GeneratedDispatchAdapter?.Catalog, catalog))
                {
                    count++;
                }
            }
            return count;
        }

        private bool TryPrewarmGroupWorkspace(
            GeneratedBurstCatalogV2 catalog,
            SchedulingProfile profile,
            int requiredParticipants)
        {
            var key = new GroupWorkspaceKey(catalog, profile);
            if (_groupWorkspaces.TryGetValue(key, out var existing)
                && existing.MaximumParticipants >= requiredParticipants)
            {
                return true;
            }
            if (existing != null && existing.IsLeased) return false;
            if (!GeneratedDispatchGroupWorkspaceV2.TryCreate(
                    catalog, requiredParticipants, out var replacement, out _))
            {
                return false;
            }
            if (existing != null)
            {
                if (!existing.TryDispose(out _))
                {
                    replacement.TryDispose(out _);
                    return false;
                }
            }
            _groupWorkspaces[key] = replacement;
            return true;
        }

        private void AdvancePipelinedRuns(bool unbounded, ref double remaining)
        {
            for (var index = _pipelinedRuns.Count - 1; index >= 0; index--)
            {
                using var pipelinedAdvanceScope = s_PipelinedAdvanceMarker.Auto();
                var run = _pipelinedRuns[index];
                _stopwatch.Restart();
                var ok = run.Driver.TryAdvance(out _);
                _stopwatch.Stop();
                var elapsed = _stopwatch.Elapsed.TotalMilliseconds * 1000.0;
                run.ConsumedMicroseconds += elapsed;
                LastFrameConsumedMicroseconds += elapsed;
                if (!unbounded)
                {
                    remaining -= elapsed;
                    if (remaining < 0.0) LastFrameOverran = true;
                    var profile = run.Registrations[0].Profile;
                    if (profile.MaximumBudgetShare.HasValue)
                    {
                        _consumedByProfileThisFrame.TryGetValue(profile, out var consumedSoFar);
                        _consumedByProfileThisFrame[profile] = consumedSoFar + elapsed;
                    }
                }
                if (!ok || run.Driver.IsComplete)
                {
                    FinalizePipelinedRun(run, ok);
                    _pipelinedRuns.RemoveAt(index);
                }
                for (var member = 0; member < run.Registrations.Count; member++)
                    AddJobsEntry(run.Registrations[member], run.Explanation, run.HasWorkEstimate,
                        ok ? SchedulerFrameDisposition.PipelinedAdvanced : SchedulerFrameDisposition.Failed,
                        elapsed / run.Registrations.Count,
                        run.Driver.AdvancedThisFrame(run.Registrations[member].Host) ? 1u : 0u,
                        _frame - run.StartedFrame);
            }
        }

        private void DrainPipelinedRunContaining(ProductionTreeHost host)
        {
            for (var index = _pipelinedRuns.Count - 1; index >= 0; index--)
            {
                var run = _pipelinedRuns[index];
                for (var member = 0; member < run.Registrations.Count; member++)
                {
                    if (!ReferenceEquals(run.Registrations[member].Host, host)) continue;
                    DrainPipelinedRunAt(index);
                    return;
                }
            }
        }

        private void DrainPipelinedRunAt(int index)
        {
            var run = _pipelinedRuns[index];
            _stopwatch.Restart();
            var ok = run.Driver.TryDrain(out _);
            _stopwatch.Stop();
            run.ConsumedMicroseconds += _stopwatch.Elapsed.TotalMilliseconds * 1000.0;
            FinalizePipelinedRun(run, ok);
            _pipelinedRuns.RemoveAt(index);
        }

        private void FinalizePipelinedRun(PipelinedRun run, bool succeeded)
        {
            if (succeeded && run.Driver.TotalSteps > 0)
            {
                _jobsWorkEstimators.TryGetValue(run.Catalog, out var estimator);
                if (estimator.TryObserve((uint)run.Registrations.Count, run.Driver.TotalSteps, out _))
                    _jobsWorkEstimators[run.Catalog] = estimator;
            }
            var perMember = run.ConsumedMicroseconds / run.Registrations.Count;
            for (var index = 0; index < run.Registrations.Count; index++)
            {
                var registration = run.Registrations[index];
                registration.InFlight = false;
                registration.LastMeasuredMicroseconds = perMember;
                registration.Eligible = false;
                registration.NextEligibleFrame = _frame + registration.Profile.UpdateCadence;
            }
        }

        private void AddDirectEntry(Registration registration, double consumedMicroseconds)
        {
            var policy = registration.Profile.ForcedPolicy
                ?? (registration.Host.StepBudget.HasValue ? SchedulingPolicy.Budgeted : SchedulingPolicy.Immediate);
            _frameEntries.Add(CreateEntry(
                registration, policy, SchedulerFrameSelectionSource.DirectHostPath,
                SchedulerSelectionReason.None, false, 0.0, 0.0, 0.0,
                SchedulerEstimateConfidence.None, 0, 0, 0.0, false, 0.0, false,
                false, registration.Profile.UpdateCadence,
                registration.Host.LastFailure.Code == NativeRuntimeDiagnosticCodeV1.None
                    ? SchedulerFrameDisposition.Executed : SchedulerFrameDisposition.Failed,
                consumedMicroseconds, 0, 0));
        }

        private void AddJobsEntry(
            Registration registration, NativeAutoExplanationV1 explanation,
            bool hasWorkEstimate,
            SchedulerFrameDisposition disposition, double consumedMicroseconds,
            ulong executedSteps, ulong observedLatencyFrames)
        {
            _frameEntries.Add(CreateEntry(
                registration, (SchedulingPolicy)explanation.ChosenPolicy,
                SchedulerFrameSelectionSource.NativeAutoSelector,
                MapReason(explanation.Reason), hasWorkEstimate,
                hasWorkEstimate ? explanation.ExpectedNodeStepsPerAgent : 0.0,
                hasWorkEstimate ? explanation.EstimatedWorkPerAgentNanoseconds : 0.0,
                hasWorkEstimate ? explanation.EstimatedTotalWorkNanoseconds : 0.0,
                hasWorkEstimate ? MapConfidence(explanation.Confidence) : SchedulerEstimateConfidence.None,
                explanation.BatchSize, explanation.BatchCount, explanation.WorkerUtilizationProxy,
                explanation.HasConfiguredBudget, explanation.ConfiguredUpdateBudgetNanoseconds,
                explanation.ExceedsConfiguredBudget,
                explanation.LatencyMode == NativeAutoLatencyModeV1.PipelinedAllowed,
                explanation.UpdateCadence, disposition,
                consumedMicroseconds, executedSteps, observedLatencyFrames));
        }

        private void AddDeferredJobsEntries(
            int startIndex, int groupEnd, NativeAutoExplanationV1 explanation,
            bool hasWorkEstimate, SchedulerFrameDisposition disposition)
        {
            for (var member = startIndex; member < groupEnd; member++)
                AddJobsEntry(_due[member], explanation, hasWorkEstimate, disposition, 0.0, 0, 0);
        }

        private SchedulerFrameEntry CreateEntry(
            Registration registration, SchedulingPolicy policy,
            SchedulerFrameSelectionSource source, SchedulerSelectionReason reason,
            bool hasWorkEstimate, double expectedSteps, double estimate, double estimatedTotal,
            SchedulerEstimateConfidence confidence, uint batchSize, uint batchCount,
            double workerUtilization, bool hasConfiguredStepBudget,
            double configuredStepBudget, bool exceedsConfiguredStepBudget,
            bool pipelinedLatencyAllowed, uint updateCadence,
            SchedulerFrameDisposition disposition, double consumedMicroseconds,
            ulong executedSteps, ulong observedLatencyFrames)
        {
            var hasDeadline = registration.Profile.MaximumResponseLatencyFrames.HasValue;
            var deadline = hasDeadline
                ? registration.EligibleSinceFrame + registration.Profile.MaximumResponseLatencyFrames.Value
                : 0u;
            return new SchedulerFrameEntry(
                registration.Host.InstanceId, registration.Profile, registration.EligibleSinceFrame,
                hasDeadline, deadline, policy, source, reason, hasWorkEstimate, expectedSteps,
                estimate, estimatedTotal, confidence, batchSize, batchCount, workerUtilization,
                hasConfiguredStepBudget, configuredStepBudget, exceedsConfiguredStepBudget,
                pipelinedLatencyAllowed, updateCadence,
                disposition, consumedMicroseconds, executedSteps, observedLatencyFrames,
                registration.Host.LastRootResult, registration.Host.LastFailure);
        }

        private void AddDeferredBudgetEntries(int startIndex)
        {
            for (var index = startIndex; index < _due.Count; index++)
            {
                var registration = _due[index];
                if (!_byHost.ContainsKey(registration.Host)) continue;
                var policy = registration.Profile.ForcedPolicy
                    ?? (registration.Host.StepBudget.HasValue ? SchedulingPolicy.Budgeted : SchedulingPolicy.Immediate);
                _frameEntries.Add(CreateEntry(
                    registration, policy, SchedulerFrameSelectionSource.DirectHostPath,
                    SchedulerSelectionReason.None, registration.LastMeasuredMicroseconds > 0.0,
                    0.0, registration.LastMeasuredMicroseconds * 1000.0,
                    registration.LastMeasuredMicroseconds * 1000.0,
                    SchedulerEstimateConfidence.None, 0, 0, 0.0, false, 0.0, false,
                    false, registration.Profile.UpdateCadence, SchedulerFrameDisposition.DeferredGlobalBudget,
                    0.0, 0, 0));
            }
        }

        private void AddDeferredShareEntry(Registration registration)
        {
            var policy = registration.Profile.ForcedPolicy
                ?? (registration.Host.StepBudget.HasValue ? SchedulingPolicy.Budgeted : SchedulingPolicy.Immediate);
            _frameEntries.Add(CreateEntry(
                registration, policy, SchedulerFrameSelectionSource.DirectHostPath,
                SchedulerSelectionReason.None, registration.LastMeasuredMicroseconds > 0.0,
                0.0, registration.LastMeasuredMicroseconds * 1000.0,
                registration.LastMeasuredMicroseconds * 1000.0,
                SchedulerEstimateConfidence.None, 0, 0, 0.0, false, 0.0, false,
                false, registration.Profile.UpdateCadence, SchedulerFrameDisposition.DeferredProfileShare,
                0.0, 0, 0));
        }

        private void AddUnsupportedEntry(Registration registration)
        {
            _frameEntries.Add(CreateEntry(
                registration, registration.Profile.ForcedPolicy.Value,
                SchedulerFrameSelectionSource.NativeAutoSelector,
                SchedulerSelectionReason.ForcedByCaller, false, 0.0, 0.0, 0.0,
                SchedulerEstimateConfidence.None, 0, 0, 0.0, false, 0.0, false,
                registration.Profile.PipeliningPermitted, registration.Profile.UpdateCadence,
                SchedulerFrameDisposition.Failed,
                0.0, 0, 0));
        }

        private static SchedulerSelectionReason MapReason(NativeAutoSelectionReasonV1 reason)
        {
            switch (reason)
            {
                case NativeAutoSelectionReasonV1.ForcedByCaller: return SchedulerSelectionReason.ForcedByCaller;
                case NativeAutoSelectionReasonV1.BelowMinimumJobWorkload: return SchedulerSelectionReason.BelowMinimumJobWorkload;
                case NativeAutoSelectionReasonV1.BudgetConfigured: return SchedulerSelectionReason.BudgetConfigured;
                case NativeAutoSelectionReasonV1.PipelinedPreferredForThroughput: return SchedulerSelectionReason.PipelinedPreferredForThroughput;
                case NativeAutoSelectionReasonV1.BatchedForSameFrameThroughput: return SchedulerSelectionReason.BatchedForSameFrameThroughput;
                case NativeAutoSelectionReasonV1.FallbackToOnlyAvailablePolicy: return SchedulerSelectionReason.FallbackToOnlyAvailablePolicy;
                case NativeAutoSelectionReasonV1.PreferredOverBatchedByMeasuredCost: return SchedulerSelectionReason.PreferredOverBatchedByMeasuredCost;
                default: return SchedulerSelectionReason.None;
            }
        }

        private static SchedulerEstimateConfidence MapConfidence(NativeAutoConfidenceV1 confidence)
        {
            switch (confidence)
            {
                case NativeAutoConfidenceV1.Low: return SchedulerEstimateConfidence.Low;
                case NativeAutoConfidenceV1.Medium: return SchedulerEstimateConfidence.Medium;
                case NativeAutoConfidenceV1.High: return SchedulerEstimateConfidence.High;
                default: return SchedulerEstimateConfidence.None;
            }
        }
    }
}
