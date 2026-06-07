# Product Search

Đọc feature này theo thứ tự:

1. `ProductSearchService.cs`: điều phối request và chọn Elasticsearch/PostgreSQL.
2. `Engines/ProductElasticSearch.cs`: search chính khi có từ khóa.
3. `Engines/ProductDatabaseSearch.cs`: search fallback và search khi tắt Elasticsearch.
4. `ProductQueryPolicy.cs`: luật filter, sort và database keyword matching.
5. `ProductSearchModels.cs`: request nội bộ, response, options và giới hạn pagination.

Thư mục `Indexing` không chạy trực tiếp khi người dùng bấm Search. Nó giữ Elasticsearch
đồng bộ với PostgreSQL:

- `ProductElasticIndex.cs`: schema document, alias, version index và vector dimensions.
- `ProductElasticIndexManager.cs`: tạo/rebuild index, chuyển alias và worker chạy lúc startup.
- `ProductElasticSyncConsumer.cs`: nhận Product create/update/delete events để cập nhật index.

Embedding được tách đúng theo layer:

- `Product.Domain/Search/EmbeddingContracts.cs`: interface và response contracts.
- `Product.Infrastructure/Search/EmbeddingServices.cs`: implementation gọi Infinity/Gemini.

Luồng request:

```text
GET /api/products/search
  -> ProductSearchService
  -> ProductElasticSearch
  -> Elasticsearch
  -> hydrate dữ liệu mới nhất từ PostgreSQL
```

Nếu Elasticsearch lỗi trong `Hybrid` mode:

```text
ProductSearchService -> ProductDatabaseSearch -> PostgreSQL
```
