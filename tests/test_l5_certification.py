import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("l5_certification", ROOT / "scripts" / "l5_certification.py")
cert = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(cert)


class CertificationTest(unittest.TestCase):
    def test_fault_matrix_passes(self):
        report = cert.certify()
        self.assertTrue(report["passed"], report)
        self.assertEqual(report["check_count"], len(report["checks"]))
        self.assertGreaterEqual(report["check_count"], 18)
        self.assertTrue(all(row["pass"] for row in report["checks"]))

    def test_required_faults_present(self):
        names = {row["name"] for row in cert.certify()["checks"]}
        required = {
            "ci_red_same_stream", "retry_exhaustion_blocks", "reviewer_outage_failover",
            "self_review_never_merge_ready", "clean_expected_head_merge",
            "threads_block_merge_allow_remediation", "hard_boundary_release_go_no_go",
            "hard_boundary_emergency_stop", "stale_head_blocks", "duplicate_stream_blocks",
            "lost_response_replay_noop", "conflict_safe_replenishment", "empty_queue_legitimate_idle",
        }
        self.assertTrue(required <= names, required - names)


if __name__ == "__main__":
    unittest.main()
