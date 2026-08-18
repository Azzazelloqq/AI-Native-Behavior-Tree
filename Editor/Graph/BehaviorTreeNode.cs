using AIBT.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    public sealed class BehaviorTreeNode : Node
    {
        public BehaviorTreeNode(NodeDocument document, NodeManifest manifest)
        {
            NodeId = document.Id;
            TypeId = document.TypeId;
            Manifest = manifest;
            title = string.IsNullOrEmpty(document.DisplayName) ? document.TypeId : document.DisplayName;

            InputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
            InputPort.portName = "in";
            inputContainer.Add(InputPort);

            if (AcceptsChildren(manifest))
            {
                OutputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
                OutputPort.portName = "children";
                outputContainer.Add(OutputPort);
            }

            titleContainer.style.backgroundColor = ColorForKind(manifest?.Kind);
            extensionContainer.Add(new Label(manifest != null ? manifest.Kind.ToString() : "Unresolved") { name = "kind-label" });

            RefreshExpandedState();
            RefreshPorts();
        }

        public NodeId NodeId { get; }

        public string TypeId { get; }

        public NodeManifest Manifest { get; }

        public Port InputPort { get; }

        public Port OutputPort { get; }

        private static bool AcceptsChildren(NodeManifest manifest)
        {
            if (manifest == null)
            {
                return true;
            }

            var maximum = manifest.ChildPolicy.Maximum;
            return !maximum.HasValue || maximum.Value > 0;
        }

        private static Color ColorForKind(NodeBehaviorKind? kind)
        {
            switch (kind)
            {
                case NodeBehaviorKind.Composite:
                    return new Color(0.20f, 0.35f, 0.55f);
                case NodeBehaviorKind.Decorator:
                    return new Color(0.45f, 0.32f, 0.55f);
                case NodeBehaviorKind.Condition:
                    return new Color(0.55f, 0.45f, 0.20f);
                case NodeBehaviorKind.Action:
                    return new Color(0.25f, 0.50f, 0.30f);
                default:
                    return new Color(0.30f, 0.30f, 0.30f);
            }
        }
    }
}
