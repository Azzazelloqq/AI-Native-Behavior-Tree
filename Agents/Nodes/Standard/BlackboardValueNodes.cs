using UnityEngine;

namespace AIBT.Nodes
{
    [BtNode("aibt.unity.set-vector3", Category = "Blackboard",
        Summary = "Writes a constant Vector3 to a blackboard key and succeeds.")]
    public sealed class SetVector3Node : BtAction
    {
        public BbKey<Vector3> key;
        public float x;
        public float y;
        public float z;

        protected override NodeStatus OnTick()
        {
            Set(key, new Vector3(x, y, z));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.copy-bool", Category = "Blackboard",
        Summary = "Copies one bool blackboard value into another key and succeeds.")]
    public sealed class CopyBoolNode : BtAction
    {
        public BbKey<bool> from;
        public BbKey<bool> to;

        protected override NodeStatus OnTick()
        {
            Set(to, Get(from));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.copy-int", Category = "Blackboard",
        Summary = "Copies one int blackboard value into another key and succeeds.")]
    public sealed class CopyIntNode : BtAction
    {
        public BbKey<int> from;
        public BbKey<int> to;

        protected override NodeStatus OnTick()
        {
            Set(to, Get(from));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.copy-float", Category = "Blackboard",
        Summary = "Copies one float blackboard value into another key and succeeds.")]
    public sealed class CopyFloatNode : BtAction
    {
        public BbKey<float> from;
        public BbKey<float> to;

        protected override NodeStatus OnTick()
        {
            Set(to, Get(from));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.copy-vector3", Category = "Blackboard",
        Summary = "Copies one Vector3 blackboard value into another key and succeeds.",
        WhenToUse = "Use to remember a position, for example the last place the player was seen.")]
    public sealed class CopyVector3Node : BtAction
    {
        public BbKey<Vector3> from;
        public BbKey<Vector3> to;

        protected override NodeStatus OnTick()
        {
            Set(to, Get(from));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.increment-int", Category = "Blackboard",
        Summary = "Adds an amount (default 1) to an int blackboard value and succeeds.",
        WhenToUse = "Use for counters such as patrol index or attempts. A negative amount decrements.")]
    public sealed class IncrementIntNode : BtAction
    {
        public BbKey<int> key;
        public int amount = 1;

        protected override NodeStatus OnTick()
        {
            Set(key, unchecked(Get(key) + amount));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.increment-float", Category = "Blackboard",
        Summary = "Adds an amount to a float blackboard value and succeeds. A negative amount decrements.")]
    public sealed class IncrementFloatNode : BtAction
    {
        public BbKey<float> key;
        public float amount = 1f;

        protected override NodeStatus OnTick()
        {
            Set(key, Get(key) + amount);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.toggle-bool", Category = "Blackboard",
        Summary = "Inverts a bool blackboard value and succeeds.")]
    public sealed class ToggleBoolNode : BtAction
    {
        public BbKey<bool> key;

        protected override NodeStatus OnTick()
        {
            Set(key, !Get(key));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.compare-int-keys", Category = "Blackboard",
        Summary = "Compares two int blackboard values.",
        WhenToUse = "Use when both sides change at runtime, such as 'ammo' against 'ammo-needed'.",
        WhenNotToUse = "Use aibt.unity.compare-int when one side is a constant.")]
    public sealed class CompareIntKeysNode : BtCondition
    {
        public BbKey<int> left;
        public Comparison comparison = Comparison.GreaterOrEqual;
        public BbKey<int> right;

        protected override bool Check() => Compare.Test(Get(left), comparison, Get(right));
    }

    [BtNode("aibt.unity.compare-float-keys", Category = "Blackboard",
        Summary = "Compares two float blackboard values.",
        WhenToUse = "Use when both sides change at runtime, such as 'health' against 'flee-health'.",
        WhenNotToUse = "Use aibt.unity.compare-float when one side is a constant.")]
    public sealed class CompareFloatKeysNode : BtCondition
    {
        public BbKey<float> left;
        public Comparison comparison = Comparison.LessThan;
        public BbKey<float> right;

