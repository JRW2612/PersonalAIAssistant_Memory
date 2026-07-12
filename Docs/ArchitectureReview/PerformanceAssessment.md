# Performance Assessment

## Executive Summary

The current codebase is unlikely to have immediate raw CPU bottlenecks at its present scope. The real performance concerns are architectural:

- replay cost for long-lived streams
- synchronous or pseudo-synchronous projection flow
- polling workers
- missing broker/backpressure model
- absent vector/search implementation strategy

## Event Replay Assessment

### Current replay model

- Aggregate rehydration is done by replaying ordered events through `MemoryAggregate.LoadFromHistory(...)`.
- `MongoEventStore` exposes both full replay and tail replay methods.
- `SnapshotWorker` currently still loads the full event history even when a snapshot exists.

### Estimated replay cost

These are directional estimates based on the current code path and in-memory aggregate application, not benchmark measurements.

| Event count per stream | Estimated effect | Assessment |
| --- | --- | --- |
| 1K | low latency, typically acceptable | Fine for interactive use |
| 10K | noticeable rehydration cost under concurrency | Needs snapshot discipline |
| 100K | high replay cost and poor recovery characteristics | Not acceptable without optimized snapshot/tail replay |

### Snapshot strategy

Current state:

- `SnapshotWorkerOptions.SnapshotEventThreshold` defaults to `100`
- Snapshot repository implementation is absent
- Worker uses `GetEventsAsync(...)` instead of `GetEventsFromVersionAsync(...)`

Assessment:

- Snapshot intent is correct
- Snapshot execution path is only partially optimized

Recommendation:

- Persist snapshots with aggregate version
- Replay only tail events after the latest snapshot
- Add rebuild/recovery benchmarks for 1K, 10K, and 100K event streams

## Worker Performance Review

### Consolidation worker

Strengths:

- Uses `SemaphoreSlim` to limit concurrent compression calls
- Applies processing locks before work begins

Risks:

- Polling every cycle against SQL candidates can create avoidable database load
- Each candidate currently loads full stream history from Mongo
- Publish path still depends on in-memory bus and dual write
- No retry/backoff/circuit-breaker policy around compression provider or storage operations

Assessment:

- Concurrency: fair
- Rate limiting awareness: good
- End-to-end throughput architecture: weak

### Snapshot worker

Strengths:

- Batches streams needing snapshots
- Uses configurable threshold, batch size, and poll interval

Risks:

- Full replay even when snapshot exists
- No explicit concurrency strategy
- No evidence of large-stream backpressure handling

Assessment:

- Suitable for low-volume background processing
- Not yet tuned for large-scale recovery windows

## Projection Throughput

### Current design

- `MemoryEventProjector` can handle single events or batches
- `SqlReadModelRepository` saves frequently and individually

Risks:

- Many `SaveChangesAsync(...)` calls
- Duplicate `MarkProcessedAsync(...)` calls
- No durable consumer scaling model
- Read-model rebuild approach is undocumented

Recommendation:

- Move to broker-backed consumers
- Batch or transactionally group projection updates
- Define projection rebuild pipeline from the event store

## Storage Growth and Throughput

### Event store

Assessment:

- MongoDB is appropriate for append-only event streams at the likely scale of this solution.
- The compound unique index on `(StreamId, Version)` is a strong design choice.

Potential bottlenecks:

- stream-local hot aggregates
- lack of archival/retention policy
- large payload growth if raw memory text is unbounded

### SQL read models

Assessment:

- Read model size will likely remain manageable if it stores summaries rather than entire histories.
- Projection throughput becomes the main concern before raw storage size.

## AI and Vector Performance

### Current state

- No concrete embedding or vector implementation exists.

Future performance risks:

- embedding generation latency
- provider rate limiting
- unfiltered vector search at large cardinality
- expensive reindex operations after event replay or schema changes

Recommendation:

- Use a vector store with HNSW or equivalent ANN indexing
- Enforce metadata filters for user/tenant scope before ranking
- Decouple embedding generation from synchronous command processing

## Performance Verdict

The current design is acceptable for low-volume development and architectural validation. It is not yet tuned for enterprise throughput, large replay windows, or resilient background processing at scale.
