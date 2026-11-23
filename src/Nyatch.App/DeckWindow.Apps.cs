using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nyatch.Core;

namespace Nyatch.App;

// The Apps page: pinned, open and recently closed apps as icon tiles. The island draws it (native);
// this works out what is on it, and does what a click on a tile asks.
internal sealed partial class DeckWindow
{
    // What each tile does, by the action number the island sends back; rebuilt with the data.
    private readonly List<(Action Click, Action? RightClick)> _appActions = [];

    /// <summary>The Apps page's tiles for the island (see its DrawDeckApps).</summary>
    private string AppsData()
    {
        _appActions.Clear();
        var records = new List<string>();
        void Tile(char section, string name, string says, string? icon, IntPtr window, bool running, int windows, Action click, Action? rightClick = null)
        {
            _appActions.Add((click, rightClick));
            var action = _appActions.Count - 1;
            records.Add(string.Join('\u001F', section.ToString(), action.ToString(), Clean(name), Clean(says), Clean(icon ?? ""),
                window.ToInt64().ToString(), running ? "1" : "0", windows.ToString()));
        }

        if (_settings.Get("Deck.AppsPinned") == "true")
        foreach (var (name, shortcut) in Native.PinnedApps(12))
        {
            Tile('P', name, name, shortcut, IntPtr.Zero, false, 1, () =>
            {
                Collapse();
                Native.Open(shortcut);
            });
        }

        var active = _lastActive ?? NoteActiveApps();
        _appsShown = AppsSignature(active);
        foreach (var app in active.Take(21))
        {
            var front = app.Windows[0];
            // The window in front, or how many there are.
            var says = app.Windows.Count > 1 ? $"{app.Name} · {app.Windows.Count} windows"
                : front.Title.Length == 0 || front.Title.Contains(app.Name, StringComparison.OrdinalIgnoreCase) ? (front.Title.Length > 0 ? front.Title : app.Name)
                : $"{app.Name} – {front.Title}";
            Tile('A', app.Name, says, app.IconSource, front.Handle, true, app.Windows.Count, () =>
            {
                Collapse();
                Native.Focus(front.Handle);
            });
        }

        var running = active.Where(a => a.Id is not null).Select(a => a.Id!);
        var recentCount = _settings.Get("Deck.AppsRecent") == "true" ? Whole("Deck.AppsRecentCount", 1, 30) : 0;
        var recent = _recentApps.Closed(running, 30).Where(a => a.Command is not null || File.Exists(a.Path)).Take(recentCount).ToList();
        foreach (var app in recent)
        {
            Tile('R', app.Name, $"{app.Name} · {RecentApps.AgoWords(DateTime.Now - app.Seen)}", app.Icon ?? app.Path, IntPtr.Zero, false, 1, () =>
            {
                Collapse();
                Native.Launch(app.Command ?? app.Path);
            }, () => ItemMenu(
                ("Open", () =>
                {
                    Collapse();
                    Native.Launch(app.Command ?? app.Path);
                }),
                ("Remove from the list", () =>
                {
                    _recentApps.Forget(app.Path);
                    _recentApps.Save();
                })));
        }
        if (recent.Count > 0)
        {
            _appActions.Add((() =>
            {
                _recentApps.Clear();
                _recentApps.Save();
            }, null));
            records.Add($"C\u001F{_appActions.Count - 1}");
        }
        return string.Join('\u001E', records);

        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
    }

    /// <summary>A click (or, with 10000 added, a right-click) on the Apps page, from the island.</summary>
    private void AppsAction(int action)
    {
        var right = action >= 10000;
        var at = right ? action - 10000 : action;
        if (at < 0 || at >= _appActions.Count) return;
        var (click, rightClick) = _appActions[at];
        if (right) rightClick?.Invoke();
        else click();
        SendNativeData();
    }
}
