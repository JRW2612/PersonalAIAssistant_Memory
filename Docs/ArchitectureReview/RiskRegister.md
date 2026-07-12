# Risk Register

| Risk ID | Category | Description | Impact | Probability | Exposure | Mitigation |
| --- | --- | --- | --- | --- | --- | --- |
| R01 | Data consistency | Event append and publish are separate operations, creating lost-event risk | Critical | High | Critical | Implement transactional outbox |
| R02 | Security | No authentication or authorization model exists | Critical | High | Critical | Add JWT/OIDC and ownership policies |
| R03 | Runtime | No host project exists, so service cannot be deployed as a real application | Critical | High | Critical | Add executable host and startup composition |
| R04 | Scalability | `InMemoryEventBus` prevents multi-instance processing | High | High | High | Use MassTransit + RabbitMQ |
| R05 | Business logic | Several commands are missing handlers or real implementation | High | High | High | Complete write-side command coverage |
| R06 | Read-model correctness | Update projection mismatch can leave read models stale | High | Medium | High | Fix projector field mapping |
| R07 | Recovery | Snapshot path is incomplete and replay optimization is underused | High | Medium | High | Implement snapshot repository and tail replay |
| R08 | Testing | No automated tests found | High | High | High | Add unit/integration/contract/performance tests |
| R09 | Privacy | Memory payloads may contain PII and are currently unprotected in code | High | High | High | Add classification and payload protection strategy |
| R10 | Event evolution | No upcasters or event schema versioning | High | Medium | High | Add versioned contracts and upcaster pipeline |
| R11 | Observability | No metrics/tracing/health checks | Medium | High | High | Add telemetry stack and health monitoring |
| R12 | AI dependency | Future LLM calls may be slow, expensive, or rate-limited | Medium | Medium | Medium | Add retry/backoff/queueing and provider abstraction |
| R13 | Multi-tenancy | No tenant model exists | Medium | Medium | Medium | Introduce tenant-aware identity and storage partitioning |
| R14 | Operability | No documented rebuild, DR, or reconciliation workflow | Medium | Medium | Medium | Add operational runbooks and rebuild tooling |

## Top 5 Risks

1. `R01` dual-write inconsistency
2. `R02` lack of security controls
3. `R03` no deployable host
4. `R04` in-memory event bus
5. `R08` no automated tests

## Risk Posture

Current posture:

- unacceptable for production

Reason:

- Multiple critical risks exist in correctness, security, and deployability rather than only in performance or maintainability.
