# Production Readiness Guide

This project is closer to production-ready when the code, infrastructure, and operations below are all in place.

## Required Runtime Configuration

Set these values from the deployment platform secret store, not from source-controlled JSON files.

```text
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=<public-api-host>
Cors__AllowedOrigins__0=https://<frontend-host>
Jwt__Issuer=ShoppingWeb
Jwt__Audience=ShoppingWebClient
Jwt__PrivateKey=<identity-service-private-rsa-pem>
Jwt__PublicKey=<public-rsa-pem-shared-by-all-services>
Payment__WebhookSecret=<strong-random-secret>
ConnectionStrings__identity-db=<postgres-connection-string>
ConnectionStrings__product-db=<postgres-connection-string>
ConnectionStrings__cart-db=<if-added-later>
ConnectionStrings__order-db=<postgres-connection-string>
ConnectionStrings__payment-db=<postgres-connection-string>
ConnectionStrings__notification-db=<postgres-connection-string>
ConnectionStrings__rabbitmq=<amqp-connection-string>
ConnectionStrings__redis=<redis-connection-string>
ConnectionStrings__elasticsearch=<elasticsearch-url>
OTEL_EXPORTER_OTLP_ENDPOINT=<otel-collector-url>
```

## Health Checks

Every API and the gateway expose:

```text
/health
/health/live
/health/ready
```

Use `/health/live` for liveness probes and `/health/ready` for readiness probes.

## Security Defaults

The shared service defaults add:

- CORS with explicit production origin requirement.
- Rate limiting via `RateLimiting:PermitLimit`, `RateLimiting:WindowSeconds`, and `RateLimiting:QueueLimit`.
- Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, and `Permissions-Policy`.
- JWT bearer validation from RSA public key.
- Development-only Scalar/OpenAPI UI.

Production startup intentionally fails if `Cors:AllowedOrigins` is not configured.

## Release Checklist

- Rotate the development JWT key pair and payment webhook secret.
- Run `dotnet test shopping.slnx`.
- Run `npm run build`, `npm test -- --watch=false --browsers=ChromeHeadless`, and `npm run e2e`.
- Run `npm audit --audit-level=high`.
- Build and scan container images. CI already verifies image builds using `docker/Dockerfile.dotnet-service` and `src/Web/web-store-angular/Dockerfile`.
- Replace the frontend `/env.js` runtime config with production API gateway URLs.
- Apply EF Core migrations before routing production traffic.
- Verify `/health/ready` for all services after deploy.
- Confirm RabbitMQ queues have consumers and no growing backlog.
- Confirm Product search index alias points to the current versioned index.
- Run k6 smoke load tests against the deployed environment.
