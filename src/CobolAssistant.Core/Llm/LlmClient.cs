using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CobolAssistant.Core.Llm;

public sealed class OpenAiCompatibleClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public OpenAiCompatibleClient(HttpClient http, LlmOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Kein Sprachmodell konfiguriert.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl!.TrimEnd('/')}/chat/completions");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        request.Content = JsonContent.Create(new ChatRequest(_options.Model, new[]
        {
            new ChatMessage("system", system),
            new ChatMessage("user", user)
        }, 0.1));

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        return body?.Choices.FirstOrDefault()?.Message.Content?.Trim() ?? string.Empty;
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage Message);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices);
}
