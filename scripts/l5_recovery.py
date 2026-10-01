#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
from l5_state_machine import reduce_evidence

MAX_RETRIES = 3
SHA40 = re.compile(r"^[0-9a-fA-F]{40}$")
TOKEN64 = re.compile(r"^[0-9a-f]{64}$")
HARD_BOUNDARIES = (
    "emergency_stop", "human_only", "release_go_no_go", "blocked",
    "destructive_production", "spend_required", "secret_scope_change",
    "security_control_weakening",
)
RETRY_SCOPES = {
    "START_CANONICAL_STREAM": "STREAM", "RECONCILE_CANONICAL_PR": "STREAM",
    "CONTINUE_IMPLEMENTATION": "STREAM", "RECONCILE_HEAD_BASE": "STREAM",
    "RECONCILE_EXACT_REFS": "STREAM", "RUN_OR_RECONCILE_EXACT_HEAD_CI": "CI",
    "RECONCILE_EXACT_HEAD_CI_EVIDENCE": "CI", "REMEDIATE_SAME_PR_CI": "CI",
    "DISPATCH_ELIGIBLE_NONAUTHOR_REVIEW": "REVIEW",
    "FAILOVER_TO_ELIGIBLE_NONAUTHOR_REVIEWER": "REVIEW",
    "RECONCILE_EXACT_HEAD_REVIEW_EVIDENCE": "REVIEW",
    "RECONCILE_REVIEWER_IDENTITY": "REVIEW", "RECONCILE_MATERIAL_AUTHORSHIP": "REVIEW",
    "REMEDIATE_SAME_PR_REVIEW": "REVIEW", "RECONCILE_MERGEABILITY": "MERGE_READY",
    "VERIFY_MERGED_RESULT": "VERIFY", "RECONCILE_VERIFIED_MERGE_EVIDENCE": "VERIFY",
    "RECONCILE_POST_MERGE_STREAM_STATE": "VERIFY",
}
SAFE_MUTATIONS = {
    "REMEDIATE_SAME_PR_CI": "retry_ci",
    "DISPATCH_ELIGIBLE_NONAUTHOR_REVIEW": "dispatch_review",
    "FAILOVER_TO_ELIGIBLE_NONAUTHOR_REVIEWER": "dispatch_review",
    "REMEDIATE_SAME_PR_REVIEW": "remediate_review",
    "AWAIT_AUTHORIZED_EXPECTED_HEAD_MERGE": "merge_expected_head",
    "PLAN_REPLENISH_READY_WU": "reserve_next_wu",
    "RECONCILE_HEAD_BASE": "update_branch",
}


def _bool(snapshot: dict[str, Any], key: str) -> bool:
    value = snapshot.get(key)
    if type(value) is not bool:
        raise ValueError(f"L5_RECOVERY_{key.upper()}_UNKNOWN")
    return value


def _retry(snapshot: dict[str, Any], action: str) -> tuple[int, str | None]:
    count = snapshot.get("retry_count")
    persisted = snapshot.get("retry_action")
    if type(count) is not int or count < 0:
        raise ValueError("L5_RECOVERY_RETRY_COUNT_INVALID")
    allowed = frozenset(RETRY_SCOPES.values())
    if persisted is not None and persisted not in allowed:
        raise ValueError("L5_RECOVERY_RETRY_ACTION_INVALID")
    if count and persisted is None:
        raise ValueError("L5_RECOVERY_RETRY_ACTION_REQUIRED")
    scope = RETRY_SCOPES.get(action)
    return (count if scope == persisted else 0), scope


