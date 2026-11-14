using UnityEngine;

namespace AIBT.Nodes
{
    internal static class SceneReference
    {
        internal static string Describe(string reference) => string.IsNullOrEmpty(reference) ? "the agent" : reference;
    }

    // Nodes that act on scene objects take a 'reference' parameter: empty means the agent's own
    // GameObject, otherwise it names an entry in the agent's References list (BehaviorTreeAgent).

    [BtNode("aibt.unity.set-active", Category = "GameObject",
        Summary = "Activates or deactivates a GameObject and succeeds; fails when the reference is missing.",
        WhenToUse = "Use to show a weapon, an alert icon or an effect. Deactivating the agent itself also stops its tree.")]
    public sealed class SetActiveNode : BtAction
    {
        public string reference = string.Empty;
        public bool active = true;

        protected override NodeStatus OnTick()
        {
            var target = ResolveGameObject(reference);
            if (target == null)
            {
                WarnOnce("reference '" + SceneReference.Describe(reference) + "' is missing.");
                return NodeStatus.Failure;
            }

            target.SetActive(active);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.destroy-self", Category = "GameObject",
        Summary = "Destroys the agent's GameObject after an optional delay and succeeds.",
        WhenToUse = "Use at the end of a death or despawn sequence. Running nodes get their exit callbacks when the object is destroyed.")]
    public sealed class DestroySelfNode : BtAction
    {
        public float delay;

        protected override NodeStatus OnTick()
        {
            var self = gameObject;
            if (self == null) return NodeStatus.Failure;
            if (Application.isPlaying) Object.Destroy(self, Mathf.Max(0f, delay));
            else Object.DestroyImmediate(self); // Edit Mode tools and tests: Destroy is not allowed there.
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.get-position", Category = "GameObject",
        Summary = "Writes the position of the agent or a referenced object to a Vector3 key and succeeds; fails when the reference is missing.",
        WhenToUse = "Use to bring a scene object into the blackboard, for example the 'player' reference before move-towards or look-at.")]
    public sealed class GetPositionNode : BtAction
    {
        public BbKey<Vector3> result;
        public string reference = string.Empty;

        protected override NodeStatus OnTick()
        {
            var target = ResolveGameObject(reference);
            if (target == null) return NodeStatus.Failure;
            Set(result, target.transform.position);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.has-reference", Category = "GameObject",
        Summary = "Succeeds while a named reference is assigned, not destroyed and, optionally, active.",
        WhenToUse = "Use to stop chasing a target that died or was disabled.")]
    public sealed class HasReferenceNode : BtCondition
    {
        public string reference = "target";

        [BtParam("require-active")]
        public bool requireActive = true;

        protected override bool Check()
        {
            if (string.IsNullOrEmpty(reference)) return false;
            var target = ResolveGameObject(reference);
            return target != null && (!requireActive || target.activeInHierarchy);
        }
    }

    [BtNode("aibt.unity.raise-event", Category = "Events",
        Summary = "Raises a named event on the agent (agent.Events and the inspector's Event Responses) and succeeds.",
        WhenToUse = "Use to tell gameplay code something happened, such as 'spotted-player', without writing a node for it.",
        WhenNotToUse = "Use a blackboard key when gameplay code should read a lasting state rather than react to a moment.")]
    public sealed class RaiseEventNode : BtAction
    {
        [BtParam("event-name")]
        public string eventName = "event";

        protected override NodeStatus OnTick()
        {
            var agent = Agent;
            if (agent == null) return NodeStatus.Failure;
            agent.Events.Raise(eventName);
            return NodeStatus.Success;
        }
    }
}
