using System.Net;

namespace Collector;

record RawResult(int Status, long ElapsedMs, string RateHeaders);

class NeopleClient
{
    const string BaseUrl = "https://api.dfoneople.com/df";
    readonly HttpClient _http = new();
    readonly DateTime _created = DateTime.UtcNow;
    readonly string _apiKey;
    DateTime _lastCall = DateTime.MinValue;

    public NeopleClient(string apiKey) => _apiKey = apiKey;

    // Minimum pause between calls (0 = none).
    public TimeSpan MinGap { get; set; } = TimeSpan.FromMilliseconds(250);

    public int RequestCount { get; private set; }
    public int RateLimitHits { get; private set; }

    string BuildUrl(string path, string query) =>
        $"{BaseUrl}{path}?{(query.Length > 0 ? query + "&" : "")}apikey={_apiKey}";

    // One request with no throttle or retry, for measuring limits. Returns the outcome instead of throwing.
    public async Task<RawResult> SendOnceAsync(string path, string query = "")
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _http.GetAsync(BuildUrl(path, query));
        await response.Content.ReadAsStringAsync();
        timer.Stop();

        // Rate-limit related headers, if any.
        var limitHeaders = response.Headers
            .Where(h => h.Key.Contains("limit", StringComparison.OrdinalIgnoreCase) || h.Key.Contains("rate", StringComparison.OrdinalIgnoreCase) || h.Key.Contains("retry", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
        return new RawResult((int)response.StatusCode, timer.ElapsedMilliseconds, string.Join("; ", limitHeaders));
    }

    // Returns the raw JSON body. Retries 429/5xx with backoff.
    public async Task<string> GetAsync(string path, string query = "")
    {
        var url = BuildUrl(path, query);

        for (var attempt = 1; ; attempt++)
        {
            var wait = MinGap - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            _lastCall = DateTime.UtcNow;
            RequestCount++;

            using var response = await _http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) return body;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                RateLimitHits++;
                if (RateLimitHits == 1)
                    Console.WriteLine($"  FIRST 429 after {RequestCount} requests ({RequestCount / (DateTime.UtcNow - _created).TotalSeconds:F1} req/s average)");
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!retryable || attempt >= 4)
                throw new HttpRequestException($"{(int)response.StatusCode} on {path}: {body}");

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            Console.WriteLine($"  {(int)response.StatusCode} on {path}, retrying in {delay.TotalSeconds}s");
            await Task.Delay(delay);
        }
    }
}
