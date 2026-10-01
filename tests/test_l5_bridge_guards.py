#!/usr/bin/env python3
"""Focused concrete-bridge and merge-lock freeze regressions."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from l5_controller import GuardedWriteBridge, run_once
from l5_kernel import Intent, Lease, MemoryCASStore, Observation, RepoMode
from test_l5_controller import FakeIO, repo_snapshot


class FinalGuardTests(unittest.TestCase):
    """Exercise guards immediately adjacent to the mutation boundary."""

    def test_merge_locked_survives_governance_drift(self):
        """A governance freeze cannot erase pending post-merge verification."""
        store = MemoryCASStore()
        store.modes["repo"] = (RepoMode.MERGE_LOCKED, 3)
        io = FakeIO(repo_snapshot(rulesets_or_protection_active=False))
        result = run_once("repo", io, store)
        self.assertEqual(result.status, "BLOCKED")
        self.assertEqual(result.reason, "GOVERNANCE_DRIFT")
        self.assertEqual(store.read_repo_mode("repo")[0], RepoMode.MERGE_LOCKED)

    def test_bridge_rejects_exact_ref_mismatches(self):
        """The concrete bridge rejects head/base drift before mutation."""
        obs = Observation("a" * 40, "b" * 40)
        intent = Intent("op", "f" * 64, "ci_rerun", obs.head, obs.base, 1)
        lease = Lease("k", "run", 1, obs, 0.0, 300.0, 1, intent)
        bridge = GuardedWriteBridge(object(), object())
        head = bridge.execute_guarded(
            "retry_ci",
            {"head_sha": "c" * 40, "base_sha": obs.base},
            lease,
        )
        base = bridge.execute_guarded(
            "retry_ci",
            {"head_sha": obs.head, "base_sha": "d" * 40},
            lease,
        )
        self.assertEqual(head["reason"], "LEASE_HEAD_MISMATCH")
        self.assertEqual(base["reason"], "LEASE_BASE_MISMATCH")

    def test_bridge_invalid_authorization_is_blocked(self):
        """Invalid activation evidence cannot escape as an exception."""
        obs = Observation("a" * 40, "b" * 40)
        intent = Intent("op", "f" * 64, "ci_rerun", obs.head, obs.base, 1)
        lease = Lease("k", "run", 1, obs, 0.0, 300.0, 1, intent)
        bridge = GuardedWriteBridge(object(), object())
        result = bridge.execute_guarded(
            "retry_ci",
            {
                "head_sha": obs.head,
                "base_sha": obs.base,
                "activation_snapshot": {},
            },
            lease,
        )
        self.assertEqual(result["status"], "BLOCKED")
        self.assertTrue(result["reason"].startswith("AUTHORIZATION_INVALID:"))


if __name__ == "__main__":
    unittest.main()
