using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// A check of every switch in the settings window, run with the shots tool
/// (<c>Nyatch.exe --shots &lt;folder&gt; switches</c>): on every page it clicks each switch, then writes
/// to switches.txt which setting it changed and whether the change was passed on (to the island, the
/// second notch and Explorer). A switch that changes nothing, or is not passed on, is marked NOT WIRED.
/// It works on the shots tool's copy of the settings.
/// </summary>
internal sealed partial class SettingsWindow
{
    internal static int ChangesPassedOn;

    internal void CheckSwitches(string folder)
    {
        var report = new StringBuilder();
        var problems = 0;
        var all = _settings.Schema.All.Select(s => s.FullName).ToList();
        Dictionary<string, string> Snapshot() => all.ToDictionary(n => n, n => _settings.Get(n), StringComparer.Ordinal);
        void Idle() => Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        // Every page, and on the two Pages pages every page shown in them.
        var views = new List<(string Name, Action Open)>();
        foreach (var page in SettingsLayout.Pages)
        {
            if (page.Id == "top-pages")
                foreach (var choice in TopPageChoices) views.Add(($"{page.Id}/{choice.Id}", () => { _topChosen = choice.Id; OpenPage(page.Id); }));
            else if (page.Id == "deck-pages")
                foreach (var choice in DeckPageChoices) views.Add(($"{page.Id}/{choice.Id}", () => { _deckChosen = choice.Id; OpenPage(page.Id); }));
            else views.Add((page.Id, () => OpenPage(page.Id)));
        }

        foreach (var (name, open) in views)
        {
            open();
            Idle();
            var count = Tracks().Count;
            for (var i = 0; i < count; i++)
            {
                open();
                Idle();
                var tracks = Tracks();
                if (i >= tracks.Count) break;
                var track = tracks[i];
                var title = TitleOf(track);
                var before = Snapshot();
                var passed = ChangesPassedOn;
                var dimmed = !IsLive(track);
                track.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Idle();
                var after = Snapshot();
                var changed = all.Where(n => before[n] != after[n]).ToList();
                var told = ChangesPassedOn > passed;
                var ok = changed.Count > 0 && told;
                if (!ok && !dimmed) problems++;
                report.AppendLine($"{(ok ? "ok        " : dimmed ? "dimmed    " : "NOT WIRED ")} {name,-24} {title,-34} {(changed.Count == 0 ? "(no setting changed)" : string.Join(", ", changed.Select(n => $"{n}: {before[n]} -> {after[n]}")))}{(told ? "" : "  [not passed on]")}");
                // Back as it was, so the next switch is checked from the same place.
                foreach (var n in changed) _settings.Set(n, before[n]);
            }
        }
        report.Insert(0, $"{problems} switch(es) not wired\n\n");
        File.WriteAllText(Path.Combine(folder, "switches.txt"), report.ToString());
    }

    /// <summary>The switches' tracks on the page now shown, top to bottom.</summary>
    private List<Border> Tracks()
    {
        var found = new List<Border>();
        void Walk(DependencyObject at)
        {
            if (at is Border { Width: 40, Height: 20 } border && border.GetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty) is "On" or "Off") found.Add(border);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++) Walk(VisualTreeHelper.GetChild(at, i));
        }
        Walk(_content);
        return found;
    }

    /// <summary>The words nearest a switch: its row's title, or the automation name.</summary>
    private static string TitleOf(FrameworkElement track)
    {
        for (DependencyObject? at = track; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement e && e.GetValue(System.Windows.Automation.AutomationProperties.NameProperty) is string name && name.Length > 0) return name;
            if (at is Grid grid && FirstText(grid) is { Length: > 0 } text) return text;
        }
        return "?";
    }

    private static string? FirstText(DependencyObject at)
    {
        if (at is TextBlock { Text.Length: > 0 } t && t.FontFamily.Source.IndexOf("Icons", StringComparison.Ordinal) < 0 && t.Text != "On" && t.Text != "Off") return t.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++)
            if (FirstText(VisualTreeHelper.GetChild(at, i)) is { } found) return found;
        return null;
    }

    /// <summary>False for a switch in a dimmed row (waiting on another setting).</summary>
    private static bool IsLive(UIElement track)
    {
        for (DependencyObject? at = track; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is UIElement e && (e.Opacity < 0.9 || !e.IsEnabled)) return false;
        return true;
    }
}
