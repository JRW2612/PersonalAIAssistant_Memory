# Implementation Backlog

## Critical

| Item | Priority | Business Value | Technical Value | Complexity | Estimated Hours | Dependencies |
| --- | --- | --- | --- | --- | --- | --- |
| Add runnable host project with DI/config/health checks | Critical | Makes the solution deployable and testable | Establishes executable runtime shell | High | 20 | None |
| Replace `InMemoryEventBus` with MassTransit + RabbitMQ | Critical | Enables reliable processing and scale | Creates durable delivery and consumer model | High | 32 | Host project |
| Implement transactional outbox | Critical | Protects audit trail and read-model correctness | Fixes dual-write failure mode | High | 28 | Durable broker choice |
| Implement missing handlers for update/compress/consolidate/index | Critical | Aligns delivered features with business surface | Completes CQRS write side | Medium | 24 | Aggregate behavior decisions |
| Fix read-model update mismatch and duplicate processed marking | Critical | Prevents stale or inconsistent reads | Correctness hardening | Low | 4 | None |
| Add authentication, authorization, and ownership enforcement | Critical | Prevents data leakage and unauthorized changes | Adds security baseline | High | 28 | Host project |

## High

| Item | Priority | Business Value | Technical Value | Complexity | Estimated Hours | Dependencies |
| --- | --- | --- | --- | --- | --- | --- |
| Implement snapshot repository and tail replay | High | Improves recovery and large-stream performance | Reduces replay cost | Medium | 16 | Host or storage choice |
| Add Serilog + OpenTelemetry + health endpoints | High | Improves operations and supportability | Provides logs, traces, and metrics | Medium | 20 | Host project |
| Create unit and integration test projects | High | Reduces regression risk | Enables CI quality gates | Medium | 24 | Host not required |
| Add resilience policies for workers and external AI calls | High | Reduces operational failures | Improves retry/backoff behavior | Medium | 10 | Compression/AI implementation |
| Define and implement secrets management | High | Protects credentials and keys | Enables secure deployment | Medium | 8 | Host/environment choice |

## Medium

| Item | Priority | Business Value | Technical Value | Complexity | Estimated Hours | Dependencies |
| --- | --- | --- | --- | --- | --- | --- |
| Add event schema versioning and upcasters | Medium | Protects long-term data value | Supports safe event evolution | Medium | 20 | Event contract review |
| Introduce richer value objects for tags/ownership/tenancy | Medium | Clarifies rules and future expansion | Improves domain integrity | Medium | 12 | Command/aggregate refactor |
| Add read-model rebuild and reconciliation jobs | Medium | Improves operability and recovery | Hardens eventual consistency story | Medium | 12 | Durable messaging/outbox |
| Define tenant model and partitioning strategy | Medium | Enables enterprise SaaS growth | Improves isolation and scalability | Medium | 16 | Security model |

## Low

| Item | Priority | Business Value | Technical Value | Complexity | Estimated Hours | Dependencies |
| --- | --- | --- | --- | --- | --- | --- |
| Implement `IEmbeddingService` | Low | Unlocks AI enrichment | Enables vector indexing pipeline | Medium | 16 | Provider choice |
| Implement `IVectorMemoryRepository` | Low | Unlocks semantic retrieval | Enables ANN/vector search | Medium | 20 | Vector store choice |
| Add ranking/clustering/RAG orchestration | Low | Expands future AI features | Extends retrieval architecture | High | 24 | Embeddings + vector repo |

## Total Estimated Work

- Estimated hours for backlog above: **314**
- Estimated days at 6 productive engineering hours/day: **52**
- Estimated sprints for 2 engineers at 2-week cadence: **4 to 5**

## Sequencing Recommendation

Recommended order:

1. host
2. broker
3. outbox
4. missing handlers + correctness fixes
5. security
6. tests + observability
7. snapshot/event evolution
8. AI/vector expansion
