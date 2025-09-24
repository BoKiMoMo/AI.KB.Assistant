using System;
using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    public class ActionBatch
    {
        public string Name { get; set; } = "";
        public Action Do { get; set; } = () => { };
        public Action Undo { get; set; } = () => { };
    }

    public class UndoStack
    {
        private readonly Stack<ActionBatch> _undos = new();
        private readonly Stack<ActionBatch> _redos = new();

        public void Push(ActionBatch b)
        {
            _undos.Push(b);
            _redos.Clear();
            b.Do();
        }

        public bool CanUndo => _undos.Count > 0;
        public bool CanRedo => _redos.Count > 0;

        public void Undo()
        {
            if (!CanUndo) return;
            var b = _undos.Pop();
            b.Undo();
            _redos.Push(b);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var b = _redos.Pop();
            b.Do();
            _undos.Push(b);
        }

        public void Clear()
        {
            _undos.Clear();
            _redos.Clear();
        }
    }
}
