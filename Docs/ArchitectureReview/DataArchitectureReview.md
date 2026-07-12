# Data Architecture Review

## Executive Summary

The data architecture follows a pragmatic CQRS split:

- MongoDB for the event store
- SQL via EF Core for read models
- snapshots as an intended optimization layer
- vector storage as a future capability

This is a sound direction, but the architecture is incomplete around transaction boundaries, replay optimization, and multi-tenant/security controls.

## MongoDB Event Store Review

### Current design

- `MongoEventStore` stores events as serialized JSON inside an internal `EventDocument`
- Events are keyed by stream ID and version
- A unique compound index on `(StreamId, Version)` is created at initialization

### Data integrity

Strengths:

- Optimistic concurrency is explicitly enforced by expected version comparison
- Stream ordering is preserved
- Event retrieval supports both full replay and replay from a given version

Weaknesses:

- No cross-store transaction boundary with the event bus
- No event contract versioning or upcasting
- Payload serialization depends on concrete CLR type names
- No retention, archival, or cold-storage policy is defined

### Concurrency controls

Assessment:

- Good at stream-level concurrency
- Not sufficient for distributed consistency across projection/event publication boundaries

### Versioning

Assessment:

- Aggregate stream version exists
- Event schema version does not

Recommendation:

- Add event schema version metadata and an upcaster pipeline

## SQL Read Model Review

### Current design

- `ReadModelDbContext` manages:
  - `MemoryReadModels`
  - `ProcessedEvents`
  - `ProcessingLocks`
- `SqlReadModelRepository` supports:
  - upsert
  - idempotency checks
  - worker locking
  - transactional batch execution

### Data integrity

Strengths:

- Processed events enable idempotent projection
- Processing locks help avoid duplicate worker processing
- Repository exposes transaction wrapper for batch projections

Weaknesses:

- Projection correctness bug exists for update events
- Read-model consistency depends on successful event publication
- No replay/rebuild script or documented rebuild workflow is present
- The repository saves frequently, which may reduce throughput

### Transaction boundaries

Current state:

- SQL projection transactions are local to the read store
- Mongo event append is separate
- Message publication is separate again

Assessment:

- Transaction boundaries are fragmented
- End-to-end consistency is eventual and currently fragile

## Snapshot Review

### Current design

- Snapshot storage is abstracted by `ISnapshotRepository`
- Snapshot generation is performed by `SnapshotWorker`
- Aggregate can restore from `MemorySnapshotDto`

### Issues

- No concrete snapshot repository implementation
- `CreateSnapshotPayload(...)` does not persist `Status`
- `SnapshotWorker` does not use tail replay after snapshot version
- No evidence of snapshot cleanup, retention, or corruption handling

### Recommendation

- Implement snapshot persistence in MongoDB or SQL
- Store snapshot metadata separately from payload if needed
- Replay only events after snapshot version
- Add validation and restore tests

## Repository Review

### Event store repository

- Good foundation
- Needs contract evolution, outbox integration, and operational tooling

### Read-model repository

- Useful and pragmatic
- Needs projection correctness fixes and throughput tuning

### Vector repository

- Placeholder only

## Data Growth Estimates

These are directional estimates for architecture planning, not benchmarked numbers.

### 100K events

- Event store size: modest
- Replay performance: acceptable for development and smaller streams
- Projection scalability: manageable with single-consumer patterns

### 1M events

- Event store size: still practical for MongoDB
- Replay performance: snapshoting becomes important
- Projection scalability: broker-backed consumers and rebuild tooling become important

### 10M events

- Event store size: still viable with proper indexing and infrastructure sizing
- Replay performance: full replay is too expensive for recovery-sensitive workloads
- Projection scalability: requires durable queueing, consumer scaling, and archival strategy

## Consistency Rules Assessment

| Area | Assessment |
| --- | --- |
| Stream concurrency | Good |
| Cross-store consistency | Weak |
| Projection idempotency | Present but imperfect |
| Snapshot correctness | Partial |
| Tenant isolation | Missing |
| Auditability | Partial |

## Data Architecture Recommendation

### Keep

- MongoDB event store
- SQL read models
- snapshot layer as a replay optimization

### Add

- transactional outbox
- event schema versioning
- rebuild/reconciliation jobs
- tenant partitioning strategy
- data classification and encryption strategy
- vector store with metadata filtering

## Verdict

The data architecture is directionally strong and suitable for an event-sourced memory platform, but it is not yet enterprise-ready because cross-store consistency and lifecycle governance are incomplete.
