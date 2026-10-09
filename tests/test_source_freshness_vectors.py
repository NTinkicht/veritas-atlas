#!/usr/bin/env python3
"""Executable deterministic checks for source-freshness/v1 fixtures."""
from __future__ import annotations

import copy
import json
import re
from datetime import datetime, timezone
from fractions import Fraction
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VECTORS = ROOT / "tests" / "fixtures" / "source_freshness_vectors_v1.json"
# RFC3339 permits fractional seconds beyond Python's six-digit datetime storage.
# Preserve every accepted digit to avoid rounding away an age boundary.
RFC3339_UTC = re.compile(
    r"^(?P<whole>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(?P<fraction>\d+))?(?:Z|\+00:00)$"
)


def parse_utc(value: object) -> tuple[datetime, Fraction] | None:
    if not isinstance(value, str):
        return None
    match = RFC3339_UTC.fullmatch(value)
    if match is None:
        return None
    digits = match.group("fraction") or "0"
    # Bound hostile timestamp size while supporting precision beyond microseconds.
    if len(digits) > 1000:
        return None
    try:
        parsed = datetime.fromisoformat(match.group("whole") + "+00:00")
        fraction = Fraction(int(digits), 10 ** len(digits))
    except (ValueError, OverflowError):
        return None
    return (parsed, fraction) if parsed.tzinfo == timezone.utc else None


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
    supported = complete_policy and all(
        type(policy.get(field)) is type(canonical.get(field))
        and policy.get(field) == canonical.get(field)
        for field in required_policy_fields
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
            # Exact floor((evaluated - retrieved) / second), with no float
            # rounding or truncation of sub-microsecond RFC3339 fractions.
            whole = evaluated[0] - retrieved[0]
            age = whole.days * 86400 + whole.seconds
            if evaluated[1] < retrieved[1]:
                age -= 1
            state = "CURRENT" if age <= max_age else "STALE"

    freshness_required = canonical["freshness_required_for_current_claims"]
    claim_policy = case.get("claim_policy") or {}
    historical_context = (
        claim_policy.get("historical_evidence_allowed") is True
        and claim_policy.get("historical_time_context_preserved") is True
    )
    if state == "CURRENT":
        eligible, reason = True, None
    elif historical_context and supported:
        # A historical claim is not a current claim, even under P1.
        eligible, reason = True, None
    elif freshness_required:
        eligible, reason = False, "FRESHNESS_REQUIRED_NON_CURRENT"
    else:
        eligible, reason = False, "HISTORICAL_CONTEXT_REQUIRED"

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

    # Guard exact JSON policy types: Python considers 1 == True.
    drift = copy.deepcopy(document["cases"][0])
    drift["policy_override"] = {"revision_signal_required": 1}
    assert classify(document, drift) == (
        "UNKNOWN", False, "FRESHNESS_REQUIRED_NON_CURRENT"
    ), "numeric boolean must not match the canonical policy"

    # P1 freshness applies to current claims only, not contextualized history.
    historical = copy.deepcopy(document["cases"][2])
    historical["claim_policy"] = {
        "historical_evidence_allowed": True,
        "historical_time_context_preserved": True,
    }
    assert classify(document, historical) == ("STALE", True, None)

    print(f"source-freshness vectors: {len(ids)} passed")


if __name__ == "__main__":
    main()
