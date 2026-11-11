using UnityEngine;

namespace AIBT.Nodes
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
    }

    [BtNode("aibt.unity.log", Category = "Debug",
        Summary = "Writes a message to the Unity Console and succeeds.",
        WhenToUse = "Use to see when a branch runs while building a tree.",
        WhenNotToUse = "Remove from finished trees that tick every frame; logging is slow.")]
    public sealed class LogNode : BtAction
    {
        public string message = "Behavior tree log";
        public LogLevel level = LogLevel.Info;

        protected override NodeStatus OnTick()
        {
            var text = "[" + (Agent != null ? Agent.name : "tree") + "] " + message;
            switch (level)
            {
                case LogLevel.Warning: Debug.LogWarning(text, Agent); break;
                case LogLevel.Error: Debug.LogError(text, Agent); break;
                default: Debug.Log(text, Agent); break;
            }

            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.wait-seconds", Category = "Flow",
        Summary = "Stays running for a duration in seconds, then succeeds.",
        WhenToUse = "Use for pauses measured in game time.",
        WhenNotToUse = "Use aibt.core.cooldown to rate-limit a branch instead of delaying it.")]
    public sealed class WaitSecondsNode : BtAction
    {
        public float seconds = 1f;

        [BtParam("random-extra")]
        public float randomExtra;

        private float _remaining;

        protected override void OnEnter()
        {
            _remaining = Mathf.Max(0f, seconds) + (randomExtra > 0f ? Random.Range(0f, randomExtra) : 0f);
        }

        protected override NodeStatus OnTick()
        {
            _remaining -= DeltaTime;
            return _remaining <= 0f ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.succeed", Category = "Flow", Summary = "Succeeds immediately.")]
    public sealed class SucceedNode : BtAction
    {
        protected override NodeStatus OnTick() => NodeStatus.Success;
    }

    [BtNode("aibt.unity.fail", Category = "Flow", Summary = "Fails immediately.")]
    public sealed class FailNode : BtAction
    {
        protected override NodeStatus OnTick() => NodeStatus.Failure;
    }

    [BtNode("aibt.unity.idle", Category = "Flow",
        Summary = "Stays running until a parent interrupts it.",
        WhenToUse = "Use as the last child of a reactive selector so the tree keeps running while nothing else applies.")]
    public sealed class IdleNode : BtAction
    {
        protected override NodeStatus OnTick() => NodeStatus.Running;
    }
}
