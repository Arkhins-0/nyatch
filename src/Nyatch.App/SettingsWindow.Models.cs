using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.App;

// LLM services › Models: every LLM's models in a table each (models.json, read from GitHub), with what each is
// for, what it can take and what it costs, and a button to use one.
internal sealed partial class SettingsWindow
{
    private bool _modelsView;
    private string _modelsFilter = "all";
    private ModelCatalog? _catalog;
    private bool _catalogFetched;

    private static string KeptCatalog => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch", "models.json");
    private static string ShippedCatalog => Path.Combine(AppContext.BaseDirectory, "models.json");

    /// <summary>The button on the LLM services page's head.</summary>
    private Button ModelsButton()
    {
        var button = GhostButton("Models");
        button.ToolTip = "Every LLM's models: what each is for, what it can take, and what it costs";
        button.Click += (_, _) =>
        {
            _modelsView = true;
            ShowPage();
        };
        return button;
    }

    private void ShowModels()
    {
        _catalog ??= ModelCatalog.Kept(KeptCatalog, ShippedCatalog);
        if (!_catalogFetched)
        {
            // The newest list, from GitHub; the page is drawn again with it if it is still on show.
            _catalogFetched = true;
            _ = ModelCatalog.FetchAsync(KeptCatalog).ContinueWith(t =>
            {
                if (t.Result is not { } fresh) return;
                Dispatcher.BeginInvoke(() =>
                {
                    _catalog = fresh;
                    if (_modelsView && _page.Id == "chats") ShowPage(keepScroll: true);
                });
            });
        }

        var back = GhostButton("Back to services");
        back.Click += (_, _) =>
        {
            _modelsView = false;
            ShowPage();
        };
        PageStrip("Models", "Most used first, for each LLM: what each is for, what it can take and what it costs.", back);
        if (_catalog is not { } catalog)
        {
            Note("The list could not be read. Check the connection and open this page again.");
            return;
        }
        Note($"{catalog.About} Updated {catalog.Updated}.");

        _content.Children.Add(Segments([("all", "All"), ("free", "Free"), ("pictures", "Sees pictures")], _modelsFilter, value =>
        {
            _modelsFilter = value;
            ShowPage(keepScroll: true);
        }));

        // Connected services first.
        var services = catalog.Services
            .OrderByDescending(s => Assistant.Parse(s.Id) is var e && Assistant.Secret(e) is { } id && Secrets.Has(id))
            .ToList();
        foreach (var service in services)
        {
            var engine = Assistant.Parse(service.Id);
            var models = service.Models.Where(m => _modelsFilter switch
            {
                "free" => m.Tags.Contains("free"),
                "pictures" => m.Tags.Contains("pictures"),
                _ => true,
            }).ToList();
            if (models.Count == 0) continue;
            var connected = Assistant.Secret(engine) is { } secret && Secrets.Has(secret);
            var strip = SectionStrip(service.Name, models.Count);
            strip.Margin = new Thickness(strip.Margin.Left, 26, strip.Margin.Right, strip.Margin.Bottom);
            Note((connected ? "" : "Not connected. ") + service.Note);
            var current = engine == AssistantEngine.None ? "" : _settings.Get(Assistant.ModelSetting(engine));
            var table = new StackPanel();
            foreach (var model in models) table.Children.Add(ModelRow(engine, model, current, models.IndexOf(model) == models.Count - 1));
            _content.Children.Add(new Border
            {
                Child = table, Background = _panel, BorderBrush = _line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 0, 6),
            });
        }
    }

    /// <summary>A row of a model table: the model (its name, and the name to use), what it is for, its tags, its price, and Use.</summary>
    private FrameworkElement ModelRow(AssistantEngine engine, CatalogModel model, string current, bool last)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        name.Children.Add(new TextBlock { Text = model.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _text, TextWrapping = TextWrapping.Wrap });
        name.Children.Add(new TextBlock
        {
            Text = model.Id, FontSize = 12, Foreground = _faint, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), ToolTip = "The name to use in the settings",
        });
        grid.Children.Add(name);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        middle.Children.Add(new TextBlock { Text = model.For, FontSize = 13, Foreground = _soft, TextWrapping = TextWrapping.Wrap });
        var tags = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var free = model.Tags.Contains("free");
        tags.Children.Add(Chip(free ? "Free" : model.Price is [var i, var o] ? $"${Money(i)} / ${Money(o)}" : "Paid",
            free ? _accent : _soft, free ? _accentTint : _high, numbers: !free, margin: new Thickness(0, 0, 6, 4)));
        foreach (var tag in model.Tags.Where(t => t != "free"))
        {
            var chip = Chip(TagWords(tag), _soft, _high, margin: new Thickness(0, 0, 6, 4));
            if (_catalog?.Tags.TryGetValue(tag, out var meaning) == true) chip.ToolTip = meaning;
            tags.Children.Add(chip);
        }
        middle.Children.Add(tags);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        FrameworkElement use;
        if (engine == AssistantEngine.None)
        {
            use = new Border();
        }
        else if (string.Equals(current, model.Id, StringComparison.Ordinal))
        {
            use = Chip("In use", _accent, _accentTint, margin: new Thickness(0));
        }
        else
        {
            var button = GhostButton("Use");
            button.ToolTip = $"Chat with {model.Name} on {Assistant.Name(engine)}";
            button.Click += (_, _) =>
            {
                _settings.Set(Assistant.ModelSetting(engine), model.Id);
                Changed();
                ShowPage(keepScroll: true);
            };
            use = button;
        }
        use.HorizontalAlignment = HorizontalAlignment.Right;
        use.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(use, 2);
        grid.Children.Add(use);

        return new Border { Child = grid, BorderBrush = _line, BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
    }

    private static string Money(double value) => value.ToString(value < 1 ? "0.##" : "0.##", CultureInfo.InvariantCulture);

    private static string TagWords(string tag) => tag switch
    {
        "pictures" => "Pictures",
        "pdfs" => "PDFs",
        "text-only" => "Text only",
        "best" => "Best",
        "balanced" => "Balanced",
        "cheap" => "Low cost",
        "fast" => "Fast",
        "reasoning" => "Reasoning",
        "coding" => "Coding",
        "long-context" => "Long documents",
        "web-search" => "Web search",
        _ => tag,
    };
}
