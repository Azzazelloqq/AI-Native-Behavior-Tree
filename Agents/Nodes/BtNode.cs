using System;
using AIBT.Agents;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Base of every managed behavior-tree node. Derive from <see cref="BtAction"/> or
    /// <see cref="BtCondition"/>, not from this class. One instance exists per node per agent, so
    /// ordinary private fields are safe per-agent state.
    /// </summary>
    public abstract class BtNode
    {
        private ManagedTreeInstance _instance;
        private string _nodeId;
        private uint _runtimeIndex;

        internal BtNode()
        {
        }

        /// <summary>The agent running this node. Null only in a headless tree without a component.</summary>
        public BehaviorTreeAgent Agent => _instance?.Owner;

        /// <summary>The agent's GameObject.</summary>
        public GameObject gameObject => Agent != null ? Agent.gameObject : null;

        /// <summary>The agent's Transform.</summary>
        public Transform transform => Agent != null ? Agent.transform : null;

        /// <summary>The agent's blackboard, addressed by key name.</summary>
        public AgentBlackboard Blackboard => _instance?.Blackboard;

        /// <summary>Seconds since this tree's previous tick. Use it instead of Time.deltaTime.</summary>
        public float DeltaTime => _instance != null ? _instance.DeltaTime : 0f;

        /// <summary>Seconds this tree has been running.</summary>
        public float TreeTime => _instance != null ? _instance.TimeSeconds : 0f;

        /// <summary>This node's ID in the tree file, useful in log messages.</summary>
        public string NodeId => _nodeId;

        /// <summary>Returns a component on the agent's GameObject.</summary>
        public T GetComponent<T>()
        {
            var agent = Agent;
            return agent != null ? agent.GetComponent<T>() : default;
        }

        /// <summary>Reads the blackboard value a key parameter is bound to.</summary>
        protected T Get<T>(BbKey<T> key) => RequireInstance().Blackboard.Get(key, _nodeId);

        /// <summary>Writes the blackboard value a key parameter is bound to.</summary>
        protected void Set<T>(BbKey<T> key, T value) => RequireInstance().Blackboard.Set(key, value, _nodeId);

        /// <summary>
        /// Called once after this node's parameters are assigned and before its first use. Cache
        /// components here.
        /// </summary>
        protected virtual void OnInitialize()
        {
        }

        internal uint RuntimeIndex => _runtimeIndex;

        internal void Attach(ManagedTreeInstance instance, string nodeId, uint runtimeIndex)
        {
            _instance = instance;
            _nodeId = nodeId;
            _runtimeIndex = runtimeIndex;
        }

        internal void Initialize() => OnInitialize();

        internal abstract NodeBehaviorKindTag Kind { get; }

        internal abstract void EnterNode();

        internal abstract NodeStatus TickNode();

        internal abstract void AbortNode();

        internal abstract void ExitNode();

        private ManagedTreeInstance RequireInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("This node is not part of a running behavior tree.");
            }

            return _instance;
        }
    }

    /// <summary>
    /// What happens when a condition that already answered would now answer differently. A
    /// sequence or selector normally keeps running the child it chose; these modes make it choose
    /// again as soon as the condition changes, without waiting for that child to finish.
    /// </summary>
    public enum ConditionInterrupt
    {
        /// <summary>The condition is checked only when the tree reaches it.</summary>
        None,

        /// <summary>
        /// While the branch this condition allowed is running, the condition is re-checked every
        /// tick; when it stops holding, that branch is aborted and its parent chooses again.
        /// </summary>
        Self,

        /// <summary>
        /// After this condition failed and a later, lower-priority branch of a selector started,
        /// the condition is re-checked every tick; when it starts holding, that branch is
        /// aborted and the selector chooses again from its first child.
        /// </summary>
        LowerPriority,

        /// <summary>Both <see cref="Self"/> and <see cref="LowerPriority"/>.</summary>
        Both,
    }

    internal enum NodeBehaviorKindTag : byte
    {
        Action,
        Condition,
    }

    /// <summary>
    /// A node that does something and may take several ticks. Override <see cref="OnTick"/>;
    /// return <see cref="NodeStatus.Running"/> to be ticked again, or Success/Failure to finish.
    /// </summary>
    public abstract class BtAction : BtNode
    {
        /// <summary>Called when the tree starts this node, before its first <see cref="OnTick"/>.</summary>
        protected virtual void OnEnter()
        {
        }

        /// <summary>Called every tree tick while this node is active.</summary>
        protected abstract NodeStatus OnTick();

        /// <summary>
        /// Called when the tree interrupts this node while it is Running, for example because a
        /// higher-priority branch took over. <see cref="OnExit"/> follows.
        /// </summary>
        protected virtual void OnAbort()
        {
        }

        /// <summary>Called once whenever this node stops being active: success, failure or abort.</summary>
        protected virtual void OnExit()
        {
        }

        internal sealed override NodeBehaviorKindTag Kind => NodeBehaviorKindTag.Action;

        internal sealed override void EnterNode() => OnEnter();

        internal sealed override NodeStatus TickNode() => OnTick();

        internal sealed override void AbortNode() => OnAbort();

        internal sealed override void ExitNode() => OnExit();
    }

    /// <summary>
    /// A node that answers a yes/no question in a single tick. Override <see cref="Check"/>;
    /// true maps to Success and false to Failure. A condition must not change game state.
    /// </summary>
    public abstract class BtCondition : BtNode
    {
        /// <summary>
        /// Whether a change of this condition's answer interrupts what the tree is doing. Set per
        /// node in the tree. See <see cref="ConditionInterrupt"/>.
        /// </summary>
        public ConditionInterrupt interrupt = ConditionInterrupt.None;

        /// <summary>Returns whether the condition currently holds.</summary>
        protected abstract bool Check();

        internal bool Evaluate() => Check();

        internal sealed override NodeBehaviorKindTag Kind => NodeBehaviorKindTag.Condition;

        internal sealed override void EnterNode()
        {
        }

        internal sealed override NodeStatus TickNode() => Check() ? NodeStatus.Success : NodeStatus.Failure;

        internal sealed override void AbortNode()
        {
        }

        internal sealed override void ExitNode()
        {
        }
    }
}
