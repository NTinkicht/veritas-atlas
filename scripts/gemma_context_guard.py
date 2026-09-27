#!/usr/bin/env python3
"""Fail closed before repository evidence can leave the runner."""
from __future__ import annotations

import re

SENSITIVE_PATH = re.compile(
    r"(^|/)(?:\.env(?:\.|$)|secrets?(?:/|$)|credentials?(?:/|$)|auth\.json$|"
    r".*\.(?:pem|key|p12|pfx|jks)$)",
    re.IGNORECASE,
)
SECRET_ASSIGNMENT = re.compile(
    r"(?i)(?:api[_-]?key|token|secret|password|authorization)"
    r"\s*[:=]\s*[\"']?([^\"'\s,;}{]{12,})"
)
BEARER_LITERAL = re.compile(r"(?i)\bbearer\s+([A-Za-z0-9._~+/=-]{16,})")
PRIVATE_KEY = re.compile(r"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----")
SAFE_VALUE_PREFIXES = (
    "os.environ",
    "os.getenv",
    "getenv(",
    "env.",
    "secrets.",
    "${{",
    "[redacted]",
    "<redacted>",
    "placeholder",
    "example",
)


def validate_changed_paths(paths: list[str]) -> None:
    unsafe = [path for path in paths if not path or SENSITIVE_PATH.search(path)]
    if unsafe:
        raise SystemExit("SENSITIVE_REPOSITORY_PATH_BLOCKED")


def _safe_reference(value: str) -> bool:
    lowered = value.strip().lower()
    return any(lowered.startswith(prefix) for prefix in SAFE_VALUE_PREFIXES)


def validate_diff(diff: str) -> None:
    if PRIVATE_KEY.search(diff):
        raise SystemExit("SECRET_LIKE_DIFF_CONTENT_BLOCKED")
    for line in diff.splitlines():
        if not line.startswith(("+", "-")) or line.startswith(("+++", "---")):
            continue
        for match in SECRET_ASSIGNMENT.finditer(line):
            value = match.group(1)
            if not _safe_reference(value):
                raise SystemExit("SECRET_LIKE_DIFF_CONTENT_BLOCKED")
        for match in BEARER_LITERAL.finditer(line):
            value = match.group(1)
            if not _safe_reference(value):
                raise SystemExit("SECRET_LIKE_DIFF_CONTENT_BLOCKED")
