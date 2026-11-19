using System;
using System.IO;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Preview;
using AIBT.Editor.Registry;
using AIBT.Editor.Trace;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    public sealed partial class BehaviorTreeGraphWindow
    {
        private const long LiveRefreshMilliseconds = 33;
        private GraphLiveDebugger _live;
        private double _lastBlackboardRefresh;

        internal GraphLiveDebugger LiveDebugger => _live;

        private void PreviewCurrent()
        {
            if (Document == null) return;
            var sourceId = _sourcePath ?? "trees/unsaved.aibt.json";
            if (Path.IsPathRooted(sourceId))
            {
                var relative = FileUtil.GetProjectRelativePath(sourceId);
                sourceId = string.IsNullOrEmpty(relative) ? Path.GetFileName(sourceId) : relative;
            }
            DetachAgent();
            BehaviorTreePreviewWindow.ShowWindow().AttachToGraph(this, sourceId.Replace('\\', '/'));
        }

        // ---------------------------------------------------------------- live debugging

        private void EnableLiveDebugging()
        {
            _live = new GraphLiveDebugger(_view) { MapNode = MapLiveNode };
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += UpdateLive;
        }

        private void DisableLiveDebugging()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= UpdateLive;
            _live?.Detach();
        }

        /// <summary>Debug button: the selected agent (managed) or production host (native) in Play Mode.</summary>
        private void DebugSelected()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Notify("Select a GameObject with a BehaviorTreeAgent, NativeTreeAgent or ProductionTreeHost.");
                return;
            }

            var agent = selected.GetComponent<BehaviorTreeAgent>();
            if (agent != null)
            {
                if (!TryAttachAgent(agent, openMatchingTree: true, out var reason)) Notify(reason);
                return;
            }

            var native = selected.GetComponent<NativeTreeAgent>();
            if (native != null)
            {
                if (!TryAttachNativeAgent(native, openMatchingTree: true, out var nativeReason)) Notify(nativeReason);
                return;
            }

            var host = selected.GetComponent<ProductionTreeHost>();
            if (!TryAttachHost(host, out var explanation)) Notify(explanation);
        }

        /// <summary>
        /// Shows a native agent live on this graph when its compiled program matches the open tree.
        /// With <paramref name="openMatchingTree"/> the asset's recorded source is opened first.
        /// </summary>
        internal bool TryAttachNativeAgent(NativeTreeAgent agent, bool openMatchingTree, out string reason)
        {
            reason = null;
            if (agent == null) { reason = "No NativeTreeAgent selected."; return false; }
            if (!EditorApplication.isPlaying) { reason = "Enter Play Mode to watch " + agent.name + " run."; return false; }
            if (agent.Program == null) { reason = agent.name + " is not running" + (agent.LastError == null ? "." : ": " + agent.LastError); return false; }
            if (!MatchesProgram(agent.Program))
            {
                var path = agent.Tree == null ? null : ProductionTreeAssetStaleness.SourcePath(agent.Tree);
                if (!openMatchingTree || string.IsNullOrEmpty(path) || HasPendingChanges)
                {
                    reason = agent.name + " runs a program that does not match this graph. Open its source"
                        + (string.IsNullOrEmpty(path) ? "" : " (" + path + ")") + ", and prepare the runtime asset again if the source changed.";
                    return false;
                }
                OpenFromPath(path);
                if (!MatchesProgram(agent.Program))
                {
                    reason = agent.name + "'s program is older than " + path + ". Prepare the runtime asset again and restart Play Mode.";
                    return false;
                }
            }

            ShowLive(agent);
            return true;
        }

        /// <summary>Shows the native agent on this graph (the caller checked that it matches).</summary>
        internal void ShowLive(NativeTreeAgent agent)
        {
            ReleaseExecutionView();
            _live.Attach(agent);
            _live.Update(EditorApplication.timeSinceStartup);
            RefreshToolbarState();
            RefreshInspector();
        }

        /// <summary>Shows the managed agent on this graph (the caller checked that it matches).</summary>
        internal void ShowLive(BehaviorTreeAgent agent)
        {
            ReleaseExecutionView();
            _live.Attach(agent);
            _live.Update(EditorApplication.timeSinceStartup);
            RefreshToolbarState();
            RefreshInspector();
        }

        /// <summary>
        /// Shows <paramref name="agent"/> live on this graph. With <paramref name="openMatchingTree"/>
        /// the agent's own tree asset is opened first when this graph shows another tree.
        /// </summary>
        internal bool TryAttachAgent(BehaviorTreeAgent agent, bool openMatchingTree, out string reason)
        {
            reason = null;
            if (agent == null) { reason = "No BehaviorTreeAgent selected."; return false; }
            if (!EditorApplication.isPlaying) { reason = "Enter Play Mode to watch " + agent.name + " run."; return false; }
            if (!GraphLiveDebugger.Matches(agent, Document))
            {
                var path = agent.Tree == null ? null : AssetDatabase.GetAssetPath(agent.Tree);
                if (!openMatchingTree || string.IsNullOrEmpty(path) || HasPendingChanges)
                {
                    reason = agent.name + " runs another tree" + (string.IsNullOrEmpty(path) ? "." : " (" + path + ").");
                    return false;
                }
                OpenFromPath(path);
                if (!GraphLiveDebugger.Matches(agent, Document)) { reason = agent.name + "'s tree could not be opened."; return false; }
            }

            ShowLive(agent);
            return true;
        }

        private void DetachAgent()
        {
            if (_live == null || !_live.IsAttached) return;
            _live.Detach();
            _liveBadge?.EnableInClassList("aibt-live--hidden", true);
            RefreshToolbarState();
            RefreshInspector();
        }

        private void TryAutoAttachSelectedAgent()
        {
            if (!EditorApplication.isPlaying || Document == null || Selection.activeGameObject == null) return;
            var agent = Selection.activeGameObject.GetComponent<BehaviorTreeAgent>();
            if (agent != null && GraphLiveDebugger.Matches(agent, Document) && (_live == null || _live.Agent != agent))
                TryAttachAgent(agent, openMatchingTree: false, out _);
            var native = Selection.activeGameObject.GetComponent<NativeTreeAgent>();
            if (native != null && native.Program != null && MatchesProgram(native.Program) && (_live == null || _live.NativeAgent != native))
                TryAttachNativeAgent(native, openMatchingTree: false, out _);
        }

        private void OnSelectionChanged() => TryAutoAttachSelectedAgent();

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) DetachAgent();
            else if (change == PlayModeStateChange.EnteredPlayMode) TryAutoAttachSelectedAgent();
        }

        private double _lastLiveUpdate;

        private void UpdateLive()
        {
            if (_live == null || !_live.IsAttached) return;
            var now = EditorApplication.timeSinceStartup;
            if ((now - _lastLiveUpdate) * 1000d < LiveRefreshMilliseconds) return;
            _lastLiveUpdate = now;
            _live.Update(now);
            if (!_live.IsAttached)
            {
                DetachAgent();
                return;
            }
            _liveBadge.EnableInClassList("aibt-live--hidden", false);
            _liveText.text = "Live: " + _live.Describe();
            if (now - _lastBlackboardRefresh > 0.2d)
            {
                _lastBlackboardRefresh = now;
                RefreshLiveBlackboard();
            }
            _view.MarkDirtyRepaint();
            Repaint();
        }

        // ---------------------------------------------------------------- native host trace

        internal bool TryAttachHost(ProductionTreeHost host, out string explanation)
        {
            if (!SelectedHostTraceMenu.TryCreateSession(host, out var session, out explanation)) return false;
            if (!MatchesProgram(host.Program))
            {
                explanation = "This graph does not match the selected host's compiled program. Open the matching source or prepare and restart the updated program. Raw trace remains available through Attach Selected Host Trace.";
                return false;
            }
            DetachAgent();
            TraceTimelineWindow.ShowWindow().AttachToGraph(this, session, host.Program);
            return true;
        }

        /// <summary>
        /// True when <paramref name="program"/> was compiled from this graph: from the document's
        /// subtree expansion when it has references (ADR-P11-001), and, in a subtree window, when
        /// the program contains this window's instance.
        /// </summary>
        internal bool MatchesProgram(CompiledProgram program)
        {
            if (Document == null || program == null) return false;
            if (InstancePrefix.Length != 0)
                return SubtreeGraphEdits.InstanceMatches(Document, InstancePrefix,
                    new System.Collections.Generic.HashSet<string>(program.DebugMap.Select(entry => entry.AuthoringNodeId.Value), StringComparer.Ordinal));
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            var document = Document;
            if (SubtreeExpander.RequiresExpansion(document))
            {
                var expansion = ProjectTreeIndex.Expand(SubtreeGraphEdits.EditorDocumentId, document, _registry ?? catalog.Registry,
                    catalog.RegisteredTypes, CurrentSubtreeResolver());
                if (!expansion.Success) return false;
                document = expansion.Document;
            }
            var serialized = AIBT.Authoring.CanonicalTreeJson.Serialize(document, catalog.RegisteredTypes);
            if (!serialized.Success) return false;
            var hash = program.Header.CanonicalSemanticHash.HexadecimalValue;
            var semanticHash = BitConverter.ToString(serialized.SemanticHash).Replace("-", "").ToLowerInvariant();
            if (hash != StableHash.Sha256Hex(serialized.Utf8) && hash != semanticHash) return false;
            var ids = new System.Collections.Generic.HashSet<NodeId>(document.Nodes.Select(node => node.Id));
            return ids.Count == program.DebugMap.Count && ids.SetEquals(program.DebugMap.Select(entry => entry.AuthoringNodeId))
                && program.DebugMap.Single(entry => entry.RuntimeNodeIndex == program.Header.RootNodeIndex).AuthoringNodeId == document.Root;
        }

        // ---------------------------------------------------------------- native check and runtime asset

        /// <summary>Compiles the current tree for the native runtime and shows the result under the graph.</summary>
        internal void CheckNative()
        {
            if (_session == null) return;
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            if (catalog.Problems.Count > 0)
            {
                _nativeMessage = "Native compilation unavailable: " + string.Join("\n", catalog.Problems);
            }
            else
            {
                var result = GeneratedCompiledProgramV2Compiler.Compile(_session.Document, catalog.Generated.Values, catalog.RegisteredTypes,
                    new ReferenceCompilerOptions("trees/editor.aibt.json", ReferenceCompilationPolicy.Phase1,
                        new CompiledCompilerVersion(1, 0, 0, 5)), CurrentSubtreeResolver());
                _nativeMessage = result.Success
                    ? "Native program compiled. Player dispatch and game integration still require verification."
                    : "Native compilation: " + string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
            }
            RefreshProblems();
        }

        private void BakeRuntimeAsset()
        {
            var types = ProjectNodeCatalog.RuntimeCatalogTypes().ToArray();
            if (types.Length == 0)
            {
                ShowBanner("Create a generated catalog for your project's node shard before preparing a runtime asset.", BannerKind.Info, ("OK", HideBanner));
                return;
            }
            var menu = new GenericMenu();
            foreach (var type in types)
            {
                var selected = type;
                menu.AddItem(new GUIContent(type.FullName), false, () => BakeRuntimeAsset(selected));
            }
            menu.ShowAsContext();
        }

        private void BakeRuntimeAsset(Type catalogType)
        {
            if (_session == null || string.IsNullOrEmpty(_sourcePath)) return;
            if (_session.IsDirty)
            {
                ShowBanner("Save the tree before preparing its runtime asset.", BannerKind.Info, ("Save", () => SaveAll()), ("Cancel", HideBanner));
                return;
            }
            ProductionTreeAsset asset = null;
            try
            {
                var sourceId = _sourcePath.Replace('\\', '/');
                if (Path.IsPathRooted(sourceId))
                {
                    sourceId = FileUtil.GetProjectRelativePath(sourceId);
                    if (string.IsNullOrEmpty(sourceId)) throw new InvalidOperationException("Save the source inside the Unity project before baking.");
                }
                asset = ProductionTreeAssetBaker.Create(Document, sourceId, catalogType, out var diagnostics);
                if (asset == null)
                {
                    ShowBanner("Cannot prepare runtime asset: " + string.Join("\n", diagnostics.Select(d => d.Code.Value + ": " + d.Message)),
                        BannerKind.Error, ("OK", HideBanner));
                    return;
                }
                var path = EditorUtility.SaveFilePanelInProject("Prepare AIBT runtime asset",
                    Path.GetFileNameWithoutExtension(_sourcePath) + ".runtime", "asset", "Choose the compiled runtime asset location.");
                if (string.IsNullOrEmpty(path)) return;
                var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (existing != null)
                {
                    if (!(existing is ProductionTreeAsset)) throw new InvalidOperationException("The destination is not an AIBT runtime asset.");
                    EditorUtility.CopySerialized(asset, existing);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(asset, path);
                    existing = asset;
                    asset = null;
                }
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(existing);
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException)
            {
                ShowBanner("Cannot prepare runtime asset: " + exception.Message, BannerKind.Error, ("OK", HideBanner));
            }
            finally
            {
                if (asset != null) DestroyImmediate(asset);
            }
        }
    }
}
