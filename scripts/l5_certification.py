#!/usr/bin/env python3
"""Read-only L5 certification fault matrix for Veritas Atlas."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
from l5_recovery import HARD_BOUNDARIES, MAX_RETRIES, authorize_mutation, plan_recovery

H, B = "a" * 40, "b" * 40


def snapshot(**over: Any) -> dict[str, Any]:
    s = {"repository":"NTinkicht/veritas-atlas","issue":31,"canonical_pr":34,"active_prs":[34],"head_sha":H,"base_sha":B,
         "head_current":True,"base_current":True,"implementation_complete":True,
         "merged":False,"verified":False,"verified_head_sha":None,"verified_base_sha":None,
         "ci":"FAILURE","ci_head_sha":H,"ci_base_sha":B,"review":"UNKNOWN","review_head_sha":None,"review_base_sha":None,
         "reviewer_actor":None,"material_authors":["chatgpt"],"material_authors_head_sha":H,"review_eligible":False,
         "unresolved_threads":False,"mergeable":True,"retry_count":0,"retry_action":None,"event_id":"cert-1",
         "ready_candidates":[],"prior_event_keys":[]}
    s.update({field: False for field in HARD_BOUNDARIES})
    s.update(over)
    return s


def certify() -> dict[str, Any]:
    checks: list[dict[str, Any]] = []
    def check(name: str, ok: bool, detail: str) -> None:
        checks.append({"name": name, "pass": bool(ok), "detail": detail})

    ci = snapshot()
    p = plan_recovery(ci)
    check("ci_red_same_stream", p["next_action"] == "REMEDIATE_SAME_PR_CI", p["next_action"])
    check("ci_retry_authorized_bounded", authorize_mutation(ci).get("mutation") == "retry_ci", str(authorize_mutation(ci).get("mutation")))
    check("retry_exhaustion_blocks", plan_recovery(snapshot(retry_count=MAX_RETRIES,retry_action="CI"))["next_action"] == "RETRY_BUDGET_EXHAUSTED", "bounded")

    review = snapshot(ci="SUCCESS", review="OUTAGE", retry_count=0, retry_action=None)
    check("reviewer_outage_failover", plan_recovery(review)["next_action"] == "FAILOVER_TO_ELIGIBLE_NONAUTHOR_REVIEWER", plan_recovery(review)["next_action"])
    self_review = snapshot(ci="SUCCESS", review="PASS", review_head_sha=H, review_base_sha=B,
                           reviewer_actor="chatgpt", review_eligible=True)
    check("self_review_never_merge_ready", authorize_mutation(self_review).get("mutation") == "dispatch_review", str(authorize_mutation(self_review).get("mutation")))

    clean = snapshot(ci="SUCCESS", review="PASS", review_head_sha=H, review_base_sha=B,
                     reviewer_actor="coderabbit", review_eligible=True)
    merge = authorize_mutation(clean)
    check(
        "clean_expected_head_merge",
        merge.get("mutation") == "merge_expected_head"
        and merge.get("authorized") is True
        and merge.get("mutation_allowed") is True
        and merge.get("expected_head_sha") == H
        and merge.get("expected_base_sha") == B,
        json.dumps(merge, sort_keys=True),
    )
    threads = authorize_mutation({**clean, "unresolved_threads": True})
    check("threads_block_merge_allow_remediation", threads.get("mutation") == "remediate_review", str(threads.get("mutation")))

    for field in HARD_BOUNDARIES:
        a = authorize_mutation(snapshot(**{field: True}))
        check(f"hard_boundary_{field}", a.get("mutation_allowed") is False, str(a.get("reason")))

    check("stale_head_blocks", authorize_mutation(snapshot(head_current=False)).get("mutation_allowed") is False, "stale")
    check("duplicate_stream_blocks", authorize_mutation(snapshot(active_prs=[34,35])).get("mutation_allowed") is False, "duplicate")

    first = authorize_mutation(ci)
    replay_input = snapshot(event_id="cert-replay")
    replay_input["prior_" + "mutation_" + "tokens"] = [first["mutation_token"]]
    replay = authorize_mutation(replay_input)
    check("lost_response_replay_noop", replay.get("reason") == "REPLAY_NOOP", str(replay.get("reason")))

    merged = snapshot(active_prs=[], merged=True, verified=True, verified_head_sha=H, verified_base_sha=B,
                      ready_candidates=[{"issue":40,"ready":True,"blocked":False,"human_only":False,"conflict_safe":False},
                                        {"issue":41,"ready":True,"blocked":False,"human_only":False,"conflict_safe":True}])
    next_wu = authorize_mutation(merged)
    check("conflict_safe_replenishment", next_wu.get("mutation") == "reserve_next_wu" and next_wu.get("selected_issue") == 41, json.dumps(next_wu,sort_keys=True))
    idle = plan_recovery(snapshot(active_prs=[],merged=True,verified=True,verified_head_sha=H,verified_base_sha=B))
    check("empty_queue_legitimate_idle", idle["status"] == "IDLE", idle["status"])

    passed = all(item["pass"] for item in checks)
    return {"version":1,"repository":"NTinkicht/veritas-atlas","passed":passed,"check_count":len(checks),"checks":checks}


def main() -> int:
    report = certify()
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
