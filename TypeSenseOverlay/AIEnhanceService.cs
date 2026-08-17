using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSenseOverlay;

internal enum EnhanceMode
{
    Enhance,
    Fix,
    Rewrite,
    Shorten,
    Expand,
    Formal,
    Casual,
    Translate
}

internal sealed class AIEnhanceResult
{
    public bool Success { get; init; }
    public string Text { get; init; } = "";
    public string Error { get; init; } = "";
}

internal sealed class AIEnhanceService : IDisposable
{
    private readonly HttpClient _http;
    private readonly UserSettings _settings;

    public AIEnhanceService(UserSettings settings)
    {
        _settings = settings;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.OllamaTimeoutSeconds, 2, 60))
        };
    }

    public async Task<AIEnhanceResult> EnhanceAsync(
        string selectedText,
        EnhanceMode mode,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
            return new AIEnhanceResult { Error = "Select some text first." };

        string instruction = mode switch
        {
            EnhanceMode.Fix => "Fix grammar, spelling and punctuation while preserving the original meaning and voice.",
            EnhanceMode.Rewrite => "Rewrite the text to be clearer and more natural while preserving the meaning and the writer's voice.",
            EnhanceMode.Shorten => "Make the text substantially shorter while preserving the important meaning.",
            EnhanceMode.Expand => "Expand the text with useful detail while preserving the original meaning and voice.",
            EnhanceMode.Formal => "Rewrite the text in a polished professional tone without making it unnecessarily corporate.",
            EnhanceMode.Casual => "Rewrite the text in a natural, friendly and conversational tone.",
            EnhanceMode.Translate => "Translate the following text into fluent English. Preserve the original tone and meaning.",
            _ => "Improve clarity, grammar, flow and naturalness while preserving the writer's original meaning and voice. Do not add facts."
        };

        string prompt = $"""
You are Grey Board's local writing enhancer.
Task: {instruction}

Rules:
- Return ONLY the improved text. No quotes, labels, explanation, markdown or preamble.
- Preserve names, numbers, URLs, technical terms and facts unless correcting an obvious typo.
- Do not make the text sound like generic AI or corporate copy.
- Keep the user's intent and personality.
""";

        if (!string.IsNullOrWhiteSpace(context))
            prompt += $"\nNearby context (use only for understanding):\n{context}\n";

        prompt += $"\nText to transform:\n{selectedText}";

        try
        {
            var body = new
            {
                model = _settings.OllamaModel,
                prompt,
                stream = false,
                options = new
                {
                    temperature = 0.25
                }
            };

            string json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _settings.OllamaEndpoint.TrimEnd('/') + "/api/generate");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new AIEnhanceResult { Error = $"Ollama returned {(int)response.StatusCode}: {response.ReasonPhrase}" };

            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("response", out JsonElement result))
                return new AIEnhanceResult { Error = "Ollama returned no response text." };

            string output = result.GetString() ?? "";
            output = output.Trim();
            if (output.Length == 0)
                return new AIEnhanceResult { Error = "The model returned an empty result." };

            return new AIEnhanceResult { Success = true, Text = output };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AIEnhanceResult { Error = "Ollama timed out. Check the local model or lower the context size." };
        }
        catch (HttpRequestException)
        {
            return new AIEnhanceResult { Error = "Ollama is not reachable. Start Ollama and make sure the configured model is installed locally." };
        }
        catch (Exception ex)
        {
            return new AIEnhanceResult { Error = ex.Message };
        }
    }

    public void Dispose() => _http.Dispose();
}