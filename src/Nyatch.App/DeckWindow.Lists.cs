using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Nyatch.Core;

namespace Nyatch.App;

// The Notes page: to-do lists and notes together. The left column lists both; the right shows the one
// picked. The island draws it (native) with its own text boxes; this works out what is on it, keeps
// what is typed, and does what its clicks ask.
internal sealed partial class DeckWindow
{
    // The island's text boxes on this page.
    private const int NoteBox = 10, TodoBox = 11, RenameBox = 12;

    /// <summary>What the Notes page shows on its right: a to-do list or a note, by id.</summary>
    private (bool IsList, string? Id) _pick = (true, null);

    /// <summary>The list or note being renamed in the left column (by id), and the name typed so far.</summary>
    private string? _renaming;
    private string _renameDraft = "";

    /// <summary>A to-do typed but not added yet.</summary>
    private string _todoDraft = "";

    // What each click does, by the action number the island sends back; rebuilt with the data.
    private readonly List<(Action Click, Action? RightClick)> _noteActions = [];

    private void SaveSoon()
    {
        _saveSoon.Stop();
        _saveSoon.Start();
        UpdateStatus();
    }

    private TodoList? PickedList() => _pick.IsList ? _deck.List(_pick.Id ?? "") ?? _deck.Lists.FirstOrDefault() : null;
    private Note? PickedNote() => _pick.IsList ? null : _deck.Pages.Find(n => n.Id == _pick.Id);

    /// <summary>The Notes page for the island (see its DrawDeckNotes).</summary>
    private string NotesData()
    {
        _noteActions.Clear();
        int Act(Action click, Action? rightClick = null)
        {
            _noteActions.Add((click, rightClick));
            return _noteActions.Count - 1;
        }
        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
        var records = new List<string>();
        void Add(params object[] fields) => records.Add(string.Join('\u001F', fields.Select(f => f switch
        {
            bool b => b ? "1" : "0",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => Clean(f.ToString() ?? ""),
        })));

        // A note that has been deleted falls back to the first list.
        if (!_pick.IsList && PickedNote() is null) _pick = (true, null);
        if (_renaming is not null && _deck.List(_renaming) is null && _deck.Pages.All(n => n.Id != _renaming)) _renaming = null;

        // ----- Left: the to-do lists, then the notes -----
        Add("S", "TO-DOS", Act(() =>
        {
            var list = _deck.AddList("");
            _pick = (true, list.Id);
            StartRename(list);
            SaveSoon();
        }));
        var pickedList = PickedList();
        foreach (var list in _deck.Lists)
        {
            if (list.Id == _renaming)
            {
                Add("W", _renameDraft, "List name", list.Id);
                continue;
            }
            var open = list.Items.Count(t => !t.Done);
            var select = Act(() =>
            {
                _pick = (true, list.Id);
                SendNativeData();
            }, () => StartRename(list));
            var rename = Act(() => StartRename(list));
            Add("R", list.Name, open > 0 ? open.ToString(CultureInfo.CurrentCulture) : "", ReferenceEquals(list, pickedList), select, rename, Act(() => DeleteList(list)));
        }

        Add("S", "NOTES", Act(() =>
        {
            // Reuse an empty note rather than piling them up.
            var blank = _deck.Pages.Find(n => n.Text.Trim().Length == 0) ?? _deck.AddNote(DateTime.Now);
            _pick = (false, blank.Id);
            SendNativeData();
            Island.DeckFocusField(NoteBox);
        }));
        var pickedNote = PickedNote();
        if (_deck.Pages.Count == 0) Add("E", "No notes yet");
        foreach (var note in _deck.Pages)
        {
            if (note.Id == _renaming)
            {
                Add("W", _renameDraft, "Note name", note.Id);
                continue;
            }
            // A right-click or a double-click renames it, as a list.
            var select = Act(() =>
            {
                _pick = (false, note.Id);
                SendNativeData();
            }, () => StartRename(note));
            var rename = Act(() => StartRename(note));
            Add("R", note.Title, When(note.Changed), ReferenceEquals(note, pickedNote), select, rename, Act(() => DeleteNote(note)));
        }

        // ----- Right: a to-do list, or a note -----
        if (pickedList is { } shown)
        {
            var all = shown.Items;
            var left = all.Count(t => !t.Done);
            // No button in the head: each to-do is removed on its own, and a list from its row in the left column.
            Add("L", shown.Name, all.Count == 0 ? "" : left == 0 ? "all done" : $"{left} left", -1, Act(() => StartRename(shown)));
            Add("A", _todoDraft);
            if (all.Count == 0) Add("X", "Nothing on this list yet.");

            // In the order they were added: checking one off leaves it where it is.
            foreach (var todo in all)
            {
                var toggle = Act(() =>
                {
                    _deck.Toggle(shown.Id, todo.Id);
                    SaveSoon();
                    SendNativeData();
                });
                var remove = Act(() =>
                {
                    _deck.Remove(shown.Id, todo.Id);
                    SaveSoon();
                    SendNativeData();
                });
                Add("T", todo.Text, todo.Done, toggle, remove);
            }
        }
        else if (pickedNote is { } note)
        {
            // The note's id tells the island's text box which note it is editing (it takes another one's text when it changes).
            Add("N", "Edited " + When(note.Changed), Act(() => DeleteNote(note)), note.Text, note.Id);
        }
        return string.Join('\u001E', records);
    }

