using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Spikes.EditorGraphFrameworkGraphView
{
    // Representative AIBT-shaped node set for the P3-014 spike. Not production
    // code: exercises UnityEditor.Experimental.GraphView against a
    // behavior-tree shape only, mirroring P3-001's archetypes for comparison.
    internal class BehaviorTreeGraphView : GraphView
    {
    }

    internal enum BtNodeKind
    {
        Root,
        Sequence,
        Selector,
        Condition,
        Action
    }

    internal class BtNode : Node
    {
        public string Id;
        public BtNodeKind Kind;
        public Port InputPort;
        public Port OutputPort;

        public BtNode(string id, BtNodeKind kind)
        {
            Id = id;
            Kind = kind;
            title = kind + " " + id;

            if (kind != BtNodeKind.Root)
            {
                InputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
                InputPort.portName = "in";
                inputContainer.Add(InputPort);
            }

            if (kind == BtNodeKind.Root || kind == BtNodeKind.Sequence || kind == BtNodeKind.Selector)
            {
                OutputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
                OutputPort.portName = "children";
                outputContainer.Add(OutputPort);
            }

            RefreshExpandedState();
            RefreshPorts();
        }
    }

    internal class BehaviorTreeGraphWindow : EditorWindow
    {
        public BehaviorTreeGraphView HostedView;

        public void Populate(BehaviorTreeGraphView view)
        {
            HostedView = view;
            view.style.flexGrow = 1;
            rootVisualElement.Add(view);
        }
    }
}
