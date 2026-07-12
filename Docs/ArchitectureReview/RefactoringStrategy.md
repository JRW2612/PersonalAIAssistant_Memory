# Refactoring Strategy

## Principles

- Keep CQRS
- Keep event sourcing
- Refactor for correctness, operational readiness, and maintainability
- Avoid cosmetic churn

## Refactoring Candidates

### RF-01 Fix dual write in handlers and workers

- Description:
  - Event persistence and event publication are separate steps.
- Locations:
  - `PersonalAIAssistant.Memory.Business/Handlers/AddMemoryCommandHandler.cs`
  - `PersonalAIAssistant.Memory.Business/Handlers/DeleteMemoryCommandHandler.cs`
  - `PersonalAIAssistant.Memory.Business/Workers/ConsolidationWorker.cs`
- Impact:
  - Critical correctness and reliability issue
- Recommendation:
  - Introduce outbox persistence with atomic append
  - Move publish responsibility to an outbox dispatcher/consumer
- Estimated effort:
  - 24 to 32 hours

### RF-02 Complete missing application-layer handlers

- Description:
  - Command set is larger than actual implementation coverage.
- Locations:
  - `PersonalAIAssistant.Memory.Business/Commands/UpdateMemoryCommand.cs`
  - `PersonalAIAssistant.Memory.Business/Commands/CompressMemoryCommand.cs`
  - `PersonalAIAssistant.Memory.Business/Commands/ConsolidateMemoriesCommand.cs`
  - `PersonalAIAssistant.Memory.Business/Commands/MemoryIndexedCommand.cs`
- Impact:
  - Business capability ambiguity and partial CQRS implementation
- Recommendation:
  - Add missing handlers and decide whether consolidate/index belong in aggregate methods, domain services, or asynchronous workflows
- Estimated effort:
  - 16 to 28 hours

### RF-03 Correct the update projection contract

- Description:
  - Aggregate emits update field key `RawText`, but projector reads `Summary`.
- Locations:
  - `PersonalAIAssistant.Memory.Core/Domains/MemoryAggregate.cs`
  - `PersonalAIAssistant.Memory.Business/Projectors/MemoryEventProjector.cs`
- Impact:
  - Stale read models after update operations
- Recommendation:
  - Standardize update contracts around domain property names and let the projector derive summary fields from domain data
- Estimated effort:
  - 2 to 4 hours

### RF-04 Remove duplicate idempotency marking

- Description:
  - Processed events are marked inside handlers and after batch dispatch.
- Location:
  - `PersonalAIAssistant.Memory.Business/Projectors/MemoryEventProjector.cs`
- Impact:
  - Extra writes and confusing projection semantics
- Recommendation:
  - Centralize processed-event marking in one layer only
- Estimated effort:
  - 2 hours

### RF-05 Separate production abstractions from placeholders

- Description:
  - AI interfaces are empty internal placeholders.
- Locations:
  - `PersonalAIAssistant.Memory.Core/Interfaces/Others/IEmbeddingService.cs`
  - `PersonalAIAssistant.Memory.Core/Interfaces/Others/IVectorMemoryRepository.cs`
- Impact:
  - Future AI work lacks contract clarity
- Recommendation:
  - Define explicit input/output contracts, ownership filtering, failure semantics, and async behavior
- Estimated effort:
  - 8 to 12 hours

### RF-06 Improve snapshot correctness and performance

- Description:
  - Snapshot payload omits status and worker reloads full stream history.
- Locations:
  - `PersonalAIAssistant.Memory.Core/Domains/MemoryAggregateFactory.cs`
  - `PersonalAIAssistant.Memory.Business/Workers/SnapshotWorker.cs`
- Impact:
  - Replay inefficiency and partial snapshot fidelity
- Recommendation:
  - Persist full snapshot state, then replay only tail events
- Estimated effort:
  - 8 to 12 hours

### RF-07 Reduce primitive obsession in the domain

- Description:
  - Tags, owner IDs, provider names, and similar concepts are raw strings.
- Location:
  - `PersonalAIAssistant.Memory.Core/Domains/MemoryAggregate.cs`
- Impact:
  - Validation and policy enforcement remain weak
- Recommendation:
  - Introduce value objects where domain rules are meaningful
- Estimated effort:
  - 8 to 16 hours

### RF-08 Replace polling-first workflow with event-driven processing

- Description:
  - Workers poll repositories continuously.
- Locations:
  - `PersonalAIAssistant.Memory.Business/Workers/ConsolidationWorker.cs`
  - `PersonalAIAssistant.Memory.Business/Workers/SnapshotWorker.cs`
- Impact:
  - Wasted database effort and limited scaling model
- Recommendation:
  - Use broker-triggered consumers for primary execution, with polling retained only for reconciliation
- Estimated effort:
  - 12 to 20 hours

### RF-09 Untangle namespace and layering inconsistencies

- Description:
  - `IEventStore` lives under `Core/Interfaces/Mongo` with namespace `PersonalAIAssistant.Memory.Infrastructure.Mongo`, which leaks infrastructure terminology into the abstraction layer.
- Location:
  - `PersonalAIAssistant.Memory.Core/Interfaces/Mongo/IEventStore.cs`
- Impact:
  - Blurs clean architecture boundaries
- Recommendation:
  - Move abstractions into domain/application-neutral namespaces
- Estimated effort:
  - 4 to 6 hours

## Refactoring Order

1. RF-01 dual write
2. RF-02 missing handlers
3. RF-03 projection correctness
4. RF-06 snapshots
5. RF-09 layering cleanup
6. RF-05 AI contracts
7. RF-07 value objects
8. RF-08 event-driven processing
