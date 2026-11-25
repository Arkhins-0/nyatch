using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyatch.Core;

/// <summary>
/// The models each LLM service offers, most used first, with what each is for and can take (models.json at the top
/// of the repository). Read from GitHub, so a new list needs no new Nyatch; the last one read is kept for when
/// there is no connection, and a copy ships with the app for the very first time.
/// </summary>
public sealed record ModelCatalog(
    [property: JsonPropertyName("updated")] string Updated,
    [property: JsonPropertyName("about")] string About,
    [property: JsonPropertyName("tags")] Dictionary<string, string> Tags,
    [property: JsonPropertyName("services")] List<CatalogService> Services)
{
    public const string Url = "https://raw.githubusercontent.com/Arkhins-0/nyatch/refs/heads/main/models.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static ModelCatalog? Parse(string json)
    {
        try
        {
            var catalog = JsonSerializer.Deserialize<ModelCatalog>(json);
            return catalog is { Services.Count: > 0 } ? catalog : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The list kept from the last read, or the copy that came with the app; null if neither can be read.</summary>
    public static ModelCatalog? Kept(string keptPath, string shippedPath)
    {
        foreach (var path in new[] { keptPath, shippedPath })
        {
            try
            {
                if (File.Exists(path) && Parse(File.ReadAllText(path)) is { } catalog) return catalog;
            }
            catch (IOException)
            {
            }
        }
        return null;
    }

    /// <summary>The list on GitHub, kept for next time; null when it cannot be read (the kept one stands).</summary>
    public static async Task<ModelCatalog?> FetchAsync(string keptPath, CancellationToken cancel = default)
    {
        try
        {
            var json = await Http.GetStringAsync(Url, cancel).ConfigureAwait(false);
            if (Parse(json) is not { } catalog) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(keptPath)!);
            await File.WriteAllTextAsync(keptPath, json, cancel).ConfigureAwait(false);
            return catalog;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>One LLM service: its id as Nyatch knows it ("groq"), whether it has free models, a note, and its models.</summary>
public sealed record CatalogService(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("free")] bool Free,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("models")] List<CatalogModel> Models);

/// <summary>A model: the name to use in the settings, a readable name, what it is for, its tags, and its price per million tokens (in, out), if it has one.</summary>
public sealed record CatalogModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("for")] string For,
    [property: JsonPropertyName("tags")] List<string> Tags,
    [property: JsonPropertyName("price")] double[]? Price = null);
