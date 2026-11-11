using UnityEngine;

namespace AIBT.Samples.SimpleAgent
{
    // Three project nodes are all this sample needs. Each is an ordinary class: public fields are
    // the parameters a tree author edits, BbKey<T> fields are blackboard keys chosen in the tree,
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

    [BtNode("samples.simple.pick-random-point", Category = "Samples/Simple Agent",
        Summary = "Writes a random point around the origin to a blackboard key.")]
    public sealed class PickRandomPointNode : BtAction
    {
        public BbKey<Vector3> destination;
        public float radius = 8f;

        protected override NodeStatus OnTick()
        {
            var point = Random.insideUnitCircle * radius;
            Set(destination, new Vector3(point.x, transform.position.y, point.y));
            return NodeStatus.Success;
        }
    }

    [BtNode("samples.simple.set-alert", Category = "Samples/Simple Agent",
        Summary = "Shows or hides the exclamation mark above the agent.")]
    public sealed class SetAlertNode : BtAction
    {
        public bool visible = true;

        protected override NodeStatus OnTick()
        {
            var view = GetComponent<AlertView>();
            if (view != null) view.Show(visible);
            return NodeStatus.Success;
        }
    }
}
