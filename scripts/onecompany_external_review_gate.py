#!/usr/bin/env python3
"""Verify OneCompany-hosted external Mistral PASS evidence for Veritas."""
from __future__ import annotations

import json
import os
import re
import urllib.request

HOST_REPO = "NTinkicht/OneCompany"
HOST_ISSUE = 130
WORKFLOW_NAME = "OneCompany Mistral External Exact-Head Review"
WORKFLOW_PATH = ".github/workflows/onecompany-mistral-external-review.yml"
MAX_PAGES = 50
MISTRAL_ALIASES = frozenset({"mistral", "mistral-vibe", "mistral_vibe"})
SHA = re.compile(r"[0-9a-f]{40}\Z")
FIELD = re.compile(r"(?m)^([a-z_]+):[ \t]*([^\r\n]*?)[ \t]*$")
MARKER = re.compile(
    r"<!-- ONECOMPANY_EXTERNAL_MISTRAL_REVIEW_V1 "
    r"repo=([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+) "
    r"pr=([1-9][0-9]{0,5}) head=([0-9a-f]{40}) base=([0-9a-f]{40}) "
    r"run=([1-9][0-9]{0,19}) dispatch=([1-9][0-9]{0,19}) "
    r"verdict=(PASS|CHANGES_REQUIRED|INSUFFICIENT_EVIDENCE) "
    r"result_sha256=([0-9a-f]{64}) -->"
)


def public_json(route: str):
    token = os.environ.get("GH_TOKEN", "")
    if not token:
        raise RuntimeError("GITHUB_TOKEN_UNAVAILABLE")
    request = urllib.request.Request(
        f"https://api.github.com/{route.lstrip('/')}",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "veritas-onecompany-review-gate",
        },
    )
    with urllib.request.urlopen(request, timeout=25) as response:
        return json.load(response)


def parse_dispatch(body: str) -> tuple[str, int, str, str, frozenset[str]]:
    if body.count("MISTRAL_EXTERNAL_REVIEW_V1") != 1:
        raise ValueError("EXTERNAL_DISPATCH_MARKER_INVALID")
    fields: dict[str, str] = {}
    for name, value in FIELD.findall(body):
        if name in fields:
            raise ValueError("EXTERNAL_DISPATCH_FIELD_DUPLICATED")
        fields[name] = value.strip()
    if set(fields) != {"repo", "pr", "head_sha", "base_sha", "material_authors"}:
        raise ValueError("EXTERNAL_DISPATCH_FIELD_SET_INVALID")
    repo = fields["repo"]
    pr = int(fields["pr"])
    head, base = fields["head_sha"], fields["base_sha"]
    if not SHA.fullmatch(head) or not SHA.fullmatch(base) or head == base:
        raise ValueError("EXTERNAL_DISPATCH_SHA_INVALID")
    authors = frozenset(
        item.strip().lower()
        for item in fields["material_authors"].split(",")
        if item.strip()
    )
    if not authors or any(not re.fullmatch(r"[a-z0-9_-]+", a) for a in authors):
        raise ValueError("EXTERNAL_DISPATCH_AUTHORS_INVALID")
    return repo, pr, head, base, authors


def _trusted_run(run_id: int, dispatch_id: int) -> dict | None:
    run = public_json(f"repos/{HOST_REPO}/actions/runs/{run_id}")
    workflow_path = str(run.get("path") or "")
    workflow_file, separator, workflow_ref = workflow_path.partition("@")
    if not (
        run.get("name") == WORKFLOW_NAME
        and run.get("display_title") == f"External Mistral review dispatch {dispatch_id}"
        and workflow_file == WORKFLOW_PATH
        and (not separator or workflow_ref == "main")
        and run.get("head_branch") == "main"
        and run.get("event") == "issue_comment"
        and run.get("status") == "completed"
        and run.get("conclusion") in {"success", "failure"}
        and (run.get("head_repository") or {}).get("full_name") == HOST_REPO
    ):
        return None
    return run


