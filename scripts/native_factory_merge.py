#!/usr/bin/env python3
import json
import os
import subprocess
from pathlib import Path

REPO = os.environ["GITHUB_REPOSITORY"]
OWNER, NAME = REPO.split("/", 1)
EVENT = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
MAX_PAGES = 10
CI_WORKFLOW_NAME = "Veritas Atlas CI"
CI_WORKFLOW_PATH = ".github/workflows/ci.yml"
TRUSTED_CONTROL_PATHS = frozenset({
    CI_WORKFLOW_PATH,
    ".github/workflows/native-factory-merge-controller.yml",
    "scripts/native_factory_merge.py",
})
REQUIRED_JOBS = frozenset({
    "Runner availability diagnostic",
    "Backend build, tests and dependency audit",
    "Frontend build, lint and dependency audit",
    "Backend Docker image build",
    "Staging migration SQL (review only)",
    "Staging HTTP smoke (unauthenticated)",
})
AUTHORIZED_REVIEWERS = frozenset({
    "coderabbitai[bot]",
    "chatgpt-codex-connector[bot]",
})


def gh(path, method=None, fields=None):
    command = ["gh", "api"]
    if method:
        command += ["-X", method]
    if fields:
        for key, value in fields.items():
            command += ["-f", f"{key}={value}"]
    command.append(path)
    result = subprocess.run(command, text=True, capture_output=True, check=False)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or f"GitHub API failed: {path}")
    return json.loads(result.stdout) if result.stdout.strip() else {}


def paged(path):
    items = []
    for page in range(1, MAX_PAGES + 1):
        sep = "&" if "?" in path else "?"
        batch = gh(f"{path}{sep}per_page=100&page={page}")
        if not isinstance(batch, list):
            raise RuntimeError("PAGINATED_RESPONSE_INVALID")
        items.extend(batch)
        if len(batch) < 100:
            return items
    raise RuntimeError("PAGINATION_BOUND_EXCEEDED")


def changed_paths(number):
    return {
        item["filename"]
        for item in paged(f"repos/{REPO}/pulls/{number}/files")
        if isinstance(item, dict) and isinstance(item.get("filename"), str)
    }


def latest_ci_run(sha):
    payload = gh(
        f"repos/{REPO}/actions/runs?head_sha={sha}&event=pull_request&per_page=100"
    )
    runs = [
        run for run in payload.get("workflow_runs", [])
        if run.get("head_sha") == sha
        and run.get("event") == "pull_request"
        and run.get("name") == CI_WORKFLOW_NAME
        and run.get("path") == CI_WORKFLOW_PATH
    ]
    if not runs:
        return None
    return max(
        runs,
        key=lambda run: (
            run.get("run_number") or 0,
            run.get("run_attempt") or 0,
            run.get("id") or 0,
        ),
    )


def latest_ci_green(sha):
    run = latest_ci_run(sha)
    if (
        not run
        or run.get("status") != "completed"
        or run.get("conclusion") != "success"
    ):
        return False
    jobs = gh(
        f"repos/{REPO}/actions/runs/{run['id']}/jobs?filter=latest&per_page=100"
    ).get("jobs", [])
    latest = {}
    for job in jobs:
        name = job.get("name")
        if name in REQUIRED_JOBS:
            current = latest.get(name)
            if current is None or (job.get("id") or 0) > (current.get("id") or 0):
                latest[name] = job
    return all(
        latest.get(name, {}).get("status") == "completed"
        and latest.get(name, {}).get("conclusion") == "success"
        for name in REQUIRED_JOBS
    )


def latest_review_decisions(number, sha):
    reviews = paged(f"repos/{REPO}/pulls/{number}/reviews")
    latest = {}
    for review in reviews:
        if review.get("commit_id") != sha:
            continue
        login = (review.get("user") or {}).get("login", "").lower()
        if not login:
            continue
        key = (
            review.get("submitted_at") or "",
            review.get("id") or 0,
        )
        previous = latest.get(login)
        if previous is None or key > previous[0]:
            latest[login] = (key, review)
    return {login: item[1] for login, item in latest.items()}


