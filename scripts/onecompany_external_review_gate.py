#!/usr/bin/env python3
"""Verify OneCompany-hosted external Mistral evidence for Veritas."""
from __future__ import annotations

import io
import json
import os
import re
import urllib.error
import urllib.parse
import urllib.request
import zipfile

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



AUTO_MARKER = re.compile(r"<!-- ONECOMPANY_L4_AUTO_DISPATCH_V1 run=([1-9][0-9]{0,19}) -->")
DISPATCH_WORKFLOW = ".github/workflows/onecompany-mistral-external-dispatch.yml"
DISPATCH_WORKFLOW_NAME = "OneCompany L4 External Review Auto Dispatch"
PROOF_ARTIFACT_NAME = "onecompany-external-dispatch-proof"
PROOF_FILE = "onecompany-external-dispatch-proof.json"
MAX_PROOF_BYTES = 1_000_000


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def _download_host_proof(artifact_id: int) -> bytes | None:
    """OneCompany Actions:read only. Never forward its token to signed storage."""
    token = os.environ.get("ONECOMPANY_ACTIONS_READ_TOKEN", "")
    if not token or not isinstance(artifact_id, int) or artifact_id < 1:
        return None
    opener = urllib.request.build_opener(_NoRedirect)
    endpoint = (
        f"https://api.github.com/repos/{HOST_REPO}/actions/artifacts/"
        f"{artifact_id}/zip"
    )
    request = urllib.request.Request(endpoint, headers={
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "User-Agent": "veritas-trusted-evidence-reader",
    })
    try:
        with opener.open(request, timeout=20) as response:
            # No redirect (e.g. an API returning the archive directly).
            return response.read(MAX_PROOF_BYTES + 1)
    except urllib.error.HTTPError as response:
        if response.code not in (302, 303, 307, 308):
            return None
        location = response.headers.get("Location") or ""
    except (OSError, ValueError):
        return None

    signed = urllib.parse.urlsplit(location)
    host = (signed.hostname or "").lower()
    allowed = (
        host.endswith(".blob.core.windows.net")
        or host.endswith(".actions.githubusercontent.com")
        or host == "pipelines.actions.githubusercontent.com"
    )
    if not (
        signed.scheme == "https" and allowed and signed.username is None
        and signed.password is None and signed.port in (None, 443)
    ):
        return None
    # The host-specific bearer is deliberately NOT included on this request.
    unsigned = urllib.request.Request(
        location, headers={"User-Agent": "veritas-trusted-evidence-reader"}
    )
    try:
        with urllib.request.urlopen(unsigned, timeout=20) as response:
            return response.read(MAX_PROOF_BYTES + 1)
    except (OSError, ValueError):
        return None


def _reject_duplicate_proof_keys(pairs: list[tuple[str, object]]) -> dict:
    """Reject ambiguous JSON proof fields, including identical duplicates."""
    fields = {}
    for name, value in pairs:
        if name in fields:
            raise ValueError("DUPLICATE_PROOF_KEY")
        fields[name] = value
    return fields


def _trusted_auto_dispatch(
    comment: dict, *, dispatch_id: int, repo: str, pr: int,
    head: str, base: str,
) -> bool:
    """Authenticate a bot-authored source against the completed main run and
    run-scoped immutable source artifact. Never trust the source marker alone.
    """
    try:
        body = str(comment.get("body") or "")
        markers = AUTO_MARKER.findall(body)
        if len(markers) != 1:
            return False
        run_id = int(markers[0])
        run = public_json(f"repos/{HOST_REPO}/actions/runs/{run_id}")
        path, sep, ref = str(run.get("path") or "").partition("@")
        if not (
            run.get("id") == run_id
            and run.get("name") == DISPATCH_WORKFLOW_NAME
            and path == DISPATCH_WORKFLOW
            and (not sep or ref == "main")
            and run.get("head_branch") == "main"
            and run.get("event") in {"schedule", "workflow_dispatch"}
            and run.get("status") == "completed"
            and run.get("conclusion") == "success"
            and (run.get("head_repository") or {}).get("full_name") == HOST_REPO
            and str(run.get("created_at") or "") <= str(comment.get("created_at") or "")
        ):
            return False
        artifacts = public_json(
            f"repos/{HOST_REPO}/actions/runs/{run_id}/artifacts?per_page=100"
        )
        matches = [
            item for item in artifacts.get("artifacts", [])
            if isinstance(item, dict)
            and item.get("name") == PROOF_ARTIFACT_NAME
            and item.get("expired") is False
            and type(item.get("id")) is int and item["id"] > 0
        ]
        if len(matches) != 1:
            return False
        raw = _download_host_proof(matches[0]["id"])
        if raw is None or not 0 < len(raw) <= MAX_PROOF_BYTES:
            return False
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            if archive.namelist() != [PROOF_FILE]:
                return False
            if archive.getinfo(PROOF_FILE).file_size > MAX_PROOF_BYTES:
                return False
            proof = json.loads(archive.read(PROOF_FILE).decode("utf-8"), object_pairs_hook=_reject_duplicate_proof_keys)
        if (
            type(proof) is not dict
            or proof.get("version") != 1
            or proof.get("run_id") != run_id
            or type(proof.get("sources")) is not list
            or not 1 <= len(proof["sources"]) <= 6
        ):
            return False
        expected = {
            "comment_id": dispatch_id,
            "body": body,
            "repo": repo,
            "pr": pr,
            "head": head,
            "base": base,
        }
        return sum(source == expected for source in proof["sources"]) == 1
    except (OSError, ValueError, TypeError, KeyError, UnicodeError,
            zipfile.BadZipFile, json.JSONDecodeError):
        return False


