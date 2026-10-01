using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>Importer-owned Editor metadata. The .aibt source is the only authoring authority.</summary>
    public sealed class BehaviorTreeAsset : ScriptableObject
    {
        [SerializeField] private string _treeId;
        [SerializeField] private string _treeName;
        [SerializeField] private string[] _tags;
        [SerializeField] private int _nodeCount;
        [SerializeField] private string _status;
        [SerializeField] private string _details;
        public string TreeId => _treeId;
        public string TreeName => _treeName;
        public string Status => _status;
        public string Details => _details;
        public int NodeCount => _nodeCount;

        internal void Initialize(TreeCatalogEntry entry)
        {
            _treeId = entry.TreeId;
            _treeName = entry.Name;
            _tags = entry.Tags;
            _nodeCount = entry.NodeCount;
            _status = entry.Status;
            _details = entry.Details;
        }
    }
}
