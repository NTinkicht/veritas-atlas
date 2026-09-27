#!/usr/bin/env python3
"""Validate project-owned H7/H8 adoption evidence without granting release authority."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
H7 = ROOT / "docs/operations/onecompany-adoption/evidence/veritas/h7-runtime-writer-inventory.json"
H8 = ROOT / "docs/operations/onecompany-adoption/evidence/veritas/h8-release-rollback-2026-09-19.json"
RUNBOOK = ROOT / "docs/operations/onecompany-adoption/docs/VERITAS-H8-RELEASE-RUNBOOK.md"
TARGET = "NTinkicht/veritas-atlas"
FORBIDDEN_EXACT_KEYS = {
    "password", "token", "secret", "api_key", "apikey",
    "connection_string", "connectionstring", "authorization",
}
FORBIDDEN_KEY_SUFFIXES = ("_password", "_token", "_secret", "_api_key", "_connection_string")
REQUIRED_H8_BLOCKERS = (
    "GitHub administrative promotion protection",
    "Full writer/provider inventory",
    "authenticated DB-backed operator smoke",
    "rollback/recovery drill",
    "credential",
)
REQUIRED_H7_CONSTRAINTS = frozenset({
    "companyos_install",
    "target_mutation",
    "deployment",
    "database_mutation",
    "lease_dispatch_authority",
    "merge_authority",
    "budget_expansion",
    "autonomy_promotion",
})


def load(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"EVIDENCE_OBJECT_REQUIRED:{path.name}")
    return value


def reject_secret_fields(value, path="root") -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            normalized = str(key).lower().replace("-", "_")
            if normalized in FORBIDDEN_EXACT_KEYS or normalized.endswith(FORBIDDEN_KEY_SUFFIXES):
                raise ValueError(f"SECRET_BEARING_FIELD_FORBIDDEN:{path}.{key}")
            reject_secret_fields(child, f"{path}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            reject_secret_fields(child, f"{path}[{index}]")


def validate(h7: dict, h8: dict) -> dict:
    reject_secret_fields(h7, "h7")
    reject_secret_fields(h8, "h8")

    if h7.get("target") != TARGET or h8.get("target") != TARGET:
        raise ValueError("TARGET_MISMATCH")
    if h7.get("gate") != "H7" or h8.get("gate") != "H8":
        raise ValueError("GATE_MISMATCH")
    if h7.get("status") != "blocked" or h8.get("status") != "blocked":
        raise ValueError("H7_H8_MUST_REMAIN_BLOCKED")
    if h7.get("authority") != "read-only-evidence":
        raise ValueError("H7_EVIDENCE_AUTHORITY_DRIFT")
    if h8.get("authority") != "read-only-planning-evidence":
        raise ValueError("H8_EVIDENCE_AUTHORITY_DRIFT")
    if h7.get("fail_closed") is not True or h7.get("cleared") is not False:
        raise ValueError("H7_FAIL_CLOSED_CONTRACT_BROKEN")

    constraints = h7.get("constraints")
    if (
        not isinstance(constraints, dict)
        or set(constraints) != REQUIRED_H7_CONSTRAINTS
        or any(constraints.get(key) is not False for key in REQUIRED_H7_CONSTRAINTS)
    ):
        raise ValueError("H7_AUTHORITY_ESCALATION_DETECTED")

    release = h8.get("release_policy")
    if (
        not isinstance(release, dict)
        or release.get("manual_go_no_go_required") is not True
        or release.get("candidate_pr_merge_is_not_release") is not True
        or release.get("routine_auto_deploy_enabled") is not False
        or release.get("migration_is_independent_human_only_decision") is not True
    ):
        raise ValueError("H8_RELEASE_AUTHORITY_DRIFT")

    if any(
        h8.get(key) is not False
        for key in (
            "onecompany_mutation_authorized",
            "companyos_cutover_authorized",
            "autonomy_promotion_authorized",
            "budget_expansion_authorized",
        )
    ):
        raise ValueError("H8_AUTHORITY_ESCALATION_DETECTED")

    neon = h8.get("neon")
    if not isinstance(neon, dict) or neon.get("project_human_in_scope") is not False:
        raise ValueError("NEON_SCOPE_DRIFT")

    render = h8.get("render")
    if not isinstance(render, dict) or render.get("auto_deploy") is not False:
        raise ValueError("RENDER_AUTO_DEPLOY_MUST_REMAIN_OFF")

    verification = h8.get("verification")
    if not isinstance(verification, dict):
        raise ValueError("H8_VERIFICATION_REQUIRED")
    must_still_be_unproven = (
        "authenticated_operator_login_verified",
        "api_db_readiness_live_verified",
        "business_db_read_workflow_verified",
        "recovery_drill_verified",
        "immutable_container_digest_captured",
        "github_admin_protection_observed",
        "other_external_writers_exhaustively inventoried",
        "credential_rotation_proof_in_non_secret_release_record",
        "candidate_security_advisories_resolved_or_owner_risk_accepted",
    )
    if any(verification.get(key) is not False for key in must_still_be_unproven):
        raise ValueError("HISTORICAL_EVIDENCE_FALSELY_PROMOTED")

    blockers = h8.get("open_blockers")
    if not isinstance(blockers, list) or not blockers:
        raise ValueError("OPEN_BLOCKERS_REQUIRED")
    joined = "\n".join(str(item) for item in blockers)
    if any(fragment.lower() not in joined.lower() for fragment in REQUIRED_H8_BLOCKERS):
        raise ValueError("REQUIRED_H8_BLOCKER_MISSING")

    h7_render = ((h7.get("writer_inventory") or {}).get("render") or {})
    h8_api = render.get("api") or {}
    if h7_render.get("workspace_id") != render.get("workspace_id"):
        raise ValueError("RENDER_WORKSPACE_DRIFT")
    services = h7_render.get("services") or []
    h7_api = next((item for item in services if item.get("kind") == "web_service"), None)
    if not isinstance(h7_api, dict) or h7_api.get("id") != h8_api.get("service_id"):
        raise ValueError("RENDER_API_SERVICE_DRIFT")

    h7_neon = ((h7.get("writer_inventory") or {}).get("neon") or {})
    if h7_neon.get("project_id") != neon.get("project_id") or h7_neon.get("database") != neon.get("database"):
        raise ValueError("NEON_TARGET_DRIFT")

    if not RUNBOOK.is_file():
        raise ValueError("H8_RUNBOOK_MISSING")

    return {
        "status": "VALIDATED_BLOCKED",
        "target": TARGET,
        "h7_status": "blocked",
        "h8_status": "blocked",
        "open_blockers": len(blockers),
        "production_action_authorized": False,
        "credential_rotation_claimed_verified": False,
        "deployment_claimed": False,
    }


def main() -> int:
    try:
        result = validate(load(H7), load(H8))
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "EVIDENCE_INVALID", "reason": str(exc)}, sort_keys=True))
        return 2
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
