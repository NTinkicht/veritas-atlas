#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from evidence_provenance import verify_envelope

FIXTURE = ROOT / "tests" / "fixtures" / "evidence_provenance_v1.json"


def main() -> None:
    payload = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert payload["schema"] == "veritas-evidence-provenance-vectors/v1"
    cases = payload["cases"]
    assert isinstance(cases, list) and cases

    seen: set[str] = set()
    pair_refs: dict[str, list[str]] = {}

    for case in cases:
        case_id = case["id"]
        assert case_id not in seen, f"duplicate case id: {case_id}"
        seen.add(case_id)
        trust = case["trust"]
        result = verify_envelope(
            case["envelope"],
            retrieval_trusted=trust["retrieval"],
            classification_trusted=trust["classification"],
            digest_verified=trust["digest"],
        )

        if case["expected"] == "accept":
            assert result["accepted"] is True, (case_id, result)
            assert result["reason"] is None, (case_id, result)
            assert result["normalized_source_uri"] == case["expected_normalized_source_uri"], (
                case_id,
                result,
            )
            assert result["evidence_ref"] == case["expected_evidence_ref"], (case_id, result)
            pair = case.get("pair")
            if pair:
                pair_refs.setdefault(pair, []).append(result["evidence_ref"])
        else:
            assert case["expected"] == "reject", f"unknown outcome: {case_id}"
            assert result["accepted"] is False, (case_id, result)
            assert result["reason"] == case["reason"], (case_id, result)
            assert result["evidence_ref"] is None, (case_id, result)

    assert len(seen) == len(cases), "not every checked-in vector executed"
    assert set(pair_refs) == {"digest-drift", "parser-drift"}
    for pair, refs in pair_refs.items():
        assert len(refs) == 2, (pair, refs)
        assert refs[0] != refs[1], f"{pair} must change the exact evidence reference"

    print(f"Evidence provenance vectors PASS ({len(cases)} cases)")


if __name__ == "__main__":
    main()
