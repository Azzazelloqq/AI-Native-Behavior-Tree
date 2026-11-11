using UnityEngine;

namespace AIBT.Nodes
{
    public enum Comparison
    {
        LessThan,
        LessOrEqual,
        Equal,
        NotEqual,
        GreaterOrEqual,
        GreaterThan,
    }

    internal static class Compare
    {
        internal static bool Test(float left, Comparison comparison, float right)
        {
            switch (comparison)
            {
                case Comparison.LessThan: return left < right;
                case Comparison.LessOrEqual: return left <= right;
                case Comparison.Equal: return Mathf.Approximately(left, right);
                case Comparison.NotEqual: return !Mathf.Approximately(left, right);
                case Comparison.GreaterOrEqual: return left >= right;
                default: return left > right;
            }
        }

        internal static bool Test(int left, Comparison comparison, int right)
        {
            switch (comparison)
            {
                case Comparison.LessThan: return left < right;
                case Comparison.LessOrEqual: return left <= right;
                case Comparison.Equal: return left == right;
                case Comparison.NotEqual: return left != right;
                case Comparison.GreaterOrEqual: return left >= right;
                default: return left > right;
            }
        }
    }

    [BtNode("aibt.unity.is-true", Category = "Blackboard",
        Summary = "Succeeds while a bool blackboard value equals the expected value.",
        WhenToUse = "Use to gate a branch on a fact a script or another node writes.")]
    public sealed class IsTrueNode : BtCondition
    {
        public BbKey<bool> key;
        public bool expected = true;

        protected override bool Check() => Get(key) == expected;
    }

    [BtNode("aibt.unity.compare-float", Category = "Blackboard",
        Summary = "Compares a float blackboard value with a constant.")]
    public sealed class CompareFloatNode : BtCondition
    {
        public BbKey<float> key;
        public Comparison comparison = Comparison.LessThan;
        public float value;

        protected override bool Check() => Compare.Test(Get(key), comparison, value);
    }

    [BtNode("aibt.unity.compare-int", Category = "Blackboard",
        Summary = "Compares an int blackboard value with a constant.")]
    public sealed class CompareIntNode : BtCondition
    {
        public BbKey<int> key;
        public Comparison comparison = Comparison.Equal;
        public int value;

        protected override bool Check() => Compare.Test(Get(key), comparison, value);
    }

    [BtNode("aibt.unity.set-bool", Category = "Blackboard",
        Summary = "Writes a constant to a bool blackboard key and succeeds.")]
    public sealed class SetBoolNode : BtAction
    {
        public BbKey<bool> key;
        public bool value = true;

        protected override NodeStatus OnTick()
        {
            Set(key, value);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.set-int", Category = "Blackboard",
        Summary = "Writes a constant to an int blackboard key and succeeds.")]
    public sealed class SetIntNode : BtAction
    {
        public BbKey<int> key;
        public int value;

        protected override NodeStatus OnTick()
        {
            Set(key, value);
            return NodeStatus.Success;
        }
    }

    [BtNode("aibt.unity.set-float", Category = "Blackboard",
        Summary = "Writes a constant to a float blackboard key and succeeds.")]
    public sealed class SetFloatNode : BtAction
    {
        public BbKey<float> key;
        public float value;

        protected override NodeStatus OnTick()
        {
            Set(key, value);
            return NodeStatus.Success;
        }
    }
}
