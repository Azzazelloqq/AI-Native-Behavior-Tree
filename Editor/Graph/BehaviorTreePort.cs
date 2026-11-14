using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>A vertical, label-less port whose edge drops are routed to the owning graph view.</summary>
    internal sealed class BehaviorTreePort : Port
    {
        private BehaviorTreePort(Direction direction, Capacity capacity)
            : base(Orientation.Vertical, direction, capacity, typeof(bool))
        {
        }

        internal static BehaviorTreePort Create(Direction direction, Capacity capacity, IEdgeConnectorListener listener)
        {
            var port = new BehaviorTreePort(direction, capacity) { portName = string.Empty };
            port.m_EdgeConnector = new EdgeConnector<BehaviorTreeEdge>(listener ?? NoDropListener.Instance);
            port.AddManipulator(port.m_EdgeConnector);
            port.AddToClassList("aibt-port");
            port.portColor = new Color(0.62f, 0.65f, 0.70f);
            return port;
        }

        private sealed class NoDropListener : IEdgeConnectorListener
        {
            internal static readonly NoDropListener Instance = new NoDropListener();

            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
            }

            public void OnDrop(GraphView graphView, Edge edge)
            {
            }
        }
    }

    /// <summary>A parent-to-child edge that can be highlighted as part of the live active path.</summary>
    internal sealed class BehaviorTreeEdge : Edge
    {
        internal static readonly Color ActiveColor = new Color(1f, 0.85f, 0.29f);

        private bool _active;

        public BehaviorTreeEdge()
        {
            AddToClassList("aibt-edge");
            // GraphView recolors edges from their ports on every update, so the live path is
            // painted as an overlay stroke along the edge's own control points.
            edgeControl.generateVisualContent += PaintActivePath;
        }

        /// <summary>True while both ends of the edge are running.</summary>
        internal bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                EnableInClassList("aibt-edge--active", value);
                if (input != null && output != null) UpdateEdgeControl();
                edgeControl.MarkDirtyRepaint();
            }
        }

        private void PaintActivePath(MeshGenerationContext context)
        {
            if (!_active || selected) return;
            var points = edgeControl.controlPoints;
            if (points == null || points.Length < 2) return;
            var painter = context.painter2D;
            painter.strokeColor = ActiveColor;
            painter.lineWidth = 4f;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            // Control points are in the edge's space; the control draws in its own.
            painter.MoveTo(this.ChangeCoordinatesTo(edgeControl, points[0]));
            for (var index = 1; index < points.Length; index++) painter.LineTo(this.ChangeCoordinatesTo(edgeControl, points[index]));
            painter.Stroke();
        }
    }
}
