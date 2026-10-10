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
from typing import Any

REPO = "NTinkicht/veritas-atlas"
SERVICES = {
    "api": "srv-dan1pqbtqb8s73a979s0",
    "web": "srv-dan1pqjtqb8s73a979tg",
}
SHA = re.compile(r"^[0-9a-f]{40}$")
SERVICE_FIELDS = frozenset({
    "service_id", "deployed_commit", "deploy_status",
    "auto_deploy", "auto_deploy_trigger",
})


def inspect(snapshot: Any) -> dict:
    if not isinstance(snapshot, dict) or set(snapshot) != {
        "schema", "repository", "main_commit", "services",
    }:
        raise ValueError("RELEASE_SNAPSHOT_SCHEMA_INVALID")
    if snapshot["schema"] != "VERITAS_DEPLOY_DRIFT_V1" or snapshot["repository"] != REPO:
        raise ValueError("RELEASE_SNAPSHOT_TARGET_INVALID")
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
        "status": "SOURCE_MATCH_ONLY" if all(s["source_equal_to_main"] for s in out.values())
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
