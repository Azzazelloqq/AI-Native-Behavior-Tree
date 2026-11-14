using System;
using System.Collections.Generic;

namespace AIBT.Editor.Graph
{
    internal enum GraphEditKind : byte
    {
        Semantic,
        Layout,
    }

    /// <summary>
    /// One undo order over the two independent histories the window keeps: semantic edits
    /// (<see cref="GraphEditSession"/>) and layout moves (<c>LayoutHistory</c>). Each entry only
    /// says which history to step, so the documents themselves stay in their own histories.
    /// </summary>
    internal sealed class GraphEditHistory
    {
        private readonly List<GraphEditKind> _undo = new List<GraphEditKind>();
        private readonly List<GraphEditKind> _redo = new List<GraphEditKind>();

        /// <summary>Number of edits that can be undone; matches the undo marker Unity stores.</summary>
        internal int Position => _undo.Count;

        internal bool CanUndo => _undo.Count > 0;

        internal bool CanRedo => _redo.Count > 0;

        internal void Record(GraphEditKind kind)
        {
            _undo.Add(kind);
            _redo.Clear();
        }

        internal GraphEditKind Undo()
        {
            if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
            var kind = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(kind);
            return kind;
        }

        internal GraphEditKind Redo()
        {
            if (!CanRedo) throw new InvalidOperationException("Nothing to redo.");
            var kind = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(kind);
            return kind;
        }

        internal void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
