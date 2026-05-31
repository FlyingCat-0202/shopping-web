# Shopping Web

Microservice shopping system built with .NET, Angular, Aspire, PostgreSQL, Redis, RabbitMQ, Elasticsearch, and Playwright.

## Prerequisites

- .NET SDK 10
- Node.js 24 and npm 11
- Docker Desktop
- k6, only when running performance tests

## Run Locally

Start the full Aspire stack:

```powershell
dotnet run --project AppHost\AppHost.csproj
```

By default the AI embedding service runs on CPU. To request CUDA:

```powershell
dotnet run --project AppHost\AppHost.csproj --Parameters:infinity-ai-device=cuda
```

Start only the Angular app:

```powershell
cd src\Web\web-store-angular
npm ci
npm start
```

## Backend Tests

Run all .NET tests:

```powershell
dotnet test shopping.slnx
```

Test projects:

- `tests\Order.FlowTests`: order domain and saga flow contracts.
- `tests\Identity.ApiTests`: Identity API with PostgreSQL and RabbitMQ Testcontainers.
- `tests\Payment.ApiTests`: Payment API webhook and checkout endpoints with PostgreSQL, RabbitMQ, and Redis Testcontainers.
- `tests\Service.IntegrationTests`: Redis cart store, notification consumer, payment provider, and product Elastic document coverage.

Docker must be running for integration tests.

## Frontend Tests

```powershell
cd src\Web\web-store-angular
npm ci
npm run build
npm test -- --watch=false --browsers=ChromeHeadless
npm run e2e
npm audit
```

The Playwright suite covers storefront smoke, login, cart, COD checkout, online checkout, and admin ship/deliver workflows.

## CI

GitHub Actions workflow:

```text
.github\workflows\ci.yml
```

The pipeline runs:

- `dotnet test shopping.slnx`
- `npm audit --audit-level=high`
- `npm run build`
- Angular component tests
- Playwright E2E tests
- .NET package audit
- Docker image build checks for every API and the Angular frontend

## Production Operations

Production hardening notes and the release checklist are in:

```text
docs\production-readiness.md
```

All APIs expose:

```text
/health
/health/live
/health/ready
```

Configure `Cors:AllowedOrigins` explicitly outside Development. Startup fails in Production when it is missing.

The Angular image serves `public/env.js` as `/env.js`. Replace or mount that file per environment to point the browser at the correct API gateway URL.

## Performance Tests

k6 scripts live under:

```text
perf\k6
```

Run product search load:

```powershell
k6 run perf\k6\product-search.js -e API_BASE_URL=http://localhost:5000
```

Run order saga concurrency:

```powershell
k6 run perf\k6\order-saga-concurrency.js -e API_BASE_URL=http://localhost:5000 -e AUTH_TOKEN=<jwt> -e PRODUCT_ID=<product-guid>
```

Monitor RabbitMQ consumer backlog:

```powershell
k6 run perf\k6\rabbitmq-consumers.js -e RABBITMQ_MANAGEMENT_URL=http://localhost:15672 -e RABBITMQ_QUEUE=order-submitted
```

## Remaining Hardening

- Add Elasticsearch container tests for real index creation and search queries.
- Add MassTransit harness tests for full order saga choreography.
- Add seeded end-to-end tests against the real Aspire stack.
- Add Docker image publish and deployment stages to CI.
- Add coverage reporting and minimum coverage gates.
