# Enterprise Roadmap

## Objective

Modernize the repository from an event-sourced module into a secure, observable, scalable enterprise service without replacing CQRS or event sourcing.

## Phase 0: Stabilize The Core

Duration:

- 1 sprint

Goals:

- Fix correctness gaps before adding platform complexity

Actions:

- Fix update projection mismatch in `MemoryEventProjector`
- Remove duplicate processed-event marking
- Complete missing handlers for update, compress, consolidate, and index
- Align aggregate behavior with command surface
- Implement or stub with contracts the missing snapshot and AI abstractions intentionally

Exit criteria:

- All commands have a defined implementation status
- Build remains green
- Basic tests exist for aggregate and handlers

## Phase 1: Runtime And Reliability

Duration:

- 1 to 2 sprints

Actions:

- Add a service host project with `Program.cs`
- Introduce dependency injection composition
- Replace `InMemoryEventBus` with MassTransit + RabbitMQ
- Add transactional outbox for event publication
- Add health checks and startup validation

Exit criteria:

- Service can start locally
- Command flow works end to end
- Events publish durably and projections can recover

## Phase 2: Security And Governance

Duration:

- 1 sprint

Actions:

- Integrate JWT/OIDC with enterprise IdP
- Add authorization and ownership validation
- Add secrets management
- Define data classification and encryption approach for memory payloads
- Add audit logging requirements

Exit criteria:

- Identity is platform-derived, not caller-supplied
- Access policies are enforced
- Security controls are documented and testable

## Phase 3: Observability And Quality Gates

Duration:

- 1 sprint

Actions:

- Add Serilog
- Add OpenTelemetry tracing and metrics
- Add Prometheus/Grafana or Application Insights dashboarding
- Create unit, integration, and contract test suites
- Add CI build/test/quality pipeline

Exit criteria:

- Critical flows are observable
- Test coverage exists for aggregate, handlers, projector, repositories, and workers

## Phase 4: Data And Replay Hardening

Duration:

- 1 sprint

Actions:

- Implement snapshot repository
- Use tail replay from snapshot version
- Add event versioning/upcasters
- Add read-model rebuild/reconciliation tooling
- Define backup and restore procedures

Exit criteria:

- Recovery and rebuild are documented and demonstrable
- Replay cost is controlled for large streams

## Phase 5: AI And Vector Expansion

Duration:

- 1 to 2 sprints

Actions:

- Implement `IEmbeddingService`
- Implement `IVectorMemoryRepository`
- Add asynchronous indexing workflow
- Choose Qdrant, pgvector, or Azure AI Search based on hosting target
- Add ranking, filtering, and multi-tenant isolation design

Exit criteria:

- Memory search is semantically usable and operationally isolated

## Phase 6: Cloud And Multi-Tenancy

Duration:

- 1 to 2 sprints

Actions:

- Containerize host and workers
- Deploy to AKS/EKS or App Service/ECS depending throughput goals
- Add tenant partitioning strategy for Mongo, SQL, and vector storage
- Add DR runbooks and environment promotion pipeline

Exit criteria:

- Platform can scale horizontally
- Tenant isolation and disaster recovery are documented

## Roadmap Summary

The fastest path to enterprise readiness is:

1. correctness
2. host + durable messaging
3. security
4. observability + tests
5. replay/data lifecycle hardening
6. AI/vector scale-out
