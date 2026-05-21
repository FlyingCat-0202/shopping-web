using Elastic.Clients.Elasticsearch;
using EventBus.Contracts;
using MassTransit;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

public class SyncProductToElasticConsumer(ElasticsearchClient e, ILogger<SyncProductToElasticConsumer> logger) :
    IConsumer<ProductCreatedEvent>,
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductUpdatedEvent>
{
    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
    {
        var message = context.Message;
        try
        {
            // Map từ Event sang Read Model (Document)
            var doc = new ProductEsDocument(
                Id: message.Id,
                Name: message.Name,
                Price: message.Price,
                CategoryName: message.CategoryName,
                IsActive: message.IsActive,
                // Map thêm Description, ImageUrl nếu có trong event
                Description: "",
                ImageUrl: ""
            );

            var response = await e.IndexAsync(doc, idx => idx
                .Index("products")
                .Id(doc.Id)
            );

            if (!response.IsValidResponse)
                throw new Exception($"Insert failed: {response.DebugInformation}");
        }
        catch (Exception except)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(except, "Lỗi khi thêm sản phẩm mới {ProductId} vào Elastic docs", message.Id);

            throw; // Bắt buộc phải throw để MassTransit tự động Retry hoặc đẩy vào Error Queue
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

            throw; // Ném lại lỗi cho MassTransit
        }
    }

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var message = context.Message;
        try
        {
            // Chỉ cập nhật các trường có thay đổi (Partial Update)
            var partialDoc = new
            {
                Name = message.Name,
                Price = message.Price,
                CategoryName = message.CategoryName,
                IsActive = message.IsActive
            };

            var response = await e.UpdateAsync<ProductEsDocument, object>(
                "products",
                message.Id,
                u => u.Doc(partialDoc) // Partial update an toàn hơn
            );

            if (!response.IsValidResponse)
                throw new Exception($"Update failed: {response.DebugInformation}");
        }
        catch (Exception except)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(except, "Lỗi khi cập nhật sản phẩm {ProductId} vào Elastic docs", message.Id);

            throw; // Ném lại lỗi cho MassTransit
        }
    }
}