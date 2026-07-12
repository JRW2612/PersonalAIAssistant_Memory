# Testing Roadmap

## Current State Assessment
The repository contains the foundation, but comprehensive testing strategies for CQRS and Event Sourcing are required.

## Coverage Matrix Target

| Component | Target Coverage | Strategy |
|-----------|-----------------|----------|
| **Aggregates** | 100% | Pure Unit Tests. Given (Events) -> When (Command) -> Then (Events). |
| **Handlers** | 90% | Unit Tests mocking `IEventStore` and `IEventBus`. |
| **Repositories** | 80% | Integration Tests via Testcontainers (MongoDB / SQL Server). |
| **Workers** | 80% | Integration Tests verifying DB polling and API mocking (WireMock for LLM). |
| **Projections** | 90% | Integration Tests. Publish Event -> Assert SQL Read Model state. |

## Event Sourcing Specific Testing
1. **Event Replay Tests:** Test that loading an aggregate from 1,000 historical events correctly restores state.
2. **Snapshot Tests:** Test that an aggregate restored from a snapshot behaves identically to one restored from full event history.
3. **Upcaster Tests:** Ensure old event schemas (JSON) successfully deserialize and upgrade to current classes.

## QA Architecture
- **Frameworks:** xUnit, FluentAssertions, Moq.
- **Integration:** Testcontainers (spins up Docker Mongo and SQL Server during CI).
- **Performance:** k6 for load testing event replay throughput.
