using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.App;

// The "Pages" page of each notch, the same for both: on the left the notch's pages as cards (drag one
// to move it, its switch puts it on the notch), on the right the chosen page: a picture of it, what it
// is, and its own settings. Everything about a page is here and nowhere else; its hotkey letter is on
// the Shortcuts page, with every other hotkey.
// The order is stored as "calendar,weather,-hardware,tray": a minus before the hidden ones (and, for
// the second notch, a tilde before the ones only its hotkey opens; see DeckPages).
internal sealed partial class SettingsWindow
{
    private sealed record PageChoice(string Id, string Name, string Glyph, string About);

    /// <summary>The top notch's first page: always there, always first.</summary>
    private const string PrimaryPage = "primary";

    private static readonly PageChoice[] TopPageChoices =
    [
        new(PrimaryPage, "Clock / now playing", "", "The clock, or the player while something plays. Always the first page."),
        new("calendar", "Calendar", "", "The month, with today marked."),
        new("weather", "Weather", "", "The weather now and in the days ahead."),
        new("hardware", "Hardware monitor", "", "Processor, graphics card, memory and network."),
        new("tray", "File tray", "", "Files dropped on the notch wait here until you drag them out."),
    ];

    private static readonly PageChoice[] DeckPageChoices =
    [
        new("apps", "Apps", "", "Open and recent apps."),
        new("tray", "Tray", "", "Files dropped on a notch wait here until you drag them out."),
        new("clipboard", "Clipboard", "", "What you copied, with pins."),
        new("notifications", "Notifications", "", "Windows notifications, and Do not disturb."),
        new("claude", "Claude", "", "Chat with Claude."),
        new("chatgpt", "ChatGPT", "", "Chat with ChatGPT."),
        new("gemini", "Gemini", "", "Chat with Gemini."),
        new("focus", "Focus", "", "A timer, a stopwatch and a pomodoro."),
        new("notes", "Notes & to-dos", "", "Lists and notes."),
        new("terminal", "Terminal", "", "Command Prompt and PowerShell."),
        new("online", "Online", "", "Ping, a speed test and your links."),
    ];

    private const double PageCardHeight = 48, PageCardGap = 6;

    private string _topChosen = PrimaryPage, _deckChosen = "apps";
    /// <summary>The card to give the keyboard back to once the page is drawn again.</summary>
    private string? _focusCard;

    private sealed record PageItem(string Id, PageShow Show);