        protected override bool Check() => Compare.Test(Get(left), comparison, Get(right));
    }

    [BtNode("aibt.unity.is-vector3-set", Category = "Blackboard",
        Summary = "Succeeds while a Vector3 blackboard value is not (0, 0, 0), the value of a key nothing has written.",
        WhenToUse = "Use to check that a position such as 'last-seen' has been filled in.",
        WhenNotToUse = "When the world origin is a valid position, track it with a separate bool key instead.")]
    public sealed class IsVector3SetNode : BtCondition
    {
        public BbKey<Vector3> key;

        protected override bool Check() => Get(key) != Vector3.zero;
    }

    [BtNode("aibt.unity.random-float", Category = "Blackboard",
        Summary = "Writes a random float between min and max to a blackboard key and succeeds.")]
    public sealed class RandomFloatNode : BtAction
    {
        public BbKey<float> key;
        public float min;
        public float max = 1f;

        protected override NodeStatus OnTick()
        {
            Set(key, Random.Range(Mathf.Min(min, max), Mathf.Max(min, max)));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.random-int", Category = "Blackboard",
        Summary = "Writes a random int from min to max, both included, to a blackboard key and succeeds.")]
    public sealed class RandomIntNode : BtAction
    {
        public BbKey<int> key;
        public int min;
        public int max = 10;

        protected override NodeStatus OnTick()
        {
            var low = Mathf.Min(min, max);
            var high = Mathf.Max(min, max);
            Set(key, high == int.MaxValue ? Random.Range(low, high) : Random.Range(low, high + 1));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.random-point", Category = "Blackboard",
        Summary = "Writes a random point within a radius of a center to a Vector3 key and succeeds.",
        WhenToUse = "Use for wandering: pick a point, then move to it. Without a center key the agent's position when the tree started is used, so it stays near home.",
        WhenNotToUse = "On a NavMesh, a random point can be unreachable; use aibt.unity.nav-move-to, which fails on such points, or your own sampler.")]
    public sealed class RandomPointNode : BtAction
    {
        public BbKey<Vector3> result;

        [BtParam(Optional = true)]
        public BbKey<Vector3> center;

        public float radius = 5f;

        [BtParam("min-radius")]
        public float minRadius;

        [Tooltip("Keep the point at the center's height (pick on the XZ plane).")]
        public bool flat = true;

        private Vector3 _home;

        protected override void OnInitialize()
        {
            _home = transform != null ? transform.position : Vector3.zero;
        }

        protected override NodeStatus OnTick()
        {
            var origin = center.IsBound ? Get(center) : _home;
            var outer = Mathf.Max(0f, radius);
            var inner = Mathf.Clamp(minRadius, 0f, outer);
            Vector3 direction;
            if (flat)
            {
                var circle = Random.insideUnitCircle;
                direction = circle.sqrMagnitude > 0.000001f ? new Vector3(circle.x, 0f, circle.y).normalized : Vector3.forward;
            }
            else
            {
                direction = Random.onUnitSphere;
            }

            // Uniform over the ring's area, not clustered at the center.
            var distance = Mathf.Sqrt(Random.Range(inner * inner, outer * outer));
            Set(result, origin + direction * distance);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.distance", Category = "Blackboard",
        Summary = "Writes the distance between two Vector3 blackboard values to a float key and succeeds.",
        WhenToUse = "Use when later nodes need the number itself, for example to compare it with a key that changes.",
        WhenNotToUse = "Use aibt.unity.is-within-distance to just test the agent's own distance to a position.")]
    public sealed class DistanceNode : BtAction
    {
        public BbKey<Vector3> from;
        public BbKey<Vector3> to;
        public BbKey<float> result;

        [Tooltip("Ignore the height difference.")]
        public bool flat;

        protected override NodeStatus OnTick()
        {
            var offset = Get(to) - Get(from);
            if (flat) offset.y = 0f;
            Set(result, offset.magnitude);
            return NodeStatus.Success;
        }
    }
}
