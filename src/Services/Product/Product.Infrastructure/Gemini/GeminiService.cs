using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Product.Domain.Entities;

namespace Product.Infrastructure.Gemini;

public class GeminiEmbeddingService : IAiEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string ModelUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";

    public GeminiEmbeddingService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _apiKey = config["Gemini:ApiKey"]
            ?? throw new ArgumentNullException("Gemini:ApiKey is missing");
    }

    public async Task<float[]> GetVectorAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        var requestUrl = $"{ModelUrl}?key={_apiKey}";

        var payload = new
        {
            model = "models/text-embedding-004",
            content = new { parts = new[] { new { text = text } } }
        };

        var response = await _httpClient.PostAsJsonAsync(requestUrl, payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiEmbeddingResponse>();
        return result?.Embedding?.Values ?? throw new Exception("Không thể parse vector từ Gemini.");
    }
}