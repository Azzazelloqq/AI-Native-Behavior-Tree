using UnityEngine;

namespace AIBT.Samples.SimpleAgent
{
    // The one project node this sample still needs; everything else in its trees is a standard
    // aibt.unity.* node. It stays as the "write your own" example: public fields are the
    // parameters a tree author edits, BbKey<T> fields are blackboard keys chosen in the tree,
    // and the node may use the agent's GameObject directly.

    [BtNode("samples.simple.next-waypoint", Category = "Samples/Simple Agent",
        Summary = "Writes the next point of the agent's PatrolRoute to a blackboard key.")]
    public sealed class NextWaypointNode : BtAction
    {
        public BbKey<Vector3> destination;

        private PatrolRoute _route;

        protected override void OnInitialize() => _route = GetComponent<PatrolRoute>();

        protected override NodeStatus OnTick()
        {
            if (_route == null || _route.Count == 0) return NodeStatus.Failure;
            Set(destination, _route.Next());
            return NodeStatus.Success;
        }
    }
}
