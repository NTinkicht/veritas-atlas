"""Deterministic Veritas staging drift evidence vectors (no network/secrets)."""
from __future__ import annotations
import sys
from pathlib import Path
import unittest
from datetime import datetime, timezone

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from release_source_drift import inspect, inspect_render_readback, parse_evidence_json, MAX_EVIDENCE_BYTES, REPO, SERVICES

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
            "suspended": "not_suspended",
            "serviceDetails": (
                {"maintenanceMode": {"enabled": False}} if kind == "api" else {}
            ),
        }
        for kind, service_id in SERVICES.items()
    ]
    deployments = {
        service_id: [{
            "id": (
                "dep-dan1pqrtqb8s73a97bag" if service_id == SERVICES["api"]
                else "dep-dan1pqrtqb8s73a97b00"
            ),
            "status": "live",
            "commit": {"id": deployed},
            "createdAt": "2026-09-19T05:26:03Z",
            "startedAt": "2026-09-19T05:26:03Z",
            "finishedAt": "2026-09-19T05:27:04Z",
        }]
        for service_id in SERVICES.values()
    }
    return services, deployments


class DriftEvidenceTests(unittest.TestCase):
    def test_provider_json_size_bound_accepts_limit_and_rejects_one_byte_more(self):
        import json
        import tempfile
        payload = json.dumps(sample()).encode("utf-8")
        with tempfile.TemporaryDirectory() as folder:
            evidence = Path(folder) / "provider.json"
            evidence.write_bytes(payload + b" " * (MAX_EVIDENCE_BYTES - len(payload)))
            self.assertEqual(
                parse_evidence_json(evidence)["schema"], "VERITAS_DEPLOY_DRIFT_V1"
            )
            with evidence.open("ab") as handle:
                handle.write(b" ")
            with self.assertRaisesRegex(ValueError, "RELEASE_JSON_TOO_LARGE"):
                parse_evidence_json(evidence)


    def test_deep_json_is_a_structured_refusal_in_both_cli_modes(self):
        import json
        import subprocess
        import tempfile

        runner = str(Path(__file__).resolve().parents[1] / "scripts" / "release_source_drift.py")
        nested = "[" * 3000 + "0" + "]" * 3000
        with tempfile.TemporaryDirectory() as folder:
            evidence = Path(folder) / "deep.json"
            evidence.write_text(nested, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "RELEASE_JSON_NESTING_TOO_DEEP"):
                parse_evidence_json(evidence)
            commands = (
                [sys.executable, runner, str(evidence)],
                [
                    sys.executable, runner,
                    "--render-services", str(evidence),
                    "--render-deploys", str(evidence),
                    "--main-commit", NEW,
                    "--observed-at", NOW.strftime("%Y-%m-%dT%H:%M:%SZ"),
                ],
            )
            for cmd in commands:
                with self.subTest(mode="snapshot" if len(cmd) == 3 else "provider"):
                    result = subprocess.run(cmd, capture_output=True, text=True, check=False)
                    self.assertEqual(result.returncode, 2, result.stderr)
                    self.assertEqual(json.loads(result.stdout), {
                        "status": "EVIDENCE_INVALID",
                        "reason": "RELEASE_JSON_NESTING_TOO_DEEP",
                    })
                    self.assertNotIn("Traceback", result.stderr)

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

    def test_cli_rejects_duplicate_keys_in_both_input_modes(self):
        import json
        import subprocess
        import tempfile
        runner = str(Path(__file__).resolve().parents[1] / "scripts" / "release_source_drift.py")
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            snapshot_path = root / "snapshot.json"
            duplicate_snapshot = json.dumps(sample()).replace(
                '"repository":', '"repository": "untrusted", "repository":', 1
            )
            snapshot_path.write_text(duplicate_snapshot, encoding="utf-8")
            rejected = subprocess.run(
                [sys.executable, runner, str(snapshot_path)],
                capture_output=True, text=True, check=False,
            )
            self.assertEqual(rejected.returncode, 2)
            self.assertEqual(
                json.loads(rejected.stdout)["reason"], "RELEASE_JSON_DUPLICATE_KEY"
            )

            services, deployments = render_readback()
            services_path = root / "services.json"
            deployments_path = root / "deploys.json"
            services_path.write_text(
                json.dumps(services).replace(
                    '"name":', '"name": "untrusted", "name":', 1
                ), encoding="utf-8",
            )
            deployments_path.write_text(json.dumps(deployments), encoding="utf-8")
            rejected = subprocess.run(
                [
                    sys.executable, runner, "--render-services", str(services_path),
                    "--render-deploys", str(deployments_path),
                    "--main-commit", NEW,
                    "--observed-at", datetime.now(timezone.utc).strftime(
                        "%Y-%m-%dT%H:%M:%SZ"
                    ),
                ],
                capture_output=True, text=True, check=False,
            )
            self.assertEqual(rejected.returncode, 2)
            self.assertEqual(
                json.loads(rejected.stdout)["reason"], "RELEASE_JSON_DUPLICATE_KEY"
            )

    def test_cli_rejects_nonfinite_json_in_all_input_modes(self):
        import json
        import subprocess
        import tempfile
        runner = str(Path(__file__).resolve().parents[1] / "scripts" / "release_source_drift.py")
        services, deployments = render_readback()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            snapshot_path = root / "snapshot.json"
            services_path = root / "services.json"
            deployments_path = root / "deploys.json"
            for token in ("NaN", "Infinity", "-Infinity"):
                snapshot_path.write_text(
                    json.dumps(sample()).replace(
                        '"main_commit": "' + NEW + '"',
                        '"main_commit": ' + token, 1,
                    ), encoding="utf-8",
                )
                services_path.write_text(
                    json.dumps(services).replace(
                        '"branch": "main"', '"branch": ' + token, 1,
                    ), encoding="utf-8",
                )
                deployments_path.write_text(json.dumps(deployments), encoding="utf-8")
                cmd = [
                    sys.executable, runner,
                    "--render-services", str(services_path),
                    "--render-deploys", str(deployments_path),
                    "--main-commit", NEW,
                    "--observed-at", datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                ]
                with self.subTest(number_literal=token, mode="snapshot"):
                    result = subprocess.run(
                        [sys.executable, runner, str(snapshot_path)],
                        capture_output=True, text=True, check=False,
                    )
                    self.assertEqual(result.returncode, 2, result.stdout)
                    self.assertEqual(json.loads(result.stdout)["reason"], "RELEASE_JSON_NONFINITE_NUMBER")
                with self.subTest(number_literal=token, mode="services"):
                    result = subprocess.run(cmd, capture_output=True, text=True, check=False)
                    self.assertEqual(result.returncode, 2, result.stdout)
                    self.assertEqual(json.loads(result.stdout)["reason"], "RELEASE_JSON_NONFINITE_NUMBER")
                services_path.write_text(json.dumps(services), encoding="utf-8")
                deployments_path.write_text(
                    json.dumps(deployments).replace(
                        '"status": "live"', '"status": ' + token, 1,
                    ), encoding="utf-8",
                )
                with self.subTest(number_literal=token, mode="deployments"):
                    result = subprocess.run(cmd, capture_output=True, text=True, check=False)
                    self.assertEqual(result.returncode, 2, result.stdout)
                    self.assertEqual(json.loads(result.stdout)["reason"], "RELEASE_JSON_NONFINITE_NUMBER")


    def test_cli_rejects_exponent_overflow_in_all_input_modes(self):
        import json
        import subprocess
        import tempfile
        runner = str(Path(__file__).resolve().parents[1] / "scripts" / "release_source_drift.py")
        services, deployments = render_readback()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            snapshot_path = root / "snapshot.json"
            services_path = root / "services.json"
            deployments_path = root / "deploys.json"
            for token in ("1e999", "-1e999", "1e309"):
                snapshot_path.write_text(
                    json.dumps(sample()).replace(
                        '"main_commit": "' + NEW + '"',
                        '"main_commit": ' + token, 1,
                    ), encoding="utf-8",
                )
                services_path.write_text(json.dumps(services).replace(
                    '"branch": "main"', '"branch": ' + token, 1,
                ), encoding="utf-8")
                deployments_path.write_text(json.dumps(deployments), encoding="utf-8")
                cmd = [
                    sys.executable, runner,
                    "--render-services", str(services_path),
                    "--render-deploys", str(deployments_path),
                    "--main-commit", NEW,
                    "--observed-at", datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                ]
                for mode, command in (
                    ("snapshot", [sys.executable, runner, str(snapshot_path)]),
                    ("services", cmd),
                ):
                    with self.subTest(number_literal=token, mode=mode):
                        result = subprocess.run(command, capture_output=True, text=True, check=False)
                        self.assertEqual(result.returncode, 2, result.stdout)
                        self.assertEqual(json.loads(result.stdout)["reason"], "RELEASE_JSON_NONFINITE_NUMBER")
                services_path.write_text(json.dumps(services), encoding="utf-8")
                deployments_path.write_text(json.dumps(deployments).replace(
                    '"status": "live"', '"status": ' + token, 1,
                ), encoding="utf-8")
                with self.subTest(number_literal=token, mode="deployments"):
                    result = subprocess.run(cmd, capture_output=True, text=True, check=False)
                    self.assertEqual(result.returncode, 2, result.stdout)
                    self.assertEqual(json.loads(result.stdout)["reason"], "RELEASE_JSON_NONFINITE_NUMBER")

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
            "out_of_order", "tied_created_at", "invalid_sha", "other_service",
            "suspended", "missing_suspension", "maintenance",
            "missing_maintenance", "web_details_missing", "web_details_malformed",
            "web_maintenance_enabled", "malformed_commit",
            "missing_deploy_id", "bad_deploy_id", "duplicate_deploy_id", "missing_started",
            "missing_finished", "reversed_timeline", "completion_before_created",
            "start_before_created", "completed_after_observed", "naive_finished",
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
            elif mutation == "tied_created_at":
                deployments[api_id].append(deepcopy(deployments[api_id][0]))
            elif mutation == "invalid_sha":
                deployments[api_id][0]["commit"]["id"] = "missing"
            elif mutation == "other_service":
                deployments["unrecognized"] = []
            elif mutation == "suspended":
                services[0]["suspended"] = "suspended"
            elif mutation == "missing_suspension":
                del services[0]["suspended"]
            elif mutation == "maintenance":
                services[0]["serviceDetails"]["maintenanceMode"]["enabled"] = True
            elif mutation == "missing_maintenance":
                del services[0]["serviceDetails"]["maintenanceMode"]
            elif mutation == "web_details_missing":
                del services[1]["serviceDetails"]
            elif mutation == "web_details_malformed":
                services[1]["serviceDetails"] = None
            elif mutation == "web_maintenance_enabled":
                services[1]["serviceDetails"]["maintenanceMode"] = {"enabled": True}
            elif mutation == "malformed_commit":
                deployments[api_id][0]["commit"] = "not-a-dict"
            elif mutation == "missing_deploy_id":
                del deployments[api_id][0]["id"]
            elif mutation == "bad_deploy_id":
                deployments[api_id][0]["id"] = "dep-invalid"
            elif mutation == "duplicate_deploy_id":
                # Two different Render services cannot share a deploy identity.
                deployments[SERVICES["web"]][0]["id"] = deployments[api_id][0]["id"]
            elif mutation == "missing_started":
                del deployments[api_id][0]["startedAt"]
            elif mutation == "missing_finished":
                del deployments[api_id][0]["finishedAt"]
            elif mutation == "reversed_timeline":
                deployments[api_id][0]["finishedAt"] = "2026-09-19T05:25:00Z"
            elif mutation == "completion_before_created":
                deployments[api_id][0]["startedAt"] = "2026-09-19T05:25:00Z"
                deployments[api_id][0]["finishedAt"] = "2026-09-19T05:25:30Z"
            elif mutation == "start_before_created":
                # A deploy cannot start before the provider says it was created,
                # even if its completion timestamp is otherwise plausible.
                deployments[api_id][0]["startedAt"] = "2026-09-19T05:25:59Z"
            elif mutation == "completed_after_observed":
                deployments[api_id][0]["finishedAt"] = "2026-10-11T00:00:00Z"
            elif mutation == "naive_finished":
                deployments[api_id][0]["finishedAt"] = "2026-09-19T05:27:04"
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
