using System;
using System.Collections.Generic;
using AIBT.Agents;
using AIBT.Authoring;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// A behavior tree as a Unity asset. Importing an <c>.aibt</c> file produces one; assign it to a
    /// <see cref="BehaviorTreeAgent"/>. The <c>.aibt</c> file stays the source of truth: this asset
    /// only carries its text into the Player.
    /// </summary>
    public sealed class BehaviorTreeSource : ScriptableObject
    {
        [SerializeField, HideInInspector] private string _json;
        [SerializeField, HideInInspector] private string _sourceId;
        [SerializeField, HideInInspector] private string _treeId;
        [SerializeField, HideInInspector] private string _treeName;
        [SerializeField, HideInInspector] private int _nodeCount;
        [SerializeField, HideInInspector] private string _status;
        [SerializeField, HideInInspector] private string _details;
        [SerializeField, HideInInspector] private string[] _subtreeNodeIds;
        [SerializeField, HideInInspector] private string[] _subtreeNodeSourceIds;

        [NonSerialized] private ManagedTreeProgram _program;
        [NonSerialized] private string _compiledJson;
        [NonSerialized] private string _compileError;
        [NonSerialized] private bool _hasCompiled;
        [NonSerialized] private ManagedNodeSet _nodes;
        [NonSerialized] private ISubtreeResolver _resolver;

        /// <summary>The tree's canonical JSON text.</summary>
        public string Json => _json;

        /// <summary>The project-relative path of the source file, used in diagnostics.</summary>
        public string SourceId => _sourceId;

        public string TreeId => _treeId;

        public string TreeName => _treeName;

        public int NodeCount => _nodeCount;

        /// <summary>Import-time validation summary shown in the inspector.</summary>
        public string Status => _status;

        public string Details => _details;

        /// <summary>Creates a tree asset in memory from JSON text, for trees built or loaded at runtime.</summary>
        public static BehaviorTreeSource FromJson(string json, string name = null)
        {
            var source = CreateInstance<BehaviorTreeSource>();
            source.name = string.IsNullOrEmpty(name) ? "Runtime Tree" : name;
            source.SetContent(json, source.name + ".aibt", null, source.name, 0, null, null);
            return source;
        }

        /// <summary>
        /// Creates a tree asset in memory from JSON text whose subtree references are resolved
        /// through <paramref name="resolver"/> when the tree is first compiled (ADR-P11-001).
        /// </summary>
        public static BehaviorTreeSource FromJson(string json, string name, ISubtreeResolver resolver)
        {
            var source = FromJson(json, name);
            source._resolver = resolver;
            return source;
        }

        /// <summary>
        /// Creates an in-memory tree compiled against an explicit node set instead of the project
        /// catalog, so tests can run node classes that discovery deliberately skips.
        /// </summary>
        internal static BehaviorTreeSource FromJson(string json, string name, ManagedNodeSet nodes)
        {
            var source = FromJson(json, name);
            source._nodes = nodes;
            return source;
        }

        /// <summary>Importer entry point; replaces the content and drops any cached compilation.</summary>
        public void SetContent(string json, string sourceId, string treeId, string treeName, int nodeCount, string status, string details)
        {
            _json = json;
            _sourceId = sourceId;
            _treeId = treeId;
            _treeName = treeName;
            _nodeCount = nodeCount;
            _status = status;
            _details = details;
            _subtreeNodeIds = null;
            _subtreeNodeSourceIds = null;
            _program = null;
            _compiledJson = null;
            _compileError = null;
            _hasCompiled = false;
        }

        /// <summary>
        /// Importer entry point for a tree that uses subtrees: <paramref name="expandedJson"/> is the
        /// canonical JSON of the expanded document (<see cref="SubtreeExpansion.Document"/>), which
        /// compiles without the subtree files, and <paramref name="subtreeNodeSourceIds"/> is
        /// <see cref="SubtreeExpansion.NodeSourceIds"/>, so the compiled debug map names each inner
        /// node's subtree file exactly as compiling the source with a resolver does.
        /// </summary>
        public void SetContent(
            string expandedJson,
            string sourceId,
            string treeId,
            string treeName,
            int nodeCount,
            string status,
            string details,
            IReadOnlyDictionary<NodeId, string> subtreeNodeSourceIds)
        {
            SetContent(expandedJson, sourceId, treeId, treeName, nodeCount, status, details);
            if (subtreeNodeSourceIds == null || subtreeNodeSourceIds.Count == 0) return;
            var ids = new List<string>(subtreeNodeSourceIds.Count);
            foreach (var pair in subtreeNodeSourceIds) ids.Add(pair.Key.Value);
            ids.Sort(StringComparer.Ordinal);
            _subtreeNodeIds = ids.ToArray();
            _subtreeNodeSourceIds = new string[ids.Count];
            for (var index = 0; index < ids.Count; index++)
                _subtreeNodeSourceIds[index] = subtreeNodeSourceIds[new NodeId(ids[index])];
        }

        /// <summary>Compiles once and shares the result with every agent using this asset.</summary>
        internal bool TryGetProgram(out ManagedTreeProgram program, out string error)
        {
            if (!_hasCompiled || !ReferenceEquals(_compiledJson, _json))
            {
                _hasCompiled = true;
                _compiledJson = _json;
                _program = null;
                DiagnosticCollection diagnostics;
                var compiled = _subtreeNodeIds != null && _subtreeNodeIds.Length != 0
                    ? ManagedTreeProgram.TryCompileExpanded(_json, _sourceId, _nodes, SubtreeNodeSources(), out _program, out diagnostics)
                    : ManagedTreeProgram.TryCompile(_json, _sourceId, _nodes, _resolver, out _program, out diagnostics);
                _compileError = compiled ? null : ManagedTreeProgram.Describe(diagnostics);
            }

            program = _program;
            error = _compileError;
            return program != null;
        }

        private Dictionary<NodeId, string> SubtreeNodeSources()
        {
            var result = new Dictionary<NodeId, string>();
            var count = Math.Min(_subtreeNodeIds.Length, _subtreeNodeSourceIds == null ? 0 : _subtreeNodeSourceIds.Length);
            for (var index = 0; index < count; index++)
                if (NodeId.TryParse(_subtreeNodeIds[index], out var id)) result[id] = _subtreeNodeSourceIds[index];
            return result;
        }
    }
}
