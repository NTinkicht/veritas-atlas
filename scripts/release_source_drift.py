#!/usr/bin/env python3
"""Read-only H7/H8 deployment source freshness assessment.

Accepts an externally collected nonsecret snapshot. This NEVER contacts
Render/Neon, alters their state, or treats source freshness as release approval.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from datetime import datetime, timezone, timedelta
from typing import Any

REPO = "NTinkicht/veritas-atlas"
SERVICES = {
    "api": "srv-dan1pqbtqb8s73a979s0",
    "web": "srv-dan1pqjtqb8s73a979tg",
}
SHA = re.compile(r"^[0-9a-f]{40}$")
RENDER_DEPLOYMENT_ID = re.compile(r"^dep-[a-z0-9]{20}$")
MAX_OBSERVATION_AGE = timedelta(minutes=15)
MAX_CLOCK_SKEW = timedelta(seconds=60)
SERVICE_FIELDS = frozenset({
    "service_id", "deployed_commit", "deploy_status",
    "auto_deploy", "auto_deploy_trigger",
})


def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    """Fail closed on ambiguous JSON object keys in external evidence."""
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("RELEASE_JSON_DUPLICATE_KEY")
        result[key] = value
    return result


def reject_nonfinite_json_constant(value: str) -> None:
    """Reject non-RFC JSON constants before inspecting untrusted evidence."""
    raise ValueError("RELEASE_JSON_NONFINITE_NUMBER")


def parse_evidence_json(path: Path) -> Any:
    """Fail closed on ambiguous keys and NaN/infinite numbers."""
    return json.loads(
        path.read_text(encoding="utf-8"),
        object_pairs_hook=reject_duplicate_keys,
        parse_constant=reject_nonfinite_json_constant,
    )


def inspect(snapshot: Any, *, now: datetime | None = None) -> dict:
    if not isinstance(snapshot, dict) or set(snapshot) != {
        "schema", "repository", "main_commit", "services", "observed_at",
    }:
        raise ValueError("RELEASE_SNAPSHOT_SCHEMA_INVALID")
    if snapshot["schema"] != "VERITAS_DEPLOY_DRIFT_V1" or snapshot["repository"] != REPO:
        raise ValueError("RELEASE_SNAPSHOT_TARGET_INVALID")
    # This is only a *claimed observation timestamp* supplied alongside
    # read-only provider metadata. The script cannot authenticate a Render
    # or GitHub readback. Refuse replayed/future-dated snapshots anyway.
    stamp = snapshot["observed_at"]
    if not isinstance(stamp, str) or not re.fullmatch(
        r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", stamp
    ):
        raise ValueError("RELEASE_OBSERVATION_TIMESTAMP_INVALID")
    try:
        observed = datetime.strptime(stamp, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=timezone.utc
        )
    except ValueError as exc:
        raise ValueError("RELEASE_OBSERVATION_TIMESTAMP_INVALID") from exc
    current = datetime.now(timezone.utc) if now is None else now
    if current.tzinfo is None:
        raise ValueError("RELEASE_EVALUATION_CLOCK_INVALID")
    age = current.astimezone(timezone.utc) - observed
    if age > MAX_OBSERVATION_AGE or age <= -MAX_CLOCK_SKEW:
        raise ValueError("RELEASE_OBSERVATION_STALE_OR_FUTURE")
    target = snapshot["main_commit"]
    if not isinstance(target, str) or not SHA.fullmatch(target):
        raise ValueError("RELEASE_MAIN_SHA_INVALID")
    data = snapshot["services"]
    if not isinstance(data, dict) or set(data) != set(SERVICES):
        raise ValueError("RELEASE_SERVICE_INVENTORY_INCOMPLETE")
    out = {}
    for kind, expected_id in sorted(SERVICES.items()):
        service = data[kind]
        if not isinstance(service, dict) or set(service) != SERVICE_FIELDS:
            raise ValueError("RELEASE_SERVICE_FIELDS_INVALID")
        if service["service_id"] != expected_id:
            raise ValueError("RELEASE_SERVICE_IDENTITY_MISMATCH")
        deployed = service["deployed_commit"]
        if not isinstance(deployed, str) or not SHA.fullmatch(deployed):
            raise ValueError("RELEASE_DEPLOYED_SHA_INVALID")
        if service["auto_deploy"] is not False or service["auto_deploy_trigger"] != "off":
            raise ValueError("RELEASE_AUTO_DEPLOY_DRIFT")
        if service["deploy_status"] != "live":
            raise ValueError("RELEASE_DEPLOY_STATUS_UNKNOWN")
        out[kind] = {
            "service_id": expected_id,
            "deployed_commit": deployed,
            "source_equal_to_main": deployed == target,
        }
    return {
        "schema": "VERITAS_RELEASE_DRIFT_REPORT_V1",
        "repository": REPO,
        "main_commit": target,
        "mode": "READ_ONLY",
        "observed_at": stamp,
        "timestamp_window_valid": True,
        # This offline equality check cannot prove provider authenticity:
        # a replay with a forged new timestamp remains possible.
        "provider_readback_authenticated": False,
        "status": "SOURCE_MATCH_UNVERIFIED" if all(s["source_equal_to_main"] for s in out.values())
        else "DEPLOYED_SOURCE_DRIFT",
        "services": out,
        "deployments_triggered": 0,
        "release_authorized": False,
        "h7_writer_inventory_complete": False,
        "h8_credential_rollback_e2e_verified": False,
    }



RENDER_SERVICE_NAMES = {
    "api": "veritas-atlas-api-ntinkicht",
    "web": "veritas-atlas-web-ntinkicht",
}
RENDER_REPO_URL = "https://github.com/" + REPO


def inspect_render_readback(
    service_records: Any,
    deployments_by_service_id: Any,
    *,
    main_commit: str,
    observed_at: str,
    now: datetime | None = None,
) -> dict:
    """Normalize read-only Render metadata without contacting a provider.

    The caller supplies separately retrieved GitHub main and Render responses.
    This adapter cannot authenticate those responses: the resulting report
    MUST retain provider_readback_authenticated=False and release_authorized=False.
    Any absent/ambiguous target, auto-deploy drift, or newest non-live deploy
    is rejected rather than silently falling back to older live history.
    """
    if not isinstance(service_records, list) or not isinstance(
        deployments_by_service_id, dict
    ) or set(deployments_by_service_id) != set(SERVICES.values()):
        raise ValueError("RENDER_READBACK_INVENTORY_INVALID")

    evidence = {}
    for kind, service_id in SERVICES.items():
        matching = [
            entry
            for entry in service_records
            if isinstance(entry, dict) and entry.get("id") == service_id
        ]
        if len(matching) != 1:
            raise ValueError("RENDER_SERVICE_IDENTITY_INVALID")
        service = matching[0]
        if (
            service.get("name") != RENDER_SERVICE_NAMES[kind]
            or service.get("repo") != RENDER_REPO_URL
            or service.get("branch") != "main"
            or service.get("autoDeploy") != "no"
            or service.get("autoDeployTrigger") != "off"
        ):
            raise ValueError("RENDER_SERVICE_CONFIGURATION_DRIFT")
        # A historic successful deploy is not evidence that its service is
        # currently running. Render reports suspension separately from deploy.
        if service.get("suspended") != "not_suspended":
            raise ValueError("RENDER_SERVICE_SUSPENDED_OR_UNKNOWN")
        details = service.get("serviceDetails")
        if kind == "api":
            if (
                not isinstance(details, dict)
                or not isinstance(details.get("maintenanceMode"), dict)
                or details["maintenanceMode"].get("enabled") is not False
            ):
                raise ValueError("RENDER_SERVICE_MAINTENANCE_OR_UNKNOWN")
        elif isinstance(details, dict) and "maintenanceMode" in details:
            mode = details["maintenanceMode"]
            if not isinstance(mode, dict) or mode.get("enabled") is not False:
                raise ValueError("RENDER_SERVICE_MAINTENANCE_OR_UNKNOWN")

        candidates = deployments_by_service_id[service_id]
        if not isinstance(candidates, list) or not candidates:
            raise ValueError("RENDER_DEPLOYMENT_READBACK_INVALID")
        # Render lists deployments newest first. Refuse ambiguous ordering.
        dates = []
        for entry in candidates:
            if not isinstance(entry, dict) or not isinstance(
                entry.get("createdAt"), str
            ):
                raise ValueError("RENDER_DEPLOYMENT_READBACK_INVALID")
            try:
                date = datetime.fromisoformat(
                    entry["createdAt"].replace("Z", "+00:00")
                )
            except ValueError as exc:
                raise ValueError("RENDER_DEPLOYMENT_READBACK_INVALID") from exc
            if date.tzinfo is None:
                raise ValueError("RENDER_DEPLOYMENT_READBACK_INVALID")
            dates.append(date)
        if any(newer <= older for newer, older in zip(dates, dates[1:])):
            raise ValueError("RENDER_DEPLOYMENT_ORDER_AMBIGUOUS")
        latest = candidates[0]
        # A "live" status without a canonical deployment identity and a
        # completed timeline is incomplete provider evidence.
        deploy_id = latest.get("id")
        if not isinstance(deploy_id, str) or not RENDER_DEPLOYMENT_ID.fullmatch(deploy_id):
            raise ValueError("RENDER_DEPLOYMENT_ID_INVALID")
        started_raw = latest.get("startedAt")
        finished_raw = latest.get("finishedAt")
        if not isinstance(started_raw, str) or not isinstance(finished_raw, str):
            raise ValueError("RENDER_DEPLOYMENT_TIMELINE_INVALID")
        try:
            started = datetime.fromisoformat(started_raw.replace("Z", "+00:00"))
            finished = datetime.fromisoformat(finished_raw.replace("Z", "+00:00"))
            observed = datetime.strptime(observed_at, "%Y-%m-%dT%H:%M:%SZ").replace(
                tzinfo=timezone.utc
            )
        except ValueError as exc:
            raise ValueError("RENDER_DEPLOYMENT_TIMELINE_INVALID") from exc
        if (
            started.tzinfo is None
            or finished.tzinfo is None
            or finished < started
            or started < dates[0]
            or finished < dates[0]
            or finished > observed
        ):
            raise ValueError("RENDER_DEPLOYMENT_TIMELINE_INVALID")
        commit = latest.get("commit")
        sha = commit.get("id") if isinstance(commit, dict) else None
        if latest.get("status") != "live" or not isinstance(sha, str):
            raise ValueError("RENDER_DEPLOYMENT_NOT_LIVE")
        evidence[kind] = {
            "service_id": service_id,
            "deployed_commit": sha,
            "deploy_status": "live",
            "auto_deploy": False,
            "auto_deploy_trigger": "off",
        }

    snapshot = {
        "schema": "VERITAS_DEPLOY_DRIFT_V1",
        "repository": REPO,
        "main_commit": main_commit,
        "observed_at": observed_at,
        "services": evidence,
    }
    return inspect(snapshot, now=now)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Offline Veritas release source drift evaluation; no network or writes"
    )
    parser.add_argument("snapshot", nargs="?", type=Path)
    parser.add_argument("--render-services", type=Path)
    parser.add_argument("--render-deploys", type=Path)
    parser.add_argument("--main-commit")
    parser.add_argument("--observed-at")
    args = parser.parse_args()
    provider_args = (
        args.render_services, args.render_deploys,
        args.main_commit, args.observed_at,
    )
    try:
        if any(arg is not None for arg in provider_args):
            if args.snapshot is not None or any(arg is None for arg in provider_args):
                raise ValueError("RELEASE_INPUT_MODE_INVALID")
            report = inspect_render_readback(
                parse_evidence_json(args.render_services),
                parse_evidence_json(args.render_deploys),
                main_commit=args.main_commit,
                observed_at=args.observed_at,
            )
        else:
            if args.snapshot is None:
                raise ValueError("RELEASE_INPUT_MODE_INVALID")
            report = inspect(parse_evidence_json(args.snapshot))
    except (ValueError, OSError, UnicodeError) as exc:
        print(json.dumps({"status": "EVIDENCE_INVALID", "reason": str(exc)}, sort_keys=True))
        return 2
    print(json.dumps(report, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
