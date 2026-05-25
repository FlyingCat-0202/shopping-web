using Elastic.Clients.Elasticsearch;
using EventBus.Contracts;
using MassTransit;
using Product.Domain.Entities;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

public class SyncProductToElasticConsumer(ElasticsearchClient e, ILogger<SyncProductToElasticConsumer> logger, IAiEmbeddingService aiEmbeddingService) :
    IConsumer<ProductCreatedEvent>,
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductUpdatedEvent>
{
    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
    {
        var message = context.Message;
        try
        {
            //await Task.Delay(4200);
            float[] queryVector = await aiEmbeddingService.GetVectorAsync(message.Name);
            var doc = new ProductEsDocument(
                Id: message.Id,
                Name: message.Name,
                Price: message.Price,
                CategoryName: message.CategoryName,
                IsActive: message.IsActive,
                Description: "",
                ImageUrl: "",
                NameEmbeddingVector: (queryVector != null && queryVector.Length > 0) ? queryVector : null
            );

            var response = await e.IndexAsync(doc, idx => idx
                .Index("products")
                .Id(doc.Id));

            logger.LogInformation("Đã đẩy sản phẩm {Name} vào ES thành công!", doc.Name); // THÊM DÒNG NÀY

            if (!response.IsValidResponse)
                throw new Exception($"Insert failed: {response.DebugInformation}");
        }
        catch (Exception except)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(except, "Lỗi khi thêm sản phẩm mới {ProductId} vào Elastic docs", message.Id);

            throw;
        }
    }

    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        var message = context.Message;
        try
        {
            var response = await e.DeleteAsync("products", message.Id);

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

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var message = context.Message;
        try
        {
            float[] newVector = await aiEmbeddingService.GetVectorAsync(message.Name);
            var partialDoc = new
            {
                Name = message.Name,
                Price = message.Price,
                CategoryName = message.CategoryName,
                IsActive = message.IsActive,
                NameEmbeddingVector = newVector
            };

            var response = await e.UpdateAsync<ProductEsDocument, object>(
                "products",
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
