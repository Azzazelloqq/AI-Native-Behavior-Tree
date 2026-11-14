using UnityEngine;

namespace AIBT.Nodes
{
#if AIBT_UNITY_PHYSICS
    [BtNode("aibt.unity.line-of-sight", Category = "Checks",
        Summary = "Succeeds while a physics raycast from the agent's eyes reaches a position without hitting other colliders.",
        WhenToUse = "Use for 'can I see the player' checks. Colliders of the agent itself and of target-reference do not block. Add a field of view and range to model vision.",
        WhenNotToUse = "Use aibt.unity.is-within-distance when walls do not matter; a raycast costs more.")]
    public sealed class LineOfSightNode : BtCondition
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public BbKey<Vector3> target;

        [BtParam("eye-height")]
        public float eyeHeight = 1.5f;

        [BtParam("target-height")]
        public float targetHeight = 1f;

        [BtParam("max-distance")]
        public float maxDistance = 20f;

        [Tooltip("Full cone angle in degrees; 360 sees all around.")]
        [BtParam("field-of-view")]
        public float fieldOfView = 360f;

        [BtParam("layer-mask")]
        public int layerMask = -1;

        [BtParam("target-reference")]
        public string targetReference = string.Empty;

        protected override bool Check()
        {
            var body = transform;
            if (body == null) return false;
            var eye = body.position + Vector3.up * eyeHeight;
            var point = Get(target) + Vector3.up * targetHeight;
            var offset = point - eye;
            var distance = offset.magnitude;
            if (distance < 0.0001f) return true;
            if (maxDistance > 0f && distance > maxDistance) return false;
            if (fieldOfView < 360f)
            {
                var flat = new Vector3(offset.x, 0f, offset.z);
                var forward = new Vector3(body.forward.x, 0f, body.forward.z);
                if (flat.sqrMagnitude > 0.000001f && Vector3.Angle(forward, flat) > fieldOfView * 0.5f) return false;
            }

            var ignore = string.IsNullOrEmpty(targetReference) ? null : ResolveGameObject(targetReference);
            var count = Physics.RaycastNonAlloc(eye, offset / distance, Hits, distance, layerMask, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var hit = Hits[index].transform;
                if (hit == null || hit.IsChildOf(body)) continue;
                if (ignore != null && hit.IsChildOf(ignore.transform)) continue;
                return false;
            }

            return true;
        }
    }
#endif

    [BtNode("aibt.unity.random-chance", Category = "Checks",
        Summary = "Succeeds with a probability from 0 to 1 each time it is checked.",
        WhenToUse = "Use to vary behavior, for example a 30% chance to taunt before attacking.",
        WhenNotToUse = "Do not give it an interrupt mode: it would re-roll every tick.")]
    public sealed class RandomChanceNode : BtCondition
    {
        public float chance = 0.5f;

        protected override bool Check() => chance >= 1f || (chance > 0f && Random.value < chance);
    }

    [BtNode("aibt.unity.start-cooldown", Category = "Checks",
        Summary = "Writes the tree time at which a cooldown ends to a float key and succeeds. Pair it with aibt.unity.is-cooldown-ready.",
        WhenToUse = "Use after an ability fires. Several branches, and gameplay code, can share one cooldown through its key.",
        WhenNotToUse = "Use the aibt.core.cooldown decorator for a cooldown private to one branch.")]
    public sealed class StartCooldownNode : BtAction
    {
        public BbKey<float> key;
        public float seconds = 2f;

        protected override NodeStatus OnTick()
        {
            Set(key, TreeTime + Mathf.Max(0f, seconds));
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.is-cooldown-ready", Category = "Checks",
        Summary = "Succeeds once the tree time has reached the time stored in a float key by aibt.unity.start-cooldown.",
        WhenToUse = "Use to gate an ability. A key that was never started (0) is ready.")]
    public sealed class IsCooldownReadyNode : BtCondition
    {
        public BbKey<float> key;

        protected override bool Check() => TreeTime >= Get(key);
    }

    [BtNode("aibt.unity.time-since-changed", Category = "Checks",
        Summary = "Compares the seconds since a blackboard key last changed value with a constant.",
        WhenToUse = "Use for 'lost track of the target for 5 seconds' or 'alarm raised less than 2 seconds ago'. Any key type works.",
        WhenNotToUse = "A write of the same value is not a change; use a counter key when every write must count.")]
    public sealed class TimeSinceChangedNode : BtCondition
    {
        public BbAnyKey key;
        public Comparison comparison = Comparison.GreaterOrEqual;
        public float seconds = 5f;

        protected override bool Check() => Compare.Test(SecondsSinceChanged(key), comparison, seconds);
    }
}
