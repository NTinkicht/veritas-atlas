#!/usr/bin/env python3
"""Pure review-finding reconciliation policy for the native L4 merge controller."""
from __future__ import annotations

import hashlib
import re

SUBSTANTIVE_FINDING = re.compile(
    r"(?im)(?:P[012]\s+Badge|^\s*(?:#{1,6}\s*)?(?:[-*]\s*)?(?:\*\*)?"
    r"(?:(?:Severity\s*:\s*)?(?:\[\s*)?"
    r"(?:P[012]|MEDIUM|MAJOR|HIGH|CRITICAL|BLOCKER)"
    r"(?:\s*\])?)(?:\*\*)?\s*(?:[-—:]|\b))"
)
FINDING_RESOLUTION = re.compile(
    r"(?im)^L4-RESOLVED-FINDING:\s*(review|issue|inline):(\d+)\s+"
    r"sha=([0-9a-f]{40})\s+fingerprint=([0-9a-f]{64})\s*$"
)


def _timestamp(item: dict) -> str:
    return str(item.get("updated_at") or item.get("submitted_at") or item.get("created_at") or "")


def fingerprint(kind: str, item: dict) -> str:
    body = str(item.get("body") or "")
    payload = f"{kind}:{item.get('id')}\n{_timestamp(item)}\n{body}".encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def unresolved_substantive_findings(
    *,
    reviews: list[dict],
    issue_comments: list[dict],
    inline_comments: list[dict],
    sha: str,
    finding_comment_authors: set[str] | frozenset[str],
) -> list[str]:
    findings: set[str] = set()
    fingerprints: dict[str, str] = {}
    target_updates: dict[str, str] = {}
    candidates: list[tuple[str, str, str, str, int]] = []

    for comment in issue_comments:
        login = ((comment.get("user") or {}).get("login") or "").lower()
        if login not in finding_comment_authors:
            continue
        body = str(comment.get("body") or "")
        clean_body = FINDING_RESOLUTION.sub("", body)
        has_finding = bool(SUBSTANTIVE_FINDING.search(clean_body))
        immutable = comment.get("created_at") == comment.get("updated_at")
        if immutable and not has_finding:
            for kind, finding_id, resolved_sha, expected_fp in FINDING_RESOLUTION.findall(body):
                candidates.append((
                    f"{kind.lower()}:{finding_id}",
                    resolved_sha,
                    expected_fp,
                    str(comment.get("created_at") or ""),
                    int(comment.get("id") or 0),
                ))
        if has_finding and isinstance(comment.get("id"), int):
            key = f"issue:{comment['id']}"
            findings.add(key)
            fingerprints[key] = fingerprint("issue", comment)
            target_updates[key] = _timestamp(comment)

    for review in reviews:
        login = ((review.get("user") or {}).get("login") or "").lower()
        if login not in finding_comment_authors:
            continue
        body = str(review.get("body") or "")
        state = str(review.get("state") or "").upper()
        if (state == "CHANGES_REQUESTED" or SUBSTANTIVE_FINDING.search(body)) and isinstance(review.get("id"), int):
            key = f"review:{review['id']}"
            findings.add(key)
            fingerprints[key] = fingerprint("review", review)
            target_updates[key] = _timestamp(review)

    for comment in inline_comments:
        login = ((comment.get("user") or {}).get("login") or "").lower()
        if login not in finding_comment_authors:
            continue
        body = str(comment.get("body") or "")
        if SUBSTANTIVE_FINDING.search(body) and isinstance(comment.get("id"), int):
            key = f"inline:{comment['id']}"
            findings.add(key)
            fingerprints[key] = fingerprint("inline", comment)
            target_updates[key] = _timestamp(comment)

    resolved: set[str] = set()
    for target, resolved_sha, expected_fp, marker_created, marker_id in candidates:
        if resolved_sha != sha or target not in findings:
            continue
        if expected_fp != fingerprints.get(target):
            continue
        target_updated = target_updates.get(target, "")
        if not marker_created or not target_updated or marker_created < target_updated:
            continue
        kind, raw_id = target.split(":", 1)
        if kind == "issue" and marker_id == int(raw_id):
            continue
        resolved.add(target)

    return sorted(findings - resolved)
