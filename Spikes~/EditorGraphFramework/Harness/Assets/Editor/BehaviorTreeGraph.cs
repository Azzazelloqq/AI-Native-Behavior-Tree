using Unity.GraphToolkit.Editor;

namespace AIBT.Spikes.EditorGraphFramework
{
    // Representative AIBT-shaped node set for the P3-001 spike. Not production
    // code: exercises Unity Graph Toolkit against a behavior-tree shape only.
    [Graph("aibtspike", GraphOptions.Default)]
    internal class BehaviorTreeGraph : Graph
    {
    }

    [Node("AIBT Spike")]
    internal class SequenceNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("in");
            context.AddOutputPort("children");
        }
    }

    [Node("AIBT Spike")]
    internal class SelectorNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("in");
            context.AddOutputPort("children");
        }
    }

    [Node("AIBT Spike")]
    internal class ConditionNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("in");
        }
    }

    [Node("AIBT Spike")]
    internal class ActionNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("in");
        }
    }

    [Node("AIBT Spike")]
    internal class RootNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort("children");
        }
    }
}
