using System;
using System.Collections.Generic;
using Paril.Components;

namespace MCSkin3D
{
    // One timeline for the editor. Document identity survives renaming and moving.
    public sealed class EditorHistory
    {
        public sealed class Entry
        {
            public Skin Document { get; }
            public IUndoable Change { get; }
            public string Action => Document.Name + ": " + Change.Action;
            internal Entry(Skin document, IUndoable change) { Document = document; Change = change; }
        }

        private readonly List<Entry> entries = new List<Entry>();
        private int cursor;
        public bool CanUndo => cursor > 0;
        public bool CanRedo => cursor < entries.Count;
        public IEnumerable<Entry> UndoList => entries.GetRange(0, cursor);
        public IEnumerable<Entry> RedoList
        {
            get { for (int i = entries.Count - 1; i >= cursor; --i) yield return entries[i]; }
        }

        public void Add(Skin document, IUndoable change)
        {
            document.EnsureEditable();
            entries.RemoveRange(cursor, entries.Count - cursor);
            entries.Add(new Entry(document, change));
            cursor = entries.Count;
        }

        public void Undo(Action<Skin> activate)
        {
            if (!CanUndo) return;
            var entry = entries[cursor - 1];
            activate(entry.Document);
            entry.Change.Undo(entry.Document);
            entry.Document.Dirty = true;
            --cursor;
        }

        public void Redo(Action<Skin> activate)
        {
            if (!CanRedo) return;
            var entry = entries[cursor];
            activate(entry.Document);
            entry.Change.Redo(entry.Document);
            entry.Document.Dirty = true;
            ++cursor;
        }

        public void Remove(Skin document)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
                if (ReferenceEquals(entries[i].Document, document))
                {
                    entries.RemoveAt(i);
                    if (i < cursor) --cursor;
                }
        }
    }
}
