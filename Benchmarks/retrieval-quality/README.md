# Retrieval quality evaluation

This harness measures ranked memory retrieval against a small, versioned set of semantic, exact-entity, temporal, provenance, and user-isolation cases. Run the same cases through the vector-only baseline and hybrid retrieval, save each strategy's ranked IDs, then evaluate both with the same dataset.

## Result format

One JSON object per query in `results.jsonl`:

```json
{"query_id":"semantic-focus-location","ranked_memory_ids":["mira-focus-location"],"latency_ms":12.4,"estimated_cost_usd":0.00001}
```

`ranked_memory_ids` must be in retrieval order. Latency and estimated cost are optional. The evaluator also checks that IDs listed as forbidden for a query are never returned.

## Run

```powershell
python Benchmarks/retrieval-quality/evaluate.py `
  --dataset Benchmarks/retrieval-quality/dataset-v1.jsonl `
  --results vector-results.jsonl `
  --label vector-only

python Benchmarks/retrieval-quality/evaluate.py `
  --dataset Benchmarks/retrieval-quality/dataset-v1.jsonl `
  --results hybrid-results.jsonl `
  --label hybrid
```

The report includes Recall@K, MRR, nDCG@K, forbidden-result count, latency/cost averages when supplied, and category breakdowns. Keep the dataset version, embedding model, retrieval configuration, and result files with each comparison. The cases are synthetic starter data; replace or extend them with consented, de-identified application examples before using scores as product claims.
