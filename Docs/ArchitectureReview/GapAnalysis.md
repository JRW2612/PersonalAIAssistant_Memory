# Gap Analysis

## Summary

The codebase has a solid event-sourced core, but there is a large gap between current implementation and enterprise production expectations. The most important gap is not code style; it is the missing operational envelope around the write/read model.

## Gap Matrix

| Area | Current State | Target State | Gap Severity | Recommended Mitigation |
| --- | --- | --- | --- | --- |
| Application host | No API/worker host project or `Program.cs` | Runnable service host with DI, config, health checks, and deployment packaging | Critical | Add a host project that composes Business, Core, and Infrastructure |
| Messaging | `InMemoryEventBus` only logs events | Durable broker-backed transport | Critical | Replace with MassTransit + RabbitMQ |
| Transaction boundaries | Event append and publish are separate | Atomic store + publish semantics | Critical | Implement transactional outbox |
| Command completeness | 2 handlers implemented, several missing or stubbed | Full command coverage | High | Implement missing handlers for update, compress, consolidate, index |
| Queries/read API | No query handlers or read API | Complete CQRS read path | High | Add query services/handlers and expose them through a host |
| Security | No auth, authz, or ownership checks | Enterprise identity and policy enforcement | Critical | Add JWT/OIDC, claims/policies, ownership validation |
| Data protection | Memory payloads appear plaintext | Encryption in transit and at rest, selective field protection | Critical | Add secret management and application-level payload protection strategy |
| Snapshoting | Worker exists, repository missing, tail replay not used | Efficient snapshot lifecycle | High | Implement snapshot repository and tail replay |
| Event evolution | No versioning/upcasters | Backward-compatible event contract evolution | High | Add event version metadata and upcaster pipeline |
| AI readiness | Empty internal interfaces only | Real embeddings, vector search, ranking pipeline | High | Implement concrete AI/vector adapters |
| Observability | Basic `ILogger` only | Logs, traces, metrics, alerts | High | Add Serilog + OpenTelemetry + health checks |
| Testing | No tests in repo | Unit, integration, contract, and performance test suites | Critical | Create test projects and CI quality gates |
| Multi-tenancy | No tenant concept | Tenant-aware domain and storage boundaries | High | Add tenant ID propagation, partitioning, and authorization rules |
| Disaster recovery | No backup/recovery evidence | Backup, replay, restore, and rebuild procedures | High | Document restore workflows and automate read-model rebuild |

## Detailed Gaps

### Architecture gap

Current:

- Four class libraries
- No executable runtime boundary

Target:

- Service host with startup, dependency injection, health probes, and deployment topology

### Business logic gap

Current:

- `AddMemory` and `DeleteMemory` are wired
- `UpdateMemory`, `CompressMemory`, `ConsolidateMemory`, and `IndexMemory` are incomplete

Target:

- Full command lifecycle and consistent domain behavior for all command types

### Data consistency gap

Current:

- Handlers append events and then publish separately

Target:

- Store and publish via outbox and durable consumer model

### Operational gap

Current:

- No observability stack
- No deployment guidance beyond build
- No automated verification

Target:

- Operable service with dashboards, alerts, CI pipelines, and rollback/recovery strategy

## Gap Conclusion

The repo is closest to a reusable architectural kernel. To become enterprise-deployable, it needs a host application, durable messaging, security, observability, and completion of its incomplete command and AI surfaces.
