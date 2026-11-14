#if AIBT_UNITY_ANIMATION
using UnityEngine;

namespace AIBT.Nodes
{
    // Compiled only when the module behind AIBT_UNITY_ANIMATION is enabled (version define in AIBT.Agents.asmdef).

    [BtNode("aibt.unity.animator-trigger", Category = "Animation",
        Summary = "Sets an Animator trigger and succeeds; fails when there is no Animator.",
        WhenToUse = "Use to play a one-shot animation such as 'Attack'. The Animator is found on the referenced object or its children.")]
    public sealed class AnimatorTriggerNode : BtAction
    {
        public string parameter = "Trigger";
        public string reference = string.Empty;

        protected override NodeStatus OnTick()
        {
            var animator = ResolveComponent<Animator>(reference);
            if (animator == null)
            {
                WarnOnce("found no Animator on '" + SceneReference.Describe(reference) + "'.");
                return NodeStatus.Failure;
            }

            animator.SetTrigger(parameter);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.animator-bool", Category = "Animation",
        Summary = "Sets an Animator bool parameter and succeeds; fails when there is no Animator.")]
    public sealed class AnimatorBoolNode : BtAction
    {
        public string parameter = "Moving";
        public bool value = true;
        public string reference = string.Empty;

        protected override NodeStatus OnTick()
        {
            var animator = ResolveComponent<Animator>(reference);
            if (animator == null)
            {
                WarnOnce("found no Animator on '" + SceneReference.Describe(reference) + "'.");
                return NodeStatus.Failure;
            }

            animator.SetBool(parameter, value);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.animator-float", Category = "Animation",
        Summary = "Sets an Animator float parameter from a constant or a float key and succeeds; fails when there is no Animator.",
        WhenToUse = "Bind from-key to drive a blend tree from the blackboard, for example 'speed'.")]
    public sealed class AnimatorFloatNode : BtAction
    {
        public string parameter = "Speed";
        public float value;

        [BtParam("from-key", Optional = true)]
        public BbKey<float> fromKey;

        public string reference = string.Empty;

        protected override NodeStatus OnTick()
        {
            var animator = ResolveComponent<Animator>(reference);
            if (animator == null)
            {
                WarnOnce("found no Animator on '" + SceneReference.Describe(reference) + "'.");
                return NodeStatus.Failure;
            }

            animator.SetFloat(parameter, fromKey.IsBound ? Get(fromKey) : value);
            return NodeStatus.Success;
        }
    }
}
#endif