    private void StartRename(TodoList list) => StartRename(list.Id, list.Name);

    /// <summary>A note's name is its first line; an empty note starts with none.</summary>
    private void StartRename(Note note) => StartRename(note.Id, note.Text.Trim().Length == 0 ? "" : note.Title);

    private void StartRename(string id, string name)
    {
        _renaming = id;
        _renameDraft = name;
        SendNativeData();
        Island.DeckFocusField(RenameBox);
    }

    /// <summary>Renaming a note changes its first line; the rest of it stays as it is.</summary>
    private void RenameNote(Note note, string name)
    {
        name = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (name.Length == 0) return;
        var text = note.Text.TrimStart();
        var end = text.IndexOf('\n');
        _deck.SetNote(note.Id, name + (end >= 0 ? text[end..] : ""), DateTime.Now);
    }

    // Deleted at once: a confirmation box opened behind the notch, where it could not be seen, so a click
    // on the bin seemed to do nothing.
    private void DeleteList(TodoList list)
    {
        _deck.RemoveList(list.Id);
        SaveSoon();
        SendNativeData();
    }

    private void DeleteNote(Note note)
    {
        _deck.RemoveNote(note.Id);
        SaveSoon();
        SendNativeData();
    }

    /// <summary>A click (or, with 10000 added, a right-click) on the Notes page, from the island.</summary>
    private void NotesAction(int action)
    {
        var right = action >= 10000;
        var at = right ? action - 10000 : action;
        if (at < 0 || at >= _noteActions.Count) return;
        var (click, rightClick) = _noteActions[at];
        if (right) rightClick?.Invoke();
        else click();
    }

    /// <summary>
    /// A text box on the Notes page, from the island: 0 its text changed, 1 Enter, 2 Esc, 7 typing ended.
    /// </summary>
    private void NotesEdit(int box, int what)
    {
        switch (box)
        {
            case NoteBox when what == 0 && PickedNote() is { } note:
                _deck.SetNote(note.Id, Island.DeckEditText(), DateTime.Now);
                SaveSoon();
                SendNativeData();  // the left column follows the title (the first line)
                break;
            case TodoBox when what == 0:
                _todoDraft = Island.DeckEditText();
                break;
            case TodoBox when what == 1:
                var text = Island.DeckEditText(submitted: true);
                _todoDraft = "";
                if (text.Trim().Length == 0 || PickedList() is not { } list) break;
                _deck.Add(list.Id, text);
                SaveSoon();
                SendNativeData();
                break;
            case RenameBox when what == 0:
                _renameDraft = Island.DeckEditText();
                break;
            case RenameBox when what is 1 or 7 && _renaming is { } id:
            {
                // Enter, or a click elsewhere, keeps the name; Esc does not.
                _renaming = null;
                var name = what == 1 ? Island.DeckEditText(submitted: true) : _renameDraft;
                if (_deck.Pages.Find(n => n.Id == id) is { } renamed) RenameNote(renamed, name);
                else _deck.RenameList(id, name);
                SaveSoon();
                SendNativeData();
                break;
            }
            case RenameBox when what == 2:
                _renaming = null;
                SendNativeData();
                break;
        }
    }

    /// <summary>"14:05" today, "Yesterday", or "20 Nov".</summary>
    private static string When(DateTime time)
    {
        var days = (DateTime.Today - time.Date).Days;
        return days == 0 ? time.ToString("t", CultureInfo.CurrentCulture) : days == 1 ? "Yesterday" : time.ToString("d MMM", CultureInfo.CurrentCulture);
    }
}
