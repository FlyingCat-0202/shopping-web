using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Product.Domain.Entities;

namespace Product.Infrastructure.AISearch;

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

public class LocalEmbeddingService : IAiEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;

    public LocalEmbeddingService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;

        // Aspire sẽ tự động tiêm URL vào biến này thông qua Service Discovery
        // "infinity-ai" chính là tên bạn đã đặt trong AppHost
        _apiUrl = config["services:infinity-ai:api:0"] ?? "http://localhost:7997";
    }

    public async Task<float[]> GetVectorAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

        var requestUrl = $"{_apiUrl}/embeddings";

        // Payload chuẩn của OpenAI mà Infinity hỗ trợ
        var payload = new
        {
            model = "BAAI/bge-m3",
            input = new[] { text }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(requestUrl, payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<InfinityEmbeddingResponse>();

            // Trả về mảng vector đầu tiên
            return result?.Data?.FirstOrDefault()?.Embedding ?? throw new Exception("Infinity: Vetor bị null.");
        }
        catch (Exception ex)
        {
            // Trả về null nếu có lỗi để hệ thống tự động Fallback sang BM25 Full-text
            Console.WriteLine($"Lỗi khi gọi Infinity AI: {ex.Message}");
            return Array.Empty<float>();
        }
    }
}