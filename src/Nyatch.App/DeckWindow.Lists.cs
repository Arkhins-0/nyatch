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

    /// <summary>The list being renamed in the left column, and the name typed so far.</summary>
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
        if (_renaming is not null && _deck.List(_renaming) is null) _renaming = null;

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
                Add("W", _renameDraft);
                continue;
            }
            var open = list.Items.Count(t => !t.Done);
            var select = Act(() =>
            {
                _pick = (true, list.Id);
                SendNativeData();
            }, () => DeleteList(list));
            var rename = Act(() => StartRename(list));
            Add("R", list.Name, open > 0 ? open.ToString(CultureInfo.CurrentCulture) : "", ReferenceEquals(list, pickedList), select, rename);
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
            var select = Act(() =>
            {
                _pick = (false, note.Id);
                SendNativeData();
            }, () => DeleteNote(note));
            Add("R", note.Title, When(note.Changed), ReferenceEquals(note, pickedNote), select, -1);
        }

        // ----- Right: a to-do list, or a note -----
        if (pickedList is { } shown)
        {
            var all = shown.Items;
            var left = all.Count(t => !t.Done);
            var tidy = all.Any(t => t.Done)
                ? Act(() =>
                {
                    _deck.ClearDone(shown.Id);
                    SaveSoon();
                    SendNativeData();
                })
                : -1;
            Add("L", shown.Name, all.Count == 0 ? "" : left == 0 ? "all done" : $"{left} left", tidy, Act(() => StartRename(shown)));
            Add("A", _todoDraft);
            if (all.Count == 0) Add("X", "Nothing on this list yet.");
            // Still to do first, then the done ones.
            foreach (var todo in all.Where(t => !t.Done).Concat(all.Where(t => t.Done)))
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
            Add("N", "Edited " + When(note.Changed), Act(() => DeleteNote(note)), note.Text);
        }
        return string.Join('\u001E', records);
    }

    private void StartRename(TodoList list)
    {
        _renaming = list.Id;
        _renameDraft = list.Name;
        SendNativeData();
        Island.DeckFocusField(RenameBox);
    }

    private void DeleteList(TodoList list)
    {
        if (list.Items.Count > 0 && MessageBox.Show($"Delete the list “{list.Name}” and its {list.Items.Count} to-do(s)?", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _deck.RemoveList(list.Id);
        SaveSoon();
        SendNativeData();
    }

    private void DeleteNote(Note note)
    {
        if (note.Text.Trim().Length > 0 && MessageBox.Show($"Delete the note “{note.Title}”?", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
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
                // Enter, or a click elsewhere, keeps the name; Esc does not.
                _renaming = null;
                _deck.RenameList(id, what == 1 ? Island.DeckEditText(submitted: true) : _renameDraft);
                SaveSoon();
                SendNativeData();
                break;
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
