using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Product.Domain.Search;

namespace Product.Infrastructure.Search;

public sealed class LocalEmbeddingService : IAiEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalEmbeddingService> _logger;
    private readonly string _apiUrl;
    private readonly string _modelId;

    public LocalEmbeddingService(
        HttpClient httpClient,
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<LocalEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _modelId = config["Embedding:ModelId"] ?? "BAAI/bge-m3";

        var configuredUrl = config["services:infinity-ai:api:0"] ?? config["Embedding:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configuredUrl) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Embedding:BaseUrl must be configured outside Development.");
        }

        _apiUrl = (configuredUrl ?? "http://localhost:7997").TrimEnd('/');
    }

    public async Task<float[]> GetVectorAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

        var vectors = await GetVectorsAsync([text], cancellationToken);
        return vectors[0] ?? Array.Empty<float>();
    }
    public async Task<float[]?[]> GetVectorsAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        if (texts.Length == 0) return [];

        var requestUrl = $"{_apiUrl}/embeddings";

        var payload = new
        {
            model = _modelId,
            input = texts
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(requestUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<InfinityEmbeddingResponse>(cancellationToken);
            if (result?.Data is null)
                throw new InvalidDataException("Infinity response did not contain data.");

            var vectors = result.Data
                .OrderBy(d => d.Index)
                .Select(d => (float[]?)d.Embedding)
                .ToArray();

            if (vectors.Length != texts.Length)
            {
                throw new InvalidDataException(
                    $"Infinity returned {vectors.Length} vectors for {texts.Length} inputs.");
            }

            return vectors;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Infinity embedding request failed for {InputCount} inputs; keyword search will be used.",
                texts.Length);

            return new float[]?[texts.Length];
        }
    }
}
