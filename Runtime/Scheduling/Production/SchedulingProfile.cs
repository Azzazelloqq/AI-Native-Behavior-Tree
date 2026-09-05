using System;

namespace AIBT
{
    /// <summary>
    /// Why <see cref="SchedulingProfile.TryCreate(string,int,uint,uint?,double?,bool,SchedulingPolicy?,out SchedulingProfile,out SchedulingProfileValidationError)"/>
    /// refused to build a profile. Never silently clamped -- ADR-P7-033's "validated without silent
    /// clamping" acceptance criterion.
    /// </summary>
    public enum SchedulingProfileValidationError : byte
    {
        None = 0,
        MissingId,
        UpdateCadenceMustBeAtLeastOne,
        MaximumResponseLatencyFramesBelowUpdateCadence,
        MaximumBudgetShareOutOfRange,
        InvalidRevision,
    }

    /// <summary>
    /// Immutable runtime scheduling intent for one tree/group -- priority, cadence, latency
    /// tolerance, optional budget share and an optional forced policy. Never a raw millisecond
    /// budget or scheduler-internal weight; see <c>Documentation~/decisions/ADR-P7-033-global-scheduler-and-profiles.md</c>
    /// (AIBT-037). Built only through <see cref="TryCreate"/> (rejects invalid values, never
    /// silently clamps) or one of the built-in presets, which call the exact same path -- no
    /// special-cased scheduler branch reads a preset differently from a custom profile.
    /// </summary>
    public sealed class SchedulingProfile
    {
        private SchedulingProfile(
            string id,
            ulong stableId,
            int priority,
            uint updateCadence,
            uint? maximumResponseLatencyFrames,
            double? maximumBudgetShare,
            bool pipeliningPermitted,
            SchedulingPolicy? forcedPolicy,
            uint revision)
        {
            Id = id;
            StableId = stableId;
            Priority = priority;
            UpdateCadence = updateCadence;
            MaximumResponseLatencyFrames = maximumResponseLatencyFrames;
            MaximumBudgetShare = maximumBudgetShare;
            PipeliningPermitted = pipeliningPermitted;
            ForcedPolicy = forcedPolicy;
            Revision = revision;
        }

        /// <summary>Caller-authored stable identity, e.g. <c>"game.ai.combat"</c>.</summary>
        public string Id { get; }

        /// <summary><see cref="StableHash.Fnv1A64(string)"/> of <see cref="Id"/> -- the comparable/loggable identity.</summary>
        public ulong StableId { get; }

        /// <summary>An ordering value, never a multiplier or budget-share weight. Higher runs first among otherwise-tied agents.</summary>
        public int Priority { get; }

        /// <summary>Update every N frames. 1 matches <see cref="ProductionTreeHost"/>'s own existing per-frame contract.</summary>
        public uint UpdateCadence { get; }

        /// <summary>Unset means no hard cap -- deadline/starvation ordering alone still applies.</summary>
        public uint? MaximumResponseLatencyFrames { get; }

        /// <summary>Unset means no explicit cap; the coordinator may still admit less under real budget pressure.</summary>
        public double? MaximumBudgetShare { get; }

        /// <summary>
        /// Whether this profile permits <see cref="SchedulingPolicy.PipelinedJobs"/>' one-frame extra
        /// latency for automatic selection. False by default, matching
        /// <c>Documentation~/execution-and-scheduling.md</c>: "Automatic policy never opts into extra
        /// semantic latency unless the user explicitly permits it."
        /// </summary>
        public bool PipeliningPermitted { get; }

        /// <summary>Unset selects Auto. Set forces one policy, subject to the coordinator's own backend/latency contradiction check.</summary>
        public SchedulingPolicy? ForcedPolicy { get; }

        /// <summary>Starts at 1. Increments each time a <see cref="SchedulingProfileAsset"/> re-freezes this identity to a new value.</summary>
        public uint Revision { get; }

        /// <summary>Validates and builds a profile. Returns false with a structured reason on any invalid value; never clamps.</summary>
        public static bool TryCreate(
            string id,
            int priority,
            uint updateCadence,
            uint? maximumResponseLatencyFrames,
            double? maximumBudgetShare,
            bool pipeliningPermitted,
            SchedulingPolicy? forcedPolicy,
            out SchedulingProfile profile,
            out SchedulingProfileValidationError error)
            => TryCreate(
                id, priority, updateCadence, maximumResponseLatencyFrames, maximumBudgetShare,
                pipeliningPermitted, forcedPolicy, 1u, out profile, out error);

        internal static bool TryCreate(
            string id,
            int priority,
            uint updateCadence,
            uint? maximumResponseLatencyFrames,
            double? maximumBudgetShare,
            bool pipeliningPermitted,
            SchedulingPolicy? forcedPolicy,
            uint revision,
            out SchedulingProfile profile,
            out SchedulingProfileValidationError error)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                error = SchedulingProfileValidationError.MissingId;
                return false;
            }
            if (updateCadence < 1u)
            {
                error = SchedulingProfileValidationError.UpdateCadenceMustBeAtLeastOne;
                return false;
            }
            if (maximumResponseLatencyFrames.HasValue && maximumResponseLatencyFrames.Value < updateCadence)
            {
                error = SchedulingProfileValidationError.MaximumResponseLatencyFramesBelowUpdateCadence;
                return false;
            }
            if (maximumBudgetShare.HasValue && (maximumBudgetShare.Value <= 0.0 || maximumBudgetShare.Value > 1.0))
            {
                error = SchedulingProfileValidationError.MaximumBudgetShareOutOfRange;
                return false;
            }
            if (revision == 0u)
            {
                error = SchedulingProfileValidationError.InvalidRevision;
                return false;
            }
            profile = new SchedulingProfile(
                id, StableHash.Fnv1A64(id), priority, updateCadence, maximumResponseLatencyFrames,
                maximumBudgetShare, pipeliningPermitted, forcedPolicy, revision);
            error = SchedulingProfileValidationError.None;
            return true;
        }

        /// <summary>
        /// Default semantics: normal priority, cadence every frame, no hard latency cap, no budget-
        /// share cap, pipelining disabled, policy Auto -- exactly current <see cref="ProductionTreeHost"/>
        /// behavior before any profile existed. The zero-configuration default everywhere a profile
        /// is not supplied.
        /// </summary>
        public static readonly SchedulingProfile Normal = CreateBuiltIn("aibt.scheduling.normal", 0, pipeliningPermitted: false);

        /// <summary>
        /// Higher priority than <see cref="Normal"/>; otherwise identical -- latency-critical work
        /// still never opts into pipelined latency on its own.
        /// </summary>
        public static readonly SchedulingProfile Interactive = CreateBuiltIn("aibt.scheduling.interactive", 1, pipeliningPermitted: false);

        /// <summary>
        /// Lower priority than <see cref="Normal"/> and permits pipelined latency -- the one place a
        /// built-in preset differs by more than ordering, justified directly by its own name
        /// (background work is explicitly latency-tolerant), not a fabricated timing constant.
        /// </summary>
        public static readonly SchedulingProfile Background = CreateBuiltIn("aibt.scheduling.background", -1, pipeliningPermitted: true);

        private static SchedulingProfile CreateBuiltIn(string id, int priority, bool pipeliningPermitted)
        {
            if (!TryCreate(id, priority, 1u, null, null, pipeliningPermitted, null, out var profile, out _))
                throw new InvalidOperationException("Built-in scheduling profile failed its own validation: " + id);
            return profile;
        }
    }
}
