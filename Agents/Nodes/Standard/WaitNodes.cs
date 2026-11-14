using UnityEngine;

namespace AIBT.Nodes
{
    [BtNode("aibt.unity.wait-random", Category = "Flow",
        Summary = "Stays running for a random duration between two values in seconds, then succeeds.",
        WhenToUse = "Use for pauses that should not look mechanical, such as idling between wander points.",
        WhenNotToUse = "Use aibt.unity.wait-seconds when the duration must be exact.")]
    public sealed class WaitRandomNode : BtAction
    {
        [BtParam("min-seconds")]
        public float minSeconds = 1f;

        [BtParam("max-seconds")]
        public float maxSeconds = 3f;

        private float _remaining;

        protected override void OnEnter()
        {
            var low = Mathf.Max(0f, Mathf.Min(minSeconds, maxSeconds));
            var high = Mathf.Max(0f, Mathf.Max(minSeconds, maxSeconds));
            _remaining = Random.Range(low, high);
        }

        protected override NodeStatus OnTick()
        {
            _remaining -= DeltaTime;
            return _remaining <= 0f ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.wait-until", Category = "Flow",
        Summary = "Stays running until a bool blackboard value equals the expected value, then succeeds.",
        WhenToUse = "Use to pause a sequence until gameplay code or another branch sets a flag, such as 'door-open'.",
        WhenNotToUse = "Use aibt.unity.is-true with an interrupt when the flag should cancel running work instead of resuming it.")]
    public sealed class WaitUntilNode : BtAction
    {
        public BbKey<bool> key;
        public bool expected = true;

        [BtParam("timeout-seconds")]
        public float timeoutSeconds;

        private float _elapsed;

        protected override void OnEnter() => _elapsed = 0f;

        protected override NodeStatus OnTick()
        {
            if (Get(key) == expected) return NodeStatus.Success;
            _elapsed += DeltaTime;
            return timeoutSeconds > 0f && _elapsed >= timeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
        }
    }

    [BtNode("aibt.unity.wait-frames", Category = "Flow",
        Summary = "Stays running for a number of tree ticks, then succeeds.",
        WhenToUse = "Use to let physics or another script settle for a frame or two. A tick is a frame when Tick Mode is Every Frame.",
        WhenNotToUse = "Use aibt.unity.wait-seconds for game-time pauses; frame counts change with the frame rate.")]
    public sealed class WaitFramesNode : BtAction
    {
        public int frames = 1;

        private int _remaining;

        protected override void OnEnter() => _remaining = Mathf.Max(0, frames);

        protected override NodeStatus OnTick()
        {
            if (_remaining <= 0) return NodeStatus.Success;
            _remaining--;
            return NodeStatus.Running;
        }
    }
}
