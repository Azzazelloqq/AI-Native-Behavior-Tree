using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Serializable project-asset wrapper for <see cref="SchedulingProfile"/>. Freezes to the exact
    /// same immutable runtime value production code consumes through <see cref="TryFreeze"/> -- there
    /// is no second, asset-only validation or semantic path. An Inspector edit marks the asset dirty
    /// via <see cref="OnValidate"/> (Editor-only, matching every serialized-field-backed asset in this
    /// project); the next <see cref="TryFreeze"/> call after that bumps <see cref="SchedulingProfile.Revision"/>,
    /// per ADR-P7-033: "Asset edits or runtime replacement take effect at the next eligible update
    /// boundary and produce an observable profile revision."
    /// </summary>
    [CreateAssetMenu(menuName = "AIBT/Scheduling Profile", fileName = "New Scheduling Profile")]
    public sealed class SchedulingProfileAsset : ScriptableObject
    {
        [SerializeField] private string _id = "";
        [SerializeField] private int _priority;
        [SerializeField] private uint _updateCadence = 1u;
        [SerializeField] private bool _hasMaximumResponseLatencyFrames;
        [SerializeField] private uint _maximumResponseLatencyFrames;
        [SerializeField] private bool _hasMaximumBudgetShare;
        [SerializeField] private double _maximumBudgetShare;
        [SerializeField] private bool _pipeliningPermitted;
        [SerializeField] private bool _hasForcedPolicy;
        [SerializeField] private SchedulingPolicy _forcedPolicy;

        private uint _revision;
        private bool _dirty = true;

        /// <summary>
        /// Validates and freezes the asset's current serialized fields into an immutable
        /// <see cref="SchedulingProfile"/>. Never silently clamps -- an invalid asset fails closed,
        /// same as a hand-built runtime profile.
        /// </summary>
        public bool TryFreeze(out SchedulingProfile profile, out SchedulingProfileValidationError error)
        {
            if (_dirty)
            {
                _revision++;
                _dirty = false;
            }
            return SchedulingProfile.TryCreate(
                _id,
                _priority,
                _updateCadence,
                _hasMaximumResponseLatencyFrames ? _maximumResponseLatencyFrames : (uint?)null,
                _hasMaximumBudgetShare ? _maximumBudgetShare : (double?)null,
                _pipeliningPermitted,
                _hasForcedPolicy ? _forcedPolicy : (SchedulingPolicy?)null,
                _revision,
                out profile,
                out error);
        }

        private void OnValidate() => _dirty = true;
    }
}
