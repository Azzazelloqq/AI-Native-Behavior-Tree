using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Bridges the graph window's own history into Unity's undo stack, so Edit &gt; Undo, Ctrl+Z and
    /// Ctrl+Y step graph edits. Unity records only <see cref="Position"/>; after Unity restores it,
    /// the window steps its history until the positions match.
    /// </summary>
    internal sealed class GraphUndoMarker : ScriptableObject
    {
        [SerializeField] internal int Position;
    }
}
