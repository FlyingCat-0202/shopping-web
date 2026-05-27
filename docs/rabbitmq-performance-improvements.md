# RabbitMQ & Elasticsearch — Performance Improvements

> **Bối cảnh:** Khi seed 1.000.000 sản phẩm, hệ thống chỉ đạt ~500 msg/s publish và ~200 msg/s ack.
> Tài liệu này mô tả nguyên nhân gốc rễ và toàn bộ thay đổi đã thực hiện.

---

## Mục lục

1. [Chẩn đoán vấn đề](#1-chẩn-đoán-vấn-đề)
2. [Thay đổi 1 — Chunked DB Save](#2-thay-đổi-1--chunked-db-save)
3. [Thay đổi 2 — Parallel Batch Publish](#3-thay-đổi-2--parallel-batch-publish)
4. [Thay đổi 3 — Batch Embedding API](#4-thay-đổi-3--batch-embedding-api)
5. [Thay đổi 4 — MassTransit Batch Consumer](#5-thay-đổi-4--masstransit-batch-consumer)
6. [Luồng dữ liệu trước/sau](#6-luồng-dữ-liệu-trướcsau)
7. [Kết quả kỳ vọng](#7-kết-quả-kỳ-vọng)
8. [Nguyên tắc thiết kế rút ra](#8-nguyên-tắc-thiết-kế-rút-ra)

---

## 1. Chẩn đoán vấn đề

Toàn bộ pipeline seed có **3 điểm nghẽn độc lập** chồng lên nhau:

| # | Điểm nghẽn | Triệu chứng | File |
|---|-----------|-------------|------|
| A | EF Core track 1M entity cùng lúc | RAM tăng đột biến, Postgres tx nặng | `src/Tools/DbSeeder/ProductGenerator.cs` |
| B | Publish RabbitMQ tuần tự `foreach + await` | ~500 msg/s — nghẽn tại network round-trip | `src/Tools/DbSeeder/ProductGenerator.cs` |
| C | Consumer gọi AI embedding 1-by-1 per message | ~200 ack/s — nghẽn tại HTTP call Infinity AI | `SyncProductToElasticConsumer.cs` |

```
             TRƯỚC KHI FIX

DbSeeder                  RabbitMQ              Consumer
AddRange(1M) ──OOM──►     [queue]
SaveChanges() ──1tx──►    1M msgs buffered      foreach msg:
                                                  AI HTTP ~150ms ← nghẽn C
foreach msg:                                      ES IndexAsync
  await Publish() ← nghẽn B     ≈200 ack/s ◄────────────────────
≈500 msg/s
```

---

## 2. Thay đổi 1 — Chunked DB Save

**File:** `src/Tools/DbSeeder/ProductGenerator.cs`

### Trước

```csharp
// 1M objects trong RAM cùng lúc
dbContext.Products.AddRange(productsToAdd);
await dbContext.SaveChangesAsync(); // 1 transaction PostgreSQL khổng lồ
```

**Vấn đề:**
- EF Core `ChangeTracker` giữ state của 1M entity suốt quá trình → RAM tăng tuyến tính.
- PostgreSQL xử lý 1 transaction 1M INSERT → lock lâu, tốn WAL, dễ timeout.

### Sau

```csharp
const int dbBatchSize = 5_000;

foreach (var chunk in productsToAdd.Chunk(dbBatchSize))
{
    dbContext.Products.AddRange(chunk);
    await dbContext.SaveChangesAsync();

    // Xóa tracked entities sau mỗi chunk.
    // Thiếu dòng này → EF vẫn giữ 1M object trong heap dù đã save xong.
    dbContext.ChangeTracker.Clear();
}
```

**Kết quả:**
- Memory footprint ổn định ở ~5K entities thay vì ~1M.
- Mỗi chunk = 1 transaction nhỏ → PostgreSQL không bị lock lâu.

> **Lưu ý:** `ChangeTracker.Clear()` là **bắt buộc**. Nếu thiếu, chunking không có tác dụng
> về memory vì EF vẫn giữ reference đến toàn bộ object graph cũ.

---

## 3. Thay đổi 2 — Parallel Batch Publish

**File:** `src/Tools/DbSeeder/ProductGenerator.cs`

### Trước

```csharp
// Serial: await từng message → chờ TCP round-trip + broker ACK mỗi lần
foreach (var product in productsToAdd)
{
    // O(n) lookup trong vòng lặp nóng — overhead ẩn với 1M lần
    var categoryName = categoryMap.FirstOrDefault(c => c.Value == product.CategoryId).Key;
    await publishEndpoint.Publish(eventMsg);
}
// Throughput: ~500 msg/s
```

**Vấn đề:**
- `await Publish()` dùng Publisher Confirms → phải nhận ACK trước khi tiếp tục.
- Latency mạng ~1ms → serial tối đa lý thuyết chỉ ~1000 msg/s.
- `FirstOrDefault(c => c.Value == ...)` là O(n) trên Dictionary — cộng dồn 1M lần.

### Sau

```csharp
const int publishBatchSize = 500;
const int maxParallelBatches = 10;

// O(1) reverse lookup — xây dựng 1 lần trước vòng lặp
var idToCategoryName = categoryMap.ToDictionary(kv => kv.Value, kv => kv.Key);

await Parallel.ForEachAsync(
    productsToAdd.Chunk(publishBatchSize),
    new ParallelOptions { MaxDegreeOfParallelism = maxParallelBatches },
    async (batch, ct) =>
    {
        // Publish 500 message đồng thời — không await từng cái
        var publishTasks = batch.Select(product =>
        {
            var categoryName = idToCategoryName.GetValueOrDefault(product.CategoryId, "Unknown");
            return publishEndpoint.Publish(eventMsg, ct);
        });
        await Task.WhenAll(publishTasks);
    });
// Throughput kỳ vọng: 5.000–15.000 msg/s
```

**Cách hoạt động:**

```
Trước (serial):
  [msg1]→ACK→[msg2]→ACK→[msg3]→ACK→...    ≈500/s

Sau (parallel batches, 10 concurrent):
  Batch-1: [msg1..500]    → Task.WhenAll ──┐
  Batch-2: [msg501..1000] → Task.WhenAll ──┤ ForEachAsync (10 parallel)
  Batch-3: [msg1001..]    → Task.WhenAll ──┘
  ...                                       ≈5.000–15.000/s
```

**Tại sao `MaxDegreeOfParallelism = 10`?**
Điều chỉnh theo số channel TCP MassTransit mở đến broker. Tăng quá cao làm broker quá tải;
10 là giá trị thực tế tốt để bắt đầu, có thể tăng lên 20–50 tùy server.

---

## 4. Thay đổi 3 — Batch Embedding API

**Files thay đổi:**
- `src/Services/Product/Product.Domain/Entities/AIService.cs`
- `src/Services/Product/Product.Infrastructure/AISearch/AISearchService.cs`

### 4.1 Interface mới — `IAiEmbeddingService`

```csharp
public interface IAiEmbeddingService
{
    // Giữ nguyên — dùng cho update lẻ (1 sản phẩm)
    Task<float[]> GetVectorAsync(string text);

    // MỚI — gộp N text thành 1 HTTP request
    // Trả về float[]?[] — null nếu text đó bị lỗi, không throw toàn batch
    Task<float[]?[]> GetVectorsAsync(string[] texts);
}
```

### 4.2 `InfinityEmbeddingData` — thêm field `Index`

```csharp
// Trước: thiếu Index → không sort được kết quả batch
public record InfinityEmbeddingData(
    [property: JsonPropertyName("embedding")] float[] Embedding
);

// Sau: có Index để OrderBy đảm bảo thứ tự đúng với input array
public record InfinityEmbeddingData(
    [property: JsonPropertyName("index")]     int     Index,
    [property: JsonPropertyName("embedding")] float[] Embedding
);
```

> **Quan trọng:** Infinity AI trả về `data[]` với field `index` mapping về vị trí trong
> input. Không sort theo `Index` → vector bị gán nhầm cho sản phẩm khác.

### 4.3 `LocalEmbeddingService` — Native batch

Infinity AI vốn đã nhận `input: string[]`. Trước đây ta chỉ truyền `new[] { text }` (mảng
1 phần tử). Chỉ cần truyền cả mảng là xong:

```csharp
// Trước: input = new[] { text }   → 1 text mỗi request
// Sau:   input = texts            → N text trong 1 request

public async Task<float[]?[]> GetVectorsAsync(string[] texts)
{
    var payload = new { model = "BAAI/bge-m3", input = texts };
    var response = await _httpClient.PostAsJsonAsync(requestUrl, payload);
    var result = await response.Content.ReadFromJsonAsync<InfinityEmbeddingResponse>();

    // Sort theo Index để đảm bảo đúng thứ tự với input
    return result.Data
        .OrderBy(d => d.Index)
        .Select(d => (float[]?)d.Embedding)
        .ToArray();
}

// GetVectorAsync giờ chỉ là wrapper — không duplicate logic
public async Task<float[]> GetVectorAsync(string text)
{
    var vectors = await GetVectorsAsync([text]);
    return vectors[0] ?? Array.Empty<float>();
}
```

**Impact:** N texts → 1 HTTP request. Latency từ `N × 150ms` xuống còn `~150ms` bất kể N.

### 4.4 `GeminiEmbeddingService` — Parallel fallback

Gemini không có batch endpoint → parallel với rate-limit an toàn:

```csharp
public async Task<float[]?[]> GetVectorsAsync(string[] texts)
{
    var results = new float[]?[texts.Length];
    await Parallel.ForEachAsync(
        texts.Select((t, i) => (t, i)),
        new ParallelOptions { MaxDegreeOfParallelism = 5 }, // tránh hit rate-limit
        async (item, ct) =>
        {
            try   { results[item.i] = await GetVectorAsync(item.t); }
            catch { results[item.i] = null; } // lỗi 1 item → null, không throw cả batch
        });
    return results;
}
```

---

## 5. Thay đổi 4 — MassTransit Batch Consumer

**Files thay đổi:**
- `src/Services/Product/Product.API/IntegrationEvents/Consumers/Elastic/SyncProductToElasticConsumer.cs`
- `src/Services/Product/Product.API/Program.cs`

### 5.1 Cốt lõi vấn đề

| | Trước | Sau |
|--|-------|-----|
| Interface | `IConsumer<ProductCreatedEvent>` | `IConsumer<Batch<ProductCreatedEvent>>` |
| Consume() nhận | 1 message | Tối đa 100 messages |
| AI HTTP calls | 1 mỗi message | **1 cho toàn batch** |
| ES HTTP calls | 1 mỗi message (`IndexAsync`) | **1 cho toàn batch** (`IndexManyAsync`) |

### 5.2 Trước — 1 message mỗi lần

```csharp
public class SyncProductToElasticConsumer : IConsumer<ProductCreatedEvent>
{
    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
    {
        // 1 HTTP → AI, ~150ms
        float[] vector = await aiEmbeddingService.GetVectorAsync(message.Name);

        // 1 HTTP → ES, ~10ms
        await e.IndexAsync(doc, idx => idx.Index(ElasticProductIndex.Name).Id(doc.Id));

        // Tổng: ~160ms/message → ~6 msg/s per goroutine
    }
}
```

### 5.3 Sau — 100 messages mỗi lần

```csharp
public class SyncProductToElasticConsumer : IConsumer<Batch<ProductCreatedEvent>>
{
    public async Task Consume(ConsumeContext<Batch<ProductCreatedEvent>> context)
    {
        var messages = context.Message.Select(m => m.Message).ToArray();

        // Bước 1: 1 HTTP → AI, nhận về 100 vectors
        float[]?[] vectors = await aiEmbeddingService.GetVectorsAsync(
            messages.Select(m => m.Name).ToArray());

        // Bước 2: Build tất cả documents
        var docs = messages.Select((msg, i) => new ProductEsDocument(
            Id: msg.Id, Name: msg.Name, Price: msg.Price,
            CategoryName: msg.CategoryName, IsActive: msg.IsActive,
            Description: msg.Description, ImageUrl: msg.ImageUrl,
            NameEmbeddingVector: ElasticProductIndex.NormalizeVector(vectors[i], logger, ...)
        )).ToList();

        // Bước 3: 1 HTTP → ES Bulk, index 100 documents
        await e.IndexManyAsync(docs, ElasticProductIndex.Name);

        // Tổng: ~160ms cho 100 messages → throughput tăng ~100×
    }
}
```

### 5.4 HTTP calls so sánh (1.000.000 messages)

| | Trước | Sau |
|--|-------|-----|
| AI HTTP calls | 1.000.000 | **10.000** (giảm 100×) |
| ES HTTP calls | 1.000.000 | **10.000** (giảm 100×) |
| **Tổng** | **2.000.000** | **20.000** |

### 5.5 Đăng ký trong `Program.cs`

```csharp
// Khai báo với BatchOptions
x.AddConsumer<SyncProductToElasticConsumer>(cfg =>
{
    cfg.Options<BatchOptions>(o => o
        .SetMessageLimit(100)                    // tối đa 100 msg/batch
        .SetTimeLimit(TimeSpan.FromSeconds(2))); // flush sớm nếu chưa đủ 100
});

// Cấu hình endpoint
cfg.ReceiveEndpoint("elastic-search", e =>
{
    e.PrefetchCount = 200;         // 2 × batch size — buffer sẵn 2 batch tiếp
    e.ConcurrentMessageLimit = 2;  // 2 batch song song = 200 msgs in-flight
    e.ConfigureConsumer<SyncProductToElasticConsumer>(context);
});
```

**Cách MassTransit gom batch:**

```
RabbitMQ Queue: [msg1][msg2]...[msg200]
                       ↓ PrefetchCount=200
Consumer Buffer: [msg1..msg200]  ← kéo về local memory
                       ↓ BatchOptions: MessageLimit=100
Batch-1: [msg1..100]   → Consume() ──┐
Batch-2: [msg101..200] → Consume() ──┘ ConcurrentMessageLimit=2

TimeLimit(2s): flush ngay dù chưa đủ 100 msg → không treo messages lúc traffic thấp
```

---

## 6. Luồng dữ liệu trước/sau

### Trước

```
DbSeeder
  ├─ [1] AddRange(1M) → EF tracks 1M in RAM      ← OOM risk
  ├─ [2] SaveChangesAsync() → 1 Postgres tx lớn  ← lock lâu
  └─ [3] foreach (1M):
          await Publish()                         ← serial, chờ ACK từng cái
          categoryMap.FirstOrDefault()            ← O(n) × 1M lần

               ↓ ~500 msg/s

RabbitMQ [elastic-search]
  └─ IConsumer<ProductCreatedEvent>
       └─ per message:
            GetVectorAsync(name)  ← 1 HTTP × ~150ms  ← BOTTLENECK
            IndexAsync(doc)       ← 1 HTTP × ~10ms

               ↓ ~200 ack/s
```

### Sau

```
DbSeeder
  ├─ [1] foreach chunk (5.000):
  │       AddRange(chunk) → SaveChangesAsync() → ChangeTracker.Clear()
  │                                              ← memory ổn định ~5K objects
  └─ [2] Parallel.ForEachAsync (batch=500, parallel=10):
          Task.WhenAll(500 × Publish)            ← không await từng cái
          idToCategoryName[id]                   ← O(1) lookup

               ↓ ~5.000–15.000 msg/s

RabbitMQ [elastic-search] PrefetchCount=200
  └─ IConsumer<Batch<ProductCreatedEvent>>
       └─ per batch (100 msgs, 2 batches concurrent):
            GetVectorsAsync(100 names) ← 1 HTTP call tới Infinity AI
            IndexManyAsync(100 docs)   ← 1 ES Bulk call

               ↓ ~1.000–3.000 ack/s
```

---

## 7. Kết quả kỳ vọng

| Chỉ số | Trước | Sau | Cải thiện |
|--------|-------|-----|-----------|
| **Publish throughput** | ~500 msg/s | ~5.000–15.000 msg/s | **10–30×** |
| **Ack throughput** | ~200 msg/s | ~1.000–3.000 msg/s | **5–15×** |
| **AI HTTP calls** (1M seed) | 1.000.000 | 10.000 | **100× ít hơn** |
| **ES HTTP calls** (1M seed) | 1.000.000 | 10.000 | **100× ít hơn** |
| **RAM peak (DbSeeder)** | ~1M objects | ~5K objects | **200× ít hơn** |
| **Thời gian seed 1M** | ~33 phút | ~2–5 phút | **7–15× nhanh hơn** |

> Các con số là kỳ vọng lý thuyết. Giá trị thực tế phụ thuộc vào phần cứng, bandwidth và
> tải của Infinity AI server. Có thể điều chỉnh `MaxDegreeOfParallelism`, `publishBatchSize`,
> `MessageLimit` để tune theo môi trường cụ thể.

---

## 8. Nguyên tắc thiết kế rút ra

### Publish số lượng lớn

| Nên làm | Không nên làm |
|---------|---------------|
| `Task.WhenAll` cho batch publish | `foreach + await Publish()` |
| `Parallel.ForEachAsync` với degree phù hợp | Serial publish |
| Pre-build lookup dict trước vòng lặp | `FirstOrDefault()` trong vòng lặp nóng |
| Truyền `CancellationToken` | Bỏ qua cancellation trong bulk op |

### Lưu DB số lượng lớn

| Nên làm | Không nên làm |
|---------|---------------|
| `Chunk()` + `SaveChangesAsync()` từng batch | `AddRange(1M) + SaveChanges()` |
| `ChangeTracker.Clear()` sau mỗi chunk | Để EF tích lũy tracked entities |
| Batch size ~5.000 row | Batch quá lớn (OOM) hay quá nhỏ (overhead) |

### Consumer gọi dịch vụ ngoài

| Nên làm | Không nên làm |
|---------|---------------|
| `IConsumer<Batch<T>>` cho high-throughput | `IConsumer<T>` cho mọi trường hợp |
| Gọi AI/DB bằng batch API khi có thể | 1 HTTP request per message |
| `IndexManyAsync` / ES Bulk API | `IndexAsync` từng document |
| `TimeLimit` để tránh messages bị treo | Batch size cứng không có timeout |

### Hiểu đúng PrefetchCount và ConcurrentMessageLimit

```
PrefetchCount          = số message broker push vào consumer buffer cùng lúc
                       = nên là bội số của batch size
                       Ví dụ: 200 = 2 × 100

ConcurrentMessageLimit = số batch được xử lý song song
                       Ví dụ: 2 batch đồng thời

Quy tắc tính:
  PrefetchCount = ConcurrentMessageLimit × MessageLimit × buffer_factor
  200           = 2                      × 100          × 1
```

---

## Danh sách file thay đổi

| File | Loại thay đổi |
|------|--------------|
| `src/Tools/DbSeeder/ProductGenerator.cs` | Chunked DB save + Parallel batch publish + O(1) dict lookup |
| `src/Services/Product/Product.Domain/Entities/AIService.cs` | Thêm `GetVectorsAsync` vào interface; thêm `Index` field vào `InfinityEmbeddingData` |
| `src/Services/Product/Product.Infrastructure/AISearch/AISearchService.cs` | Implement `GetVectorsAsync` — native batch cho Infinity AI, parallel fallback cho Gemini |
| `src/Services/Product/Product.API/IntegrationEvents/Consumers/Elastic/SyncProductToElasticConsumer.cs` | Chuyển sang `IConsumer<Batch<T>>` + `IndexManyAsync` |
| `src/Services/Product/Product.API/Program.cs` | Thêm `BatchOptions` config; điều chỉnh `PrefetchCount` và `ConcurrentMessageLimit` |
