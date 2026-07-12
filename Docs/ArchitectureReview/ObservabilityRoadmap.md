# Observability Roadmap

## Current State
- Basic `ILogger` implementation.
- No distributed tracing.
- No metrics.

## Target Architecture
The Enterprise architecture will adopt the **OpenTelemetry (OTel)** standard.

### 1. Tracing
- **Implementation:** OpenTelemetry .NET SDK.
- **Trace Context:** Propagate `TraceId` and `SpanId` across:
  - HTTP API Requests.
  - MediatR Handlers.
  - Event Bus Messages (Inject into message headers).
  - MongoDB and EF Core Database calls.
- **Destination:** Jaeger / Azure Application Insights / AWS X-Ray.

### 2. Metrics
- **System Metrics:** CPU, Memory, GC collections via `System.Diagnostics.Metrics`.
- **Custom Metrics:**
  - `memory_events_appended_total`
  - `llm_compression_duration_seconds`
  - `snapshot_generation_duration_seconds`
  - `consolidation_queue_depth`
- **Destination:** Prometheus & Grafana.

### 3. Logging
- **Implementation:** Serilog.
- **Enrichment:** Auto-enrich logs with `UserId`, `TenantId`, `TraceId`, and `AggregateId`.
- **Destination:** Elasticsearch / Splunk / Azure Log Analytics.
