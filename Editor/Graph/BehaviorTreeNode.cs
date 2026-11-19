using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AIBT.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// A node card: kind accent and tag, readable title, one-line parameter summary, a child-order
    /// badge, a problem badge and a glow used by live execution views. Ports sit on the top
    /// (parent) and bottom (children) edges. Presentation only.
    /// <para>
    /// A subtree reference (ADR-P11-001) is one card with the subtree's name, a SUBTREE badge and
    /// one row per interface key showing what it is connected to; it has no child port.
    /// </para>
    /// </summary>
    public sealed class BehaviorTreeNode : Node
    {
        private readonly Label _kind;
        private readonly Label _title;
        private readonly Label _summary;
        private readonly Label _order;
        private readonly Label _problem;
        private readonly Label _subtreeBadge;
        private readonly VisualElement _rows;
        private readonly VisualElement _glow;
        private string _kindClass;

        public BehaviorTreeNode(NodeDocument document, NodeManifest manifest)
            : this(document, manifest, null)
        {
        }

        internal BehaviorTreeNode(NodeDocument document, NodeManifest manifest, IEdgeConnectorListener listener)
            : this(document, manifest, listener, null)
        {
        }

        internal BehaviorTreeNode(NodeDocument document, NodeManifest manifest, IEdgeConnectorListener listener, SubtreeCardInfo subtree)
        {
            AddToClassList("aibt-node");
            GraphStyles.ApplyGraph(this);
            NodeId = document.Id;
            IsSubtreeReference = document.Subtree != null;
            capabilities = Capabilities.Selectable | Capabilities.Movable;

            // Ports live in the card's own top and bottom slots: GraphView keeps its default
            // input/output containers inside #contents, which the card does not use.
            var inputSlot = new VisualElement { name = "aibt-node-input" };
            inputSlot.AddToClassList("aibt-node__ports");
            InputPort = BehaviorTreePort.Create(Direction.Input, Port.Capacity.Single, listener);
            InputPort.SetEnabled(false);
            inputSlot.Add(InputPort);
            mainContainer.Insert(0, inputSlot);

            var accent = new VisualElement { name = "aibt-node-accent", pickingMode = PickingMode.Ignore };
            accent.AddToClassList("aibt-node__accent");
            mainContainer.Add(accent);
            var body = new VisualElement { name = "aibt-node-body", pickingMode = PickingMode.Ignore };
            body.AddToClassList("aibt-node__body");
            _kind = AddLabel(body, "aibt-node-kind", "aibt-node__kind");
            _title = AddLabel(body, "aibt-node-title", "aibt-node__title");
            _summary = AddLabel(body, "aibt-node-summary", "aibt-node__summary");
            _rows = new VisualElement { name = "aibt-node-subtree-rows", pickingMode = PickingMode.Ignore };
            _rows.AddToClassList("aibt-node__rows");
            body.Add(_rows);
            mainContainer.Add(body);

            if (AcceptsChildren(document, manifest))
            {
                OutputPort = BehaviorTreePort.Create(Direction.Output, Port.Capacity.Multi, listener);
                OutputPort.SetEnabled(false);
                var outputSlot = new VisualElement { name = "aibt-node-output" };
                outputSlot.AddToClassList("aibt-node__ports");
                outputSlot.Add(OutputPort);
                mainContainer.Add(outputSlot);
            }

            _glow = new VisualElement { name = "aibt-node-glow", pickingMode = PickingMode.Ignore };
            _glow.AddToClassList("aibt-node__glow");
            Insert(0, _glow);
            _order = AddLabel(this, "aibt-node-order", "aibt-node__badge");
            _order.AddToClassList("aibt-node__order");
            _order.AddToClassList("aibt-node__badge--hidden");
            _problem = AddLabel(this, "aibt-node-problem", "aibt-node__badge");
            _problem.AddToClassList("aibt-node__problem");
            _problem.AddToClassList("aibt-node__badge--hidden");
            _problem.text = "!";
            _problem.pickingMode = PickingMode.Position;
            _subtreeBadge = AddLabel(this, "aibt-node-subtree-badge", "aibt-node__badge");
            _subtreeBadge.AddToClassList("aibt-node__subtree-badge");
            _subtreeBadge.text = "SUBTREE";
            _subtreeBadge.tooltip = "A subtree reference. Double-click to open the subtree.";
            _subtreeBadge.EnableInClassList("aibt-node__badge--hidden", !IsSubtreeReference);

            Bind(document, manifest, subtree);
            RefreshExpandedState();
            RefreshPorts();
        }

        public NodeId NodeId { get; }

        public string TypeId { get; private set; }

        public NodeManifest Manifest { get; private set; }

        /// <summary>True for a subtree reference node (ADR-P11-001).</summary>
        internal bool IsSubtreeReference { get; }

        /// <summary>The interface-key rows of a reference card ("key → parent key", "key = value", "key: not connected").</summary>
        internal IReadOnlyList<string> SubtreeRows => _rows.Children().OfType<Label>().Select(label => label.text).ToList();

        /// <summary>True when the card shows its SUBTREE badge.</summary>
        internal bool ShowsSubtreeBadge => !_subtreeBadge.ClassListContains("aibt-node__badge--hidden");

        public Port InputPort { get; }

        public Port OutputPort { get; }

        /// <summary>Text of the card's kind tag, e.g. "MEMORY SEQUENCE" or "ACTION · WAIT".</summary>
        internal string KindText => _kind.text;

        internal string Summary => _summary.text;

        /// <summary>The child-order badge text, or null when the node has no parent.</summary>
        internal string OrderText => _order.ClassListContains("aibt-node__badge--hidden") ? null : _order.text;

        /// <summary>The problem badge tooltip, or null when the node has no reported problem.</summary>
        internal string ProblemText => _problem.ClassListContains("aibt-node__badge--hidden") ? null : _problem.tooltip;

        internal float HighlightStrength => _glow.resolvedStyle.opacity;

        internal Color HighlightColor { get; private set; }

        /// <summary>Updates the card text for a document node whose type did not change.</summary>
        internal void Bind(NodeDocument document, NodeManifest manifest) => Bind(document, manifest, null);

        internal void Bind(NodeDocument document, NodeManifest manifest, SubtreeCardInfo subtree)
        {
            if (document.Subtree != null)
            {
                BindReference(document, subtree);
                return;
            }

            _rows.Clear();
            TypeId = document.TypeId;
            Manifest = manifest;
            var readable = ReadableTitle(document.TypeId);
            title = string.IsNullOrEmpty(document.DisplayName) ? readable : document.DisplayName;
            _title.text = title;
            tooltip = document.TypeId;
            _kind.text = KindTag(manifest, readable, !string.IsNullOrEmpty(document.DisplayName) && document.DisplayName != readable);
            var summary = NodeParameterSummary.Describe(document, manifest);
            _summary.text = summary;
            _summary.tooltip = summary;
            _summary.EnableInClassList("aibt-node__summary--empty", summary.Length == 0);

            var kindClass = manifest == null ? "aibt-node--unresolved" : "aibt-node--" + manifest.Kind.ToString().ToLowerInvariant();
            if (_kindClass != kindClass)
            {
                if (_kindClass != null) RemoveFromClassList(_kindClass);
                AddToClassList(kindClass);
                _kindClass = kindClass;
            }
        }

        private void BindReference(NodeDocument document, SubtreeCardInfo subtree)
        {
            TypeId = null;
            Manifest = null;
            var reference = document.Subtree;
            var name = subtree?.Name;
            title = !string.IsNullOrEmpty(document.DisplayName) ? document.DisplayName
                : !string.IsNullOrEmpty(name) ? name : reference.TreeId.Value;
            _title.text = title;
            _kind.text = "SUBTREE · " + reference.TreeId.Value;
            var state = subtree == null ? null
                : subtree.Status == SubtreeResolutionStatus.NotFound ? "Subtree not found"
                : subtree.Status == SubtreeResolutionStatus.Ambiguous ? "Ambiguous tree ID" : null;
            var summary = state ?? (string.IsNullOrEmpty(document.DisplayName) || string.IsNullOrEmpty(name) ? string.Empty : name);
            _summary.text = summary;
            _summary.tooltip = summary;
            _summary.EnableInClassList("aibt-node__summary--empty", summary.Length == 0);
            tooltip = reference.TreeId.Value + (string.IsNullOrEmpty(subtree?.SourcePath) ? string.Empty : "\n" + subtree.SourcePath)
                      + "\nDouble-click to open the subtree.";

            _rows.Clear();
            foreach (var row in KeyRows(reference, subtree?.Document))
            {
                var label = new Label(row) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("aibt-node__row");
                label.EnableInClassList("aibt-node__row--open", row.EndsWith(": not connected", System.StringComparison.Ordinal));
                _rows.Add(label);
            }

            const string kindClass = "aibt-node--subtree";
            if (_kindClass != kindClass)
            {
                if (_kindClass != null) RemoveFromClassList(_kindClass);
                AddToClassList(kindClass);
                _kindClass = kindClass;
            }
        }

        /// <summary>One row per interface key (the subtree's, plus any the reference binds that the subtree lacks), in ordinal order.</summary>
        internal static IEnumerable<string> KeyRows(SubtreeReference reference, TreeDocument subtree)
        {
            var keys = new SortedSet<string>(System.StringComparer.Ordinal);
            if (subtree?.Interface != null) foreach (var key in subtree.Interface.Keys) keys.Add(key);
            foreach (var binding in reference.Keys) keys.Add(binding.InterfaceKey);
            foreach (var key in keys)
            {
                if (!reference.TryGetBinding(key, out var binding)) yield return key + ": not connected";
                else if (binding.IsConstant) yield return key + " = " + NodeParameterSummary.Format(binding.Constant);
                else yield return key + " → " + binding.ParentKey;
            }
        }

        /// <summary>Shows the 1-based execution position among the parent's children; null hides it.</summary>
        internal void SetOrder(int? order)
        {
            _order.EnableInClassList("aibt-node__badge--hidden", !order.HasValue);
            _order.text = order.HasValue ? order.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            _order.tooltip = order.HasValue ? "Runs " + Ordinal(order.Value) + " among its siblings." : string.Empty;
        }

        /// <summary>Shows a problem badge with the given messages; null hides it.</summary>
        internal void SetProblem(string messages, bool isError)
        {
            var hidden = string.IsNullOrEmpty(messages);
            _problem.EnableInClassList("aibt-node__badge--hidden", hidden);
            _problem.EnableInClassList("aibt-node__problem--warning", !hidden && !isError);
            EnableInClassList("aibt-node--invalid", !hidden && isError);
            _problem.tooltip = hidden ? string.Empty : messages;
        }

        internal void SetRoot(bool isRoot) => EnableInClassList("aibt-node--root", isRoot);

        internal void SetEditable(bool editable)
        {
            capabilities = editable
                ? Capabilities.Selectable | Capabilities.Movable | Capabilities.Deletable | Capabilities.Copiable
                : Capabilities.Selectable | Capabilities.Movable;
            InputPort.SetEnabled(editable);
            OutputPort?.SetEnabled(editable);
        }

        // Node's default menu disconnects ports directly; graph edits go through the window instead.
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
        }

        /// <summary>Paints the execution glow around the card; strength 0 hides it. Never moves the node.</summary>
        internal void SetHighlight(Color color, float strength)
        {
            strength = Mathf.Clamp01(strength);
            HighlightColor = strength > 0f ? color : Color.clear;
            _glow.style.opacity = strength;
            _glow.style.borderTopColor = color;
            _glow.style.borderBottomColor = color;
            _glow.style.borderLeftColor = color;
            _glow.style.borderRightColor = color;
        }

        internal static string ReadableTitle(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return string.Empty;
            var leaf = typeId.Substring(typeId.LastIndexOf('.') + 1).Replace('-', ' ').Replace('_', ' ');
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(leaf);
        }

        internal static string KindTag(NodeManifest manifest, string readableType, bool showType)
        {
            if (manifest == null) return "UNRESOLVED · " + readableType.ToUpperInvariant();
            if (manifest.Kind == NodeBehaviorKind.Composite) return readableType.ToUpperInvariant();
            var kind = manifest.Kind.ToString().ToUpperInvariant();
            return showType ? kind + " · " + readableType.ToUpperInvariant() : kind;
        }

        /// <summary>False for a subtree reference, which never has children; otherwise as for its manifest.</summary>
        internal static bool AcceptsChildren(NodeDocument document, NodeManifest manifest)
            => document?.Subtree == null && AcceptsChildren(manifest);

        internal static bool AcceptsChildren(NodeManifest manifest)
        {
            if (manifest == null)
            {
                return true;
            }

            var maximum = manifest.ChildPolicy.Maximum;
            return !maximum.HasValue || maximum.Value > 0;
        }

        private static Label AddLabel(VisualElement parent, string name, string className)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private static string Ordinal(int value)
        {
            var suffix = value % 100 >= 11 && value % 100 <= 13 ? "th"
                : value % 10 == 1 ? "st" : value % 10 == 2 ? "nd" : value % 10 == 3 ? "rd" : "th";
            return value.ToString(CultureInfo.InvariantCulture) + suffix;
        }
    }
}

namespace AIBT.Editor.Graph
{
    /// <summary>What a reference card shows about its subtree: name, file and how the tree ID resolved.</summary>
    internal sealed class SubtreeCardInfo
    {
        internal SubtreeCardInfo(string name, string sourcePath, SubtreeResolutionStatus status, TreeDocument document)
        {
            Name = name;
            SourcePath = sourcePath;
            Status = status;
            Document = document;
        }

        internal string Name { get; }

        internal string SourcePath { get; }

        internal SubtreeResolutionStatus Status { get; }

        /// <summary>The subtree document when its tree ID resolved to one file; else null.</summary>
        internal TreeDocument Document { get; }
    }
}
