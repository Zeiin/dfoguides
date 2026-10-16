using System.Text.Json;
using System.Text.RegularExpressions;

namespace Collector;

// Reading dfmax's server-rendered pages. They embed their data in a __NUXT_DATA__ script as one flat JSON array in which every
// object property is an index into that array (the Nuxt "devalue" format), so { "x": 12 } means x is the value at position 12.
static partial class Dfmax
{
    public const string Host = "https://dfmax.xyz";

    [GeneratedRegex("<script[^>]*id=\"__NUXT_DATA__\"[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NuxtData();

    public static HttpClient NewClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dfoguides-tools/0.1 (personal hobby project)");
        return http;
    }

    // Each path segment is escaped on its own (class names are Korean, and some contain parentheses and spaces).
    public static string EscapePath(string path) => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    public static JsonElement Payload(string html) => JsonDocument.Parse(NuxtData().Match(html).Groups[1].Value).RootElement;

    // The string a property of an object points to, or null.
    public static string? Text(JsonElement root, JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var i) && i.ValueKind == JsonValueKind.Number && root[i.GetInt32()].ValueKind == JsonValueKind.String
            ? root[i.GetInt32()].GetString()
            : null;

    // The whole number a property of an object points to, or null.
    public static int? Number(JsonElement root, JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var i) && i.ValueKind == JsonValueKind.Number && root[i.GetInt32()].ValueKind == JsonValueKind.Number
            ? root[i.GetInt32()].GetInt32()
            : null;

    // Downloads a page once and keeps it in the given file.
    public static async Task<string> CachedTextAsync(HttpClient http, string url, string file, int pauseMs)
    {
        if (File.Exists(file)) return await File.ReadAllTextAsync(file);
        var text = await http.GetStringAsync(url);
        await File.WriteAllTextAsync(file, text);
        await Task.Delay(pauseMs);
        return text;
    }
}
