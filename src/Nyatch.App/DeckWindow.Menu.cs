using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Nyatch.Core;

namespace Nyatch.App;

// The second notch's right-click menu: its own settings, the top notch's most used ones, and its pages.
// It is Windows' own menu, like the top notch's, so the two look and behave the same.
// Settings changed here are saved and applied as if changed in the settings window.
internal sealed partial class DeckWindow
{
    /// <summary>Called after a setting is changed from this pill (the app saves it and applies it to both notches).</summary>
    public Action? SettingsChanged { get; set; }

    /// <summary>Opens Nyatch's settings window.</summary>
    public Action? OpenSettings { get; set; }

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct MenuPoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out MenuPoint point);

    private const uint MenuString = 0x0, MenuPopup = 0x10, MenuSeparator = 0x800, MenuChecked = 0x8;
    private const uint ReturnCommand = 0x100, RightButton = 0x2;

    /// <summary>A menu being built: its items' actions, by command number.</summary>
    private sealed class MenuBuild
    {
        public List<Action> Actions { get; } = [];

        public UIntPtr Add(Action action)
        {
            Actions.Add(action);
            return (UIntPtr)(uint)Actions.Count;  // command 0 means "nothing chosen"
        }
    }

    private void StartMenu()
    {
        _pill.MouseRightButtonUp += (_, click) =>
        {
            click.Handled = true;
            ShowMenu();
        };
    }

    private void ShowMenu()
    {
        var window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero) return;
        var build = new MenuBuild();
        var menus = new List<IntPtr>();
        IntPtr Sub()
        {
            var made = CreatePopupMenu();
            menus.Add(made);
            return made;
        }

        var root = Sub();

        var second = Sub();
        AddChoice(build, Sub, second, "Deck.Position", "Position");
        AddChoice(build, Sub, second, "Deck.Shows", "When closed, show");
        AppendMenuW(second, MenuSeparator, UIntPtr.Zero, null);
        AddSwitch(build, second, "Deck.ExpandOnHover", "Open on hover");
        AddSwitch(build, second, "Deck.AlwaysOnTop", "Always on top");
        AppendMenuW(second, MenuSeparator, UIntPtr.Zero, null);
        AppendMenuW(second, MenuString, build.Add(() => Change("Deck.Enabled", "false")), "Hide the second notch");
        AppendMenuW(root, MenuPopup, (UIntPtr)(ulong)second, "Second notch");

        var top = Sub();
        AddChoice(build, Sub, top, "Appearance.Position", "Position");
        AddChoice(build, Sub, top, "Appearance.ShapeStyle", "Shape");
        AddChoice(build, Sub, top, "Themes.ThemePreset", "Theme (both notches)");
        AppendMenuW(top, MenuSeparator, UIntPtr.Zero, null);
        AddSwitch(build, top, "Modules.MediaPillClock", "Clock beside the playing media");
        AppendMenuW(root, MenuPopup, (UIntPtr)(ulong)top, "Top notch");

        // Shared by both notches.
        var clock = Sub();
        AddChoice(build, Sub, clock, "Modules.DateStyle", "Date layout");
        AddChoice(build, Sub, clock, "Modules.ClockFormat", "Time format");
        AddSwitch(build, clock, "Modules.ShowSeconds", "Show seconds");
        AppendMenuW(root, MenuPopup, (UIntPtr)(ulong)clock, "Clock and date (both notches)");

        var go = Sub();
        for (var i = 0; i < _tabs.Count; i++)
        {
            var at = i;
            AppendMenuW(go, MenuString | (_expanded && i == _tab ? MenuChecked : 0), build.Add(() =>
            {
                _tab = at;
                if (_expanded) ShowTab(at, 0);
                else Expand();
            }), _tabs[i].Name.Replace("&", "&&"));
        }
        AppendMenuW(root, MenuPopup, (UIntPtr)(ulong)go, "Go to");

        AppendMenuW(root, MenuSeparator, UIntPtr.Zero, null);
        AppendMenuW(root, MenuString, build.Add(() => OpenSettings?.Invoke()), "Nyatch settings…");

        // The usual dance for a menu on a window that is not in front: bring it forward, and post a
        // message afterwards so the menu closes properly when you click elsewhere.
        SetForegroundWindow(window);
        GetCursorPos(out var at2);
        var chosen = TrackPopupMenu(root, ReturnCommand | RightButton, at2.X, at2.Y, 0, window, IntPtr.Zero);
        PostMessageW(window, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        // Destroying the top menu destroys the submenus attached to it.
        DestroyMenu(root);
        if (chosen > 0 && chosen <= build.Actions.Count) build.Actions[(int)chosen - 1]();
    }

    /// <summary>A small menu at the pointer (a right-click on an item), each choice doing its action.</summary>
    private void ItemMenu(params (string Text, Action Do)[] choices)
    {
        var window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero) return;
        var menu = CreatePopupMenu();
        for (var i = 0; i < choices.Length; i++) AppendMenuW(menu, MenuString, (UIntPtr)(uint)(i + 1), choices[i].Text);
        SetForegroundWindow(window);
        GetCursorPos(out var at);
        var chosen = TrackPopupMenu(menu, ReturnCommand | RightButton, at.X, at.Y, 0, window, IntPtr.Zero);
        PostMessageW(window, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        // Done once the menu's own message loop is over, so what it starts (a shell, timers) runs normally.
        if (chosen > 0 && chosen <= choices.Length) Dispatcher.BeginInvoke(choices[chosen - 1].Do);
    }

    private Setting? Find(string fullName) => _settings.Schema.All.FirstOrDefault(s => s.FullName == fullName);

    /// <summary>A submenu with the setting's choices, the current one ticked.</summary>
    private void AddChoice(MenuBuild build, Func<IntPtr> sub, IntPtr parent, string fullName, string header)
    {
        if (Find(fullName) is not { Kind: SettingKind.Choice } setting) return;
        var current = _settings.Get(fullName);
        var list = sub();
        foreach (var option in setting.Options)
        {
            var value = option.Value;
            AppendMenuW(list, MenuString | (value == current ? MenuChecked : 0), build.Add(() => Change(fullName, value)), option.Label.Replace("&", "&&"));
        }
        AppendMenuW(parent, MenuPopup, (UIntPtr)(ulong)list, header);
    }

    /// <summary>An on/off item for a switch setting, ticked while on.</summary>
    private void AddSwitch(MenuBuild build, IntPtr parent, string fullName, string header)
    {
        if (Find(fullName) is not { Kind: SettingKind.Switch }) return;
        var on = _settings.Get(fullName) == "true";
        AppendMenuW(parent, MenuString | (on ? MenuChecked : 0), build.Add(() => Change(fullName, on ? "false" : "true")), header);
    }

    private void Change(string fullName, string value)
    {
        if (!_settings.Set(fullName, value)) return;
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// The top notch's right-click menu changes settings through here, as this notch's menu does, so they are
    /// saved, both notches follow, and the Settings window shows them. An empty value is the default.
    /// </summary>
    private void ListenToTopMenu() =>
        Island.SetChangeHost((name, value) => Dispatcher.BeginInvoke(() =>
        {
            if (value.Length > 0)
            {
                Change(name, value);
                return;
            }
            if (!_settings.IsChanged(name)) return;
            _settings.Reset(name);
            SettingsChanged?.Invoke();
        }));
}
