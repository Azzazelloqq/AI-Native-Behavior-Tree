using System;
using System.Collections.Generic;
using AIBT.Authoring;

namespace AIBT.Editor.Editing
{
    /// <summary>
    /// Undo/redo for semantic edits, as a snapshot stack over <see cref="TreeDocument"/>
    /// instances accepted by <see cref="SemanticEditTransaction"/> -- mirrors
    /// AIBT.Editor.Organization.LayoutHistory's shape for the same reason: every accepted edit
    /// already produces a distinct document, so undo/redo is just moving a cursor through them.
    /// </summary>
    public sealed class SemanticEditHistory
    {
        private readonly List<TreeDocument> _undoStack = new List<TreeDocument>();
        private readonly List<TreeDocument> _redoStack = new List<TreeDocument>();

        public SemanticEditHistory(TreeDocument initial)
        {
            Current = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public TreeDocument Current { get; private set; }

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Records an accepted edit, making <paramref name="next"/> current and clearing redo.</summary>
        public void Do(TreeDocument next)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            _undoStack.Add(Current);
            _redoStack.Clear();
            Current = next;
        }

        public TreeDocument Undo()
        {
            if (!CanUndo)
            {
                throw new InvalidOperationException("Nothing to undo.");
            }

            _redoStack.Add(Current);
            Current = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            return Current;
        }

        public TreeDocument Redo()
        {
            if (!CanRedo)
            {
                throw new InvalidOperationException("Nothing to redo.");
            }

            _undoStack.Add(Current);
            Current = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            return Current;
        }
    }
}
