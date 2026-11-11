using UnityEngine;

namespace AIBT.Nodes
{
    [BtNode("aibt.unity.move-towards", Category = "Movement",
        Summary = "Moves the agent's Transform in a straight line to a position and succeeds on arrival.",
        WhenToUse = "Use for simple movement without obstacles. The target is re-read every tick, so it can move.",
        WhenNotToUse = "Use a NavMesh or your own locomotion node when the agent must path around obstacles.")]
    public sealed class MoveTowardsNode : BtAction
    {
        public BbKey<Vector3> target;
        public float speed = 3f;

        [BtParam("stop-distance")]
        public float stopDistance = 0.1f;

        [BtParam("keep-height")]
        public bool keepHeight;

        [BtParam("face-direction")]
        public bool faceDirection = true;

        protected override NodeStatus OnTick()
        {
            var body = transform;
            if (body == null) return NodeStatus.Failure;
            var destination = Get(target);
            var position = body.position;
            if (keepHeight) destination.y = position.y;
            var offset = destination - position;
            var distance = offset.magnitude;
            if (distance <= stopDistance) return NodeStatus.Success;

            var step = Mathf.Min(Mathf.Max(0f, speed) * DeltaTime, distance - stopDistance);
            body.position = position + offset / distance * step;
            if (faceDirection && distance > 0.0001f) body.rotation = Quaternion.LookRotation(offset / distance, Vector3.up);
            return distance - step <= stopDistance ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.look-at", Category = "Movement",
        Summary = "Turns the agent towards a position and succeeds once it faces it.")]
    public sealed class LookAtNode : BtAction
    {
        public BbKey<Vector3> target;

        [BtParam("degrees-per-second")]
        public float degreesPerSecond = 360f;

        [BtParam("keep-height")]
        public bool keepHeight = true;

        protected override NodeStatus OnTick()
        {
            var body = transform;
            if (body == null) return NodeStatus.Failure;
            var destination = Get(target);
            if (keepHeight) destination.y = body.position.y;
            var direction = destination - body.position;
            if (direction.sqrMagnitude < 0.000001f) return NodeStatus.Success;

            var wanted = Quaternion.LookRotation(direction.normalized, Vector3.up);
            if (degreesPerSecond <= 0f)
            {
                body.rotation = wanted;
                return NodeStatus.Success;
            }

            body.rotation = Quaternion.RotateTowards(body.rotation, wanted, degreesPerSecond * DeltaTime);
            return Quaternion.Angle(body.rotation, wanted) < 0.5f ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.is-within-distance", Category = "Movement",
        Summary = "Succeeds while the agent is within a distance of a position.")]
    public sealed class IsWithinDistanceNode : BtCondition
    {
        public BbKey<Vector3> target;
        public float distance = 5f;

        [BtParam("keep-height")]
        public bool keepHeight;

        protected override bool Check()
        {
            var body = transform;
            if (body == null) return false;
            var destination = Get(target);
            var position = body.position;
            if (keepHeight) destination.y = position.y;
            return (destination - position).sqrMagnitude <= distance * distance;
        }
    }
}