def _trusted_run(run_id: int, dispatch_id: int, *, expected_event: str) -> dict | None:
    run = public_json(f"repos/{HOST_REPO}/actions/runs/{run_id}")
    workflow_path = str(run.get("path") or "")
    workflow_file, separator, workflow_ref = workflow_path.partition("@")
    if not (
        run.get("name") == WORKFLOW_NAME
        and run.get("display_title") == f"External Mistral review dispatch {dispatch_id}"
        and workflow_file == WORKFLOW_PATH
        and (not separator or workflow_ref == "main")
        and run.get("head_branch") == "main"
        and run.get("event") == expected_event
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
        comment.get("created_at") != comment.get("updated_at")
        or not str(comment.get("issue_url") or "").endswith(f"/repos/{HOST_REPO}/issues/{HOST_ISSUE}")
    ):
        return None
    actor = (comment.get("user") or {}).get("login")
    body = str(comment.get("body") or "")
    if actor == "NTinkicht":
        # Owner-triggered legacy review cannot claim auto-dispatch identity.
        if AUTO_MARKER.search(body):
            return None
    elif actor == "github-actions[bot]":
        if not _trusted_auto_dispatch(
            comment, dispatch_id=dispatch_id, repo=repo, pr=pr,
            head=head, base=base,
        ):
            return None
    else:
        return None
    try:
        d_repo, d_pr, d_head, d_base, d_authors = parse_dispatch(str(comment.get("body") or ""))
    except (ValueError, TypeError):
        return None
    if (
        d_repo != repo or d_pr != pr or d_head != head or d_base != base
        or d_authors != frozenset(authors)
    ):
        return None
    return comment


def external_mistral_status(
    *,
    repo: str,
    pr: int,
    head: str,
    base: str,
    authors: set[str] | frozenset[str],
) -> str:
    """Return the latest trusted exact-target verdict or a fail-closed status.

    PASS is the only merge-satisfying value. CHANGES_REQUIRED and
    INSUFFICIENT_EVIDENCE remain adverse. MISSING means no authenticated exact
    target verdict exists yet; INVALID_TARGET means the caller supplied an
    unsafe or self-authored target; UNAVAILABLE means trusted evidence could not
    be reconciled from GitHub.
    """
    normalized_authors = {str(actor).strip().lower() for actor in authors}
    if (
        not SHA.fullmatch(head)
        or not SHA.fullmatch(base)
        or head == base
        or not normalized_authors
        or normalized_authors & MISTRAL_ALIASES
    ):
        return "INVALID_TARGET"
    prefix = f"repo={repo} pr={pr} head={head} base={base} "
    candidates: list[tuple[str, int, str]] = []
    try:
        for page in range(1, MAX_PAGES + 1):
            comments = public_json(
                f"repos/{HOST_REPO}/issues/{HOST_ISSUE}/comments?per_page=100&page={page}"
            )
            if not isinstance(comments, list):
                return "UNAVAILABLE"
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
                    f"repo={marker_repo} pr={marker_pr} head={marker_head} base={marker_base} "
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
                run = _trusted_run(
                    int(run_id), int(dispatch_id),
                    expected_event=(
                        "repository_dispatch"
                        if (dispatch.get("user") or {}).get("login") == "github-actions[bot]"
                        else "issue_comment"
                    ),
                )
                if run is None:
                    continue
                conclusion = str(run.get("conclusion") or "")
                if verdict == "PASS" and conclusion != "success":
                    continue
                if verdict != "PASS" and conclusion != "failure":
                    continue
                candidates.append((
                    str(run.get("created_at") or comment.get("created_at") or ""),
                    int(run_id),
                    verdict,
                ))
            if len(comments) < 100:
                break
            if page == MAX_PAGES:
                return "UNAVAILABLE"
        if not candidates:
            return "MISSING"
        _created, _run_id, latest_verdict = max(candidates, key=lambda item: (item[0], item[1]))
        return latest_verdict
    except Exception:
        return "UNAVAILABLE"


def external_mistral_pass(
    *,
    repo: str,
    pr: int,
    head: str,
    base: str,
    authors: set[str] | frozenset[str],
) -> bool:
    """Backward-compatible merge gate: only an authenticated PASS is true."""
    return external_mistral_status(
        repo=repo,
        pr=pr,
        head=head,
        base=base,
        authors=authors,
    ) == "PASS"