    /// <summary>The top notch's saved order; pages it does not name yet come at the end, shown.</summary>
    private static List<PageItem> ReadTopOrder(string value)
    {
        var items = new List<PageItem>();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var id = raw.TrimStart('-');
            if (id != PrimaryPage && TopPageChoices.Any(c => c.Id == id) && items.All(i => i.Id != id)) items.Add(new PageItem(id, raw.StartsWith('-') ? PageShow.Hidden : PageShow.Shown));
        }
        foreach (var choice in TopPageChoices)
        {
            if (choice.Id != PrimaryPage && items.All(i => i.Id != choice.Id)) items.Add(new PageItem(choice.Id, PageShow.Shown));
        }
        return items;
    }

    /// <summary>The feature a page of the top notch needs on, if it has one.</summary>
    private static string? FeatureOf(string topPage) => topPage switch
    {
        "weather" => "Modules.Weather",
        "tray" => "Modules.FileTrayModule",
        _ => null,
    };

    private void ShowNotchPages(bool top)
    {
        Head(_page);
        var choices = top ? TopPageChoices : DeckPageChoices;
        var chosen = top ? _topChosen : _deckChosen;
        if (choices.All(c => c.Id != chosen)) chosen = choices[0].Id;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(296) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var list = PageList(top, chosen);
        var detail = PageDetail(top, choices.First(c => c.Id == chosen));
        grid.Children.Add(list);
        grid.Children.Add(detail);
        // Side by side; one under the other when the window is narrow.
        void Arrange()
        {
            var narrow = grid.ActualWidth > 0 && grid.ActualWidth < 620;
            grid.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(296);
            grid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 20);
            grid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(detail, narrow ? 0 : 2);
            Grid.SetRow(detail, narrow ? 1 : 0);
            detail.Margin = new Thickness(0, narrow ? 18 : 0, 0, 0);
        }
        grid.SizeChanged += (_, _) => Arrange();
        Arrange();
        _content.Children.Add(grid);
    }

    // ---------- The list ----------

    private FrameworkElement PageList(bool top, string chosen)
    {
        var choices = top ? TopPageChoices : DeckPageChoices;
        var items = top ? ReadTopOrder(_settings.Get("Appearance.TopPages"))
            : DeckPages.Order(_settings.Get("Deck.Pages")).Select(o => new PageItem(o.Id, o.Show)).ToList();
        // While the LLM panel is on, Claude, ChatGPT and Gemini are there, not on this notch: not listed, but
        // kept as they are in the saved order, for when the panel is turned off.
        var aside = !top && NotchUse.SideInUse(_settings) ? items.Where(i => i.Id is "claude" or "chatgpt" or "gemini").ToList() : [];
        items = items.Except(aside).ToList();
        var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
        // A page of the top notch is on it while it is listed and its feature is on.
        bool On(PageItem item) => item.Show == PageShow.Shown && (!top || FeatureOf(item.Id) is not { } feature || _settings.Get(feature) == "true");

        var whole = new StackPanel();
        var onCount = items.Count(On) + (top ? 1 : 0);
        var quick = items.Count(i => i.Show == PageShow.HotkeyOnly);
        var total = items.Count + (top ? 1 : 0);
        SectionStrip(whole, "Pages", null, new TextBlock
        {
            Text = $"{onCount} of {total} on the notch" + (quick > 0 ? $" · {quick} by hotkey" : ""),
            FontSize = 12,
            Foreground = _faint,
        });
        var list = new StackPanel { Background = Brushes.Transparent };

        void Choose(string id)
        {
            if (top) _topChosen = id;
            else _deckChosen = id;
            _focusCard = id;
            ShowPage(keepScroll: true);
        }
        void Save()
        {
            if (top) _settings.Set("Appearance.TopPages", string.Join(",", items.Select(i => (i.Show == PageShow.Hidden ? "-" : "") + i.Id)));
            else _settings.Set("Deck.Pages", DeckPages.OrderText(items.Concat(aside).Select(i => (i.Id, i.Show))));
            Changed();
        }

        if (top)
        {
            // The first page is fixed: a locked card, so the order reads as a whole.
            var first = PageCard(choices[0], "Always first", 1, on: true, chosen == PrimaryPage, locked: true, out var hold, out _);
            hold.MouseLeftButtonUp += (_, _) => Choose(PrimaryPage);
            first.KeyDown += (_, key) =>
            {
                if (key.Key is Key.Enter or Key.Space) Choose(PrimaryPage);
            };
            first.Tag = PrimaryPage;
            whole.Children.Add(first);
        }
        whole.Children.Add(list);

        // ----- Dragging: the list holds the mouse; the card follows it and the others make room -----
        Border? dragged = null;
        var offset = 0.0;   // pointer's distance from the card's top when the drag began
        var moved = false;
        var step = PageCardHeight + PageCardGap;

        var number = top ? 1 : 0;
        for (var i = 0; i < items.Count; i++)
        {
            var at = i;
            var item = items[i];
            var choice = choices.First(c => c.Id == item.Id);
            var on = On(item);
            if (on) number++;
            var state = on ? (top ? "Shown" : keys.TryGetValue(item.Id, out var k) ? $"On the notch · {k}" : "On the notch")
                : item.Show == PageShow.HotkeyOnly && keys.TryGetValue(item.Id, out var only) ? $"Hotkey only · {only}"
                : "Hidden";
            var card = PageCard(choice, state, number, on, item.Id == chosen, locked: false, out var grip, out var toggleHost, byHotkey: !on && item.Show == PageShow.HotkeyOnly);
            var toggle = Switch(on, value =>
            {
                if (top)
                {
                    items[at] = item with { Show = value ? PageShow.Shown : PageShow.Hidden };
                    // The feature behind the page goes on with it; the tray goes off with it too.
                    if (FeatureOf(item.Id) is { } feature && (value || item.Id == "tray")) _settings.Set(feature, value ? "true" : "false");
                }
                else
                {
                    // The second notch keeps at least one page on it.
                    if (!value && items.Count(x => x.Show == PageShow.Shown) <= 1)
                    {
                        ShowPage(keepScroll: true);
                        return;
                    }
                    // Off the notch, a page with a letter still opens by the hotkey.
                    items[at] = item with { Show = value ? PageShow.Shown : keys.ContainsKey(item.Id) ? PageShow.HotkeyOnly : PageShow.Hidden };
                }
                Save();
                ShowPage(keepScroll: true);
            });
            toggle.SetValue(AutomationName, $"{choice.Name}: on the notch");
            toggleHost.Child = toggle;
            card.Tag = item.Id;
            // Dragging starts anywhere on the card but its switch; a click without moving chooses it.
            grip.MouseLeftButtonDown += (_, press) =>
            {
                dragged = card;
                offset = press.GetPosition(list).Y - list.Children.IndexOf(card) * step;
                moved = false;
                list.CaptureMouse();
                press.Handled = true;
            };
            card.KeyDown += (_, key) =>
            {
                if (key.Key is Key.Enter or Key.Space && Keyboard.Modifiers == ModifierKeys.None)
                {
                    key.Handled = true;
                    Choose(item.Id);
                    return;
                }
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || key.Key is not (Key.Up or Key.Down)) return;
                var to = at + (key.Key == Key.Up ? -1 : 1);
                if (to < 0 || to >= items.Count) return;
                key.Handled = true;
                (items[at], items[to]) = (items[to], items[at]);
                Save();
                _focusCard = item.Id;
                ShowPage(keepScroll: true);
            };
            list.Children.Add(card);
        }

        list.MouseMove += (_, move) =>
        {
            if (dragged is null) return;
            var y = move.GetPosition(list).Y;
            var index = list.Children.IndexOf(dragged);
            var topAt = Math.Clamp(y - offset, 0, (items.Count - 1) * step);
            if (!moved && Math.Abs(topAt - index * step) < 4) return;
            if (!moved)
            {
                moved = true;
                Panel.SetZIndex(dragged, 1);
                dragged.BorderBrush = _accent;
                dragged.Opacity = 0.92;
            }
            var target = Math.Clamp((int)Math.Round(topAt / step), 0, items.Count - 1);
            if (target != index)
            {
                var item = items[index];
                items.RemoveAt(index);
                items.Insert(target, item);
                list.Children.RemoveAt(index);
                list.Children.Insert(target, dragged);
                index = target;
            }
            // The card stays under the pointer while its slot moves.
            dragged.RenderTransform = new TranslateTransform(0, topAt - index * step);
        };
        void EndDrag()
        {
            if (dragged is not { } card) return;
            dragged = null;
            list.ReleaseMouseCapture();
            if (moved)
            {
                Save();
                ShowPage(keepScroll: true);
            }
            else if (card.Tag is string id) Choose(id);
        }
        list.MouseLeftButtonUp += (_, _) => EndDrag();
        list.LostMouseCapture += (_, _) => EndDrag();

        whole.Children.Add(new TextBlock
        {
            Text = "Drag to reorder.",
            FontSize = 12,
            Foreground = _faint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 2, 0),
        });

        // The keyboard goes back to the card it was on.
        if (_focusCard is { } focus)
        {
            _focusCard = null;
            whole.Loaded += (_, _) =>
            {
                var card = list.Children.OfType<Border>().Concat(whole.Children.OfType<Border>()).FirstOrDefault(c => c.Tag as string == focus);
                if (card is { Focusable: true } && IsKeyboardFocusWithin) card.Focus();
            };
        }
        return whole;
    }

    /// <summary>One page's card. <paramref name="grip"/> is the part a drag (or a click) starts from; the switch goes in <paramref name="toggleHost"/>.</summary>
    private Border PageCard(PageChoice choice, string state, int number, bool on, bool chosen, bool locked, out FrameworkElement grip, out Border toggleHost, bool byHotkey = false)
    {
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new TextBlock { Text = locked ? IconLock : IconGrip, FontFamily = Glyphs, FontSize = locked ? 10 : 12, Foreground = _faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            Background = on ? _accentTint : _high,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = choice.Glyph, FontFamily = Glyphs, FontSize = 14, Foreground = on ? _accent : byHotkey ? _soft : _faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = choice.Name, FontSize = 14, Foreground = on || byHotkey ? _text : _faint, TextTrimming = TextTrimming.CharacterEllipsis });
        if (on) title.Children.Add(new TextBlock { Text = number.ToString(), FontSize = 11, Foreground = _faint, Margin = new Thickness(7, 2, 0, 0), FontFamily = Numbers });
        words.Children.Add(title);
        words.Children.Add(new TextBlock { Text = state, FontSize = 12, Foreground = _faint, TextTrimming = TextTrimming.CharacterEllipsis });

        toggleHost = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(handle, 0);
        Grid.SetColumn(tile, 1);
        Grid.SetColumn(words, 2);
        Grid.SetColumn(toggleHost, 3);

        // The grip is the whole card left of the switch.
        var hold = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Grid.SetColumnSpan(hold, locked ? 4 : 3);
        line.Children.Add(hold);
        line.Children.Add(handle);
        line.Children.Add(tile);
        line.Children.Add(words);
        line.Children.Add(toggleHost);
        handle.IsHitTestVisible = tile.IsHitTestVisible = words.IsHitTestVisible = false;
        grip = hold;

        var rest = chosen ? _accent : _line;
        var card = new Border
        {
            Child = line,
            Height = PageCardHeight,
            Background = chosen ? _accentTint : _panel,
            BorderBrush = rest,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, PageCardGap),
            Focusable = true,
            FocusVisualStyle = null,
        };
        card.SetValue(AutomationName, locked ? $"{choice.Name}, always first" : $"{choice.Name}, {state}. Enter shows it; Ctrl+Up or Ctrl+Down moves it.");
        if (chosen) card.SetValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty, "chosen");
        if (!chosen)
        {
            card.MouseEnter += (_, _) => card.Background = _high;
            card.MouseLeave += (_, _) => card.Background = _panel;
        }
        card.GotKeyboardFocus += (_, _) => card.BorderBrush = _accent;
        card.LostKeyboardFocus += (_, _) => card.BorderBrush = rest;
        return card;
    }

    // ---------- The chosen page ----------

    private FrameworkElement PageDetail(bool top, PageChoice choice)
    {
        var id = choice.Id;
        var whole = new StackPanel();

        // What it looks like, and what it is.
        var inside = new StackPanel { Margin = new Thickness(16) };
        whole.Children.Add(new Border { Child = inside, Background = _panel, BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 24) });
        var picture = Framed(top ? TopPagePicture(id) : DeckPagePicture(id), new Thickness(0, 0, 0, 4));
        picture.HorizontalAlignment = HorizontalAlignment.Center;
        inside.Children.Add(picture);

        var order = DeckPages.Order(_settings.Get("Deck.Pages"));
        var show = top ? PageShow.Shown : order.First(o => o.Id == id).Show;
        var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
        var topOn = !top || id == PrimaryPage || (ReadTopOrder(_settings.Get("Appearance.TopPages")).First(i => i.Id == id).Show == PageShow.Shown
            && (FeatureOf(id) is not { } feature || _settings.Get(feature) == "true"));
        var state = top ? (id == PrimaryPage ? "Always first" : topOn ? "On the notch" : "Hidden")
            : show switch { PageShow.Shown => "On the notch", PageShow.HotkeyOnly => "Hotkey only", _ => "Hidden" };
        var off = top ? !topOn : show == PageShow.Hidden;
        whole.Children.Insert(0, SectionStripOf(choice.Name, Chip(state, off ? _faint : _accent, off ? _high : _accentTint)));

        // A line of quiet facts and links under the words.
        void Fact(string label, params UIElement[] parts)
        {
            var line = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            line.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = _faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            foreach (var part in parts)
            {
                if (part is FrameworkElement element)
                {
                    element.VerticalAlignment = VerticalAlignment.Center;
                    if (element.Margin == default) element.Margin = new Thickness(0, 0, 6, 0);
                }
                line.Children.Add(part);
            }
            inside.Children.Add(line);
        }
        TextBlock Quiet(string text) => new() { Text = text, FontSize = 12, Foreground = _soft };

        if (!top)
        {
            // Its hotkey, as it stands; set on the Shortcuts page with the others.
            var hotkeyOn = _settings.Get("Shortcuts.DeckHotkeyEnabled") == "true";
            var hotkey = DeckPages.HotkeyWords(_settings.Get("Shortcuts.DeckModifiers"), _settings.Get("Shortcuts.DeckKey"));
            var link = LinkChip("Shortcuts", () => OpenPage("shortcuts"));
            if (!hotkeyOn) Fact("Hotkey", Quiet("The hotkey is off."), link);
            else if (show != PageShow.Hidden && keys.TryGetValue(id, out var letter)) Fact("Hotkey", KeyCaps(hotkey), Quiet("then"), KeyCap(letter.ToString()), link);
            else Fact("Hotkey", Quiet("No letter."), link);

            if (id is "claude" or "chatgpt" or "gemini")
            {
                var secret = id switch { "claude" => Secrets.Anthropic, "chatgpt" => Secrets.OpenAi, _ => Secrets.Gemini };
                var connected = Secrets.Has(secret);
                Fact("Service", Chip(connected ? "Key connected" : "No key yet", connected ? _accent : _faint, connected ? _accentTint : _high), LinkChip("Chats", () => OpenPage("chats")));
            }
            // Where the page's own settings are.
            var settingsOn = id switch
            {
                "apps" or "tray" or "clipboard" => "deck-files",
                "focus" => "focus",
                "terminal" or "online" => "deck-tools",
                _ => null,
            };
            if (settingsOn is not null && SettingsLayout.Page(settingsOn) is { } ownPage) Fact("Settings", LinkChip(ownPage.Name, () => OpenPage(ownPage.Id)));
        }
        else if (id == PrimaryPage)
        {
            Fact("Settings", LinkChip("Clock & date", () => OpenPage("clock")), LinkChip("Media & lyrics", () => OpenPage("media")));
        }
        else if (id == "tray")
        {
            // The same files as the second notch's Tray page: how many it keeps is set there.
            Fact("Settings", LinkChip("Apps & files", () => OpenPage("deck-files")));
        }

        // Its own settings.
        if (top && _page.Sections.FirstOrDefault(s => s.Key == id) is { } section)
        {
            var rows = Rows(whole);
            foreach (var name in section.Settings)
            {
                if (_settings.Schema.Find(name) is { } setting) SettingRow(rows, setting, section.Settings);
            }
            if (section.Note.Length > 0) Note(section.Note, whole);
        }
        return whole;
    }
}
