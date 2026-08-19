using System;
using System.Collections.Generic;
using AIBT.Editor.Layout;

namespace AIBT.Editor.Organization
{
    /// <summary>
    /// Undo/redo for manual organization actions, as a snapshot stack over immutable
    /// <see cref="LayoutDocument"/> instances -- every <see cref="LayoutOrganizationOperations"/>
    /// call returns a new document, so undo/redo is just moving a cursor through prior snapshots.
    /// </summary>
    public sealed class LayoutHistory
    {
        private readonly List<LayoutDocument> _undoStack = new List<LayoutDocument>();
        private readonly List<LayoutDocument> _redoStack = new List<LayoutDocument>();

        public LayoutHistory(LayoutDocument initial)
        {
            Current = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public LayoutDocument Current { get; private set; }

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Records a completed action, making <paramref name="next"/> current and clearing redo.</summary>
        public void Do(LayoutDocument next)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            _undoStack.Add(Current);
            _redoStack.Clear();
            Current = next;
        }

        public LayoutDocument Undo()
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

        public LayoutDocument Redo()
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
