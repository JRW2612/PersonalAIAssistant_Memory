# AI Memory Management Competitive Benchmark

**Reviewed:** 10 November 2026  
**Scope:** Repository implementation compared with current public capabilities of Mem0, Zep/Graphiti, Letta, and LangGraph/LangMem. This is a product and engineering gap assessment, not a claim that the systems are feature-equivalent.

## Executive view

This repository has a useful foundation for a differentiated memory service: event-sourced memory changes, a vector retrieval path with importance/recency scoring, editable agent tools, and structured temporal facts. Its strongest potential position is **governed, auditable memory for personal and enterprise assistants**, with temporal history and user ownership as core product properties.

It is not yet competitive on demonstrated retrieval quality or ease of adoption. The retrieval service currently does vector search and rescoring; the repository has no published benchmark results that demonstrate recall, precision, latency, or cost. Competitors make memory lifecycle and cross-session retrieval easy to consume through established APIs, and Zep/Graphiti combines vector, full-text, and graph search while retaining temporal fact history. The priority should be to prove quality, improve retrieval and provenance, then make integrations straightforward.

## Capability comparison

| Capability | This repository | Mem0 | Zep / Graphiti | Letta | LangGraph / LangMem |
| --- | --- | --- | --- | --- | --- |
| Memory lifecycle | Add, update, delete, compression, consolidation commands and agent tools exist in the codebase. Verify the deployable API contract and end-to-end behavior before advertising these as production-ready. | Public API documents add, search, list, get, update, delete, history, filtering, and batch operations. | Adds episodes and derives facts, entities, and relationships; supports high-level context retrieval and direct graph search. | Agent and developer can edit memory blocks; archival memory is separate from in-context blocks. | Store supports namespaced long-term items; LangMem managers extract, update, and delete memories. |
| Retrieval | Vector search, overfetch, then a score combining vector relevance, recency, and importance. No keyword or graph candidate path is visible in `MemoryRetrievalService`. | Search supports semantic, keyword, entity, and temporal signals, plus metadata filters. | Hybrid vector, full-text, and graph traversal; graph facts carry validity timestamps and source episode links. | Core memory is pinned in context; archival memory is retrieved through agent tools. | Store can perform semantic search and content filtering within namespaces. |
| Temporal reasoning | Structured subject-predicate-object facts and invalidation events exist. Retrieval/context assembly integration and point-in-time evaluation need verification. | Temporal scoring and metadata are available in search; explicit temporal graph history is not its central documented representation. | Temporal graph is a first-class model: valid/invalid times and historical relationships. | Stateful messages and memory blocks, with agent-controlled memory editing. | Semantic, episodic, and procedural memory patterns; application chooses its storage and update logic. |
| Provenance and explainability | Event history and consolidation provenance concepts exist; source-level attribution in retrieval responses needs verification. | Memory history and per-signal score details are documented. | Facts link to the source episodes from which they were derived. | Message history remains retrievable after context compaction. | Developer-defined stores and schemas; source attribution is application-specific. |
| Agent integration | Agent tools include archival insert/search, replacement, temporal fact upsert, and working-memory append. | REST and SDK workflows center on add/search/update/delete. | SDK and graph APIs return context or raw graph artifacts. | Stateful-agent runtime and tools are the product model. | Native store integration within LangGraph; LangMem handles memory management. |
| Evidence of quality | No benchmark report or reproducible evaluation harness found in the checked-in docs. | Publishes benchmark claims and discusses LoCoMo; numbers must be interpreted with each vendor's methodology. | Publishes LoCoMo and LongMemEval results; methodology and workload must be compared before drawing conclusions. | Product focuses on stateful agents and explicit memory control. | Documentation emphasizes application-specific design and recommends evaluation. |

Competitor capabilities are summarized from their official documentation. Feature availability can vary by product tier, SDK version, and hosting model.

## What the code already supports

- Event-sourced memory records and updates, with projections into a SQL read model.
- Embedding generation and vector-backed retrieval with a post-search importance/recency scorer.
- Agent-facing tools for archival insertion and search, replacement, temporal fact upsert, and working-memory append.
- Temporal fact records with assertion and invalidation operations.
- Consolidation/compression workflows and memory ownership checks.

