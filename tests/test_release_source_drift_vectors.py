"""Deterministic Veritas staging drift evidence vectors (no network/secrets)."""
from __future__ import annotations
import sys
from pathlib import Path
import unittest
from datetime import datetime, timezone

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from release_source_drift import inspect, REPO, SERVICES

OLD = "47bd085cb5e75d4b72acffc7a9e1e54300a61977"
NEW = "3adb55ca908552a838a821a1a7fab0982cbba63a"
NOW = datetime(2026, 10, 11, 0, 0, tzinfo=timezone.utc)


def sample(main=NEW, deployed=OLD):
    return {
        "schema": "VERITAS_DEPLOY_DRIFT_V1",
        "repository": REPO,
        "main_commit": main,
        "observed_at": "2026-10-10T23:59:00Z",
        "services": {
            name: {
                "service_id": service,
                "deployed_commit": deployed,
                "deploy_status": "live",
                "auto_deploy": False,
                "auto_deploy_trigger": "off",
            }
            for name, service in SERVICES.items()
        },
    }


class DriftEvidenceTests(unittest.TestCase):
    def test_verified_old_release_is_reported_as_drift_and_never_approved(self):
        result = inspect(sample(), now=NOW)
        self.assertEqual(result["status"], "DEPLOYED_SOURCE_DRIFT")
        self.assertEqual(result["deployments_triggered"], 0)
        self.assertFalse(result["release_authorized"])
        self.assertFalse(result["h8_credential_rollback_e2e_verified"])

    def test_matching_commits_are_not_a_deployment_approval(self):
        result = inspect(sample(deployed=NEW), now=NOW)
        self.assertEqual(result["status"], "SOURCE_MATCH_UNVERIFIED")
        self.assertTrue(result["timestamp_window_valid"])
        self.assertFalse(result["provider_readback_authenticated"])
        self.assertFalse(result["release_authorized"])
        self.assertFalse(result["h7_writer_inventory_complete"])

    def test_missing_service_unknown_deploy_or_auto_deploy_fails(self):
        for mutation in ("missing", "bad_id", "auto", "paused", "secret"):
            payload = sample()
            service = payload["services"]["web"]
            if mutation == "missing":
                del payload["services"]["web"]
            if mutation == "bad_id":
                service["service_id"] = "other-service"
            if mutation == "auto":
                service["auto_deploy"] = True
            if mutation == "paused":
                service["deploy_status"] = "paused"
            if mutation == "secret":
                service["api_key"] = "placeholder"
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                inspect(payload, now=NOW)

    def test_stale_future_or_invalid_observation_is_denied(self):
        for stamp in (
            "2026-10-10T23:30:00Z",
            "2026-10-11T00:01:00Z",  # exactly 60s ahead is rejected
            "2026-10-11T00:02:00Z",
            "2026-02-30T23:59:00Z",
            "2026-10-10T23:59:00+00:00",
            None,
        ):
            payload = sample(main=NEW, deployed=NEW)
            payload["observed_at"] = stamp
            with self.subTest(stamp=stamp), self.assertRaises(ValueError):
                inspect(payload, now=NOW)

    def test_snapshot_cannot_claim_provider_verified_even_when_equal(self):
        result = inspect(sample(main=NEW, deployed=NEW), now=NOW)
        self.assertEqual(result["status"], "SOURCE_MATCH_UNVERIFIED")
        self.assertFalse(result["release_authorized"])
        self.assertFalse(result["provider_readback_authenticated"])
        self.assertEqual(result["observed_at"], "2026-10-10T23:59:00Z")

    def test_reject_malformed_sha_or_extra_target(self):
        for value in ("short", "Z" * 40, 0):
            with self.subTest(value=value), self.assertRaises(ValueError):
                inspect(sample(main=value), now=NOW)
        payload = sample()
        payload["authorization"] = "placeholder"
        with self.assertRaises(ValueError):
            inspect(payload, now=NOW)


if __name__ == "__main__":
    unittest.main()
