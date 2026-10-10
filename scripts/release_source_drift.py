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
MAX_OBSERVATION_AGE = timedelta(minutes=15)
MAX_CLOCK_SKEW = timedelta(seconds=60)
SERVICE_FIELDS = frozenset({
    "service_id", "deployed_commit", "deploy_status",
    "auto_deploy", "auto_deploy_trigger",
})


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
    if age > MAX_OBSERVATION_AGE or age < -MAX_CLOCK_SKEW:
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


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate supplied Veritas deployment metadata only")
    parser.add_argument("snapshot", type=Path)
    args = parser.parse_args()
    try:
        report = inspect(json.loads(args.snapshot.read_text(encoding="utf-8")))
    except (ValueError, OSError, UnicodeError) as exc:
        print(json.dumps({"status": "EVIDENCE_INVALID", "reason": str(exc)}, sort_keys=True))
        return 2
    print(json.dumps(report, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
