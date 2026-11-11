using System;
using AIBT.Agents;
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

        [NonSerialized] private ManagedTreeProgram _program;
        [NonSerialized] private string _compiledJson;
        [NonSerialized] private string _compileError;
        [NonSerialized] private bool _hasCompiled;

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
            _program = null;
            _compiledJson = null;
            _compileError = null;
            _hasCompiled = false;
        }

        /// <summary>Compiles once and shares the result with every agent using this asset.</summary>
        internal bool TryGetProgram(out ManagedTreeProgram program, out string error)
        {
            if (!_hasCompiled || !ReferenceEquals(_compiledJson, _json))
            {
                _hasCompiled = true;
                _compiledJson = _json;
                _program = null;
                _compileError = ManagedTreeProgram.TryCompile(_json, _sourceId, null, out _program, out var diagnostics)
                    ? null
                    : ManagedTreeProgram.Describe(diagnostics);
            }

            program = _program;
            error = _compileError;
            return program != null;
        }
    }
}
