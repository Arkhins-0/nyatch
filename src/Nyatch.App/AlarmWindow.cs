using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// An alarm's own window, from the Focus page: its time, its label, the days it rings on, and its sound. A new
/// alarm, or one to change or delete. Enter saves, Esc closes. In the settings window's colours.
/// </summary>
internal sealed class AlarmWindow : Window
{
    public event Action<Alarm>? Saved;
    public event Action<string>? Deleted;

    private readonly Alarm _alarm;
    private readonly TextBox _hour, _minute, _label;
    private readonly ToggleBox[] _days;
    private readonly AlarmDays[] _dayFlags;
    private readonly bool _twelveHour;
    private readonly ToggleBox? _am, _pm;
    private readonly string _settingsSound;
    private string _sound;
    private readonly TextBlock _soundName;
    private readonly Button _soundDefault;
    private readonly SolidColorBrush _panel, _high, _line, _text, _soft, _accent;
    private readonly Color _onAccent;

    /// <param name="twelveHour">The time with AM and PM (as the clock is set in Nyatch's settings).</param>
    /// <param name="firstDay">The day the week starts on (Nyatch's settings).</param>
    public AlarmWindow(Alarm? alarm, string settingsSound, string theme, bool twelveHour, DayOfWeek firstDay)
    {
        _alarm = alarm ?? new Alarm(Guid.NewGuid().ToString("N"), 7, 0);
        _twelveHour = twelveHour;
        _settingsSound = settingsSound;
        _sound = _alarm.Sound;

        // The settings window's colours.
        if (theme is not ("light" or "dark" or "black")) theme = Native.IsLightTheme() ? "light" : "dark";
        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var (panel, high, line, text, soft) = theme switch
        {
            "dark" => ("#2B2B2B", "#383838", "#3A3A3A", "#FFFFFF", "#C5C5C5"),
            "black" => ("#111111", "#1F1F1F", "#2A2A2A", "#FFFFFF", "#BDBDBD"),
            _ => ("#FFFFFF", "#F3F3F3", "#E0E0E0", "#1A1A1A", "#5D5D5D"),
        };
        var (accent, _) = SettingsWindow.WindowsAccent(theme == "light");
        _panel = new SolidColorBrush(C(panel));
        _high = new SolidColorBrush(C(high));
        _line = new SolidColorBrush(C(line));
        _text = new SolidColorBrush(C(text));
        _soft = new SolidColorBrush(C(soft));
        _accent = new SolidColorBrush(accent);
        _onAccent = theme == "light" ? Colors.White : Colors.Black;

        Title = alarm is null ? "New alarm" : "Alarm";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 452;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = _text;
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/nyatch.ico"));
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UriFormatException)
        {
        }

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };

        // Head: the title, and a close button.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var close = Plain("", "Close", glyph: true);
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.MouseLeftButtonDown += (_, _) => DragMove();
        body.Children.Add(head);

        // The time, as two boxes: type it, or turn the wheel or press the arrows. With a 12-hour clock, AM and PM beside it.
        var time = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        var shownHour = twelveHour ? (_alarm.Hour % 12 == 0 ? 12 : _alarm.Hour % 12) : _alarm.Hour;
        _hour = twelveHour ? TimeBox(shownHour, 1, 12) : TimeBox(shownHour, 0, 23);
        _minute = TimeBox(_alarm.Minute, 0, 59);
        time.Children.Add(_hour);
        time.Children.Add(new TextBlock { Text = ":", FontSize = 40, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, -6, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        time.Children.Add(_minute);
        if (twelveHour)
        {
            var halves = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _am = new ToggleBox(this, "AM", "AM", _alarm.Hour < 12, width: 52, height: 33);
            _pm = new ToggleBox(this, "PM", "PM", _alarm.Hour >= 12, width: 52, height: 33);
            _am.Changed += () => _pm.On = !_am.On;
            _pm.Changed += () => _am.On = !_pm.On;
            halves.Children.Add(_am.Element);
            halves.Children.Add(_pm.Element);
            time.Children.Add(halves);
        }
        body.Children.Add(time);

        // The label.
        body.Children.Add(Caption("Label"));
        _label = new TextBox
        {
            Text = _alarm.Label, Height = 36, FontSize = 14, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center,
            Background = _high, Foreground = _text, CaretBrush = _text, BorderBrush = _line, BorderThickness = new Thickness(1), MaxLength = 60,
            Template = PlainBox(6), Margin = new Thickness(0, 0, 0, 18),
        };
        FocusEdge(_label);
        _label.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Label");
        body.Children.Add(_label);

        // The days, from the first day of the week in the settings: a box each, and quick choices.
        body.Children.Add(Caption("Repeat"));
        var order = firstDay == DayOfWeek.Sunday ? Alarm.Week.Skip(6).Concat(Alarm.Week.Take(6)).ToArray() : [.. Alarm.Week];
        _dayFlags = order.Select(d => d.Flag).ToArray();
        var week = new UniformGrid { Columns = 7, Margin = new Thickness(-3, 0, -3, 8) };
        _days = order.Select(d => new ToggleBox(this, d.Short[..1], d.Short, _alarm.Days.HasFlag(d.Flag))).ToArray();
        foreach (var day in _days) week.Children.Add(day.Element);
        body.Children.Add(week);
        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        foreach (var (words, days) in new[] { ("Once", AlarmDays.None), ("Weekdays", AlarmDays.Weekdays), ("Weekends", AlarmDays.Weekend), ("Every day", AlarmDays.Every) })
        {
            var pick = Plain(words, words);
            pick.Margin = new Thickness(0, 0, 6, 6);
            pick.Click += (_, _) =>
            {
                for (var i = 0; i < _days.Length; i++) _days[i].On = days.HasFlag(_dayFlags[i]);
            };
            quick.Children.Add(pick);
        }
        body.Children.Add(quick);

        // The sound: this alarm's own, or the one in the settings.
        body.Children.Add(Caption("Sound"));
        var sound = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var hear = Plain("", "Play it", glyph: true);
        hear.Click += (_, _) => Ringer.Try(_sound.Length > 0 ? _sound : _settingsSound);
        var choose = Plain("Choose…", "Choose a sound");
        choose.Margin = new Thickness(6, 0, 0, 0);
        choose.Click += (_, _) =>
        {
            var pick = new Microsoft.Win32.OpenFileDialog { Title = "Choose a sound", Filter = "Sounds (*.wav;*.mp3;*.wma;*.m4a)|*.wav;*.mp3;*.wma;*.m4a" };
            if (pick.ShowDialog(this) != true) return;
            _sound = pick.FileName;
            ShowSound();
        };
        _soundDefault = Plain("Default", "Use the alarm sound in the settings");
        _soundDefault.Margin = new Thickness(6, 0, 0, 0);
        _soundDefault.Click += (_, _) =>
        {
            _sound = "";
            ShowSound();
        };
        buttons.Children.Add(hear);
        buttons.Children.Add(choose);
        buttons.Children.Add(_soundDefault);
        DockPanel.SetDock(buttons, Dock.Right);
        sound.Children.Add(buttons);
        _soundName = new TextBlock { Foreground = _soft, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        sound.Children.Add(_soundName);
        body.Children.Add(sound);
        ShowSound();

        // Delete (an alarm already made), Cancel and Save.
        var foot = new DockPanel();
        var save = Plain("Save", "Save", primary: true);
        save.Margin = new Thickness(8, 0, 0, 0);
        save.Click += (_, _) => Save();
        var cancel = Plain("Cancel", "Cancel");
        cancel.Click += (_, _) => Close();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, save } };
        DockPanel.SetDock(right, Dock.Right);
        foot.Children.Add(right);
        if (alarm is not null)
        {
            var delete = Plain("Delete", "Delete this alarm");
            ((TextBlock)((Border)delete.Content).Child).Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x4A, 0x4A));
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            delete.Click += (_, _) =>
            {
                Deleted?.Invoke(_alarm.Id);
                Close();
            };
            foot.Children.Add(delete);
        }
        else foot.Children.Add(new Border());
        body.Children.Add(foot);

        Content = new Border
        {
            Child = body,
            Background = _panel,
            BorderBrush = _line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black },
        };

        PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Escape)
            {
                key.Handled = true;
                Close();
            }
            else if (key.Key == Key.Enter)
            {
                key.Handled = true;
                Save();
            }
        };
        Loaded += (_, _) =>
        {
            _hour.Focus();
            _hour.SelectAll();
        };
        // A sound being tried here stops with the window, however it is closed.
        Closed += (_, _) => Ringer.StopTrying();
    }

    private void ShowSound()
    {
        _soundName.Text = _sound.Length > 0 ? Path.GetFileNameWithoutExtension(_sound)
            : _settingsSound.Length > 0 ? Path.GetFileNameWithoutExtension(_settingsSound) + " (from Settings)" : "Windows alarm (from Settings)";
        _soundName.ToolTip = _sound.Length > 0 ? _sound : null;
        _soundDefault.Visibility = _sound.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }


    private void Save()
    {
        var minute = Whole(_minute.Text, 0, 59);
        var hour = _twelveHour ? Whole(_hour.Text, 1, 12) % 12 + (_pm?.On == true ? 12 : 0) : Whole(_hour.Text, 0, 23);
        var days = AlarmDays.None;
        for (var i = 0; i < _days.Length; i++)
        {
            if (_days[i].On) days |= _dayFlags[i];
        }
        // Saving turns it on: a time was just set for it.
        Saved?.Invoke(_alarm with { Hour = hour, Minute = minute, Label = _label.Text.Trim(), Days = days, On = true, Sound = _sound });
        Close();
    }

    private static int Whole(string text, int min, int max) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : min;

    // ---------- Parts ----------

    /// <summary>A two-digit box for the hour or the minute: typed, or turned with the wheel and the arrow keys.</summary>
    private TextBox TimeBox(int value, int min, int max)
    {
        var box = new TextBox
        {
            Text = value.ToString("00", CultureInfo.InvariantCulture),
            Width = 96, Height = 76, FontSize = 44, FontWeight = FontWeights.SemiBold, MaxLength = 2,
            TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
            Background = _high, Foreground = _text, CaretBrush = _text, BorderBrush = _line, BorderThickness = new Thickness(1),
            Template = PlainBox(8),
        };
        FocusEdge(box);
        box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, max == 59 ? "Minute" : "Hour");
        void Step(int by)
        {
            var span = max - min + 1;
            var n = ((Whole(box.Text, min, max) - min + by) % span + span) % span + min;
            box.Text = n.ToString("00", CultureInfo.InvariantCulture);
            box.SelectAll();
        }
        box.PreviewMouseWheel += (_, wheel) =>
        {
            wheel.Handled = true;
            Step(wheel.Delta > 0 ? 1 : -1);
        };
        box.PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Up) Step(1);
            else if (key.Key == Key.Down) Step(-1);
            else return;
            key.Handled = true;
        };
        box.PreviewTextInput += (_, input) => input.Handled = !input.Text.All(char.IsDigit);
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        // A click puts the whole number up for typing over, rather than a caret in it.
        box.PreviewMouseLeftButtonDown += (_, click) =>
        {
            if (box.IsKeyboardFocusWithin) return;
            click.Handled = true;
            box.Focus();
        };
        box.LostKeyboardFocus += (_, _) => box.Text = Whole(box.Text, min, max).ToString("00", CultureInfo.InvariantCulture);
        return box;
    }

    /// <summary>
    /// A text box drawn plainly: a rounded box and the text, with none of the extras of the Windows look (its
    /// clear button and underline did not fit a big, short box).
    /// </summary>
    private static ControlTemplate PlainBox(double radius)
    {
        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(SnapsToDevicePixelsProperty, true);
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(VerticalAlignmentProperty, new TemplateBindingExtension(VerticalContentAlignmentProperty));
        host.SetValue(MarginProperty, new TemplateBindingExtension(PaddingProperty));
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(FocusableProperty, false);
        border.AppendChild(host);
        template.VisualTree = border;
        return template;
    }

    /// <summary>The box's edge in the accent while it has the keyboard.</summary>
    private void FocusEdge(TextBox box)
    {
        box.GotKeyboardFocus += (_, _) => box.BorderBrush = _accent;
        box.LostKeyboardFocus += (_, _) => box.BorderBrush = _line;
    }

    /// <summary>A small caption over a part.</summary>
    private TextBlock Caption(string text) => new() { Text = text, Foreground = _soft, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 8) };

    /// <summary>A button with rounded corners: plain, or in the accent (the main one).</summary>
    private Button Plain(string text, string name, bool primary = false, bool glyph = false)
    {
        var back = primary ? _accent : _high;
        var face = new Border { CornerRadius = new CornerRadius(6), Background = back, Padding = new Thickness(glyph ? 9 : 14, 6, glyph ? 9 : 14, 6), BorderBrush = _line, BorderThickness = new Thickness(primary ? 0 : 1), SnapsToDevicePixels = true };
        var words = new TextBlock
        {
            Text = text, FontSize = glyph ? 12 : 13.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = glyph ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") : FontFamily,
            Foreground = primary ? new SolidColorBrush(_onAccent) : _text,
        };
        face.Child = words;
        var button = new Button { Content = face, Cursor = Cursors.Hand, Focusable = true, Template = ButtonTemplate() };
        button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, name);
        button.MouseEnter += (_, _) => face.Opacity = 0.85;
        button.MouseLeave += (_, _) => face.Opacity = 1;
        return button;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return template;
    }

    /// <summary>A box that is on or off (a day, AM or PM): filled in the accent while on, plain with an edge while off.</summary>
    private sealed class ToggleBox
    {
        private readonly AlarmWindow _window;
        private readonly Border _face;
        private readonly TextBlock _letter;
        private bool _on;

        public event Action? Changed;

        public ToggleBox(AlarmWindow window, string letter, string name, bool on, double width = double.NaN, double height = 40)
        {
            _window = window;
            _letter = new TextBlock { Text = letter, FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _face = new Border { Width = width, Height = height, Margin = new Thickness(3), CornerRadius = new CornerRadius(8), Child = _letter, Cursor = Cursors.Hand, Focusable = true, SnapsToDevicePixels = true };
            _face.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, name);
            _face.MouseLeftButtonUp += (_, _) => Flip();
            _face.KeyDown += (_, key) =>
            {
                if (key.Key is not Key.Space) return;
                key.Handled = true;
                Flip();
            };
            _face.MouseEnter += (_, _) => _face.Opacity = 0.88;
            _face.MouseLeave += (_, _) => _face.Opacity = 1;
            On = on;
        }

        public FrameworkElement Element => _face;

        private void Flip()
        {
            On = !On;
            Changed?.Invoke();
        }

        public bool On
        {
            get => _on;
            set
            {
                _on = value;
                _face.Background = value ? _window._accent : _window._high;
                _face.BorderBrush = _window._line;
                _face.BorderThickness = new Thickness(value ? 0 : 1);
                _letter.Foreground = value ? new SolidColorBrush(_window._onAccent) : _window._text;
            }
        }
    }
}
