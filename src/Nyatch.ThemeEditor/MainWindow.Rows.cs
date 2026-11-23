using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.ThemeEditor;

/// <summary>
/// The slot rows: each with a box to select it and buttons for what can be done to it (add or replace,
/// crop, make smaller, restore, remove). Several rows can be selected and done at once. Slow work (making
/// pictures smaller) waits in a queue and runs one at a time in the background; the window stays usable
/// and the rows being worked on say so.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly HashSet<string> _selected = [];
    private readonly HashSet<string> _busy = [];
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly Border _selectionBar = new() { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private int _waiting;

    private static Button IconButton(string glyph, string tip, Action click, bool danger = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 14 },
            ToolTip = tip,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        if (danger) ((TextBlock)button.Content).Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x56, 0x4F));
        button.Click += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    private Border SlotRow(PackProject pack, CharacterSlot slot)
    {
        var shown = pack.Shown(slot);
        var state = StateOf(pack, slot);
        var busy = _busy.Contains(slot.Name);
        var hasFile = pack.Has(slot.Name);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pick = new CheckBox { IsChecked = _selected.Contains(slot.Name), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Padding = new Thickness(0), ToolTip = "Select" };
        pick.Click += (_, e) =>
        {
            e.Handled = true;
            if (pick.IsChecked == true) _selected.Add(slot.Name);
            else _selected.Remove(slot.Name);
            DrawSelectionBar();
        };
        grid.Children.Add(pick);

        var thumb = new AnimatedImage { Still = true, Width = 40, Height = 40, Opacity = state == State.Borrowed ? 0.4 : 1, HorizontalAlignment = HorizontalAlignment.Left };
        thumb.Picture = Load(shown?.Path);
        Grid.SetColumn(thumb, 1);
        grid.Children.Add(thumb);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = slot.Name, FontWeight = FontWeights.SemiBold });
        words.Children.Add(Quiet(slot.When, 12));
        Grid.SetColumn(words, 2);
        grid.Children.Add(words);

        var label = busy ? "Working…" : state switch
        {
            State.Own => "Own picture",
            State.Copy => $"Copy of {_copies[slot.Name]}",
            State.Borrowed => $"Uses {shown!.Value.Name}",
            _ => slot.Core ? "Needed" : "Empty",
        };
        var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        var chipText = new TextBlock { Text = label, FontSize = 12 };
        var tint = busy ? Accent() : state switch
        {
            State.Own => Accent(),
            State.Copy => Color.FromRgb(0xE8, 0xA8, 0x38),
            State.Empty when slot.Core => Color.FromRgb(0xE0, 0x56, 0x4F),
            _ => (Color?)null,
        };
        if (tint is { } c)
        {
            chip.Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B));
            chipText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        }
        else chipText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        chip.Child = chipText;
        Grid.SetColumn(chip, 3);
        grid.Children.Add(chip);

        // What can be done to this slot.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !busy };
        actions.Children.Add(IconButton("", hasFile ? "Replace" : "Add picture", () => Choose(slot.Name)));
        if (hasFile)
        {
            actions.Children.Add(IconButton("", "Crop", () =>
            {
                _slot = slot;
                StartCrop(pack, slot);
                DrawSlots();
            }));
            actions.Children.Add(IconButton("", "Make smaller", () => _ = Work([slot.Name], "Making smaller", ShrinkOne)));
            if (pack.IsCropped(slot.Name)) actions.Children.Add(IconButton("", "Restore original", () => Restore([slot.Name])));
            actions.Children.Add(IconButton("", "Remove", () => Remove([slot.Name]), danger: true));
        }
        Grid.SetColumn(actions, 4);
        grid.Children.Add(actions);

        var chosen = _slot?.Name == slot.Name;
        var row = new Border { Child = grid, Padding = new Thickness(6, 6, 6, 6), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, AllowDrop = true };
        void Paint(bool over)
        {
            if (chosen) row.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            else if (over || _selected.Contains(slot.Name)) row.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorTertiaryBrush");
            else row.Background = Brushes.Transparent;
            row.BorderBrush = chosen ? new SolidColorBrush(Accent()) : Brushes.Transparent;
        }
        Paint(false);
        if (chosen) row.Loaded += (_, _) => row.BringIntoView();
        row.MouseEnter += (_, _) => Paint(true);
        row.MouseLeave += (_, _) => Paint(false);
        row.MouseLeftButtonUp += (_, e) =>
        {
            // Ctrl+click selects, as in Explorer; a plain click shows the slot.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (!_selected.Remove(slot.Name)) _selected.Add(slot.Name);
                DrawSlots();
                return;
            }
            Pick(slot);
        };
        row.Drop += (_, drop) =>
        {
            drop.Handled = true;
            if (Dropped(drop) is { } file)
            {
                _slot = slot;
                Put(slot.Name, file);
            }
        };
        return row;
    }

    /// <summary>The bar over the list while rows are selected: what to do to all of them.</summary>
    private void DrawSelectionBar()
    {
        _selected.RemoveWhere(n => CharacterSlots.Find(n) is null);
        if (_selected.Count == 0 || _pack is not { } pack)
        {
            _selectionBar.Visibility = Visibility.Collapsed;
            return;
        }
        var line = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock { Text = $"{_selected.Count} selected", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        Button Text(string text, Action click, bool accent = false)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 2, 6, 2), Padding = new Thickness(10, 4, 10, 5) };
            if (accent) button.Style = (Style)FindResource("AccentButtonStyle");
            button.Click += (_, _) => click();
            return button;
        }
        line.Children.Add(Text("Make smaller", () => _ = Work(_selected.Where(pack.Has).ToList(), "Making smaller", ShrinkOne), accent: true));
        if (_selected.Any(pack.IsCropped)) line.Children.Add(Text("Restore original", () => Restore(_selected.Where(pack.IsCropped).ToList())));
        line.Children.Add(Text("Remove", () => Remove(_selected.Where(pack.Has).ToList())));
        line.Children.Add(Text("Select all shown", () =>
        {
            foreach (var name in ShownSlots()) _selected.Add(name);
            DrawSlots();
        }));
        line.Children.Add(Text("Clear", () =>
        {
            _selected.Clear();
            DrawSlots();
        }));
        _selectionBar.Child = line;
        _selectionBar.Padding = new Thickness(8, 4, 8, 4);
        _selectionBar.CornerRadius = new CornerRadius(8);
        _selectionBar.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        _selectionBar.Visibility = Visibility.Visible;
    }

    /// <summary>The slots the list shows now (search and filter applied).</summary>
    private IEnumerable<string> ShownSlots()
    {
        if (_pack is not { } pack) return [];
        var filter = _filter.ToLowerInvariant();
        return CharacterSlots.All
            .Where(s => filter.Length == 0 || s.Name.Contains(filter) || s.When.ToLowerInvariant().Contains(filter) || s.Place.ToLowerInvariant().Contains(filter))
            .Where(s => _show == 0 || (int)StateOf(pack, s) == _show - 1)
            .Select(s => s.Name);
    }

    private string ShrinkOne(string name)
    {
        var done = _pack!.Shrink(name);
        return done.After < done.Before
            ? $"{name}: {Size(done.Before)} to {Size(done.After)}, {done.Width} × {done.Height} px" + (done.Frames < done.FramesBefore ? $", {done.Frames} of {done.FramesBefore} frames." : ".")
            : $"{name} is already small.";
    }

    /// <summary>Do slow work to some slots in the background, one at a time; more can be added while it runs.</summary>
    private async Task Work(IReadOnlyList<string> names, string doing, Func<string, string> job)
    {
        var mine = names.Where(n => _busy.Add(n)).ToList();
        if (mine.Count == 0) return;
        _waiting += mine.Count;
        DrawSlots();
        foreach (var name in mine)
        {
            await _queue.WaitAsync();
            try
            {
                Say($"{doing} {name}… ({_waiting} waiting)");
                Say(await Task.Run(() => job(name)));
            }
            catch (Exception error)
            {
                Say($"{name} failed: {error.Message}");
            }
            finally
            {
                _queue.Release();
                _busy.Remove(name);
                _waiting--;
                Changed();
            }
        }
    }

    private void Remove(IReadOnlyList<string> names)
    {
        if (_pack is not { } pack || names.Count == 0) return;
        names = names.Where(n => !_busy.Contains(n)).ToList();
        if (names.Count > 1 && MessageBox.Show(this, $"Remove the pictures of {names.Count} slots? Their files are deleted.", "Nyatch Theme Editor", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        foreach (var name in names) pack.RemovePicture(name);
        _selected.ExceptWith(names);
        Say(names.Count == 1 ? $"{names[0]} removed." : $"{names.Count} pictures removed.");
        Changed();
    }

    private void Restore(IReadOnlyList<string> names)
    {
        if (_pack is not { } pack) return;
        names = names.Where(n => !_busy.Contains(n)).ToList();
        foreach (var name in names) pack.Uncrop(name);
        _crop = null;
        Say(names.Count == 1 ? $"{names[0]} is back to its original picture." : $"{names.Count} pictures are back to their originals.");
        Changed();
    }
}