def review_gate_clean(number, sha):
    decisions = latest_review_decisions(number, sha)
    approved = any(
        login in AUTHORIZED_REVIEWERS and review.get("state") == "APPROVED"
        for login, review in decisions.items()
    )
    adverse = any(
        login in AUTHORIZED_REVIEWERS and review.get("state") == "CHANGES_REQUESTED"
        for login, review in decisions.items()
    )
    return approved and not adverse


def has_unresolved_threads(number):
    cursor = None
    for _ in range(MAX_PAGES):
        query = """query($owner:String!,$name:String!,$number:Int!,$after:String){
          repository(owner:$owner,name:$name){
            pullRequest(number:$number){
              reviewThreads(first:100,after:$after){
                nodes{isResolved}
                pageInfo{hasNextPage endCursor}
              }
            }
          }
        }"""
        command = [
            "gh", "api", "graphql",
            "-F", f"owner={OWNER}", "-F", f"name={NAME}",
            "-F", f"number={number}", "-f", f"query={query}",
        ]
        if cursor:
            command += ["-F", f"after={cursor}"]
        result = subprocess.run(command, text=True, capture_output=True, check=False)
        if result.returncode:
            raise RuntimeError("REVIEW_THREAD_RECONCILIATION_UNAVAILABLE")
        connection = json.loads(result.stdout)["data"]["repository"]["pullRequest"]["reviewThreads"]
        if any(not node.get("isResolved") for node in connection["nodes"]):
            return True
        page = connection["pageInfo"]
        if not page.get("hasNextPage"):
            return False
        cursor = page.get("endCursor")
        if not cursor:
            raise RuntimeError("REVIEW_THREAD_CURSOR_MISSING")
    raise RuntimeError("REVIEW_THREAD_PAGE_BOUND_EXCEEDED")


def candidates():
    if os.environ["GITHUB_EVENT_NAME"] == "pull_request_review":
        number = EVENT.get("pull_request", {}).get("number")
        return [int(number)] if number else []
    pulls = paged(f"repos/{REPO}/pulls?state=open")
    return [
        int(pr["number"])
        for pr in pulls
        if not pr.get("draft")
        and pr.get("base", {}).get("ref") == "main"
        and pr.get("head", {}).get("repo", {}).get("full_name") == REPO
    ]


def gates(number):
    pr = gh(f"repos/{REPO}/pulls/{number}")
    if pr.get("state") != "open" or pr.get("draft"):
        return None
    if pr.get("base", {}).get("ref") != "main":
        return None
    if pr.get("head", {}).get("repo", {}).get("full_name") != REPO:
        return None

    sha = pr["head"]["sha"]
    if changed_paths(number) & TRUSTED_CONTROL_PATHS:
        print(f"PR #{number}: TRUSTED_CONTROL_CHANGE_REQUIRES_EXTERNAL_MERGE")
        return None
    if not latest_ci_green(sha):
        print(f"PR #{number}: LATEST_EXACT_HEAD_CI_NOT_GREEN")
        return None
    if not review_gate_clean(number, sha):
        print(f"PR #{number}: AUTHORIZED_EXACT_HEAD_REVIEW_NOT_CLEAN")
        return None
    if has_unresolved_threads(number):
        print(f"PR #{number}: UNRESOLVED_REVIEW_THREADS")
        return None
    return pr, sha


for number in candidates():
    first = gates(number)
    if not first:
        continue
    _, sha = first

    pr = gh(f"repos/{REPO}/pulls/{number}")
    if (
        pr.get("state") != "open"
        or pr.get("head", {}).get("sha") != sha
        or pr.get("mergeable") is not True
    ):
        print(f"PR #{number}: HEAD_MOVED_OR_NOT_MERGEABLE")
        continue

    # Repeat every mutable review/CI predicate immediately before the
    # expected-head merge call.
    if not latest_ci_green(sha):
        print(f"PR #{number}: FINAL_CI_RECHECK_BLOCKED")
        continue
    if not review_gate_clean(number, sha) or has_unresolved_threads(number):
        print(f"PR #{number}: FINAL_REVIEW_RECHECK_BLOCKED")
        continue

    merged = gh(
        f"repos/{REPO}/pulls/{number}/merge",
        method="PUT",
        fields={"sha": sha, "merge_method": "squash"},
    )
    if not merged.get("merged"):
        raise RuntimeError(f"MERGE_REJECTED PR #{number}")
    print(f"NATIVE_FACTORY_MERGED PR #{number} exact head {sha}")
