#!/usr/bin/env python3
"""Executable source-freshness-query/v1 projection vectors.

This deterministic model runs the *existing* source-freshness/v1 classifier
at each trusted request instant, then verifies filter/count/facet/page results.
It deliberately does NOT claim a production search adapter or UI exists.
VER-P0-09/17 later require separate real API/PostgreSQL/browser acceptance.
"""
from __future__ import annotations

import copy
import json
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from test_source_freshness_vectors import classify

ROOT = HERE.parent
QUERY_VECTORS = HERE / "fixtures" / "source_freshness_query_vectors_v1.json"
SOURCE_VECTORS = HERE / "fixtures" / "source_freshness_vectors_v1.json"


def project_query(document: dict, policies: dict, case: dict) -> dict:
    """Compute a fresh, class-scoped projection, never trust indexed CURRENT.

    A production cursor may instead fail closed on age/policy drift; this
    deterministic reference re-filters the current page from the fresh set.
    """
    fresh_records = []
    states = {}
    for record in document["records"]:
        evidence_case = {
            "policy": record["policy"],
            "retrieved_at": record["retrieved_at"],
            "evaluated_at": case["evaluated_at"],
            "revision_verified": record["id"] not in case.get("revision_unverified_ids", []),
            "replacement_proven": record["id"] in case.get("superseded_ids", []),
            "replacement_authority_verified": record["id"] in case.get("superseded_ids", []),
        }
        if case.get("policy_version_override") is not None:
            evidence_case["policy_override"] = {
                "policy_version": case["policy_version_override"],
            }
        state, _eligible, _reason = classify(policies, evidence_case)
        states[record["id"]] = state
        if case["freshness_filter"] is None or state == case["freshness_filter"]:
            fresh_records.append(record)

    # The cached IDs and historical indexed labels are intentionally *not*
    # read as authorization; the fresh evaluated_at and policy govern each
    # filter/page/facet/count response.
    fresh_records.sort(key=lambda item: item["id"])
    count = len(fresh_records)
    facets = dict(sorted(Counter(item["category"] for item in fresh_records).items()))
    page_size = case.get("page_size", count or 1)
    page_index = case.get("page_index", 0)
    visible = fresh_records[page_index * page_size : (page_index + 1) * page_size]
    return {
        "ids": [item["id"] for item in visible],
        "total": count,
        "facets": facets,
        "states": states,
    }


def main() -> None:
    document = json.loads(QUERY_VECTORS.read_text(encoding="utf-8"))
    policies = json.loads(SOURCE_VECTORS.read_text(encoding="utf-8"))
    assert document["schema_version"] == "source-freshness-query-v1"
    assert policies["schema_version"] == "source-freshness-vectors/v1"
    assert document["source_classification_fixture"] == (
        "tests/fixtures/source_freshness_vectors_v1.json"
    )
    cases = document["cases"]
    ids = [item["id"] for item in cases]
    assert len(ids) == len(set(ids)) and len(ids) >= 9
    record_ids = [item["id"] for item in document["records"]]
    assert len(record_ids) == len(set(record_ids))

    for case in cases:
        result = project_query(document, policies, case)
        assert result["ids"] == case["expected_ids"], (case["id"], result)
        assert result["total"] == case["expected_total"], (case["id"], result)
        assert result["facets"] == case["expected_facets"], (case["id"], result)
        for record, state in case.get("expected_states", {}).items():
            assert result["states"][record] == state, (case["id"], record, result)

    # A regression accepting prior indexed state would falsely retain EDGE
    # past the max retrieval-age boundary. Assert that boundary is material.
    bad = copy.deepcopy(next(c for c in cases if c["id"] == "current-next-second"))
    assert "edge" in bad["cache_previous_ids"]
    result = project_query(document, policies, bad)
    assert "edge" not in result["ids"]
    assert result["states"]["edge"] == "STALE"

    # Missing trusted revision and unknown policy cannot turn a cached
    # CURRENT into a current-filtered result.
    missing = project_query(
        document,
        policies,
        next(c for c in cases if c["id"] == "revision-proof-missing"),
    )
    assert missing["states"]["later"] == "UNKNOWN"
    drift = project_query(
        document,
        policies,
        next(c for c in cases if c["id"] == "policy-version-changed"),
    )
    assert drift["total"] == 0
    assert set(drift["states"].values()) == {"UNKNOWN"}
    print(f"source-freshness-query vectors: {len(cases)} passed (deterministic contract only)")


if __name__ == "__main__":
    main()
