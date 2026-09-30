import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("l5_recovery", ROOT / "scripts" / "l5_recovery.py")
l5 = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(l5)
H, B = "a" * 40, "b" * 40


def snap(**over):
    s = {"repository":"NTinkicht/veritas-atlas","issue":31,"canonical_pr":34,"active_prs":[34],"head_sha":H,"base_sha":B,
         "head_current":True,"base_current":True,"implementation_complete":True,"emergency_stop":False,"human_only":False,
         "release_go_no_go":False,"blocked":False,"destructive_production":False,"spend_required":False,"secret_scope_change":False,
         "security_control_weakening":False,"merged":False,"verified":False,"verified_head_sha":None,"verified_base_sha":None,
         "ci":"FAILURE","ci_head_sha":H,"ci_base_sha":B,"review":"UNKNOWN","review_head_sha":None,"review_base_sha":None,
         "reviewer_actor":None,"material_authors":["chatgpt"],"material_authors_head_sha":H,"review_eligible":False,
         "unresolved_threads":False,"mergeable":True,"retry_count":0,"retry_action":None,"event_id":"evt-1",
         "ready_candidates":[],"prior_event_keys":[],"prior_mutation_tokens":[]}
    s.update(over); return s


class RecoveryTest(unittest.TestCase):
    def test_ci_failure_plans_same_pr_retry_and_authorizes_only_that_write(self):
        p = l5.plan_recovery(snap())
        self.assertEqual((p["next_action"], p["retry_count_after"], p["retry_action_after"]), ("REMEDIATE_SAME_PR_CI", 1, "CI"))
        a = l5.authorize_mutation(snap())
        self.assertEqual(a["mutation"], "retry_ci")

    def test_release_go_no_go_and_all_hard_boundaries_block(self):
        for field in l5.HARD_BOUNDARIES:
            a = l5.authorize_mutation(snap(**{field: True}))
            self.assertFalse(a["mutation_allowed"], field)

    def test_phase_scoped_retry_budget_exhausts(self):
        p = l5.plan_recovery(snap(retry_count=3, retry_action="CI"))
        self.assertEqual((p["status"], p["next_action"]), ("BLOCKED", "RETRY_BUDGET_EXHAUSTED"))
        p = l5.plan_recovery(snap(ci="SUCCESS", review="UNKNOWN", retry_count=3, retry_action="CI"))
        self.assertEqual((p["retry_count_after"], p["retry_action_after"]), (1, "REVIEW"))

    def test_replay_event_and_mutation_are_idempotent(self):
        p = l5.plan_recovery(snap())
        replay = l5.plan_recovery(snap(prior_event_keys=[p["event_key"]]))
        self.assertEqual(replay["status"], "REPLAY_NOOP")
        auth = l5.authorize_mutation(snap())
        again = l5.authorize_mutation(snap(event_id="evt-2", prior_mutation_tokens=[auth["mutation_token"]]))
        self.assertEqual(again["reason"], "REPLAY_NOOP")

    def test_duplicate_stream_and_stale_refs_never_authorize(self):
        self.assertFalse(l5.authorize_mutation(snap(active_prs=[34, 35]))["mutation_allowed"])
        self.assertFalse(l5.authorize_mutation(snap(head_current=False))["mutation_allowed"])

    def test_clean_merge_requires_exact_ci_review_and_no_threads(self):
        s = snap(ci="SUCCESS", review="PASS", review_head_sha=H, review_base_sha=B,
                 reviewer_actor="coderabbit", review_eligible=True)
        a = l5.authorize_mutation(s)
        self.assertEqual(a["mutation"], "merge_expected_head")
        blocked = l5.authorize_mutation({**s, "unresolved_threads": True})
        self.assertFalse(blocked["mutation_allowed"])
        self.assertEqual(blocked["planned_action"], "REMEDIATE_SAME_PR_REVIEW")

    def test_verified_merge_replenishes_only_conflict_safe_ready_work(self):
        s = snap(active_prs=[], merged=True, verified=True, verified_head_sha=H, verified_base_sha=B,
                 ready_candidates=[{"issue":41,"ready":True,"blocked":False,"human_only":False,"conflict_safe":True},
                                   {"issue":40,"ready":True,"blocked":False,"human_only":False,"conflict_safe":False}])
        a = l5.authorize_mutation(s)
        self.assertEqual((a["mutation"], a["selected_issue"]), ("reserve_next_wu", 41))

    def test_empty_ready_queue_is_legitimate_idle(self):
        s = snap(active_prs=[], merged=True, verified=True, verified_head_sha=H, verified_base_sha=B)
        p = l5.plan_recovery(s)
        self.assertEqual((p["status"], p["next_action"]), ("IDLE", "IDLE_NO_CONFLICT_SAFE_READY_WORK"))

    def test_malformed_candidate_safety_fails_closed(self):
        s = snap(active_prs=[], merged=True, verified=True, verified_head_sha=H, verified_base_sha=B,
                 ready_candidates=[{"issue":41,"ready":True,"blocked":False,"human_only":False}])
        with self.assertRaises(ValueError): l5.plan_recovery(s)


if __name__ == "__main__": unittest.main()
