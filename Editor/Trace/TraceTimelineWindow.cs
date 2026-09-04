using System;
using System.Collections.Generic;
using AIBT.Authoring;
using AIBT.Editor.Debugger;
using AIBT.Editor.Graph;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Trace
{
    /// <summary>
    /// Visualizes the execution trace read by P3-010's <see cref="NativeExecutionDebuggerSession"/>:
    /// a scrubbable step timeline, live/scrubbed active-node highlighting on a private
    /// <see cref="BehaviorTreeGraphView"/> instance (P3-003's read-only adapter, consumed as-is, not
    /// modified), and a diagnostic-event list correlated to the step/node that produced each one.
    /// This window never reads the trace channel itself and never changes P3-010's attach/read
    /// protocol -- it only calls <see cref="NativeExecutionDebuggerSession.TryReadTrace"/> and
    /// replays the resulting view via <see cref="TraceTimelineModel"/>.
    /// </summary>
    public sealed class TraceTimelineWindow : EditorWindow
    {
        private static readonly Color ActiveNodeColor = new Color(0.20f, 0.75f, 0.95f);
        private const float MaxBorderWidth = 3f;
        private const double AnimationRatePerSecond = 1000d / 200d; // ~200ms full fade, per the card's own disclosed first-pass minimum.
        private const double LiveRefreshIntervalSeconds = 0.1d;

        private NativeExecutionDebuggerSession _session;
        private TreeDocument _document;
        private NodeRegistry _registry;
        private IReadOnlyDictionary<uint, NodeId> _nodeIdByRuntimeIndex = new Dictionary<uint, NodeId>();

        private BehaviorTreeGraphView _graphView;
        private Label _statusLabel;
        private Label _droppedBanner;
        private SliderInt _scrubSlider;
        private Label _scrubLabel;
        private ScrollView _diagnosticsView;

        private TraceTimelineModel _model = TraceTimelineModel.Empty;
        private int _scrubStepIndex = -1;

        private HashSet<NodeId> _targetActiveNodeIds = new HashSet<NodeId>();
        private readonly Dictionary<NodeId, float> _highlightAlphaByNode = new Dictionary<NodeId, float>();
        private double _lastAnimationTickTime;
        private double _lastLiveRefreshTime;

        [MenuItem("AIBT/Trace Timeline")]
        public static TraceTimelineWindow ShowWindow()
        {
            var window = GetWindow<TraceTimelineWindow>();
            window.titleContent = new GUIContent("AIBT Trace");
            return window;
        }

        /// <summary>Attaches to a caller-owned debugger session. Never creates or mutates the session/channel.</summary>
        public void AttachSession(NativeExecutionDebuggerSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>The timeline built from the last <see cref="Refresh"/>.</summary>
        public TraceTimelineModel CurrentModel => _model;

        /// <summary>The step index currently scrubbed to, or -1 if there is no history.</summary>
        public int CurrentStepIndex => _scrubStepIndex;

        /// <summary>
        /// Supplies the graph/node-identity context needed to translate the channel's raw runtime
        /// node indices into <see cref="NodeId"/>s for highlighting on the graph adapter.
        /// </summary>
        public void LoadGraphContext(TreeDocument document, NodeRegistry registry, CompiledProgram compiledProgram)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (compiledProgram == null) throw new ArgumentNullException(nameof(compiledProgram));

            var map = new Dictionary<uint, NodeId>();
            foreach (var entry in compiledProgram.DebugMap)
            {
                map[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            }

            _nodeIdByRuntimeIndex = map;
            Refresh();
        }

        private void OnEnable()
        {
            BuildLayout();
            _lastAnimationTickTime = EditorApplication.timeSinceStartup;
            _lastLiveRefreshTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        /// <summary>
        /// Drives two independent things every Editor tick: (a) auto-refresh while a session is
        /// attached and the Editor is actually in Play mode -- this is what makes highlighting
        /// "live" rather than requiring a manual Refresh click; (b) the active-highlight fade
        /// animation, which always advances regardless of live-refresh state so scrubbing still
        /// animates smoothly.
        /// </summary>
        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            if (_session != null && _session.IsAttached && EditorApplication.isPlaying
                && now - _lastLiveRefreshTime >= LiveRefreshIntervalSeconds)
            {
                _lastLiveRefreshTime = now;
                Refresh();
            }

            AdvanceHighlightAnimation(now);
        }

        private void BuildLayout()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(() => Refresh()) { text = "Refresh" });
            rootVisualElement.Add(toolbar);

            _statusLabel = new Label("No session attached.") { style = { paddingLeft = 4, paddingTop = 2, paddingBottom = 2 } };
            rootVisualElement.Add(_statusLabel);

            _droppedBanner = new Label { style = { paddingLeft = 4, paddingTop = 2, paddingBottom = 2, display = DisplayStyle.None, backgroundColor = new Color(0.55f, 0.15f, 0.15f), color = Color.white } };
            rootVisualElement.Add(_droppedBanner);

            var scrubRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            _scrubSlider = new SliderInt(0, 0) { style = { flexGrow = 1, marginLeft = 4, marginRight = 4 } };
            _scrubSlider.RegisterValueChangedCallback(evt => Scrub(evt.newValue));
            _scrubLabel = new Label("step -/-") { style = { minWidth = 90 } };
            scrubRow.Add(_scrubSlider);
            scrubRow.Add(_scrubLabel);
            rootVisualElement.Add(scrubRow);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            rootVisualElement.Add(body);

            _graphView = new BehaviorTreeGraphView { name = "aibt-trace-graph-view" };
            _graphView.style.flexGrow = 1;
            body.Add(_graphView);

            _diagnosticsView = new ScrollView { style = { width = 320 } };
            body.Add(_diagnosticsView);
        }

        /// <summary>Reads the current trace snapshot and rebuilds the timeline, scrubbed to the latest step.</summary>
        public void Refresh()
        {
            if (_session == null || !_session.IsAttached)
            {
                _statusLabel.text = "No session attached.";
                _model = TraceTimelineModel.Build(default);
                _scrubStepIndex = -1;
                RefreshScrubControl();
                RefreshDiagnostics();
                return;
            }

            if (!_session.TryReadTrace(out var view, out var failure))
            {
                _statusLabel.text = "Channel not readable right now: " + failure.Code;
                return;
            }

            _model = TraceTimelineModel.Build(view);
            _scrubStepIndex = _model.Steps.Count - 1;

            _droppedBanner.style.display = _model.HasDroppedEvents || _model.IsFaulted ? DisplayStyle.Flex : DisplayStyle.None;
            _droppedBanner.text = _model.IsFaulted
                ? "Channel faulted -- trace is incomplete."
                : "Channel full / events dropped (" + _model.DroppedCount + ") -- trace is not the complete history.";

            _statusLabel.text = "steps=" + _model.Steps.Count + " diagnostics=" + _model.Diagnostics.Count;

            RefreshScrubControl();
            RefreshDiagnostics();
            ApplyHighlight(_scrubStepIndex);
        }

        private void RefreshScrubControl()
        {
            var maxIndex = Math.Max(0, _model.Steps.Count - 1);
            _scrubSlider.highValue = maxIndex;
            _scrubSlider.SetValueWithoutNotify(Math.Max(0, _scrubStepIndex));
            _scrubLabel.text = _model.Steps.Count == 0
                ? "step -/-"
                : "step " + (_scrubStepIndex + 1) + "/" + _model.Steps.Count;
        }

        private void Scrub(int stepIndex)
        {
            _scrubStepIndex = Math.Max(0, Math.Min(stepIndex, _model.Steps.Count - 1));
            _scrubLabel.text = _model.Steps.Count == 0
                ? "step -/-"
                : "step " + (_scrubStepIndex + 1) + "/" + _model.Steps.Count;
            ApplyHighlight(_scrubStepIndex);
        }

        /// <summary>
        /// Sets which nodes are the current highlight target -- does not itself paint any border.
        /// <see cref="AdvanceHighlightAnimation"/> (driven every Editor tick by
        /// <see cref="OnEditorUpdate"/>) animates each node's own border toward this target, so a
        /// node lights up/fades out over ~200ms rather than snapping instantly.
        /// </summary>
        private void ApplyHighlight(int stepIndex)
        {
            // Rebuilding the graph on every highlight (including every live-mode auto-refresh tick)
            // is the existing, pre-P7-027 behavior -- not made worse here, just called more often in
            // live mode. Avoiding the rebuild (and preserving pan/zoom) is P7-025's own scope.
            if (_document != null && _registry != null)
            {
                _graphView.Populate(_document, _registry);
            }

            var activeRuntimeIndices = _model.ActiveRuntimeNodeIndicesAtStep(stepIndex);
            _targetActiveNodeIds = new HashSet<NodeId>();
            foreach (var runtimeIndex in activeRuntimeIndices)
            {
                if (_nodeIdByRuntimeIndex.TryGetValue(runtimeIndex, out var nodeId))
                {
                    _targetActiveNodeIds.Add(nodeId);
                }
            }

            // The graph was just repopulated with fresh VisualElements -- apply each node's own
            // already-tracked alpha immediately so a still-active node doesn't visually reset to
            // zero on every rebuild; AdvanceHighlightAnimation continues animating it from there.
            foreach (var pair in _graphView.NodesById)
            {
                _highlightAlphaByNode.TryGetValue(pair.Key, out var alpha);
                SetBorder(pair.Value, alpha);
            }
        }

        /// <summary>Advances every currently-graphed node's highlight alpha toward 1 (in <see cref="_targetActiveNodeIds"/>) or 0, and paints its border.</summary>
        private void AdvanceHighlightAnimation(double now)
        {
            var delta = (float)Math.Max(0d, now - _lastAnimationTickTime) * (float)AnimationRatePerSecond;
            _lastAnimationTickTime = now;
            if (_graphView == null)
            {
                return;
            }

            foreach (var pair in _graphView.NodesById)
            {
                var target = _targetActiveNodeIds.Contains(pair.Key) ? 1f : 0f;
                _highlightAlphaByNode.TryGetValue(pair.Key, out var alpha);
                alpha = Mathf.MoveTowards(alpha, target, delta);
                _highlightAlphaByNode[pair.Key] = alpha;
                SetBorder(pair.Value, alpha);
            }
        }

        private static void SetBorder(VisualElement element, float alpha)
        {
            SetBorder(element, Color.Lerp(Color.clear, ActiveNodeColor, alpha), Mathf.Lerp(0f, MaxBorderWidth, alpha));
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        private void RefreshDiagnostics()
        {
            _diagnosticsView.Clear();
            foreach (var diagnostic in _model.Diagnostics)
            {
                var nodeText = diagnostic.Record.RuntimeNodeIndex == CompiledIndex.Invalid
                    ? "(no node)"
                    : "node " + diagnostic.Record.RuntimeNodeIndex;
                _diagnosticsView.Add(new Label(
                    "step " + (diagnostic.StepIndex + 1) + " -- code " + diagnostic.Record.DiagnosticCodeNumber + " -- " + nodeText));
            }
        }
    }
}
