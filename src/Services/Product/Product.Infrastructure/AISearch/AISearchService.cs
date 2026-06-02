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

    public async Task<float[]> GetVectorAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        var requestUrl = $"{ModelUrl}?key={_apiKey}";

        var payload = new
        {
            model = "models/text-embedding-004",
            content = new { parts = new[] { new { text = text } } }
        };

        var response = await _httpClient.PostAsJsonAsync(requestUrl, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiEmbeddingResponse>(cancellationToken);
        return result?.Embedding?.Values ?? throw new Exception("Không thể parse vector từ Gemini.");
    }

    // Gemini không có batch endpoint nên fallback: gọi song song, giới hạn 5 request đồng thời
    // để tránh hit rate-limit của Gemini API.
    public async Task<float[]?[]> GetVectorsAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        var results = new float[]?[texts.Length];
        await Parallel.ForEachAsync(
            texts.Select((text, i) => (text, i)),
            new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = cancellationToken },
            async (item, ct) =>
            {
                try   { results[item.i] = await GetVectorAsync(item.text, ct); }
                catch { results[item.i] = null; }
            });
        return results;
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

    public async Task<float[]> GetVectorAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

        var vectors = await GetVectorsAsync([text], cancellationToken);
        return vectors[0] ?? Array.Empty<float>();
    }

    // Infinity AI hỗ trợ input là string[] → gộp toàn bộ texts thành 1 HTTP request duy nhất.
    // Đây là chì khóa tối ưu: thay vì N round-trip, chỉ còn 1 round-trip bất kể batch size.
    public async Task<float[]?[]> GetVectorsAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        if (texts.Length == 0) return [];

        var requestUrl = $"{_apiUrl}/embeddings";

        var payload = new
        {
            model = "BAAI/bge-m3",
            input = texts           // ← gửi toàn bộ mảng trong 1 lần
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(requestUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<InfinityEmbeddingResponse>(cancellationToken);
            if (result?.Data is null) throw new Exception("Infinity: Data bị null.");

            // API trả về theo đúng thứ tự input
            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => (float[]?)d.Embedding)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi khi gọi Infinity AI batch: {ex.Message}");
            // Trả về mảng null cho tất cả — consumer sẽ bỏ qua vector, search fallback BM25
            return new float[]?[texts.Length];
        }
    }
}
