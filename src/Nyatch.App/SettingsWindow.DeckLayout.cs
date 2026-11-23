using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.App;

// The second notch's Layout page: where it sits, and what it shows while closed: its words and the
// emoji beside them, chosen from a picker (Nyatch's paw prints, none, or any emoji).
internal sealed partial class SettingsWindow
{
    /// <summary>The emoji offered beside the closed notch's words; any other can be typed or pasted.</summary>
    private static readonly string[] EmojiChoices =
    [
        "🐱", "😺", "😸", "😻", "🐈", "🦊", "🐶", "🐼", "🐧", "🦄",
        "✨", "⭐", "🌙", "☀️", "🌈", "🔥", "⚡", "❄️", "🌸", "🍀",
        "❤️", "💜", "💙", "💚", "🎧", "🎵", "🎮", "💻", "🚀", "☕",
        "🍵", "🍕", "📌", "🕒", "📅", "👾", "🤖", "👻", "💎", "🎯",
    ];

    private void ShowDeckLayout()
    {
        Head(_page);
        Picture(() => Scene(top: false, second: true));
        foreach (var section in _page.Sections)
        {
            if (section.Title != "When closed")
            {
                Section(section);
                continue;
            }
            // The closed notch as it will look, then its words and its emoji.
            _anchors.Add((section.Title, SectionStrip(section.Title, null)));
            var on = _settings.Get("Deck.Enabled") == "true";
            var stage = new Border { Margin = new Thickness(0, 0, 0, 12), Opacity = on ? 1 : 0.5 };
            stage.Child = Stage(("Closed", ClosedSecondNotch()));
            var scene = _refreshPicture;
            _refreshPicture = () =>
            {
                scene?.Invoke();
                stage.Child = Stage(("Closed", ClosedSecondNotch()));
            };
            _content.Children.Add(stage);
            var rows = Rows();
            foreach (var name in section.Settings)
            {
                if (_settings.Schema.Find(name) is { } setting) SettingRow(rows, setting, section.Settings);
            }
            EmojiRow(rows, on);
        }
    }

    /// <summary>The emoji beside the closed notch's words: a tile for each choice, and a box for any other.</summary>
    private void EmojiRow(StackPanel rows, bool inUse)
    {
        if (_settings.Schema.Find("Deck.Emoji") is not { } setting) return;
        var current = _settings.Get("Deck.Emoji");
        if (current.Length == 0) current = "paws";
        void Choose(string value)
        {
            if (value == current || !_settings.Set("Deck.Emoji", value)) return;
            Changed();
            ShowPage(keepScroll: true);
        }

        var reset = IconButton(IconReset, "Reset to the default: Nyatch's paw prints", () => Choose(setting.Default));
        reset.Visibility = _settings.IsChanged("Deck.Emoji") ? Visibility.Visible : Visibility.Collapsed;
        reset.VerticalAlignment = VerticalAlignment.Top;
        var row = Row(rows, "Emoji", "", reset, hover: false);
        if (WordsOf(row) is not { } words) return;
        words.Margin = new Thickness(0);

        var tiles = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        Border Tile(string value, string name, FrameworkElement picture)
        {
            var chosen = value == current;
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            picture.VerticalAlignment = VerticalAlignment.Center;
            var tile = new Border
            {
                Child = picture,
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 6, 6),
                Background = chosen ? _accentTint : _high,
                BorderBrush = chosen ? _accent : Brushes.Transparent,
                BorderThickness = new Thickness(chosen ? 2 : 1),
                Cursor = Cursors.Hand,
                Focusable = true,
                ToolTip = name,
            };
            tile.SetValue(AutomationName, name);
            if (chosen) tile.SetValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty, "chosen");
            else
            {
                tile.MouseEnter += (_, _) => tile.BorderBrush = _line;
                tile.MouseLeave += (_, _) => tile.BorderBrush = Brushes.Transparent;
                tile.GotKeyboardFocus += (_, _) => tile.BorderBrush = _accent;
                tile.LostKeyboardFocus += (_, _) => tile.BorderBrush = Brushes.Transparent;
            }
            tile.MouseLeftButtonUp += (_, _) => Choose(value);
            tile.KeyDown += (_, key) =>
            {
                if (key.Key is not (Key.Enter or Key.Space)) return;
                key.Handled = true;
                Choose(value);
            };
            return tile;
        }
        tiles.Children.Add(Tile("paws", "Nyatch's paw prints", Paws(18, new SolidColorBrush(_accent.Color))));
        tiles.Children.Add(Tile("none", "None", new TextBlock { Text = "None", FontSize = 10.5, Foreground = _soft }));
        if (HerPictureIfOn(26, "closed", "face", "happy", "idle") is { } her) tiles.Children.Add(Tile("character", "The character's face", her));
        foreach (var emoji in EmojiChoices) tiles.Children.Add(Tile(emoji, emoji, EmojiPicture(emoji, 18)));
        // One typed or pasted earlier that is not among the choices: shown, chosen.
        var own = current is not ("paws" or "none" or "character") && !EmojiChoices.Contains(current);
        if (own) tiles.Children.Add(Tile(current, current, EmojiPicture(current, 18)));
        words.Children.Add(tiles);

        // Any other emoji: typed, pasted, or from the Windows emoji panel.
        var other = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var field = TextField("", 150, "Another emoji", typed =>
        {
            var first = StringInfo.GetNextTextElement(typed.Trim());
            if (first.Length == 0 || first.All(c => c < 0x80)) return false;
            Dispatcher.BeginInvoke(() => Choose(first));
            return true;
        });
        other.Children.Add(field);
        other.Children.Add(new TextBlock { Text = "Win + . opens the emoji panel.", FontSize = 12, Foreground = _faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        words.Children.Add(other);

        if (inUse) return;
        row.Opacity = 0.45;
        row.IsEnabled = false;
    }
}
