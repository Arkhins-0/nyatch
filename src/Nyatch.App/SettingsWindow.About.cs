using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nyatch.App;

// About, and the licence and notice that come with Nyatch, shown in full.
internal sealed partial class SettingsWindow
{
    private const string Website = "https://arkhins.com";
    private const string Email = "arkhins@arkhins.com";

    private void ShowAbout()
    {
        var head = new Grid { Margin = new Thickness(0, 4, 0, 24) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var logo = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/nyatch-logo.png")), Width = 56, Height = 56 };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        head.Children.Add(new Border { Child = logo, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = "Nyatch", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = _text, FontFamily = Numbers, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Chip(_updater.Current.ToString(), _accent, _accentTint, numbers: true, margin: new Thickness(10, 3, 0, 0)));
        words.Children.Add(title);
        words.Children.Add(new TextBlock { Text = "By Arkhins", FontSize = 13, Foreground = _soft, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(words, 1);
        head.Children.Add(words);
        // With a character on: her picture, waving, at the right.
        if (HerPictureIfOn(64, "about", "happy", "idle") is { } her)
        {
            her.HorizontalAlignment = HorizontalAlignment.Right;
            her.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(her, 1);
            head.Children.Add(her);
        }
        _content.Children.Add(head);

        var rows = Rows();
        var site = GhostButton("arkhins.com");
        site.Click += (_, _) => Native.Open(Website);
        Row(rows, "Website", "", site, hover: false);
        var mail = GhostButton("Email");
        mail.Click += (_, _) => Feedback.SendFeedback();
        Row(rows, "Contact", Email, mail, hover: false);

        rows = Rows();
        Row(rows, $"© {Math.Max(2026, DateTime.Now.Year)} Arkhins", "Apache License 2.0. The name and logo are not covered by the licence.", null, hover: false);
        Row(rows, "Terms", "", GoButton("Open Terms", () => OpenPage("terms")));
        Row(rows, "Notice", "", GoButton("Open Notice", () => OpenPage("notice")));
    }

    /// <summary>The Terms page: the services Nyatch reaches, and the licence in full.</summary>
    private void ShowTerms()
    {
        Head(_page);
        SectionStrip("Services", null);
        var rows = Rows();
        Row(rows, "Weather", "wttr.in", null, hover: false);
        Row(rows, "Lyrics", "Better Lyrics and LRCLIB", null, hover: false);
        Row(rows, "Quick Lookup", "Wiktionary, Free Dictionary API and Wikipedia", null, hover: false);
        Row(rows, "Online page", "Cloudflare", null, hover: false);
        Row(rows, "Chats", "Anthropic, OpenAI and Google, with your own keys", null, hover: false);
        Note("Each is used under its own terms.");
        if (_content.Children[^1] is TextBlock note) note.Margin = new Thickness(2, -12, 2, 22);

        SectionStrip("Licence", null);
        ShowShipped("LICENSE", "The licence is not beside Nyatch on this PC. It is the Apache License 2.0: apache.org/licenses/LICENSE-2.0");
    }

    /// <summary>The Notice page: the notice that comes with Nyatch, in full.</summary>
    private void ShowNotice()
    {
        Head(_page);
        ShowShipped("NOTICE", "The notice is not beside Nyatch on this PC.");
    }

    /// <summary>A file that ships beside Nyatch, as selectable text in a card.</summary>
    private void ShowShipped(string name, string missing)
    {
        string? text = null;
        foreach (var folder in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..") })
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            if (!File.Exists(path)) continue;
            try
            {
                text = File.ReadAllText(path).Replace("\r\n", "\n").Trim();
            }
            catch (IOException)
            {
            }
            break;
        }
        var box = new TextBox
        {
            Text = text ?? missing,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = _soft,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Padding = new Thickness(0),
        };
        _content.Children.Add(new Border
        {
            Child = box,
            Background = _panel,
            BorderBrush = _line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 0, 0, 20),
        });
    }
}
