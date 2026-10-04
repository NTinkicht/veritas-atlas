#!/usr/bin/env python3
"""Executable deterministic checks for citation-integrity/v1 fixtures."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VECTORS = ROOT / "tests" / "fixtures" / "citation_integrity_vectors_v1.json"


def content_digest(fixture: dict) -> str:
    return hashlib.sha256(fixture["content_utf8"].encode("utf-8")).hexdigest()


def validate_case(document: dict, case: dict) -> tuple[str, str | None, str | None]:
    fixtures = document["canonical_fixtures"]
    fixture = copy.deepcopy(fixtures[case["fixture"]])
    additional = copy.deepcopy(fixtures[case["additional_fixture"]]) if case.get("additional_fixture") else None
    override = case.get("override")
    if override:
        component, value = override["component"], override["value"]
        if component == "reference":
            pass
        elif component.startswith("additional_fixture."):
            additional[component.split(".", 1)[1]] = value
        else:
            fixture[component] = value

    refs = case["claim_references"]
    available = {fixture["immutable_reference"]: fixture}
    if additional:
        available[additional["immutable_reference"]] = additional

    for ref in refs:
        evidence = available.get(ref)
        if evidence is None:
            return "reject", "EVIDENCE_NOT_FOUND", None
        if content_digest(evidence) != evidence["content_sha256"]:
            return "reject", "EVIDENCE_DIGEST_MISMATCH", None
        if not evidence.get("provenance_complete"):
            return "reject", "EVIDENCE_PROVENANCE_INCOMPLETE", None
        expected_fixture = next((v for v in fixtures.values() if v["immutable_reference"] == ref), None)
        if expected_fixture and evidence["parser_version"] != expected_fixture["parser_version"]:
            return "reject", "EVIDENCE_REFERENCE_PARSER_MISMATCH", None
        if evidence["scope"] != case["policy"]["scope"]:
            return "reject", "EVIDENCE_SCOPE_MISMATCH", None
        if case["policy"]["freshness_required"]:
            if evidence["freshness"] == "STALE":
                return "reject", "EVIDENCE_STALE", None
            if evidence["freshness"] != "CURRENT":
                return "reject", "EVIDENCE_FRESHNESS_UNKNOWN", None

    if case.get("replacement"):
        return "accept_old_reference_without_substitution", None, refs[0]
    if case.get("expected_freshness_status"):
        return "accept_with_status", None, fixture["freshness"]
    return "accept", None, refs[0] if refs else None


def main() -> None:
    document = json.loads(VECTORS.read_text(encoding="utf-8"))
    assert document["schema_version"] == "citation-integrity/v1"
    ids = [case["id"] for case in document["cases"]]
    assert len(ids) == len(set(ids)), "case ids must be unique"
    for case in document["cases"]:
        outcome, reason, detail = validate_case(document, case)
        assert outcome == case["expected"], (case["id"], outcome, case["expected"])
        if case["expected"] == "reject":
            assert reason == case["reason"], (case["id"], reason, case["reason"])
        if "expected_reference" in case:
            assert detail == case["expected_reference"], case["id"]
        if "expected_freshness_status" in case:
            assert detail == case["expected_freshness_status"], case["id"]
    print(f"citation-integrity vectors: {len(ids)} passed")


if __name__ == "__main__":
    main()
