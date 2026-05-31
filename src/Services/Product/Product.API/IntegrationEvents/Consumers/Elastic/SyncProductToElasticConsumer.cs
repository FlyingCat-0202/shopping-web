using Elastic.Clients.Elasticsearch;
using EventBus.Contracts;
using MassTransit;
using Product.Domain.Entities;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

// ════════════════════════════════════════════════════════════════════════════
// Batch Consumer — nhận tối đa N message 1 lần thay vì từng message riêng lẻ.
//
// Vấn đề cũ (IConsumer<ProductCreatedEvent>):
//   Mỗi message gọi AI embedding 1 lần → N message = N HTTP round-trip tới
//   Infinity AI → consumer bị nghẽn ở ~200 ack/s.
//
// Giải pháp (IConsumer<Batch<ProductCreatedEvent>>):
//   Batch 100 message → gọi AI 1 lần (truyền 100 text cùng lúc, Infinity
//   hỗ trợ input: string[]) → bulk index 100 doc vào ES 1 lần.
//   Kết quả: từ N round-trip xuống còn 2 round-trip bất kể batch size.
// ════════════════════════════════════════════════════════════════════════════
public class SyncProductToElasticConsumer(
    ElasticsearchClient e,
    ILogger<SyncProductToElasticConsumer> logger,
    IAiEmbeddingService aiEmbeddingService)
    : IConsumer<Batch<ProductCreatedEvent>>,
      IConsumer<ProductDeletedEvent>,
      IConsumer<ProductUpdatedEvent>
{
    // ── Batch handler: ProductCreatedEvent ────────────────────────────────────
    public async Task Consume(ConsumeContext<Batch<ProductCreatedEvent>> context)
    {
        var messages = context.Message
            .Select(m => m.Message)
            .ToArray();

        // Bước 1: Lấy embedding cho toàn bộ batch bằng 1 HTTP request duy nhất.
        //         Infinity AI nhận input: string[] → trả về vector[] theo đúng thứ tự.
        var names = messages.Select(m => m.Name).ToArray();
        float[]?[] vectors = await aiEmbeddingService.GetVectorsAsync(names);

        // Bước 2: Build documents rồi bulk index — 1 ES request cho N documents.
        var docs = messages.Select((msg, i) =>
        {
            var vector = ElasticProductIndex.NormalizeVector(vectors[i], logger, $"product {msg.Id}");
            return new ProductEsDocument(
                Id:                  msg.Id,
                Name:                msg.Name,
                Price:               msg.Price,
                CategoryName:        msg.CategoryName,
                IsActive:            msg.IsActive,
                CategoryId:          msg.CategoryId,
                StockQuantity:       msg.StockQuantity,
                StockStatus:         ElasticProductIndex.StockStatus(msg.StockQuantity),
                NameSort:            msg.Name,
                Description:         msg.Description,
                ImageUrl:            msg.ImageUrl,
                NameEmbeddingVector: vector
            );
        }).ToList();

        var bulkResponse = await e.IndexManyAsync(docs, ElasticProductIndex.Name);

        if (bulkResponse.Errors)
        {
            foreach (var item in bulkResponse.ItemsWithErrors)
                logger.LogError("Bulk index lỗi tại doc {Id}: {Error}", item.Id, item.Error?.Reason);

            throw new Exception($"Bulk index có {bulkResponse.ItemsWithErrors.Count()} lỗi.");
        }

        logger.LogInformation("Bulk indexed {Count} sản phẩm vào Elasticsearch.", messages.Length);
    }

    // ── Single handler: ProductDeletedEvent ──────────────────────────────────
    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        var message = context.Message;
        try
        {
            var response = await e.DeleteAsync(ElasticProductIndex.Name, message.Id);

            if (!response.IsValidResponse)
            {
                if (response.ApiCallDetails.HttpStatusCode == 404)
                {
                    logger.LogInformation("Sản phẩm {ProductId} không tồn tại trong Elastic, bỏ qua xóa.", message.Id);
                    return;
                }

                throw new Exception($"Delete failed: {response.DebugInformation}");
            }
        }
        catch (Exception except)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(except, "Lỗi khi xóa sản phẩm {ProductId} khỏi Elastic docs", message.Id);

            throw;
        }
    }

    // ── Single handler: ProductUpdatedEvent ──────────────────────────────────
    // Update vẫn gọi AI từng cái vì đây là thao tác lẻ, không cần batch.
    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var message = context.Message;
        try
        {
            float[] newVector = await aiEmbeddingService.GetVectorAsync(message.Name);
            var embeddingVector = ElasticProductIndex.NormalizeVector(newVector, logger, $"product {message.Id}");
            var partialDoc = new
            {
                Name               = message.Name,
                Price              = message.Price,
                CategoryName       = message.CategoryName,
                IsActive           = message.IsActive,
                CategoryId         = message.CategoryId,
                StockQuantity      = message.StockQuantity,
                StockStatus        = ElasticProductIndex.StockStatus(message.StockQuantity),
                NameSort           = message.Name,
                Description        = message.Description,
                ImageUrl           = message.ImageUrl,
                NameEmbeddingVector = embeddingVector
            };

            var response = await e.UpdateAsync<ProductEsDocument, object>(
                ElasticProductIndex.Name,
                message.Id,
                u => u.Doc(partialDoc));

            if (!response.IsValidResponse)
                throw new Exception($"Update failed: {response.DebugInformation}");
        }
        catch (Exception except)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(except, "Lỗi khi cập nhật sản phẩm {ProductId} vào Elastic docs", message.Id);

            throw;
        }
    }
}
