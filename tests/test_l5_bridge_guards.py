#!/usr/bin/env python3
"""Focused concrete-bridge and merge-lock freeze regressions."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from l5_controller import GuardedWriteBridge, run_once
from l5_kernel import Intent, Lease, MemoryCASStore, Observation, RepoMode
from test_l5_controller import FakeIO, repo_snapshot
from test_l5_recovery import snap


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
        bridge = GuardedWriteBridge(object(), object())
        activation = snap()

        head_intent = Intent("op-h", "f" * 64, "ci_rerun", "c" * 40, obs.base, 1)
        head_lease = Lease("kh", "run", 1, obs, 0.0, 300.0, 1, head_intent)
        head = bridge.execute_guarded(
            "retry_ci",
            {
                "head_sha": obs.head,
                "base_sha": obs.base,
                "activation_snapshot": activation,
            },
            head_lease,
        )

        base_intent = Intent("op-b", "e" * 64, "ci_rerun", obs.head, "d" * 40, 1)
        base_lease = Lease("kb", "run", 1, obs, 0.0, 300.0, 1, base_intent)
        base = bridge.execute_guarded(
            "retry_ci",
            {
                "head_sha": obs.head,
                "base_sha": obs.base,
                "activation_snapshot": activation,
            },
            base_lease,
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
