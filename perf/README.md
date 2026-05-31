# Performance Tests

These scripts use k6 and are intentionally not part of the default CI gate because they need a running stack and seeded data.

Run product search latency:

```powershell
k6 run perf/k6/product-search.js -e API_BASE_URL=http://localhost:5000
```

Run order saga concurrency:

```powershell
k6 run perf/k6/order-saga-concurrency.js -e API_BASE_URL=http://localhost:5000 -e AUTH_TOKEN=<jwt> -e PRODUCT_ID=<product-guid>
```

Monitor RabbitMQ consumer backlog:

```powershell
k6 run perf/k6/rabbitmq-consumers.js -e RABBITMQ_MANAGEMENT_URL=http://localhost:15672 -e RABBITMQ_QUEUE=order-submitted
```
