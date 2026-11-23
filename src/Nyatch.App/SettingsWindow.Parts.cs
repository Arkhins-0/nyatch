using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nyatch.App;

// The settings window's building blocks, in the look of Windows 11's own settings apps (PowerToys):
// a card for each setting with its icon, its name and line on the left and its control on the right,
// small gaps between cards, group titles above them, and the Windows accent colour. Every page is
// made of these, so the pages look alike.
internal sealed partial class SettingsWindow
{
    // ---------- Heads ----------

    /// <summary>The page head: a title, one line under it, and the page's actions on the right.</summary>
    private void PageStrip(string title, string sub, params UIElement[] actions)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions)
        {
            if (action is FrameworkElement element) element.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(action);
        }
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = _text, FontFamily = Numbers });
        if (sub.Length > 0) words.Children.Add(new TextBlock { Text = sub, FontSize = 13, Foreground = _soft, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        head.Children.Add(words);
        _content.Children.Add(head);
    }

    /// <summary>A section head: its title and count, with any actions on the right.</summary>
    private FrameworkElement SectionStrip(string title, int? count, params UIElement[] actions) => SectionStrip(_content, title, count, actions);

    private FrameworkElement SectionStrip(Panel into, string title, int? count, params UIElement[] actions)
    {
        _ = count;  // Windows' settings apps show no counts on their groups
        var head = new DockPanel { Margin = new Thickness(1, into.Children.Count == 0 ? 0 : 8, 0, 8), MinHeight = 22 };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions) right.Children.Add(action);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _text, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(words);
        into.Children.Add(head);
        return head;
    }

    /// <summary>A section head on its own, to place by hand.</summary>
    private FrameworkElement SectionStripOf(string title, params UIElement[] actions)
    {
        var holder = new StackPanel();
        SectionStrip(holder, title, null, actions);
        return holder;
    }

    /// <summary>A quiet line under a card.</summary>
    private void Note(string text, Panel? into = null) =>
        (into ?? _content).Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = _faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -14, 2, 24) });

    /// <summary>The picture at the top of a page, drawn again whenever one of the page's settings changes.</summary>
    private void Picture(Func<FrameworkElement> draw)
    {
        var host = new Border { Margin = new Thickness(0, 0, 0, 22), HorizontalAlignment = HorizontalAlignment.Stretch };
        host.Child = draw();
        _refreshPicture = () => host.Child = draw();
        _content.Children.Add(host);
    }

    // ---------- Small pieces ----------

    /// <summary>A small tag or count.</summary>
    private static Border Chip(string text, Brush ink, Brush back, bool numbers = false, Thickness? margin = null) => new()
    {
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = ink, FontFamily = numbers ? Numbers : Body },
        Background = back,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(7, 2, 7, 2),
        Margin = margin ?? new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A quiet link: words with a chevron, that go somewhere.</summary>
    private Border LinkChip(string text, Action go)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var words = new TextBlock { Text = text, FontSize = 12, Foreground = _soft, VerticalAlignment = VerticalAlignment.Center };
        var arrow = new TextBlock { Text = IconChevron, FontFamily = Glyphs, FontSize = 8, Foreground = _faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 1, 0, 0) };
        line.Children.Add(words);
        line.Children.Add(arrow);
        var chip = new Border { Child = line, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 7, 3), Background = _high, Cursor = Cursors.Hand, Focusable = true, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
        chip.SetValue(AutomationName, text);
        chip.MouseEnter += (_, _) => words.Foreground = _text;
        chip.MouseLeave += (_, _) => words.Foreground = _soft;
        chip.GotKeyboardFocus += (_, _) => chip.BorderBrush = _accent;
        chip.LostKeyboardFocus += (_, _) => chip.BorderBrush = Brushes.Transparent;
        chip.MouseLeftButtonUp += (_, up) =>
        {
            up.Handled = true;
            go();
        };
        chip.KeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Enter or Key.Space)) return;
            key.Handled = true;
            go();
        };
        return chip;
    }

    /// <summary>A hotkey as key caps: "Ctrl + Alt + N" becomes three caps.</summary>
    private FrameworkElement KeyCaps(string words, bool quiet = false)
    {
        var caps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in words.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) caps.Children.Add(KeyCap(part, quiet));
        return caps;
    }

    private Border KeyCap(string key, bool quiet = false) => new()
    {
        Child = new TextBlock { Text = key, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = quiet ? _faint : _text, FontFamily = Numbers },
        Background = _high,
        BorderBrush = _line,
        BorderThickness = new Thickness(1, 1, 1, 2),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(7, 2, 7, 2),
        Margin = new Thickness(0, 0, 4, 0),
        MinWidth = 24,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A bordered field with an icon and a quiet hint while empty (the search boxes).</summary>
    private Border Field(TextBox box, string glyph, string hint)
    {
        box.Style = (Style)Resources["BareField"];
        box.CaretBrush = _accent;
        box.BorderThickness = new Thickness(0);
        box.Background = Brushes.Transparent;
        box.Padding = new Thickness(0);
        box.FontSize = 13;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.Foreground = _text;
        var words = new TextBlock { Text = hint, Foreground = _faint, FontSize = 13, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        box.TextChanged += (_, _) => words.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var line = new DockPanel();
        if (glyph.Length > 0)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 12, Foreground = _faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
        }
        line.Children.Add(new Grid { Children = { box, words } });
        var frame = new Border { Child = line, Background = _panel, CornerRadius = new CornerRadius(4), Height = 32, Padding = new Thickness(10, 0, 10, 0), BorderThickness = new Thickness(1), BorderBrush = _line, Cursor = Cursors.IBeam };
        box.GotKeyboardFocus += (_, _) => frame.BorderBrush = _accent;
        box.LostKeyboardFocus += (_, _) => frame.BorderBrush = _line;
        frame.MouseLeftButtonUp += (_, _) => box.Focus();
        return frame;
    }

    // ---------- Cards and rows ----------

    /// <summary>A group of cards, one under the other with small gaps.</summary>
    private StackPanel Rows(Panel? into = null)
    {
        var rows = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        (into ?? _content).Children.Add(rows);
        return rows;
    }

    /// <summary>
    /// One card: its icon (if it has one), a title and a quiet line on the left, whatever belongs on the
    /// right. A card set in (<paramref name="indent"/>) belongs to the one above it: it sits close under
    /// it, and its words line up with the words above.
    /// </summary>
    private Border Row(StackPanel rows, string title, string line, UIElement? right, bool hover = true, int indent = 0, string glyph = "")
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(glyph.Length > 0 ? 36 : 0, 0, 20, 0) };
        words.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = _text, TextWrapping = TextWrapping.Wrap });
        if (line.Length > 0) words.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = _soft, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0), LineHeight = 16 });
        grid.Children.Add(words);
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        if (glyph.Length > 0)
        {
            grid.Children.Add(new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 20, Foreground = _text, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false });
        }
        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(16 + indent * 36, 13, 16, 13),
            MinHeight = 64,
            Background = _panel,
            BorderBrush = _line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 3),
            Tag = "card",
        };
        // A setting that belongs to the one above it joins that card (as Windows' expanding cards do): the
        // two share an edge, with a line between them, and only the outside corners are round.
        if (indent > 0 && rows.Children.Count > 0 && rows.Children[^1] is Border above && above.Tag is "card" or "part")
        {
            var top = rows.Children[^1] as Border;
            top!.CornerRadius = top.Tag is "card" ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0);
            top.Margin = new Thickness(0);
            row.CornerRadius = new CornerRadius(0, 0, 4, 4);
            row.BorderThickness = new Thickness(1, 0, 1, 1);
            row.MinHeight = 52;
            row.Padding = new Thickness(16 + 36 + (indent - 1) * 24, 10, 16, 10);
            row.Tag = "part";
        }
        if (hover)
        {
            row.MouseEnter += (_, _) => row.Background = _high;
            row.MouseLeave += (_, _) => row.Background = _panel;
        }
        rows.Children.Add(row);
        return row;
    }

    /// <summary>The words of a row (its title and line), to put something under them.</summary>
    private static StackPanel? WordsOf(Border row) => row.Child is Grid grid && grid.Children.Count > 0 ? grid.Children[0] as StackPanel : null;

    private Button MainButton(string text) => new() { Content = text, Style = (Style)Resources["Main"] };

    private Button GhostButton(string text) => new() { Content = text, Style = (Style)Resources["Ghost"] };

    /// <summary>An icon-only button, 28px, with its name for screen readers.</summary>
    private Border IconButton(string glyph, string name, Action press, Brush? ink = null)
    {
        var button = new Border
        {
            Child = new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 12, Foreground = ink ?? _soft, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = name,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
        };
        button.SetValue(AutomationName, name);
        button.MouseEnter += (_, _) => button.Background = _high;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.GotKeyboardFocus += (_, _) => button.BorderBrush = _accent;
        button.LostKeyboardFocus += (_, _) => button.BorderBrush = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, up) =>
        {
            up.Handled = true;
            press();
        };
        button.KeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Enter or Key.Space)) return;
            key.Handled = true;
            press();
        };
        return button;
    }

    /// <summary>A chevron that goes to another page.</summary>
    private Border GoButton(string name, Action go) => IconButton(IconChevron, name, go);

    /// <summary>Nothing to show yet: an icon, one line, and at most one action.</summary>
    private void EmptyState(string glyph, string title, string line, UIElement? action = null)
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 36, 0, 36) };
        box.Children.Add(new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 26, Foreground = _faint, HorizontalAlignment = HorizontalAlignment.Center });
        box.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        box.Children.Add(new TextBlock { Text = line, FontSize = 13, Foreground = _soft, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        if (action is FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.Margin = new Thickness(0, 14, 0, 0);
            box.Children.Add(element);
        }
        _content.Children.Add(new Border { Child = box, Background = _panel, BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) });
    }

    // ---------- Controls ----------

    /// <summary>An on/off switch, as Windows draws it: a round track with a knob, "On" or "Off" beside it when <paramref name="label"/>.</summary>
    private FrameworkElement Switch(bool on, Action<bool> changed) => Switch(on, changed, false);

    private FrameworkElement Switch(bool on, Action<bool> changed, bool label)
    {
        var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = knob, Cursor = Cursors.Hand, Focusable = true, VerticalAlignment = VerticalAlignment.Center };
        var words = new TextBlock { FontSize = 14, Foreground = _text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 24, TextAlignment = TextAlignment.Right };
        var state = on;
        void Show()
        {
            track.Background = state ? _accent : Brushes.Transparent;
            track.BorderBrush = state ? _accent : _soft;
            knob.Background = state ? _onAccent : _soft;
            knob.Margin = new Thickness(state ? 23 : 3, 0, 0, 0);
            words.Text = state ? "On" : "Off";
            track.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, state ? "On" : "Off");
        }
        void Flip()
        {
            state = !state;
            Show();
            changed(state);
        }
        track.MouseLeftButtonUp += (_, _) => Flip();
        track.KeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Space or Key.Enter)) return;
            key.Handled = true;
            Flip();
        };
        track.GotKeyboardFocus += (_, _) => track.BorderBrush = _accentDeep;
        track.LostKeyboardFocus += (_, _) => Show();
        Show();
        if (!label) return track;
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        line.Children.Add(words);
        line.Children.Add(track);
        words.MouseLeftButtonUp += (_, _) => Flip();
        return line;
    }

    /// <summary>A few choices side by side, the chosen one lit. For a short list of short names.</summary>
    private FrameworkElement Segments(IReadOnlyList<(string Value, string Label)> options, string chosen, Action<string> choose)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new Border { Child = bar, Background = _high, BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        var items = new List<(string Value, Border Item, TextBlock Words)>();
        void Show(string value)
        {
            foreach (var (v, item, words) in items)
            {
                var on = v == value;
                item.Background = on ? _panel : Brushes.Transparent;
                item.BorderBrush = on ? _line : Brushes.Transparent;
                words.Foreground = on ? _accent : _soft;
                words.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                item.SetValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty, on ? "chosen" : "");
            }
        }
        foreach (var (value, label) in options)
        {
            var words = new TextBlock { Text = label, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center };
            var item = new Border { Child = words, CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand, Focusable = true, MinWidth = 34, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            item.SetValue(AutomationName, label);
            void Pick()
            {
                Show(value);
                choose(value);
            }
            item.MouseLeftButtonUp += (_, _) => Pick();
            item.GotKeyboardFocus += (_, _) => item.BorderBrush = _accent;
            item.LostKeyboardFocus += (_, _) => item.BorderBrush = Brushes.Transparent;
            item.KeyDown += (_, key) =>
            {
                if (key.Key is not (Key.Enter or Key.Space)) return;
                key.Handled = true;
                Pick();
            };
            items.Add((value, item, words));
            bar.Children.Add(item);
        }
        Show(chosen);
        return box;
    }

    /// <summary>A slider with its value written beside it.</summary>
    private FrameworkElement SliderBox(int min, int max, int step, string unit, int value, Action<int> changed)
    {
        // Drawn here, like the switch, so it has the window's accent and weight: a thin track, the part
        // up to the value in the accent, and a round thumb. Drag, click, or use the arrow keys.
        const double width = 168, thumb = 14;
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var says = new TextBlock { FontSize = 12.5, FontFamily = Numbers, Foreground = _soft, Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = _line, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(thumb / 2, 0, thumb / 2, 0) };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = _accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(thumb / 2, 0, 0, 0) };
        var knob = new Border { Width = thumb, Height = thumb, CornerRadius = new CornerRadius(thumb / 2), Background = _accent, BorderBrush = _panel, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Grid { Width = width, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null, Children = { track, fill, knob } };
        var now = Math.Clamp(value, min, max);
        void Show()
        {
            var at = (now - min) / (double)Math.Max(1, max - min) * (width - thumb);
            fill.Width = at;
            knob.Margin = new Thickness(at, 0, 0, 0);
            says.Text = now.ToString(CultureInfo.CurrentCulture) + unit;
            slider.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, says.Text);
        }
        void Set(int next)
        {
            next = Math.Clamp((int)Math.Round(next / (double)step) * step, min, max);
            if (next == now) return;
            now = next;
            Show();
            changed(now);
        }
        void FromPointer(MouseEventArgs at) => Set(min + (int)Math.Round((at.GetPosition(slider).X - thumb / 2) / (width - thumb) * (max - min)));
        slider.MouseLeftButtonDown += (_, press) =>
        {
            slider.Focus();
            slider.CaptureMouse();
            FromPointer(press);
            press.Handled = true;
        };
        slider.MouseMove += (_, move) =>
        {
            if (slider.IsMouseCaptured) FromPointer(move);
        };
        slider.MouseLeftButtonUp += (_, _) => slider.ReleaseMouseCapture();
        slider.KeyDown += (_, key) =>
        {
            var big = Math.Max(step, (max - min) / 10 / step * step);
            switch (key.Key)
            {
                case Key.Left or Key.Down: Set(now - step); break;
                case Key.Right or Key.Up: Set(now + step); break;
                case Key.PageDown: Set(now - big); break;
                case Key.PageUp: Set(now + big); break;
                case Key.Home: Set(min); break;
                case Key.End: Set(max); break;
                default: return;
            }
            key.Handled = true;
        };
        slider.GotKeyboardFocus += (_, _) => knob.BorderBrush = _accentTint;
        slider.LostKeyboardFocus += (_, _) => knob.BorderBrush = _panel;
        Show();
        line.Children.Add(slider);
        line.Children.Add(says);
        return line;
    }

    /// <summary>A number with minus and plus beside it: for offsets and other numbers typed or nudged.</summary>
    private FrameworkElement Stepper(int min, int max, int step, string unit, int value, Action<int> changed)
    {
        var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), FontFamily = Numbers, FontSize = 13, TextAlignment = TextAlignment.Center, MinWidth = 44, Style = (Style)Resources["BareField"], CaretBrush = _accent, Foreground = _text, VerticalContentAlignment = VerticalAlignment.Center };
        var frame = new Border { Background = _panel, BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 32, VerticalAlignment = VerticalAlignment.Center };
        var now = Math.Clamp(value, min, max);
        void Set(int next)
        {
            next = Math.Clamp(next, min, max);
            box.Text = next.ToString(CultureInfo.InvariantCulture);
            if (next == now) return;
            now = next;
            changed(next);
        }
        void Commit() => Set(int.TryParse(box.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var typed) ? typed : now);
        var less = IconButton("", "Less", () => Set(now - step));
        var more = IconButton(IconAdd, "More", () => Set(now + step));
        less.Margin = new Thickness(1, 0, 0, 0);
        more.Margin = new Thickness(0, 0, 1, 0);
        var middle = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(box);
        if (unit.Length > 0) middle.Children.Add(new TextBlock { Text = unit, FontSize = 12, Foreground = _faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 4, 0) });
        var line = new DockPanel { MinWidth = 132 };
        DockPanel.SetDock(less, Dock.Left);
        DockPanel.SetDock(more, Dock.Right);
        line.Children.Add(less);
        line.Children.Add(more);
        line.Children.Add(middle);
        frame.Child = line;
        box.GotKeyboardFocus += (_, _) =>
        {
            frame.BorderBrush = _accent;
            box.SelectAll();
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            frame.BorderBrush = _line;
            Commit();
        };
        box.PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) Commit();
            else if (key.Key == Key.Up) Set(now + step);
            else if (key.Key == Key.Down) Set(now - step);
            else return;
            key.Handled = true;
        };
        return frame;
    }

    // A palette for the colour wells: greys, then clear, soft and deep tones, then a few more.
    private static readonly string[] Swatches =
    [
        "#000000", "#08080A", "#1E1E22", "#3A3C46", "#6E717C", "#9B9BA5", "#D0D2D8", "#FFFFFF",
        "#FF453A", "#FF9500", "#FFD60A", "#34C759", "#00C7BE", "#4CC9F0", "#0A84FF", "#7B68EE",
        "#FF8A80", "#FFB86B", "#FFE08A", "#7EE0A1", "#7ADFD6", "#9BDDF5", "#7FB5FF", "#B3A5F5",
        "#8E1B16", "#8A4B00", "#7A6300", "#1B6B34", "#0B5E5A", "#1F6F8B", "#0B4A8F", "#3F2F8F",
        "#BF5AF2", "#FF375F", "#F28FAD", "#C9A27C", "#5C6B73", "#2B2D35", "#16171A", "#F5F5F7",
    ];

    private static readonly Regex HexColour = new("^#?([0-9a-fA-F]{6})$", RegexOptions.Compiled);

    /// <summary>A colour: a swatch that opens a palette, and the hex beside it to type or paste.</summary>
    private FrameworkElement ColourWell(string hex, Action<string> changed)
    {
        var current = hex;
        var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderBrush = _line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 6, 0), ToolTip = "Choose a colour" };
        var box = new TextBox { Text = hex.ToUpperInvariant(), FontFamily = Numbers, FontSize = 13, Width = 70, MaxLength = 7, Style = (Style)Resources["BareField"], CaretBrush = _accent, Foreground = _text, VerticalContentAlignment = VerticalAlignment.Center };
        var frame = new Border { Background = _panel, BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 32, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(swatch);
        line.Children.Add(box);
        void Paint() => swatch.Background = new SolidColorBrush(DeckWindow.Hex(current, Colors.Gray));
        void Take(string value)
        {
            var match = HexColour.Match(value.Trim());
            if (!match.Success)
            {
                box.Text = current.ToUpperInvariant();
                return;
            }
            var next = "#" + match.Groups[1].Value.ToUpperInvariant();
            box.Text = next;
            if (next.Equals(current, StringComparison.OrdinalIgnoreCase)) return;
            current = next;
            Paint();
            changed(next);
        }

        // The palette, under the well.
        var palette = new WrapPanel { Width = 8 * 28 };
        var popup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, PlacementTarget = frame, VerticalOffset = 6, PopupAnimation = PopupAnimation.Fade };
        foreach (var colour in Swatches)
        {
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(3), Background = new SolidColorBrush(DeckWindow.Hex(colour, Colors.Gray)), BorderBrush = _line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = colour, Focusable = true };
            dot.SetValue(AutomationName, colour);
            void Pick()
            {
                popup.IsOpen = false;
                Take(colour);
            }
            dot.MouseLeftButtonUp += (_, _) => Pick();
            dot.KeyDown += (_, key) =>
            {
                if (key.Key is Key.Enter or Key.Space) Pick();
            };
            palette.Children.Add(dot);
        }
        popup.Child = new Border
        {
            Child = palette,
            Background = _panel,
            BorderBrush = _line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(8),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.22, Color = Colors.Black },
        };
        line.Children.Add(popup);

        swatch.MouseLeftButtonUp += (_, _) => popup.IsOpen = true;
        box.GotKeyboardFocus += (_, _) => frame.BorderBrush = _accent;
        box.LostKeyboardFocus += (_, _) =>
        {
            frame.BorderBrush = _line;
            Take(box.Text);
        };
        box.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) Take(box.Text);
        };
        frame.Child = line;
        Paint();
        return frame;
    }

    /// <summary>A text box in the window's look, saved on Enter or on moving on.</summary>
    private Border TextField(string value, double width, string hint, Func<string, bool> accept)
    {
        var box = new TextBox { Text = value };
        var frame = Field(box, "", hint);
        frame.Width = width;
        frame.VerticalAlignment = VerticalAlignment.Center;
        var saved = value;
        void Commit()
        {
            var typed = box.Text.Trim();
            if (typed == saved) return;
            if (accept(typed)) saved = typed;
            else box.Text = saved;
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) Commit();
        };
        return frame;
    }

    /// <summary>A picture in a quiet dark frame, like a small screen.</summary>
    private static Border Framed(FrameworkElement picture, Thickness margin) => new()
    {
        Child = picture,
        CornerRadius = new CornerRadius(8),
        Background = Frozen("#1B1C21"),
        Padding = new Thickness(8),
        Margin = margin,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
}
