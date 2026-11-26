using System;
using System.Runtime.InteropServices;

namespace Nyatch.App;

/// <summary>
/// The island, which lives in the native library Nyatch.Island.dll. It has
/// its own window and threads; this only starts it, tells it when the
/// settings file has changed, and stops it.
/// </summary>
/// <summary>One thing in the file tray. Kind is "F" (file), "D" (folder) or "T" (a text snippet).</summary>
internal sealed record TrayItem(string Kind, string Path, string Name, string Text);


internal static class Island
{
    private const string Library = "Nyatch.Island.dll";

    [DllImport(Library, ExactSpelling = true)]
    private static extern int NyatchStart();

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchReload();

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchStop();

    /// <summary>Show the island. False if it could not start.</summary>
    public static bool Start() => NyatchStart() != 0;

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int NyatchRenderEmoji(string text, int size, [Out] byte[] pixels);

    /// <summary>
    /// An emoji drawn in colour, as size x size pixels (32-bit BGRA, premultiplied), or null if it could
    /// not be drawn. For the settings window: WPF text has no colour glyphs.
    /// </summary>
    public static byte[]? RenderEmoji(string text, int size)
    {
        if (text.Length == 0 || size < 8 || size > 512) return null;
        var pixels = new byte[size * size * 4];
        try
        {
            return NyatchRenderEmoji(text, size, pixels) != 0 ? pixels : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Apply the settings file again.</summary>
    public static void Reload() => NyatchReload();

    public static void Stop() => NyatchStop();

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int NyatchTrayList([Out] char[]? buffer, int chars);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchTrayAdd(string path);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchTrayRemove(string path);

    /// <summary>What is in the file tray, which the island and the second pill share.</summary>
    public static System.Collections.Generic.List<TrayItem> Tray()
    {
        var items = new System.Collections.Generic.List<TrayItem>();
        var needed = NyatchTrayList(null, 0);
        if (needed <= 1) return items;
        var buffer = new char[needed + 64];
        if (NyatchTrayList(buffer, buffer.Length) > buffer.Length) return items;
        var text = new string(buffer, 0, System.Array.IndexOf(buffer, '\0') is var end && end >= 0 ? end : buffer.Length);
        foreach (var record in text.Split('\u001E', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\u001F');
            if (fields.Length >= 4) items.Add(new TrayItem(fields[0], fields[1], fields[2], fields[3]));
        }
        return items;
    }

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchTrayPaste();

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchTrayClear();

    /// <summary>Put what is on the clipboard into the tray: copied files, otherwise copied text.</summary>
    public static void TrayPaste() => NyatchTrayPaste();

    /// <summary>Empty the tray. The files themselves are not touched.</summary>
    public static void TrayClear() => NyatchTrayClear();

    public static void TrayAdd(string path) => NyatchTrayAdd(path);

    // ---------- The second notch, drawn by the island ----------

    /// <summary>What happens on the second notch: 1 = it opens, 2 = right-click at (x, y), 3 = hidden for Alt, 4 = it closes.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void DeckEventHandler(int what, int x, int y);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchDeckSetHost(DeckEventHandler? handler);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchDeckSetText(string text);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchDeckOpen(int open);

    [DllImport(Library, ExactSpelling = true)]
    private static extern int NyatchDeckOpenRect(out int left, out int top, out int right, out int bottom);

    // Kept here so the delegate the island calls is never collected.
    private static DeckEventHandler? _deckHandler;

    /// <summary>Called on the second notch's own thread.</summary>
    public static void DeckSetHost(DeckEventHandler handler)
    {
        _deckHandler = handler;
        NyatchDeckSetHost(handler);
    }

    public static void DeckSetText(string text) => NyatchDeckSetText(text);

    /// <summary>A setting the top notch's right-click menu changed: its name, and its value ("" for the default).</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public delegate void SettingChangeHandler([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string value);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSetChangeHost(SettingChangeHandler? handler);

    // Kept here so the delegate the island calls is never collected.
    private static SettingChangeHandler? _changeHandler;

    /// <summary>Called on the island's thread: pass it on to the app's.</summary>
    public static void SetChangeHost(SettingChangeHandler handler)
    {
        _changeHandler = handler;
        NyatchSetChangeHost(handler);
    }

    public static void DeckOpen(bool open) => NyatchDeckOpen(open ? 1 : 0);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchDeckHold(int hold);

    [DllImport(Library, ExactSpelling = true)]
    private static extern float NyatchDeckOpenness();

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchDeckSetPages(System.IntPtr window);

    /// <summary>The window that shows the second notch's pages; the island keeps its outline just under it.</summary>
    public static void DeckSetPages(System.IntPtr window) => NyatchDeckSetPages(window);

    /// <summary>How far the island's outline of the second notch is open, 0 to 1.</summary>
    public static double DeckOpenness() => NyatchDeckOpenness();

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchDeckShowPage(string page, int index, int count);

    /// <summary>Which page is on show: one the island draws ("notifications"), or "" for one of the app's. index and count are for the page dots.</summary>
    public static void DeckShowPage(string page, int index, int count) => NyatchDeckShowPage(page, index, count);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchDeckSetPageData(string data);

    /// <summary>What the native page on show displays (the Focus page's clocks).</summary>
    public static void DeckSetPageData(string data) => NyatchDeckSetPageData(data);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchShowNotice(string app, string title, string body);

    /// <summary>A notice of Nyatch's own on the top notch for a few seconds, as a notification is shown.</summary>
    public static void ShowNotice(string app, string title, string body = "") => NyatchShowNotice(app, title, body);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchSetRinging(string? title);

    /// <summary>The top notch shows what rings ("Timer finished") with Enter and Space, until it is told null.</summary>
    public static void SetRinging(string? title) => NyatchSetRinging(title);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSetFocusState(int state);

    /// <summary>How the Focus page's Pomodoro is, for the top notch's menu: 0 not started, 1 running, 2 paused.</summary>
    public static void SetFocusState(int state) => NyatchSetFocusState(state);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int NyatchDeckEditText(int which, [Out] char[]? buffer, int capacity);

    /// <summary>What is typed in the native page's text box with the keyboard, or (<paramref name="submitted"/>) what Enter last sent.</summary>
    public static string DeckEditText(bool submitted = false)
    {
        var which = submitted ? 1 : 0;
        var length = NyatchDeckEditText(which, null, 0);
        if (length <= 0) return "";
        var buffer = new char[length + 64];
        length = NyatchDeckEditText(which, buffer, buffer.Length);
        return new string(buffer, 0, System.Math.Min(length, buffer.Length));
    }

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchDeckSetEditText(int id, string text);

    /// <summary>Put text in a native text box while it is typed in (the terminal's history).</summary>
    public static void DeckSetEditText(int id, string text) => NyatchDeckSetEditText(id, text);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchDeckFocusField(int id);

    /// <summary>Give a native text box the keyboard, once it is on show (a new note, a new terminal tab).</summary>
    public static void DeckFocusField(int id) => NyatchDeckFocusField(id);

    /// <summary>Keep the second notch open while something is typed in it.</summary>
    public static void DeckHold(bool hold) => NyatchDeckHold(hold ? 1 : 0);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchDeckKeyboard(int on);

    /// <summary>Opened from the keyboard: the second notch takes the keyboard, and Esc or another app closes it.</summary>
    public static void DeckKeyboard(bool on) => NyatchDeckKeyboard(on ? 1 : 0);

    /// <summary>Where the open second notch is on screen, in pixels; null until it has been placed.</summary>
    public static System.Drawing.Rectangle? DeckOpenRect() =>
        NyatchDeckOpenRect(out var l, out var t, out var r, out var b) != 0 ? System.Drawing.Rectangle.FromLTRB(l, t, r, b) : null;

    public static void TrayRemove(string path) => NyatchTrayRemove(path);

    // ----- The side panel: the same calls as the second notch's, for its own window -----

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSideSetHost(DeckEventHandler? handler);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSideOpen(int open);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchSideShowPage(string page, int index, int count);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchSideSetPageData(string data);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int NyatchSideEditText(int which, [Out] char[]? buffer, int capacity);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void NyatchSideSetEditText(int id, string text);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSideFocusField(int id);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSideKeyboard(int on);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void NyatchSideHold(int hold);

    /// <summary>Keep the side panel open (while its menu is up, or something is typed in it).</summary>
    public static void SideHold(bool hold) => NyatchSideHold(hold ? 1 : 0);

    // Kept here so the delegate the island calls is never collected.
    private static DeckEventHandler? _sideHandler;

    /// <summary>Called on the side panel's own thread: 1 it opened, 4 it closed, 5 a page dot, 6 the wheel past a page's end, 7 a button, 9 a text box.</summary>
    public static void SideSetHost(DeckEventHandler handler)
    {
        _sideHandler = handler;
        NyatchSideSetHost(handler);
    }

    public static void SideOpen(bool open) => NyatchSideOpen(open ? 1 : 0);

    /// <summary>Which page the side panel shows ("claude"), with its place among the pages for the dots.</summary>
    public static void SideShowPage(string page, int index, int count) => NyatchSideShowPage(page, index, count);

    public static void SideSetPageData(string data) => NyatchSideSetPageData(data);

    public static string SideEditText(bool submitted = false)
    {
        var which = submitted ? 1 : 0;
        var length = NyatchSideEditText(which, null, 0);
        if (length <= 0) return "";
        var buffer = new char[length + 64];
        length = NyatchSideEditText(which, buffer, buffer.Length);
        return new string(buffer, 0, System.Math.Min(length, buffer.Length));
    }

    public static void SideSetEditText(int id, string text) => NyatchSideSetEditText(id, text);

    public static void SideFocusField(int id) => NyatchSideFocusField(id);

    /// <summary>Opened from the keyboard: the side panel takes the keyboard, and Esc or another app closes it.</summary>
    public static void SideKeyboard(bool on) => NyatchSideKeyboard(on ? 1 : 0);
}
