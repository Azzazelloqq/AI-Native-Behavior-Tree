#if AIBT_UNITY_AI
using UnityEngine;
using UnityEngine.AI;

namespace AIBT.Nodes
{
    // Compiled only when the built-in AI module (com.unity.modules.ai) is enabled; the
    // AIBT.Agents assembly defines AIBT_UNITY_AI through a version define, so the package still
    // compiles in a project that disabled the module.

    [BtNode("aibt.unity.nav-move-to", Category = "Navigation",
        Summary = "Sends the agent's NavMeshAgent to a position and succeeds on arrival; fails when no complete path exists.",
        WhenToUse = "Use for movement around obstacles on a baked NavMesh. The target is re-read every tick, so it can move.",
        WhenNotToUse = "Use aibt.unity.move-towards for an agent without a NavMeshAgent.")]
    public sealed class NavMoveToNode : BtAction
    {
        public BbKey<Vector3> target;

        [BtParam("stop-distance")]
        public float stopDistance = 0.5f;

        [Tooltip("Movement speed; 0 keeps the NavMeshAgent's own speed.")]
        public float speed;

        [BtParam("repath-distance")]
        public float repathDistance = 0.5f;

        private NavMeshAgent _navigator;
        private Vector3 _destination;
        private float _originalSpeed;

        protected override void OnInitialize() => _navigator = GetComponent<NavMeshAgent>();

        protected override void OnEnter()
        {
            if (_navigator == null) return;
            _originalSpeed = _navigator.speed;
            if (speed > 0f) _navigator.speed = speed;
            _destination = new Vector3(float.NaN, float.NaN, float.NaN);
        }

        protected override NodeStatus OnTick()
        {
            if (_navigator == null || !_navigator.isActiveAndEnabled)
            {
                WarnOnce("needs an enabled NavMeshAgent on the agent's GameObject.");
                return NodeStatus.Failure;
            }

            if (!_navigator.isOnNavMesh)
            {
                WarnOnce("the NavMeshAgent is not on a NavMesh.");
                return NodeStatus.Failure;
            }

            var goal = Get(target);
            var flat = _navigator.transform.position - goal;
            flat.y = 0f;
            if (flat.magnitude <= Mathf.Max(stopDistance, 0.01f)) return NodeStatus.Success;

            if (float.IsNaN(_destination.x) || (goal - _destination).sqrMagnitude > repathDistance * repathDistance)
            {
                _destination = goal;
                _navigator.isStopped = false;
                if (!_navigator.SetDestination(goal)) return NodeStatus.Failure;
            }

            if (_navigator.pathPending) return NodeStatus.Running;
            if (_navigator.pathStatus != NavMeshPathStatus.PathComplete) return NodeStatus.Failure;
            return NodeStatus.Running;
        }

        protected override void OnExit()
        {
            if (_navigator == null || !_navigator.isActiveAndEnabled) return;
            if (_navigator.isOnNavMesh) _navigator.ResetPath();
            if (speed > 0f) _navigator.speed = _originalSpeed;
        }
    }

    [BtNode("aibt.unity.nav-stop", Category = "Navigation",
        Summary = "Stops the agent's NavMeshAgent and clears its path, then succeeds.",
        WhenToUse = "Use before an attack or animation that must not slide the agent along an old path.")]
    public sealed class NavStopNode : BtAction
    {
        protected override NodeStatus OnTick()
        {
            var navigator = GetComponent<NavMeshAgent>();
            if (navigator == null || !navigator.isActiveAndEnabled)
            {
                WarnOnce("needs an enabled NavMeshAgent on the agent's GameObject.");
                return NodeStatus.Failure;
            }

            if (navigator.isOnNavMesh)
            {
                navigator.ResetPath();
                navigator.velocity = Vector3.zero;
            }

            return NodeStatus.Success;
        }
    }
}
#endif
