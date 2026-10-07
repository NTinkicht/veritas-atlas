#!/usr/bin/env python3
"""Executable deterministic checks for source-freshness/v1 fixtures."""
from __future__ import annotations

import copy
import json
import re
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VECTORS = ROOT / "tests" / "fixtures" / "source_freshness_vectors_v1.json"
RFC3339_UTC = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z$")


def parse_utc(value: object) -> datetime | None:
    if not isinstance(value, str) or not RFC3339_UTC.fullmatch(value):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo == timezone.utc else None


def classify(document: dict, case: dict) -> tuple[str, bool, str | None]:
    canonical = document["policies"][case["policy"]]
    policy = copy.deepcopy(canonical)
    policy.update(case.get("policy_override") or {})

    required_policy_fields = {
        "policy_id",
        "policy_version",
        "source_match",
        "freshness_required_for_current_claims",
        "max_retrieval_age_seconds",
        "revision_signal",
        "revision_signal_required",
        "replacement_authority",
    }
    complete_policy = all(
        field in policy and policy[field] not in (None, "")
        for field in required_policy_fields
    )
    supported = (
        complete_policy
        and policy.get("policy_id") == canonical["policy_id"]
        and policy.get("policy_version") == canonical["policy_version"]
        and policy.get("source_match") == canonical["source_match"]
        and policy.get("revision_signal") == canonical["revision_signal"]
        and policy.get("replacement_authority")
        == canonical["replacement_authority"]
    )

    trusted_replacement = (
        supported
        and case.get("replacement_proven") is True
        and case.get("replacement_authority_verified") is True
    )
    if trusted_replacement:
        state = "SUPERSEDED"
    else:
        retrieved = parse_utc(case.get("retrieved_at"))
        evaluated = parse_utc(case.get("evaluated_at"))
        max_age = policy.get("max_retrieval_age_seconds")
        required_revision_ok = (
            policy.get("revision_signal_required") is False
            or case.get("revision_verified") is True
        )

        valid_age_policy = (
            isinstance(max_age, int)
            and not isinstance(max_age, bool)
            and max_age >= 0
        )
        valid_times = (
            retrieved is not None
            and evaluated is not None
            and retrieved <= evaluated
        )

        if (
            not supported
            or not valid_age_policy
            or not required_revision_ok
            or not valid_times
        ):
            state = "UNKNOWN"
        else:
            age = int((evaluated - retrieved).total_seconds())
            state = "CURRENT" if age <= max_age else "STALE"

    freshness_required = canonical["freshness_required_for_current_claims"]
    if freshness_required:
        eligible = state == "CURRENT"
        reason = None if eligible else "FRESHNESS_REQUIRED_NON_CURRENT"
    elif state == "CURRENT":
        eligible = True
        reason = None
    elif state in {"STALE", "UNKNOWN"}:
        claim_policy = case.get("claim_policy") or {}
        eligible = (
            claim_policy.get("historical_evidence_allowed") is True
            and claim_policy.get("historical_time_context_preserved") is True
        )
        reason = None if eligible else "HISTORICAL_CONTEXT_REQUIRED"
    else:
        eligible = False
        reason = "HISTORICAL_CONTEXT_REQUIRED"

    return state, eligible, reason


def main() -> None:
    document = json.loads(VECTORS.read_text(encoding="utf-8"))
    assert document["schema_version"] == "source-freshness-vectors/v1"
    ids = [case["id"] for case in document["cases"]]
    assert len(ids) == len(set(ids)), "case ids must be unique"

    for case in document["cases"]:
        state, eligible, reason = classify(document, case)
        assert state == case["expected_state"], (case["id"], state, case["expected_state"])
        assert eligible is case["expected_eligible"], (case["id"], eligible)
        if "reason" in case:
            assert reason == case["reason"], (case["id"], reason, case["reason"])
        else:
            assert reason is None, (case["id"], reason)

    print(f"source-freshness vectors: {len(ids)} passed")


if __name__ == "__main__":
    main()
