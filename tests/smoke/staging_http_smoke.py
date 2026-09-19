#!/usr/bin/env python3
"""Read-only external staging smoke. No real credentials or database mutations.

This checks HTTP reachability and the unauthenticated boundary; `/health`
and `/health/live` are liveness checks, NOT proof of database connectivity.
"""
from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.request

API = "https://veritas-atlas-api-ntinkicht.onrender.com"
WEB = "https://veritas-atlas-web-ntinkicht.onrender.com"
HEADERS = {"User-Agent": "veritas-staging-ci-smoke/1.0"}


def fetch(path: str, *, method: str = "GET", data: bytes | None = None,
          headers: dict[str, str] | None = None) -> tuple[int, dict[str, str], bytes]:
    request = urllib.request.Request(
        path, data=data, method=method, headers={**HEADERS, **(headers or {})}
    )
    try:
        with urllib.request.urlopen(request, timeout=25) as response:
            return response.status, dict(response.headers), response.read(128 * 1024)
    except urllib.error.HTTPError as error:
        with error:
            return error.code, dict(error.headers), error.read(128 * 1024)


def wait_200(url: str, *, label: str, attempts: int = 8) -> bytes:
    last_error = "not attempted"
    for attempt in range(1, attempts + 1):
        try:
            status, _headers, body = fetch(url)
            if status == 200:
                print(f"PASS {label}: HTTP 200 on attempt {attempt}")
                return body
            last_error = f"HTTP {status}"
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            last_error = type(exc).__name__
        print(f"{label} attempt {attempt}/{attempts}: {last_error}")
        if attempt < attempts:
            time.sleep(12)
    raise AssertionError(f"{label} did not become available: {last_error}")


def assert_status(label: str, url: str, expected: int, **kwargs: object) -> dict[str, str]:
    status, headers, _body = fetch(url, **kwargs)
    if status != expected:
        raise AssertionError(f"{label}: expected HTTP {expected}, got HTTP {status}")
    print(f"PASS {label}: HTTP {status}")
    return headers


def main(*, require_db_readiness: bool = False) -> None:
    wait_200(f"{API}/health/live", label="API liveness")
    wait_200(f"{API}/health", label="API health endpoint (liveness only)")
    if require_db_readiness:
        # Run only after the approved API release contains /health/ready.
        # This tests the configured staging PostgreSQL connection, not auth/E2E.
        assert_status("database readiness", f"{API}/health/ready", 200)
    html = wait_200(f"{WEB}/", label="web homepage")
    if b"<html" not in html.lower():
        raise AssertionError("web homepage response was not HTML")

    assert_status("unauthenticated /me rejected", f"{API}/api/v1/auth/me", 401)

    # Existing development fixture is public in DevUserStore.cs. This
    # MUST be rejected by staging's ASPNETCORE_ENVIRONMENT=Production.
    # Never pass the real staging administrator password into this job.
    fixture = json.dumps({"username": "admin1", "password": "password123"}).encode()
    assert_status(
        "development fixture rejected in staging",
        f"{API}/api/v1/auth/login",
        401,
        method="POST",
        data=fixture,
        headers={"Content-Type": "application/json"},
    )

    cors = assert_status(
        "frontend CORS preflight", f"{API}/api/v1/auth/login", 204,
        method="OPTIONS",
        headers={
            "Origin": WEB,
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type",
        },
    )
    allow_origin = next(
        (value for key, value in cors.items()
         if key.lower() == "access-control-allow-origin"),
        None,
    )
    if allow_origin != WEB:
        raise AssertionError("frontend CORS origin not allowed")
    print("PASS frontend CORS origin allowed")
    print("PASS read-only staging smoke; database-backed/authenticated E2E remains separate")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--require-db-readiness", action="store_true",
        help="Post-deploy only: require a successful live PostgreSQL readiness probe.",
    )
    main(require_db_readiness=parser.parse_args().require_db_readiness)
