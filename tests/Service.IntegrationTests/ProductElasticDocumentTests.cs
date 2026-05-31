using Microsoft.Extensions.Logging.Abstractions;
using Product.API.IntegrationEvents.Consumers.Elastic;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public class ProductElasticDocumentTests
{
    [Fact]
    public void ProductElasticDocumentCarriesFilterSortAndStockFields()
    {
        var document = new ProductEsDocument(
            Id: Guid.NewGuid(),
            Name: "Trail Jacket",
            Price: 89.99m,
            CategoryName: "Outerwear",
            IsActive: true,
            CategoryId: 12,
            StockQuantity: 7,
            StockStatus: ElasticProductIndex.StockStatus(7),
            NameSort: "Trail Jacket",
            Description: "Water resistant shell",
            ImageUrl: "https://cdn.test/jacket.png",
            NameEmbeddingVector: new float[ElasticProductIndex.EmbeddingDimensions]);

        ElasticProductIndex.Name.ShouldBe("products");
        ElasticProductIndex.VersionedName.ShouldStartWith("products-v");
        document.CategoryId.ShouldBe(12);
        document.StockQuantity.ShouldBe(7);
        document.StockStatus.ShouldBe(ElasticProductIndex.InStock);
        document.NameSort.ShouldBe(document.Name);
        document.NameEmbeddingVector.ShouldNotBeNull();
        document.NameEmbeddingVector.Length.ShouldBe(ElasticProductIndex.EmbeddingDimensions);
    }

    [Fact]
    public void ProductElasticIndexDropsVectorsWithWrongDimensions()
    {
        var valid = new float[ElasticProductIndex.EmbeddingDimensions];
        var invalid = new float[12];

        ElasticProductIndex.NormalizeVector(valid, NullLogger.Instance, "valid vector").ShouldBeSameAs(valid);
        ElasticProductIndex.NormalizeVector(invalid, NullLogger.Instance, "invalid vector").ShouldBeNull();
        ElasticProductIndex.StockStatus(0).ShouldBe(ElasticProductIndex.OutOfStock);
    }
}