def _trusted_dispatch(
    dispatch_id: int,
    *,
    repo: str,
    pr: int,
    head: str,
    base: str,
    authors: set[str] | frozenset[str],
) -> dict | None:
    comment = public_json(f"repos/{HOST_REPO}/issues/comments/{dispatch_id}")
    if (
        (comment.get("user") or {}).get("login") != "NTinkicht"
        or comment.get("created_at") != comment.get("updated_at")
        or not str(comment.get("issue_url") or "").endswith(f"/repos/{HOST_REPO}/issues/{HOST_ISSUE}")
    ):
        return None
    try:
        d_repo, d_pr, d_head, d_base, d_authors = parse_dispatch(
            str(comment.get("body") or "")
        )
    except (ValueError, TypeError):
        return None
    if (
        d_repo != repo or d_pr != pr or d_head != head or d_base != base
        or d_authors != frozenset(authors)
    ):
        return None
    return comment


def external_mistral_pass(
    *,
    repo: str,
    pr: int,
    head: str,
    base: str,
    authors: set[str] | frozenset[str],
) -> bool:
    """Accept only the latest trusted exact-target OneCompany verdict."""
    normalized_authors = {str(actor).strip().lower() for actor in authors}
    if (
        not SHA.fullmatch(head)
        or not SHA.fullmatch(base)
        or not normalized_authors
        or normalized_authors & MISTRAL_ALIASES
    ):
        return False
    prefix = f"repo={repo} pr={pr} head={head} base={base} "
    candidates: list[tuple[str, int, str, dict]] = []
    try:
        for page in range(1, MAX_PAGES + 1):
            comments = public_json(
                f"repos/{HOST_REPO}/issues/{HOST_ISSUE}/comments?per_page=100&page={page}"
            )
            if not isinstance(comments, list):
                return False
            for comment in comments:
                if (
                    not isinstance(comment, dict)
                    or (comment.get("user") or {}).get("login") != "github-actions[bot]"
                    or comment.get("created_at") != comment.get("updated_at")
                ):
                    continue
                body = str(comment.get("body") or "")
                matches = MARKER.findall(body)
                if len(matches) != 1:
                    continue
                (
                    marker_repo, marker_pr, marker_head, marker_base,
                    run_id, dispatch_id, verdict, _result_digest,
                ) = matches[0]
                visible = re.findall(
                    r"(?m)^VERDICT:\s*(PASS|CHANGES_REQUIRED|INSUFFICIENT_EVIDENCE)\s*$",
                    body,
                )
                if len(visible) != 1 or visible[0] != verdict:
                    continue
                if (
                    f"repo={marker_repo} pr={marker_pr} "
                    f"head={marker_head} base={marker_base} "
                    != prefix
                ):
                    continue
                dispatch = _trusted_dispatch(
                    int(dispatch_id), repo=repo, pr=pr, head=head, base=base,
                    authors=authors,
                )
                if dispatch is None:
                    continue
                if str(dispatch.get("created_at")) > str(comment.get("created_at")):
                    continue
                run = _trusted_run(int(run_id), int(dispatch_id))
                if run is None:
                    continue
                # PASS runs must be green. Adverse verdicts are intentionally
                # published before the workflow's final PASS-only step makes
                # the run fail, so a trusted failure is valid adverse evidence.
                conclusion = str(run.get("conclusion") or "")
                if verdict == "PASS" and conclusion != "success":
                    continue
                if verdict != "PASS" and conclusion != "failure":
                    continue
                candidates.append((
                    str(run.get("created_at") or comment.get("created_at") or ""),
                    int(run_id),
                    verdict,
                    run,
                ))
            if len(comments) < 100:
                break
            if page == MAX_PAGES:
                return False
        if not candidates:
            return False
        _created, _run_id, latest_verdict, _run = max(
            candidates, key=lambda item: (item[0], item[1])
        )
        return latest_verdict == "PASS"
    except Exception:
        return False

