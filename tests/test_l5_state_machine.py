import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "scripts" / "l5_state_machine.py"
SPEC = importlib.util.spec_from_file_location("l5_state_machine", MODULE)
l5 = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(l5)


class L5StateMachineTest(unittest.TestCase):
    def base(self):
        head, base = "a" * 40, "b" * 40
        return {
            "repository":"NTinkicht/veritas-atlas","issue":27,"canonical_pr":30,
            "active_prs":[30],"head_sha":head,"base_sha":base,
            "head_current":True,"base_current":True,"implementation_complete":True,
            "emergency_stop":False,"human_only":False,"release_go_no_go":False,
            "blocked":False,"merged":False,"verified":False,
            "verified_head_sha":None,"verified_base_sha":None,
            "ci":"SUCCESS","ci_head_sha":head,"ci_base_sha":base,
            "review":"PASS","review_head_sha":head,"review_base_sha":base,
            "reviewer_actor":"mistral-vibe","material_authors":["chatgpt"],
            "material_authors_head_sha":head,"review_eligible":True,
            "unresolved_threads":False,"mergeable":True,
        }

    def test_ready_is_read_only(self):
        result = l5.reduce_evidence(self.base())
        self.assertEqual(result["state"], "MERGE_READY")
        self.assertFalse(result["mutation_allowed"])

    def test_hard_boundaries_fail_closed(self):
        for field in ("emergency_stop","human_only","release_go_no_go","blocked","unresolved_threads"):
            sample = self.base(); sample.pop(field)
            with self.assertRaises(ValueError): l5.reduce_evidence(sample)
        self.assertEqual(l5.reduce_evidence({**self.base(),"emergency_stop":True})["next_action"], "EMERGENCY_STOP_HOLD")
        self.assertEqual(l5.reduce_evidence({**self.base(),"release_go_no_go":True})["next_action"], "HUMAN_OR_POLICY_BLOCKED")

    def test_duplicate_and_vanished_streams_reconcile(self):
        self.assertEqual(l5.reduce_evidence({**self.base(),"active_prs":[30,31]})["next_action"], "DUPLICATE_STREAM_RECONCILIATION_REQUIRED")
        self.assertEqual(l5.reduce_evidence({**self.base(),"active_prs":[]})["next_action"], "RECONCILE_CANONICAL_PR")

    def test_exact_ci_review_and_authorship_binding(self):
        self.assertEqual(l5.reduce_evidence({**self.base(),"ci_head_sha":"c"*40})["next_action"], "RECONCILE_EXACT_HEAD_CI_EVIDENCE")
        self.assertEqual(l5.reduce_evidence({**self.base(),"review_base_sha":"c"*40})["next_action"], "RECONCILE_EXACT_HEAD_REVIEW_EVIDENCE")
        self.assertEqual(l5.reduce_evidence({**self.base(),"material_authors_head_sha":"c"*40})["next_action"], "RECONCILE_MATERIAL_AUTHORSHIP")
        self.assertEqual(l5.reduce_evidence({**self.base(),"reviewer_actor":"chatgpt"})["next_action"], "DISPATCH_ELIGIBLE_NONAUTHOR_REVIEW")

    def test_malformed_status_and_mergeable_evidence_fails_closed(self):
        for field in ("ci","review"):
            sample = self.base(); sample[field] = 1
            with self.assertRaises(ValueError): l5.reduce_evidence(sample)
        with self.assertRaises(ValueError): l5.reduce_evidence({**self.base(),"mergeable":1})

    def test_uppercase_sha_is_valid(self):
        sample = self.base()
        sample.update(head_sha="A"*40, ci_head_sha="A"*40, review_head_sha="A"*40, material_authors_head_sha="A"*40)
        self.assertEqual(l5.reduce_evidence(sample)["state"], "MERGE_READY")

    def test_unresolved_findings_remediate_same_stream(self):
        self.assertEqual(l5.reduce_evidence({**self.base(),"unresolved_threads":True})["next_action"], "REMEDIATE_SAME_PR_REVIEW")
        self.assertEqual(l5.reduce_evidence({**self.base(),"review":"CHANGES_REQUESTED"})["next_action"], "REMEDIATE_SAME_PR_REVIEW")

    def test_merged_work_requires_exact_verification_before_replenish(self):
        sample = {**self.base(),"active_prs":[],"merged":True,"verified":False}
        self.assertEqual(l5.reduce_evidence(sample)["next_action"], "VERIFY_MERGED_RESULT")
        sample.update(verified=True)
        self.assertEqual(l5.reduce_evidence(sample)["next_action"], "RECONCILE_VERIFIED_MERGE_EVIDENCE")
        sample.update(verified_head_sha=sample["head_sha"], verified_base_sha=sample["base_sha"])
        self.assertEqual(l5.reduce_evidence(sample)["next_action"], "REPLENISH_NEXT_READY_WU")

    def test_merged_missing_canonical_identity_reconciles(self):
        sample = {**self.base(),"canonical_pr":None,"active_prs":[],"merged":True}
        self.assertEqual(l5.reduce_evidence(sample)["next_action"], "RECONCILE_CANONICAL_PR")


if __name__ == "__main__":
    unittest.main()
