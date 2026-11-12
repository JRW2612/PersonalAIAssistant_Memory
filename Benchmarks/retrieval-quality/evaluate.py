#!/usr/bin/env python3
"""Evaluate ranked memory IDs against a versioned JSONL retrieval dataset."""

import argparse
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


def read_jsonl(path):
    with Path(path).open(encoding="utf-8") as stream:
        return [json.loads(line) for line in stream if line.strip()]


def dcg(relevant, ranked, k):
    return sum((1.0 / math.log2(index + 2)) for index, memory_id in enumerate(ranked[:k]) if memory_id in relevant)


def score_query(case, result, k):
    expected = set(case["expected_memory_ids"])
    forbidden = set(case.get("forbidden_memory_ids", []))
    ranked = result.get("ranked_memory_ids", [])[:k]
    found = [memory_id for memory_id in ranked if memory_id in expected]

    recall = len(set(found)) / len(expected) if expected else None
    reciprocal_rank = next((1.0 / (index + 1) for index, memory_id in enumerate(ranked) if memory_id in expected), 0.0)
    ideal_hits = min(len(expected), k)
    ideal_dcg = sum(1.0 / math.log2(index + 2) for index in range(ideal_hits))
    ndcg = dcg(expected, ranked, k) / ideal_dcg if ideal_dcg else None

    return {
        "query_id": case["query_id"],
        "category": case["category"],
        "recall_at_k": recall,
        "reciprocal_rank": reciprocal_rank if expected else None,
        "ndcg_at_k": ndcg,
        "forbidden_hits": sorted(set(ranked) & forbidden),
        "latency_ms": result.get("latency_ms"),
        "estimated_cost_usd": result.get("estimated_cost_usd"),
    }


def mean_defined(rows, key):
    values = [row[key] for row in rows if row[key] is not None]
    return statistics.mean(values) if values else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", required=True, help="Versioned dataset JSONL")
    parser.add_argument("--results", required=True, help="Ranked results JSONL from one retrieval strategy")
    parser.add_argument("--k", type=int, default=5, help="Cutoff for ranking metrics")
    parser.add_argument("--label", default="run", help="Name for this strategy/run")
    args = parser.parse_args()
    if args.k <= 0:
        parser.error("--k must be greater than zero")

    cases = read_jsonl(args.dataset)
    results = {result["query_id"]: result for result in read_jsonl(args.results)}
    expected_ids = {case["query_id"] for case in cases}
    missing = sorted(expected_ids - results.keys())
    extra = sorted(results.keys() - expected_ids)
    if missing or extra:
        parser.error(f"result query IDs must match dataset; missing={missing}, extra={extra}")

    rows = [score_query(case, results[case["query_id"]], args.k) for case in cases]
    categories = defaultdict(list)
    for row in rows:
        categories[row["category"]].append(row)

    report = {
        "label": args.label,
        "dataset": str(Path(args.dataset)),
        "k": args.k,
        "query_count": len(rows),
        "recall_at_k": mean_defined(rows, "recall_at_k"),
        "mrr": mean_defined(rows, "reciprocal_rank"),
        "ndcg_at_k": mean_defined(rows, "ndcg_at_k"),
        "forbidden_hit_count": sum(len(row["forbidden_hits"]) for row in rows),
        "mean_latency_ms": mean_defined(rows, "latency_ms"),
        "mean_estimated_cost_usd": mean_defined(rows, "estimated_cost_usd"),
        "by_category": {
            category: {
                "query_count": len(category_rows),
                "recall_at_k": mean_defined(category_rows, "recall_at_k"),
                "mrr": mean_defined(category_rows, "reciprocal_rank"),
                "ndcg_at_k": mean_defined(category_rows, "ndcg_at_k"),
                "forbidden_hit_count": sum(len(row["forbidden_hits"]) for row in category_rows),
            }
            for category, category_rows in sorted(categories.items())
        },
        "queries": rows,
    }
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
