# Technical Debt Report

## Summary

The main technical debt in this repository is architectural incompleteness rather than poor code hygiene. Most debt items directly affect correctness, operability, or scale.

## Debt Register

### TD-01 Dual write between event store and bus

- Location:
  - `PersonalAIAssistant.Memory.Business/Handlers/AddMemoryCommandHandler.cs`
  - `PersonalAIAssistant.Memory.Business/Handlers/DeleteMemoryCommandHandler.cs`
  - `PersonalAIAssistant.Memory.Business/Workers/ConsolidationWorker.cs`
- Description:
  - Events are stored first and published second through `IEventBus`.
- Impact:
  - Lost publications if the process stops between persistence and publish
  - Read-model divergence
  - Broken auditability
- Risk:
  - Critical
- Remediation:
  - Add an outbox persisted atomically with event append
  - Publish asynchronously from an outbox dispatcher
- Estimated effort:
  - 24 to 32 hours

### TD-02 Development-only event bus

- Location:
  - `PersonalAIAssistant.Memory.Infrastructure/InMemory/InMemoryEventBus.cs`
- Description:
  - Event bus implementation only logs events.
- Impact:
  - No delivery guarantees
  - No retries
  - No dead-lettering
  - No horizontal scale
- Risk:
  - Critical
- Remediation:
  - Replace with MassTransit + RabbitMQ
- Estimated effort:
  - 24 to 40 hours

### TD-03 Incomplete command implementation

- Location:
  - `PersonalAIAssistant.Memory.Business/Handlers/CompressMemoryCommandHandler.cs`
  - `PersonalAIAssistant.Memory.Business/Handlers/ConsolidateMemoriesCommandHandler.cs`
  - missing handlers for `UpdateMemoryCommand` and `MemoryIndexedCommand`
- Description:
  - Several commands exist without real application behavior.
- Impact:
  - Public command surface overstates actual capability
  - Business logic paths are inconsistent
- Risk:
  - High
- Remediation:
  - Implement the missing handlers and align them with explicit aggregate behaviors
- Estimated effort:
  - 16 to 28 hours

### TD-04 Read-model update bug

- Location:
  - `PersonalAIAssistant.Memory.Business/Projectors/MemoryEventProjector.cs`
  - `PersonalAIAssistant.Memory.Core/Domains/MemoryAggregate.cs`
- Description:
  - Aggregate emits `UpdatedFields["RawText"]`, but the projector looks for `nameof(MemoryReadModel.Summary)`.
- Impact:
  - Update events may not change the SQL read model as intended
- Risk:
  - High
- Remediation:
  - Standardize update field names or project from aggregate field semantics rather than read-model property names
- Estimated effort:
  - 2 to 4 hours

### TD-05 Duplicate processed-event marking

- Location:
  - `PersonalAIAssistant.Memory.Business/Projectors/MemoryEventProjector.cs`
- Description:
  - Individual handlers call `MarkProcessedAsync(...)`, and batch dispatcher marks again after dispatch.
- Impact:
  - Extra database writes
  - Confusing idempotency semantics
- Risk:
  - Medium
- Remediation:
  - Centralize processed-event marking in one place only
- Estimated effort:
  - 2 hours

### TD-06 Snapshot implementation gap

- Location:
  - `PersonalAIAssistant.Memory.Core/Interfaces/Others/ISnapshotRepository.cs`
  - `PersonalAIAssistant.Memory.Business/Workers/SnapshotWorker.cs`
  - `PersonalAIAssistant.Memory.Core/Domains/MemoryAggregateFactory.cs`
- Description:
  - Snapshot infrastructure is abstracted but not implemented in-repo, and worker replays the full stream even when snapshots exist.
- Impact:
  - Limited replay optimization
  - Unverifiable recovery path
- Risk:
  - High
- Remediation:
  - Implement repository, use `GetEventsFromVersionAsync(...)`, and add snapshot tests
- Estimated effort:
  - 12 to 20 hours

### TD-07 Event evolution debt

- Location:
  - `PersonalAIAssistant.Memory.Infrastructure/Mongo/MongoEventStore.cs`
  - `PersonalAIAssistant.Events/*`
- Description:
  - No event contract versioning or upcasting support.
- Impact:
  - Fragile long-term event-store compatibility
- Risk:
  - High
- Remediation:
  - Add version metadata and an upcaster pipeline
- Estimated effort:
  - 16 to 24 hours

### TD-08 Primitive obsession in domain

- Location:
  - `PersonalAIAssistant.Memory.Core/Domains/MemoryAggregate.cs`
- Description:
  - Tags, user identity, provider names, and provenance values are primitives rather than value objects.
- Impact:
  - Weak validation and harder policy enforcement
- Risk:
  - Medium
- Remediation:
  - Introduce `Tag`, `OwnerId`, `TenantId`, and provider abstractions where justified
- Estimated effort:
  - 8 to 16 hours

### TD-09 Empty AI abstractions

- Location:
  - `PersonalAIAssistant.Memory.Core/Interfaces/Others/IEmbeddingService.cs`
  - `PersonalAIAssistant.Memory.Core/Interfaces/Others/IVectorMemoryRepository.cs`
- Description:
  - Interfaces exist but define no contract.
- Impact:
  - AI readiness is nominal rather than real
- Risk:
  - Medium
- Remediation:
  - Define request/response contracts, ownership filters, ranking semantics, and retry/error behavior
- Estimated effort:
  - 8 to 12 hours

### TD-10 Missing host/runtime shell

- Location:
  - repository-wide
- Description:
  - No host, DI composition root, appsettings, or environment configuration.
- Impact:
  - The solution cannot be exercised as a deployed service
- Risk:
  - Critical
- Remediation:
  - Add service host project and runtime composition
- Estimated effort:
  - 16 to 24 hours

## Debt Prioritization

### Must address before production

- TD-01
- TD-02
- TD-03
- TD-04
- TD-06
- TD-10

### Can follow in the next hardening wave

- TD-05
- TD-07
- TD-08
- TD-09
