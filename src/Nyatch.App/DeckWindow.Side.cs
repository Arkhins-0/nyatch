using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

// The side panel: Claude, ChatGPT and Gemini in a panel the island draws on the edge of the screen
// (right or left; top, centre or bottom; half, three quarters or all of its height). It slides out on
// hover or with its hotkey. While it is on, the chats are there and not on the second notch; the second
// notch's page settings still say which chats there are and in what order.
internal sealed partial class DeckWindow
{
    private const int SideHotkeyId = 0x4E02;
    // The three chats the second notch also has (its page settings say which of them are shown).
    private static readonly string[] AiIds = ["claude", "chatgpt", "gemini"];

    /// <summary>The other services (DeepSeek, Mistral and the rest): on the side panel once their key is connected.</summary>
    private static IEnumerable<string> MoreAiIds => Assistant.Chats.Select(Assistant.Id).Where(id => !AiIds.Contains(id));

    // The chat last on show, kept between runs, so the panel and its tab open on it.
    private static string LastChatFile => System.IO.Path.Combine(SettingsStore.DefaultFolder, "side-chat.txt");

    private readonly List<string> _sideTabs = [];
    private int _sideTab;
    private bool _sideOpen;
    private bool _sideHotkeyHooked;

    private bool SideOn => NotchUse.SideInUse(_settings);

    /// <summary>The chat on the side panel's page.</summary>
    private AiPage? SideAi => _sideTab >= 0 && _sideTab < _sideTabs.Count ? _aiPages[_sideTabs[_sideTab]] : null;

    /// <summary>The chat on the second notch's page (only when the side panel is off).</summary>
    private AiPage? DeckAi => _tab >= 0 && _tab < _tabs.Count && _tabs[_tab].Native is { } id && _aiPages.TryGetValue(id, out var page) ? page : null;

    /// <summary>Whether the place the chats are in (the side panel, or the second notch) is open.</summary>
    private bool AiShowing => SideOn ? _sideOpen : _expanded;

