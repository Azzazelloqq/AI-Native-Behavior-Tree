using UnityEngine;

namespace AIBT.Nodes
{
    [BtNode("aibt.unity.rotate-towards", Category = "Movement",
        Summary = "Turns the agent to face along a direction stored in a Vector3 key and succeeds once it does.",
        WhenToUse = "Use when you have a direction (a velocity, a normal, a 'facing' key) rather than a position.",
        WhenNotToUse = "Use aibt.unity.look-at to face a position.")]
    public sealed class RotateTowardsNode : BtAction
    {
        public BbKey<Vector3> direction;

        [BtParam("degrees-per-second")]
        public float degreesPerSecond = 360f;

        [BtParam("keep-upright")]
        public bool keepUpright = true;

        protected override NodeStatus OnTick()
        {
            var body = transform;
            if (body == null) return NodeStatus.Failure;
            var wantedDirection = Get(direction);
            if (keepUpright) wantedDirection.y = 0f;
            if (wantedDirection.sqrMagnitude < 0.000001f) return NodeStatus.Success;
            var wanted = Quaternion.LookRotation(wantedDirection.normalized, Vector3.up);
            if (degreesPerSecond <= 0f)
            {
                body.rotation = wanted;
                return NodeStatus.Success;
            }

            body.rotation = Quaternion.RotateTowards(body.rotation, wanted, degreesPerSecond * DeltaTime);
            return Quaternion.Angle(body.rotation, wanted) < 0.5f ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.turn-by", Category = "Movement",
        Summary = "Turns the agent around its up axis by an angle at a speed and succeeds when the turn is done.",
        WhenToUse = "Use to look around while idle or searching. A negative angle turns left.")]
    public sealed class TurnByNode : BtAction
    {
        public float degrees = 90f;

        [BtParam("degrees-per-second")]
        public float degreesPerSecond = 180f;

        private float _remaining;

        protected override void OnEnter() => _remaining = degrees;

        protected override NodeStatus OnTick()
        {
            var body = transform;
            if (body == null) return NodeStatus.Failure;
            if (Mathf.Abs(_remaining) < 0.001f) return NodeStatus.Success;
            var step = degreesPerSecond <= 0f ? Mathf.Abs(_remaining) : Mathf.Min(Mathf.Abs(_remaining), degreesPerSecond * DeltaTime);
            var signed = Mathf.Sign(_remaining) * step;
            body.Rotate(0f, signed, 0f, Space.World);
            _remaining -= signed;
            return Mathf.Abs(_remaining) < 0.001f ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.move-direction", Category = "Movement",
        Summary = "Moves the agent along a direction at a speed for a duration, then succeeds.",
        WhenToUse = "Use for dashes, knock-back or drifting. Without a direction key it moves along its own forward. A duration of 0 keeps moving until a parent interrupts it.",
        WhenNotToUse = "Use aibt.unity.move-towards to reach a position.")]
    public sealed class MoveDirectionNode : BtAction
    {
        [BtParam(Optional = true)]
        public BbKey<Vector3> direction;

        public float speed = 2f;

        public float duration = 1f;

        [BtParam("face-direction")]
        public bool faceDirection;

        private float _elapsed;

        protected override void OnEnter() => _elapsed = 0f;

        protected override NodeStatus OnTick()
        {
            var body = transform;
            if (body == null) return NodeStatus.Failure;
            var heading = direction.IsBound ? Get(direction) : body.forward;
            if (heading.sqrMagnitude > 0.000001f)
            {
                heading.Normalize();
                body.position += heading * (speed * DeltaTime);
                var flat = new Vector3(heading.x, 0f, heading.z);
                if (faceDirection && flat.sqrMagnitude > 0.000001f) body.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }

            _elapsed += DeltaTime;
            return duration > 0f && _elapsed >= duration ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.keep-distance", Category = "Movement",
        Summary = "Moves the agent towards or away from a position until its distance is between a minimum and a maximum, then succeeds.",
        WhenToUse = "Use for ranged attackers and companions that should stay neither too close nor too far. Set min-distance alone to flee.",
        WhenNotToUse = "Use a NavMesh node when obstacles are in the way; this moves in a straight line.")]
    public sealed class KeepDistanceNode : BtAction
    {
        public BbKey<Vector3> target;

        [BtParam("min-distance")]
        public float minDistance = 3f;

        [BtParam("max-distance")]
        public float maxDistance = 6f;

        public float speed = 3f;

        [BtParam("keep-height")]
        public bool keepHeight = true;

        [BtParam("face-target")]
        public bool faceTarget = true;

        protected override NodeStatus OnTick()
        {
            var body = transform;
            if (body == null) return NodeStatus.Failure;
            var position = body.position;
            var goal = Get(target);
            if (keepHeight) goal.y = position.y;
            var offset = goal - position;
            var distance = offset.magnitude;
            var low = Mathf.Max(0f, minDistance);
            var high = maxDistance > 0f ? Mathf.Max(low, maxDistance) : float.PositiveInfinity;
            var toward = distance > 0.0001f ? offset / distance : -body.forward;
            if (faceTarget && distance > 0.0001f)
                body.rotation = Quaternion.LookRotation(new Vector3(toward.x, keepHeight ? 0f : toward.y, toward.z), Vector3.up);
            if (distance >= low && distance <= high) return NodeStatus.Success;

            var step = Mathf.Max(0f, speed) * DeltaTime;
            if (distance < low)
            {
                step = Mathf.Min(step, low - distance);
                body.position = position - toward * step;
                return distance + step >= low ? NodeStatus.Success : NodeStatus.Running;
            }

            step = Mathf.Min(step, distance - high);
            body.position = position + toward * step;
            return distance - step <= high ? NodeStatus.Success : NodeStatus.Running;
        }
    }
}
