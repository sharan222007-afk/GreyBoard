using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSenseOverlay;

internal sealed class OllamaSuggestionService : IDisposable
{
    private readonly HttpClient _http;
    private readonly UserSettings _settings;

    public OllamaSuggestionService(UserSettings settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.OllamaTimeoutSeconds, 2, 30)) };
    }

    public async Task<List<string>> GetSuggestionsAsync(string previous, string prefix, CancellationToken cancellationToken)
    {
        if (!_settings.AIEnhanceEnabled || string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2)
            return new List<string>();

        string prompt =
            "You are a fast autocomplete engine inside Grey Board.\n" +
            "Previous word: " + previous + "\n" +
            "Current partial word: " + prefix + "\n\n" +
            "Return ONLY valid JSON in this exact shape:\n" +
            "{\"suggestions\":[\"word1\",\"word2\",\"word3\"]}\n\n" +
            "Rules:\n" +
            "- Complete the current partial word, do not replace it with an unrelated word.\n" +
            "- Each suggestion must start with the exact partial word, case-insensitively.\n" +
            "- Prefer natural, common words and the previous-word context.\n" +
            "- No explanations, punctuation, markdown or extra keys.\n";

        try
        {
            var body = new
            {
                model = _settings.OllamaModel,
                prompt,
                stream = false,
                format = "json",
                options = new { temperature = 0.1 }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.OllamaEndpoint.TrimEnd('/') + "/api/generate");
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new List<string>();

            string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument outer = JsonDocument.Parse(raw);
            if (!outer.RootElement.TryGetProperty("response", out JsonElement responseText))
                return new List<string>();

            string json = responseText.GetString() ?? "";
            using JsonDocument result = JsonDocument.Parse(json);
            if (!result.RootElement.TryGetProperty("suggestions", out JsonElement suggestions) || suggestions.ValueKind != JsonValueKind.Array)
                return new List<string>();

            var output = new List<string>(3);
            foreach (JsonElement item in suggestions.EnumerateArray())
            {
                string? word = item.GetString();
                if (string.IsNullOrWhiteSpace(word) ||
                    !word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!output.Contains(word, StringComparer.OrdinalIgnoreCase))
                    output.Add(word.Trim());
                if (output.Count == 3) break;
            }
            return output;
        }
        catch
        {
            return new List<string>();
        }
    }

    public void Dispose() => _http.Dispose();
}