def _event_key(snapshot: dict[str, Any]) -> str:
    event_id = snapshot.get("event_id")
    if not isinstance(event_id, str) or not event_id.strip():
        raise ValueError("L5_RECOVERY_EVENT_ID_INVALID")
    material = {k: snapshot.get(k) for k in ("repository", "issue", "canonical_pr", "head_sha", "base_sha")}
    material["event_id"] = event_id.strip()
    return hashlib.sha256(json.dumps(material, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def _tokens(values: Any, error: str) -> set[str]:
    if not isinstance(values, list) or len(set(values)) != len(values) or any(not isinstance(v, str) or not TOKEN64.fullmatch(v) for v in values):
        raise ValueError(error)
    return set(values)


def _candidates(snapshot: dict[str, Any]) -> list[int]:
    rows = snapshot.get("ready_candidates")
    if not isinstance(rows, list):
        raise ValueError("L5_RECOVERY_READY_CANDIDATES_INVALID")
    seen, out = set(), []
    for row in rows:
        if not isinstance(row, dict) or type(row.get("issue")) is not int or row["issue"] < 1 or row["issue"] in seen:
            raise ValueError("L5_RECOVERY_READY_CANDIDATE_INVALID")
        seen.add(row["issue"])
        flags = {k: row.get(k) for k in ("ready", "blocked", "human_only", "conflict_safe")}
        if any(type(v) is not bool for v in flags.values()):
            raise ValueError("L5_RECOVERY_READY_CANDIDATE_SAFETY_UNKNOWN")
        if flags["ready"] and not flags["blocked"] and not flags["human_only"] and flags["conflict_safe"]:
            out.append(row["issue"])
    return sorted(out)


def plan_recovery(snapshot: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(snapshot, dict):
        raise ValueError("L5_RECOVERY_SNAPSHOT_INVALID")
    key = _event_key(snapshot)
    if key in _tokens(snapshot.get("prior_event_keys", []), "L5_RECOVERY_PRIOR_EVENT_KEYS_INVALID"):
        return {"status": "REPLAY_NOOP", "event_key": key, "next_action": "NONE_ALREADY_RECORDED", "mutation_allowed": False}
    state = reduce_evidence(snapshot)
    action = state["next_action"]
    count, scope = _retry(snapshot, action)
    result = {"status": "PLANNED", "event_key": key, "state": state["state"], "next_action": action,
              "retry_count": count, "retry_action": scope, "mutation_allowed": False}
    if action in {"EMERGENCY_STOP_HOLD", "HUMAN_OR_POLICY_BLOCKED", "DUPLICATE_STREAM_RECONCILIATION_REQUIRED"}:
        result["status"] = "BLOCKED"; return result
    if action == "REPLENISH_NEXT_READY_WU":
        candidates = _candidates(snapshot)
        if not candidates:
            result.update(status="IDLE", next_action="IDLE_NO_CONFLICT_SAFE_READY_WORK", retry_count=0, retry_action=None); return result
        result.update(next_action="PLAN_REPLENISH_READY_WU", selected_issue=candidates[0], retry_count=0, retry_action=None); return result
    if action == "AWAIT_AUTHORIZED_EXPECTED_HEAD_MERGE":
        result.update(status="READY", retry_count=0, retry_action=None); return result
    if action in RETRY_SCOPES:
        if count >= MAX_RETRIES:
            result.update(status="BLOCKED", next_action="RETRY_BUDGET_EXHAUSTED"); return result
        result["retry_count_after"] = count + 1
        result["retry_action_after"] = scope
        return result
    raise ValueError(f"L5_RECOVERY_UNHANDLED_ACTION:{action}")


def _mutation_token(plan: dict[str, Any], snapshot: dict[str, Any]) -> str:
    material = {
        "mutation": SAFE_MUTATIONS.get(plan.get("next_action")), "next_action": plan.get("next_action"),
        "repository": snapshot.get("repository"), "issue": snapshot.get("issue"), "canonical_pr": snapshot.get("canonical_pr"),
        "head_sha": snapshot.get("head_sha"), "base_sha": snapshot.get("base_sha"), "selected_issue": plan.get("selected_issue"),
        "retry_action_after": plan.get("retry_action_after"), "retry_count_after": plan.get("retry_count_after"),
    }
    return hashlib.sha256(json.dumps(material, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def authorize_mutation(snapshot: dict[str, Any]) -> dict[str, Any]:
    for field in HARD_BOUNDARIES:
        if _bool(snapshot, field):
            return {"authorized": False, "mutation_allowed": False, "reason": "HARD_BOUNDARY"}
    head, base = snapshot.get("head_sha"), snapshot.get("base_sha")
    if not isinstance(head, str) or not SHA40.fullmatch(head) or not isinstance(base, str) or not SHA40.fullmatch(base):
        raise ValueError("L5_ACTIVATION_EXACT_REFS_INVALID")
    if not _bool(snapshot, "head_current") or not _bool(snapshot, "base_current"):
        return {"authorized": False, "mutation_allowed": False, "reason": "STALE_HEAD_OR_BASE"}
    plan = plan_recovery(snapshot)
    action = plan.get("next_action")
    if action not in SAFE_MUTATIONS:
        return {"authorized": False, "mutation_allowed": False, "reason": "ACTION_NOT_WHITELISTED", "planned_action": action}
    token = _mutation_token(plan, snapshot)
    if token in _tokens(snapshot.get("prior_mutation_tokens", []), "L5_ACTIVATION_PRIOR_MUTATION_TOKENS_INVALID"):
        return {"authorized": False, "mutation_allowed": False, "reason": "REPLAY_NOOP", "mutation_token": token}
    mutation = SAFE_MUTATIONS[action]
    if mutation == "merge_expected_head":
        if plan.get("status") != "READY" or snapshot.get("ci") != "SUCCESS" or snapshot.get("review") != "PASS":
            raise ValueError("L5_ACTIVATION_MERGE_EVIDENCE_INVALID")
        if _bool(snapshot, "unresolved_threads") or not _bool(snapshot, "mergeable"):
            raise ValueError("L5_ACTIVATION_MERGE_NOT_CLEAN")
    result = {"authorized": True, "mutation_allowed": True, "reason": "AUTHORIZED", "mutation": mutation,
              "mutation_token": token, "expected_head_sha": head, "expected_base_sha": base,
              "canonical_pr": snapshot.get("canonical_pr"), "issue": snapshot.get("issue"),
              "retry_count_after": plan.get("retry_count_after"), "retry_action_after": plan.get("retry_action_after")}
    if mutation == "reserve_next_wu":
        result["selected_issue"] = plan["selected_issue"]
    return result


def selftest() -> None:
    h, b = "a" * 40, "b" * 40
    s = {"repository":"NTinkicht/veritas-atlas","issue":31,"canonical_pr":34,"active_prs":[34],"head_sha":h,"base_sha":b,
         "head_current":True,"base_current":True,"implementation_complete":True,"emergency_stop":False,"human_only":False,
         "release_go_no_go":False,"blocked":False,"destructive_production":False,"spend_required":False,"secret_scope_change":False,
         "security_control_weakening":False,"merged":False,"verified":False,"verified_head_sha":None,"verified_base_sha":None,
         "ci":"FAILURE","ci_head_sha":h,"ci_base_sha":b,"review":"UNKNOWN","review_head_sha":None,"review_base_sha":None,
         "reviewer_actor":None,"material_authors":["chatgpt"],"material_authors_head_sha":h,"review_eligible":False,
         "unresolved_threads":False,"mergeable":True,"retry_count":0,"retry_action":None,"event_id":"evt-1",
         "ready_candidates":[],"prior_event_keys":[],"prior_mutation_tokens":[]}
    p = plan_recovery(s)
    assert p["next_action"] == "REMEDIATE_SAME_PR_CI" and p["retry_count_after"] == 1
    assert authorize_mutation(s)["mutation"] == "retry_ci"
    assert not authorize_mutation({**s, "release_go_no_go": True})["mutation_allowed"]
    print("l5_recovery selftest PASS")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        selftest()
    else:
        raise SystemExit("use --selftest")
