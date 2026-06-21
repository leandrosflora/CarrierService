# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architectural context

Before modifying code, contracts, events, or service structure, consult the architecture repository at `https://github.com/leandrosflora/meli-envios-architecture`:

- `docs/contracts/meli-envios-apis.openapi.yaml` — canonical HTTP contracts; implement exactly what is defined there
- `docs/contracts/kafka-events.md` — Kafka event schemas and envelope format
- `docs/adr`, `docs/c4`, `docs/sequence-diagrams` — ADRs and diagrams

Do not invent dependencies, integrations, or events outside those patterns. Do not access another microservice's database. Do not create new Kafka topics without documenting them in the architecture repository.

## Commands

```bash
dotnet restore
dotnet build
dotnet run --project CarrierService.csproj
dotnet test                        # no test project currently exists in repo
```

For EF Core migrations (no versioned migrations exist yet):

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

Swagger UI is available at `http://localhost:5247/swagger` when `ASPNETCORE_ENVIRONMENT=Development`.

## Architecture

Single-project `.NET 8` Minimal API service. Layers are enforced by folder convention, not by separate assemblies:

- `Domain/` — entities (`Carrier`, `CarrierServiceLevel`, `CarrierLane`, `CarrierCategoryRestriction`) and enums (`CarrierStatus`, `TransportMode`). No dependencies on other layers.
- `Contracts/` — request/response DTOs. The enums are serialized as strings throughout.
- `Application/` — `CarrierAvailabilityService`, `CarrierStatusService`, and `CarrierStaticRuleEvaluator`. Depends on ports (`Application/Ports/`) never on infrastructure directly.
- `Application/Models/` — `CarrierProfileSnapshot` and nested snapshots (`CarrierServiceLevelSnapshot`, `CarrierLaneSnapshot`). These are the read models used by the rule evaluator and cached in Redis.
- `Adapters/` — `ICarrierAdapter` implementations for external partners, plus `CarrierAdapterFactory` (keyed by `CarrierCode`).
- `Infrastructure/` — EF Core (`CarrierDbContext`, `CarrierRepository`), Redis (`RedisCarrierProfileCache`), outbox (`OutboxWriter`, `OutboxMessage`), background worker (`CarrierHealthRefreshWorker`).
- `Api/` — endpoint mapping via extension methods (`MapCarrierAdministrationEndpoints`, `MapCarrierAvailabilityEndpoints`).

### Availability check flow

`POST /carrier-availability/search` → `CarrierAvailabilityService.SearchAsync`:

1. Checks are grouped by `CarrierCode` and processed in parallel (`Task.WhenAll`).
2. Per group: load `CarrierProfileSnapshot` from Redis (TTL 30 s), falling back to PostgreSQL on miss.
3. `CarrierStaticRuleEvaluator.Evaluate` applies local rules against the snapshot. Rules are short-circuit: the evaluator iterates service levels by `Priority` and returns the first passing one.
4. If the local rule fails → return `unavailable` with the specific `ReasonCode`, source `"Local"`.
5. If it passes and `RequiresRealTimeValidation=false` → return `available`, source `"Local"`, `validUntil = now + 30 s`.
6. If it passes and `RequiresRealTimeValidation=true` → call the external adapter via `CarrierAdapterFactory.TryGet`. Missing adapter → `PartnerUnavailable`. Adapter exception → `PartnerUnavailable` with source `"CircuitBreakerOrTimeout"`.

### Adding a new external adapter

1. Implement `ICarrierAdapter` (expose `CarrierCode`, `CheckAvailabilityAsync`, `CheckHealthAsync`).
2. Register it in `Program.cs` as typed `HttpClient` with `AddStandardResilienceHandler`.
3. Add `builder.Services.AddTransient<ICarrierAdapter>(...)` so the `CarrierAdapterFactory` picks it up.
4. Add the carrier's `BaseUrl` to `appsettings` under `Carriers:<Key>:BaseUrl`.

### Feature flag: mock repository

Set `FeatureFlags:MockCarrierRepository=true` in config to use `MockCarrierRepository` instead of PostgreSQL/Redis. The mock provides two pre-seeded carriers (`MELI`, `EXTERNAL`) with fixed node IDs. The EF Core health check is also skipped in this mode.

### Outbox

`OutboxWriter` serializes domain events to `OutboxMessages` (PostgreSQL) within the same EF Core transaction as the command. Events are stored but **no publisher worker exists yet** — the outbox is write-only in the current codebase.

### Background worker

`CarrierHealthRefreshWorker` runs every 15 s. It fetches carriers with `RequiresRealTimeValidation=true` and status `Active|Degraded`, calls each adapter's health endpoint, and updates carrier status accordingly, triggering cache invalidation.

## Key constraints

- `CarrierCode` is always normalized to `UPPER_INVARIANT` before storage and lookup.
- Redis cache key: `carrier-profile:{normalizedCode}`. Any mutation (status change, new service level, new lane) must call `ICarrierProfileCache.RemoveAsync`.
- `plannedDepartureAtUtc` must be UTC. The rule evaluator converts it to the lane's `TimeZoneId` before comparing against `CutoffTime` and `OperatingDays`.
- HTTP resilience: all external adapter clients use total timeout 3 s, attempt timeout 2 s, max 1 retry, circuit breaker. `ConfigureStandardResilience` in `Program.cs` validates that `totalTimeout > attemptTimeout` and `samplingDuration >= 2 × attemptTimeout` at startup.
- `CarrierStaticRuleEvaluator` returns the **first service level** (by `Priority`) that passes all package and lane checks. When no service level passes, `DetermineFailure` inspects the snapshot to return the most specific reason code.

## Known gaps (do not assume these exist)

- No EF Core migrations in the repository.
- No authentication/authorization on any endpoint.
- Webhook endpoint (`POST /carriers/{code}/webhooks/status`) does not validate `Signature`, enforce idempotency on `EventId`, or enforce ordering by `OccurredAt`.
- No outbox publisher.
- No OpenTelemetry or Prometheus metrics.
- The file `CarrierService.http` contains a legacy `/weatherforecast` example that does not apply to this service.