    private void StartSide()
    {
        Island.SideSetHost((what, x, y) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => OnSide(what, x, y)));
    }

    private void OnSide(int what, int x, int y)
    {
        switch (what)
        {
            case 1:
                _sideOpen = true;
                ShowSidePage();
                break;
            case 2:
                // A right-click (not on a text box or a link): its menu, of chat settings.
                ShowSideMenu();
                break;
            case 3:
            case 4:
                _sideOpen = false;
                break;
            case 5:
                // A page dot.
                if (x >= 0 && x < _sideTabs.Count)
                {
                    _sideTab = x;
                    ShowSidePage();
                }
                break;
            case 6:
                // The wheel past the end of the page: the next chat (or the one before).
                if (_sideTabs.Count > 1)
                {
                    _sideTab = (_sideTab + (x > 0 ? 1 : _sideTabs.Count - 1)) % _sideTabs.Count;
                    ShowSidePage();
                }
                break;
            case 7:
                if (SideAi is { } page && x >= 0 && x < page.Actions.Count) page.Actions[x]();
                break;
            case 9:
                if (x == ReplyBox) AiEdit(x, y);
                break;
            case 10:
                // Its handle was dragged to a new height (25, 50, 75 or 100): kept as the setting.
                if (x is 25 or 50 or 75 or 100) Change("Side.Height", x.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>The side panel's settings, again when they change: which chats it has, and its hotkey.</summary>
    private void ApplySide()
    {
        var current = _sideTab >= 0 && _sideTab < _sideTabs.Count ? _sideTabs[_sideTab] : null;
        _sideTabs.Clear();
        if (SideOn)
        {
            var order = DeckPages.Order(_settings.Get("Deck.Pages"));
            _sideTabs.AddRange(order.Where(o => o.Show != PageShow.Hidden && AiIds.Contains(o.Id)).Select(o => o.Id));
            if (_sideTabs.Count == 0) _sideTabs.AddRange(AiIds);  // never an empty panel
            _sideTabs.AddRange(MoreAiIds.Where(id => HasKey(_aiPages[id])));
        }
        if (current is null)
        {
            try
            {
                current = System.IO.File.Exists(LastChatFile) ? System.IO.File.ReadAllText(LastChatFile).Trim() : null;
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
            }
        }
        _sideTab = current is not null && _sideTabs.IndexOf(current) is var at && at >= 0 ? at : 0;
        if (SideOn) ShowSidePage();
        else
        {
            _sideOpen = false;
            Island.SideOpen(false);
        }
        RegisterSideHotkey();
    }

    /// <summary>Tell the island which chat is on the side panel, and what it shows.</summary>
    private void ShowSidePage()
    {
        if (!SideOn || SideAi is not { } page) return;
        // Kept for next time: the tab shows this chat's logo, and the panel opens on it.
        try
        {
            System.IO.File.WriteAllText(LastChatFile, _sideTabs[_sideTab]);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
        }
        // No page dots: the chat button in the corner changes the chat.
        Island.SideShowPage(_sideTabs[_sideTab], _sideTab, 0);
        Island.SideSetPageData(AiData(page));
    }

    /// <summary>Open the side panel on a chat, ready to type in (from the hotkey, or the second notch's page picker).</summary>
    private void OpenSide(string? id = null)
    {
        if (!SideOn) return;
        if (id is not null && _sideTabs.IndexOf(id) is var at && at >= 0) _sideTab = at;
        Island.SideKeyboard(true);
        Island.SideOpen(true);
        ShowSidePage();
        if (SideAi is { } page && HasKey(page) && !page.History) Island.SideFocusField(ReplyBox);
    }

    private void RegisterSideHotkey()
    {
        var window = new WindowInteropHelper(this).EnsureHandle();
        if (!_sideHotkeyHooked)
        {
            HwndSource.FromHwnd(window)?.AddHook((IntPtr _, int message, IntPtr w, IntPtr _, ref bool handled) =>
            {
                if (message == HotkeyMessage && w.ToInt32() == SideHotkeyId)
                {
                    handled = true;
                    if (_sideOpen) Island.SideOpen(false);
                    else OpenSide();
                }
                return IntPtr.Zero;
            });
            _sideHotkeyHooked = true;
        }
        UnregisterHotKey(window, SideHotkeyId);
        if (!SideOn || _settings.Get("Shortcuts.SideHotkeyEnabled") != "true") return;
        if (ParseHotkey(_settings.Get("Shortcuts.SideModifiers"), _settings.Get("Shortcuts.SideKey")) is not { } hotkey) return;
        RegisterHotKey(window, SideHotkeyId, hotkey.Modifiers | 0x4000, hotkey.Key);
    }

    /// <summary>
    /// The side panel's right-click menu: which chat, a new one or the past ones, the assistant's model,
    /// keeping chats, where the panel sits, and the settings pages for the chats. Windows' own menu, as the
    /// notches have.
    /// </summary>
    private void ShowSideMenu()
    {
        var window = new WindowInteropHelper(this).EnsureHandle();
        var build = new MenuBuild();
        var menus = new List<IntPtr>();
        IntPtr Sub()
        {
            var made = CreatePopupMenu();
            menus.Add(made);
            return made;
        }
        var root = Sub();
        var page = SideAi;

        // Which chat.
        var chats = Sub();
        var withKeys = 0;
        for (var i = 0; i < _sideTabs.Count; i++)
        {
            if (!HasKey(_aiPages[_sideTabs[i]])) continue;
            var at = i;
            AppendMenuW(chats, MenuString | (i == _sideTab ? MenuChecked : 0), build.Add(() =>
            {
                _sideTab = at;
                OpenSide();
            }), Assistant.Name(_aiPages[_sideTabs[i]].Engine));
            withKeys++;
        }
        if (withKeys > 0) AppendMenuW(chats, MenuSeparator, UIntPtr.Zero, null);
        AppendMenuW(chats, MenuString, build.Add(() =>
        {
            Island.SideOpen(false);
            OpenSettingsAt?.Invoke("chats");
        }), "+  Add LLM…");
        AppendMenuW(root, MenuPopup, (UIntPtr)(ulong)chats, "Chat with");
        if (page is not null)
        {
            AppendMenuW(root, MenuString, build.Add(() =>
            {
                page.Running?.Cancel();
                page.Current = null;
                page.Asking = null;
                page.History = false;
                OpenSide();
            }), "New chat");
            AppendMenuW(root, MenuString | (page.History ? MenuChecked : 0), build.Add(() =>
            {
                page.History = !page.History;
                OpenSide();
            }), "Past chats");
        }
        AppendMenuW(root, MenuSeparator, UIntPtr.Zero, null);

        // The assistant's model, and keeping chats.
        if (page is not null)
        {
            var model = _settings.Get(Assistant.ModelSetting(page.Engine));
            AppendMenuW(root, MenuString | 0x1 /* greyed */, UIntPtr.Zero, $"Model: {model}".Replace("&", "&&"));
            AppendMenuW(root, MenuString, build.Add(() => OpenSettingsAt?.Invoke("chats")), "Change model…");
        }
        AddSwitch(build, root, "Assistant.KeepHistory", "Keep chats");
        AppendMenuW(root, MenuSeparator, UIntPtr.Zero, null);

        // Where the panel sits.
        var place = Sub();
        AddChoice(build, Sub, place, "Side.Edge", "Edge");
        AddChoice(build, Sub, place, "Side.Anchor", "Position on the edge");
        AddChoice(build, Sub, place, "Side.Height", "Height");
        AppendMenuW(place, MenuSeparator, UIntPtr.Zero, null);
        AppendMenuW(place, MenuString, build.Add(() => Change("Side.Enabled", "false")), "Hide the LLM panel");
        AppendMenuW(root, MenuPopup, (UIntPtr)(ulong)place, "LLM panel");
        AppendMenuW(root, MenuSeparator, UIntPtr.Zero, null);

        AppendMenuW(root, MenuString, build.Add(() => OpenSettingsAt?.Invoke("Keys")), "API keys…");
        AppendMenuW(root, MenuString, build.Add(() => OpenSettingsAt?.Invoke("chats")), "LLM services…");
        AppendMenuW(root, MenuString, build.Add(() => OpenSettingsAt?.Invoke("history")), "Chat history…");

        // Open while the menu is up (the pointer leaves the panel for it).
        Island.SideHold(true);
        SetForegroundWindow(window);
        GetCursorPos(out var at2);
        var chosen = TrackPopupMenu(root, ReturnCommand | RightButton, at2.X, at2.Y, 0, window, IntPtr.Zero);
        PostMessageW(window, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(root);
        Island.SideHold(false);
        if (chosen > 0 && chosen <= build.Actions.Count) build.Actions[(int)chosen - 1]();
    }

    /// <summary>The list of chats, from the button in the side panel's corner: the one chosen goes on show.</summary>
    private void ShowChatPicker()
    {
        var window = new WindowInteropHelper(this).EnsureHandle();
        var menu = CreatePopupMenu();
        // Only the LLMs with a key: the others cannot answer. Then a way to add one.
        var keyed = 0;
        for (var i = 0; i < _sideTabs.Count; i++)
        {
            if (!HasKey(_aiPages[_sideTabs[i]])) continue;
            AppendMenuW(menu, MenuString | (i == _sideTab ? MenuChecked : 0), (UIntPtr)(uint)(i + 1), Assistant.Name(_aiPages[_sideTabs[i]].Engine));
            keyed++;
        }
        if (keyed > 0) AppendMenuW(menu, MenuSeparator, UIntPtr.Zero, null);
        const uint AddLlm = 1000;
        AppendMenuW(menu, MenuString, (UIntPtr)AddLlm, "+  Add LLM…");
        Island.SideHold(true);
        SetForegroundWindow(window);
        GetCursorPos(out var at);
        var chosen = TrackPopupMenu(menu, ReturnCommand | 0x0 /* left button */, at.X, at.Y, 0, window, IntPtr.Zero);
        PostMessageW(window, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        Island.SideHold(false);
        if (chosen == AddLlm)
        {
            // The Services page, where a key is connected.
            Island.SideOpen(false);
            OpenSettingsAt?.Invoke("chats");
            return;
        }
        if (chosen < 1 || chosen > _sideTabs.Count) return;
        _sideTab = (int)chosen - 1;
        OpenSide();
    }

    // ----- The chats talk to whichever place they are in -----

    private void AiFocusField()
    {
        if (SideOn) Island.SideFocusField(ReplyBox);
        else Island.DeckFocusField(ReplyBox);
    }

    private string AiEditText(bool submitted = false) => SideOn ? Island.SideEditText(submitted) : Island.DeckEditText(submitted);

    private void AiSetEditText(string text)
    {
        if (SideOn) Island.SideSetEditText(ReplyBox, text);
        else Island.DeckSetEditText(ReplyBox, text);
    }

    private void AiSendData(AiPage page)
    {
        if (SideOn) Island.SideSetPageData(AiData(page));
        else SendNativeData();
    }
}
