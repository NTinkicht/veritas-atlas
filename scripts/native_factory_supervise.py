#!/usr/bin/env python3
"""Read-only L4 backlog/PR reconciler for Veritas Atlas.

This runtime never mutates GitHub. It proves that continuous-flow state can be
reconstructed from live GitHub and deterministically identifies the next
explicitly dependency-ready issue. The authorized ChatGPT scheduled supervisors
remain the mutating implementation/CI/review/merge layer.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess

READY_LABEL = "l4-ready"
BLOCKING_LABELS = frozenset({"l4-blocked", "human-only", "release-go-no-go"})
MAX_PAGES = 10


def gh(path: str):
    result = subprocess.run(
        ["gh", "api", path],
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or f"GitHub API failed: {path}")
    return json.loads(result.stdout) if result.stdout.strip() else {}


def paged(repo: str, path: str):
    items = []
    for page in range(1, MAX_PAGES + 1):
        sep = "&" if "?" in path else "?"
        batch = gh(f"repos/{repo}/{path}{sep}per_page=100&page={page}")
        if not isinstance(batch, list):
            raise RuntimeError("PAGINATED_RESPONSE_INVALID")
        items.extend(batch)
        if len(batch) < 100:
            return items
    raise RuntimeError("PAGINATION_BOUND_EXCEEDED")


def labels(issue: dict) -> set[str]:
    result = set()
    for label in issue.get("labels") or []:
        if isinstance(label, dict) and isinstance(label.get("name"), str):
            result.add(label["name"].lower())
        elif isinstance(label, str):
            result.add(label.lower())
    return result


def select_ready_issue(issues: list[dict]) -> dict | None:
    candidates = []
    for issue in issues:
        if issue.get("pull_request") is not None or issue.get("state") != "open":
            continue
        names = labels(issue)
        if READY_LABEL not in names or names & BLOCKING_LABELS:
            continue
        if not isinstance(issue.get("number"), int):
            continue
        candidates.append(issue)
    return min(candidates, key=lambda row: row["number"]) if candidates else None


def active_internal_prs(pulls: list[dict], repo: str) -> list[dict]:
    """Return same-repository implementation PRs targeting main, including drafts."""
    result = []
    for pr in pulls:
        head = pr.get("head") or {}
        head_repo = head.get("repo") or {}
        if (
            (pr.get("base") or {}).get("ref") == "main"
            and head_repo.get("full_name") == repo
        ):
            result.append(pr)
    return result


def reconcile(repo: str) -> dict:
    pulls = active_internal_prs(paged(repo, "pulls?state=open"), repo)
    if pulls:
        return {
            "action": "RECONCILE_OPEN_PRS",
            "repository": repo,
            "open_prs": sorted(int(pr["number"]) for pr in pulls),
            "mutation_authorized_here": False,
        }

    issues = paged(repo, "issues?state=open")
    ready = select_ready_issue(issues)
    if ready is not None:
        return {
            "action": "START_READY_WORK",
            "repository": repo,
            "issue": ready["number"],
            "title": ready.get("title"),
            "selection_basis": READY_LABEL,
            "mutation_authorized_here": False,
        }

    backlog = [
        int(issue["number"])
        for issue in issues
        if issue.get("pull_request") is None
        and issue.get("state") == "open"
        and isinstance(issue.get("number"), int)
    ]
    return {
        "action": "IDLE_BACKLOG_NOT_READY" if backlog else "IDLE_NO_BACKLOG",
        "repository": repo,
        "open_backlog": sorted(backlog),
        "mutation_authorized_here": False,
    }


def selftest() -> None:
    ready = {
        "number": 7,
        "state": "open",
        "title": "ready",
        "labels": [{"name": READY_LABEL}],
    }
    blocked = {
        "number": 3,
        "state": "open",
        "title": "blocked",
        "labels": [{"name": READY_LABEL}, {"name": "human-only"}],
    }
    ordinary = {
        "number": 2,
        "state": "open",
        "title": "ordinary",
        "labels": [],
    }
    assert select_ready_issue([blocked, ordinary, ready])["number"] == 7
    assert select_ready_issue([blocked, ordinary]) is None

    draft_internal = {
        "number": 9,
        "draft": True,
        "base": {"ref": "main"},
        "head": {"repo": {"full_name": "NTinkicht/veritas-atlas"}},
    }
    fork = {
        "number": 10,
        "draft": False,
        "base": {"ref": "main"},
        "head": {"repo": {"full_name": "someone/fork"}},
    }
    deleted_fork = {
        "number": 11,
        "draft": False,
        "base": {"ref": "main"},
        "head": {"repo": None},
    }
    other_base = {
        "number": 12,
        "draft": False,
        "base": {"ref": "release"},
        "head": {"repo": {"full_name": "NTinkicht/veritas-atlas"}},
    }
    active = active_internal_prs(
        [draft_internal, fork, deleted_fork, other_base],
        "NTinkicht/veritas-atlas",
    )
    assert [pr["number"] for pr in active] == [9]
    print("native_factory_supervise selftest PASS")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        selftest()
        return 0
    repo = os.environ.get("GITHUB_REPOSITORY")
    if not repo:
        print("SUPERVISION_BLOCKED: GITHUB_REPOSITORY missing")
        return 2
    try:
        print(json.dumps(reconcile(repo), indent=2, sort_keys=True))
        return 0
    except Exception as exc:
        print(f"SUPERVISION_BLOCKED: {exc}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
