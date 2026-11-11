using UnityEngine;

namespace AIBT.Samples.SimpleAgent
{
    /// <summary>
    /// Gameplay code that feeds the tree: every frame it writes what the guard perceives into the
    /// agent's blackboard. The tree never looks at the scene itself; it reacts to these two facts.
    /// </summary>
    [RequireComponent(typeof(BehaviorTreeAgent))]
    public sealed class GuardSenses : MonoBehaviour
    {
        public Transform target;
        public float sightRange = 5f;
        public float sightAngle = 120f;
        public float loseRange = 6f;

        private BehaviorTreeAgent _agent;
        private bool _tracking;

        public bool IsTracking => _tracking;

        private void Awake() => _agent = GetComponent<BehaviorTreeAgent>();

        private void Update()
        {
            var blackboard = _agent.Blackboard;
            if (target == null || blackboard == null) return;

            var offset = target.position - transform.position;
            offset.y = 0f;
            var distance = offset.magnitude;
            var inCone = distance < 0.01f || Vector3.Angle(transform.forward, offset) <= sightAngle * 0.5f;
            // Spot the target inside the sight cone; keep it until it gets farther than loseRange.
            _tracking = _tracking ? distance <= loseRange : distance <= sightRange && inCone;

            blackboard.Set("target-visible", _tracking);
            if (_tracking) blackboard.Set("target-position", target.position);
        }
    }
}
