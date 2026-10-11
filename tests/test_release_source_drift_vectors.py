"""Deterministic Veritas staging drift evidence vectors (no network/secrets)."""
from __future__ import annotations
import sys
from pathlib import Path
import unittest
from datetime import datetime, timezone

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from release_source_drift import inspect, inspect_render_readback, REPO, SERVICES

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



def render_readback(deployed=OLD):
    # Public, nonsecret metadata structure observed from Render on 2026-10-11.
    services = [
        {
            "id": service_id,
            "name": "veritas-atlas-api-ntinkicht" if kind == "api"
                    else "veritas-atlas-web-ntinkicht",
            "repo": "https://github.com/" + REPO,
            "branch": "main",
            "autoDeploy": "no",
            "autoDeployTrigger": "off",
        }
        for kind, service_id in SERVICES.items()
    ]
    deployments = {
        service_id: [{
            "status": "live",
            "commit": {"id": deployed},
            "createdAt": "2026-09-19T05:26:03Z",
        }]
        for service_id in SERVICES.values()
    }
    return services, deployments


class DriftEvidenceTests(unittest.TestCase):
    def test_render_cli_offline_files_and_partial_mode_fail_closed(self):
        import json
        import subprocess
        import tempfile
        services, deployments = render_readback()
        runner = str(Path(__file__).resolve().parents[1] / "scripts" / "release_source_drift.py")
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            services_file = root / "services.json"
            deploys_file = root / "deploys.json"
            services_file.write_text(json.dumps(services), encoding="utf-8")
            deploys_file.write_text(json.dumps(deployments), encoding="utf-8")
            cmd = [
                sys.executable, runner,
                "--render-services", str(services_file),
                "--render-deploys", str(deploys_file),
                "--main-commit", NEW,
                "--observed-at", datetime.now(timezone.utc).strftime(
                    "%Y-%m-%dT%H:%M:%SZ"
                ),
            ]
            accepted = subprocess.run(cmd, capture_output=True, text=True, check=False)
            self.assertEqual(accepted.returncode, 0, accepted.stderr + accepted.stdout)
            report = json.loads(accepted.stdout)
            self.assertEqual(report["status"], "DEPLOYED_SOURCE_DRIFT")
            self.assertFalse(report["provider_readback_authenticated"])
            self.assertFalse(report["release_authorized"])
            self.assertEqual(report["deployments_triggered"], 0)

            partial = subprocess.run(
                cmd[:4] + cmd[6:], capture_output=True, text=True, check=False
            )
            self.assertEqual(partial.returncode, 2)
            self.assertEqual(
                json.loads(partial.stdout)["status"], "EVIDENCE_INVALID"
            )

    def test_render_readback_reproduces_observed_staging_source_drift(self):
        services, deployments = render_readback()
        report = inspect_render_readback(
            services, deployments, main_commit=NEW,
            observed_at="2026-10-10T23:59:00Z", now=NOW,
        )
        self.assertEqual(report["status"], "DEPLOYED_SOURCE_DRIFT")
        self.assertEqual(
            {r["deployed_commit"] for r in report["services"].values()},
            {OLD},
        )
        self.assertFalse(report["provider_readback_authenticated"])
        self.assertFalse(report["release_authorized"])
        self.assertEqual(report["deployments_triggered"], 0)

    def test_render_readback_blocks_bad_inventory_and_newest_nonlive_deploy(self):
        from copy import deepcopy
        baseline_services, baseline_deployments = render_readback()
        for mutation in (
            "missing_service", "duplicate_service", "wrong_repo",
            "auto_deploy", "missing_deploy", "newest_failed",
            "out_of_order", "invalid_sha", "other_service",
        ):
            services, deployments = (
                deepcopy(baseline_services), deepcopy(baseline_deployments)
            )
            api_id = SERVICES["api"]
            if mutation == "missing_service":
                services = [s for s in services if s["id"] != api_id]
            elif mutation == "duplicate_service":
                services.append(deepcopy(services[0]))
            elif mutation == "wrong_repo":
                services[0]["repo"] = "https://github.com/other/repo"
            elif mutation == "auto_deploy":
                services[0]["autoDeploy"] = "yes"
            elif mutation == "missing_deploy":
                deployments[api_id] = []
            elif mutation == "newest_failed":
                deployments[api_id].insert(0, {
                    "status": "build_failed",
                    "commit": {"id": NEW},
                    "createdAt": "2026-10-11T00:00:00Z",
                })
            elif mutation == "out_of_order":
                deployments[api_id].append({
                    "status": "live",
                    "commit": {"id": OLD},
                    "createdAt": "2026-10-10T00:00:00Z",
                })
                # Keep oldest first: newest-first Render ordering must reject it.
            elif mutation == "invalid_sha":
                deployments[api_id][0]["commit"]["id"] = "missing"
            elif mutation == "other_service":
                deployments["unrecognized"] = []
            with self.subTest(case=mutation), self.assertRaises(ValueError):
                inspect_render_readback(
                    services, deployments, main_commit=NEW,
                    observed_at="2026-10-10T23:59:00Z", now=NOW,
                )

    def test_render_readback_matching_source_is_still_not_release_permission(self):
        services, deployments = render_readback(deployed=NEW)
        result = inspect_render_readback(
            services, deployments, main_commit=NEW,
            observed_at="2026-10-10T23:59:00Z", now=NOW,
        )
        self.assertEqual(result["status"], "SOURCE_MATCH_UNVERIFIED")
        self.assertFalse(result["provider_readback_authenticated"])
        self.assertFalse(result["release_authorized"])
        with self.assertRaises(ValueError):
            inspect_render_readback(
                services, deployments, main_commit=NEW,
                observed_at="2026-10-10T23:30:00Z", now=NOW,
            )

    def test_verified_old_release_is_reported_as_drift_and_never_approved(self):
        result = inspect(sample(), now=NOW)
        self.assertEqual(result["status"], "DEPLOYED_SOURCE_DRIFT")
        self.assertEqual(result["deployments_triggered"], 0)
        self.assertFalse(result["release_authorized"])
        self.assertFalse(result["h8_credential_rollback_e2e_verified"])

    def test_mixed_service_sources_fail_closed_independently(self):
        # One current service cannot conceal another service still on an old SHA.
        payload = sample()
        payload["services"]["api"]["deployed_commit"] = NEW
        result = inspect(payload, now=NOW)
        self.assertEqual(result["status"], "DEPLOYED_SOURCE_DRIFT")
        self.assertTrue(result["services"]["api"]["source_equal_to_main"])
        self.assertFalse(result["services"]["web"]["source_equal_to_main"])
        self.assertEqual(result["services"]["api"]["deployed_commit"], NEW)
        self.assertEqual(result["services"]["web"]["deployed_commit"], OLD)
        self.assertEqual(result["deployments_triggered"], 0)
        self.assertFalse(result["provider_readback_authenticated"])
        self.assertFalse(result["release_authorized"])

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