These are promising ingredients, but their presence in code does not demonstrate operational reliability, production API completeness, or retrieval quality. The repository's existing architecture review documents describe an older solution state in several places; update those assessments as implementation changes land.

## Highest-value gaps

1. **No reproducible quality evidence.** Without a shared question set and labeled expected memories, claims about recall or accuracy cannot be compared fairly.
2. **Single-path retrieval.** Vector-only candidate generation can miss exact names, identifiers, and relationships that lexical or graph retrieval finds.
3. **Weak evidence returned to agents.** Retrieved memory results should expose stable source IDs, source text or event references, timestamps, and why a result ranked highly.
4. **Memory scope and filtering need a product contract.** Make user, agent, application, session, and tenant boundaries explicit and filterable, while retaining strict authorization.
5. **Memory conflict behavior needs visible guarantees.** A changed preference should supersede the old current fact without erasing its history; users and agents need inspect, correct, export, and delete controls.
6. **Integration friction.** Provide a concise REST contract, SDK or MCP integration, quickstart, and a one-command local deployment path.

## Recommended roadmap

### 1. Establish the baseline

Create a small, versioned evaluation set with semantic recall, exact entity/name lookup, preference updates, temporal questions, source attribution, and cross-user isolation cases. Report Recall@K, MRR or nDCG, answer support/grounding, latency percentiles, and token/cost estimates. Record the model, embedding, index, prompt, and dataset version so runs can be reproduced. Include comparisons against a plain vector baseline and at least one external memory service under the same questions and context budget.

### 2. Improve retrieval without hiding its evidence

Add a lexical candidate path and combine it with vector candidates using rank fusion. Add opt-in filters for memory type, tags, source, and time range. Use temporal facts for current-state and point-in-time questions. Preserve candidate and score breakdowns in a diagnostic response, and return source IDs and timestamps to the caller. Keep tenant and owner filters enforced server-side across every candidate source.

### 3. Make memory lifecycle a dependable product surface

Publish consistent create, search, get, update, delete, history, and export operations. Define whether insertion is verbatim or extraction-based, make that choice explicit, and surface updates/deletions as auditable events. Add APIs for reviewing and correcting extracted facts, with provenance retained after consolidation.

### 4. Add adoption paths

Ship one minimal .NET client and an MCP server or compatible tool definition set, plus runnable examples for adding a memory, searching it, correcting a fact, and deleting/exporting a user's data. Document self-hosting and supported model/vector providers with verified configuration.

### 5. Differentiate through governed temporal memory

Make the product's defining workflow: **“What did the assistant know at time T, what source established that, and what changed it?”** Preserve invalidated facts, link them to source episodes/events, support point-in-time retrieval, and show the evidence trail in API responses. Combine that with demonstrable data residency, encryption, tenant isolation, retention, and deletion behavior. Publish measured results and threat-model scope before making compliance or security claims.

## Suggested acceptance criteria for a competitive release

- A benchmark command reproduces Recall@K, MRR/nDCG, grounding, latency, and cost metrics from a checked-in versioned dataset.
- Hybrid search improves exact-entity and temporal question performance against the current vector-only baseline without reducing tenant isolation.
- Each returned memory includes stable ID, creation/update time, source/provenance reference, and optional ranking explanation.
- A corrected fact is current in ordinary retrieval, while the prior value remains retrievable in history with validity bounds.
- A clean local setup can create, search, update, inspect history, export, and delete a user's memories from documented interfaces.

## Sources

- [Mem0 API reference](https://github.com/mem0ai/mem0/blob/main/skills/mem0/references/api-reference.md) and [how Mem0 works](https://github.com/mem0ai/mem0/blob/main/docs/core-concepts/how-it-works.mdx)
- [Zep concepts and retrieval](https://help.getzep.com/v2/concepts), [Zep facts and validity](https://help.getzep.com/facts), and [Graphiti overview](https://help.getzep.com/graphiti/getting-started/welcome)
- [Letta stateful agents](https://docs.letta.com/v1-sdk/concepts/stateful-agents) and [memory blocks](https://docs.letta.com/v1-sdk/memory/memory-blocks)
- [LangGraph memory overview](https://langchain-ai.github.io/langgraph/concepts/memory/) and [LangMem semantic extraction](https://langchain-ai.github.io/langmem/guides/extract_semantic_memories/)
