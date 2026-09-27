#!/usr/bin/env python3
import json
import os
import subprocess
from pathlib import Path

from native_factory_provenance import (
    attested_material_actors,
    authenticated_material_actors,
    complete_pr_commit_history,
    owner_attestation_allowed,
)
from native_factory_review_policy import unresolved_substantive_findings as reconcile_findings
from native_factory_ruleset_policy import strict_ruleset_enforces
from onecompany_external_review_gate import external_mistral_pass

REPO = os.environ["GITHUB_REPOSITORY"]
OWNER, NAME = REPO.split("/", 1)
EVENT = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
MAX_PAGES = 10
CI_WORKFLOW_NAME = "Veritas Atlas CI"
CI_WORKFLOW_PATH = ".github/workflows/ci.yml"
TRUSTED_CONTROL_PATHS = frozenset({
    "scripts/native_factory_merge.py",
    "scripts/native_factory_supervise.py",
    "scripts/native_factory_provenance.py",
    "scripts/native_factory_review_policy.py",
    "scripts/native_factory_ruleset_policy.py",
    "scripts/onecompany_external_review_gate.py",
    "tests/test_native_factory_provenance.py",
    "tests/test_native_factory_review_policy.py",
    "tests/test_native_factory_ruleset_policy.py",
    "tests/test_onecompany_external_review_gate.py",
    "docs/operations/AUTONOMY_POLICY.md",
    "docs/operations/autonomy-policy.json",
    "docs/operations/onecompany-adoption/docs/VERITAS-H8-RELEASE-RUNBOOK.md",
    "docs/FIRST_RENDER_STAGING_DEPLOYMENT.md",
})
TRUSTED_CONTROL_PREFIXES = (
    ".github/workflows/",
    "scripts/",
    "docs/operations/onecompany-adoption/docs/VERITAS-H",
)
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

# Canonical material-actor vocabulary. ChatGPT and Codex are distinct actors;
# the GitHub Codex connector reviews as actor=codex. Commit trailers and
# platform logins are normalized through the same table before comparison.
ACTOR_ALIASES = {
    "coderabbitai[bot]": "coderabbit",
    "coderabbit": "coderabbit",
    "chatgpt-codex-connector[bot]": "codex",
    "chatgpt-codex-connector": "codex",
    "codex": "codex",
    "chatgpt": "chatgpt",
}
FINDING_COMMENT_AUTHORS = AUTHORIZED_REVIEWERS | frozenset({
    "ntinkicht",
    "github-actions[bot]",
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
    paths = set()
    for item in paged(f"repos/{REPO}/pulls/{number}/files"):
        if not isinstance(item, dict):
            continue
        for field in ("filename", "previous_filename"):
            value = item.get(field)
            if isinstance(value, str) and value:
                paths.add(value)
    return paths


def latest_ci_run(number, sha, base_sha):
    payload = gh(
        f"repos/{REPO}/actions/runs?head_sha={sha}&event=pull_request&per_page=100"
    )
    runs = [
        run for run in payload.get("workflow_runs", [])
        if run.get("head_sha") == sha
        and run.get("event") == "pull_request"
        and run.get("name") == CI_WORKFLOW_NAME
        and run.get("path") == CI_WORKFLOW_PATH
        and any(
            isinstance(pr, dict)
            and pr.get("number") == number
            and (pr.get("base") or {}).get("ref") == "main"
            and (pr.get("base") or {}).get("sha") == base_sha
            and (pr.get("head") or {}).get("sha") == sha
            for pr in (run.get("pull_requests") or [])
        )
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


def latest_ci_green(number, sha, base_sha):
    run = latest_ci_run(number, sha, base_sha)
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


def material_authors(number, sha):
    """Return material actors only from a complete PR commit history."""
    pr = gh(f"repos/{REPO}/pulls/{number}")
    expected = pr.get("commits")
    commits = paged(f"repos/{REPO}/pulls/{number}/commits")
    # GitHub caps this endpoint at 250 commits; compare against the PR's
    # authoritative count and reject any truncated or oversized history.
    if not complete_pr_commit_history(commits, expected):
        return set()
    authors = authenticated_material_actors(commits, aliases=ACTOR_ALIASES)
    if authors:
        return authors
    if not owner_attestation_allowed(commits):
        return set()
    return attested_material_actors(
        paged(f"repos/{REPO}/issues/{number}/comments"),
        sha=sha,
        aliases=ACTOR_ALIASES,
    )

def reviewer_actor(login):
    return ACTOR_ALIASES.get(login, login)


def trusted_control_change(paths):
    return any(
        path in TRUSTED_CONTROL_PATHS
        or any(path.startswith(prefix) for prefix in TRUSTED_CONTROL_PREFIXES)
        for path in paths
    )


def unresolved_substantive_findings(number, sha):
    """Return trusted Medium/P2+ findings not reconciled against exact content."""
    return reconcile_findings(
        reviews=paged(f"repos/{REPO}/pulls/{number}/reviews"),
        issue_comments=paged(f"repos/{REPO}/issues/{number}/comments"),
        inline_comments=paged(f"repos/{REPO}/pulls/{number}/comments"),
        sha=sha,
        finding_comment_authors=FINDING_COMMENT_AUTHORS,
    )

def latest_review_decisions(number):
    reviews = paged(f"repos/{REPO}/pulls/{number}/reviews")
    latest = {}
    for review in reviews:
        state = str(review.get("state") or "").upper()
        if state not in {"APPROVED", "CHANGES_REQUESTED", "DISMISSED"}:
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


def review_gate_clean(number, sha, base_sha):
    decisions = latest_review_decisions(number)
    authors = material_authors(number, sha)
    if not authors:
        return False

    eligible_approvals = []
    for login, review in decisions.items():
        if (
            login in AUTHORIZED_REVIEWERS
            and review.get("state") == "APPROVED"
            and review.get("commit_id") == sha
            and reviewer_actor(login) not in authors
        ):
            eligible_approvals.append(review.get("id"))

    adverse = any(
        review.get("state") == "CHANGES_REQUESTED"
        for review in decisions.values()
    )
    if adverse:
        return False

    # A valid native approval is sufficient. Only invoke the external failover
    # when the native lane did not produce an eligible exact-head approval.
    if not eligible_approvals:
        if not external_mistral_pass(
            repo=REPO,
            pr=number,
            head=sha,
            base=base_sha,
            authors=authors,
        ):
            return False

    if unresolved_substantive_findings(number, sha):
        return False
    return True


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


def strict_base_enforcement():
    """Require platform-enforced strict base synchronization before merge.

    Prefer classic protection when readable. If the Actions token cannot read
    that admin endpoint, accept only a readable ACTIVE repository ruleset that
    targets main, has no bypass actors, requires PRs, prevents deletion/force
    pushes, and requires every deterministic job with strict status-check policy.
    """
    try:
        protection = gh(f"repos/{REPO}/branches/main/protection")
        checks = protection.get("required_status_checks")
        if isinstance(checks, dict) and checks.get("strict") is True:
            return True
    except RuntimeError:
        pass

    try:
        repository = gh(f"repos/{REPO}")
        default_branch = repository.get("default_branch")
        if default_branch != "main":
            return False
        summaries = gh(f"repos/{REPO}/rulesets")
    except RuntimeError:
        return False
    if not isinstance(summaries, list):
        return False
    for summary in summaries:
        if (
            not isinstance(summary, dict)
            or summary.get("enforcement") != "active"
            or not summary.get("id")
        ):
            continue
        try:
            detail = gh(f"repos/{REPO}/rulesets/{summary['id']}")
        except RuntimeError:
            continue
        if strict_ruleset_enforces(
            detail,
            branch="main",
            required_checks=REQUIRED_JOBS,
            default_branch=default_branch,
        ):
            return True
    return False


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
    base_sha = pr.get("base", {}).get("sha")
    if not isinstance(base_sha, str) or len(base_sha) != 40:
        print(f"PR #{number}: BASE_SHA_UNAVAILABLE")
        return None
    if trusted_control_change(changed_paths(number)):
        print(f"PR #{number}: TRUSTED_CONTROL_CHANGE_REQUIRES_EXTERNAL_MERGE")
        return None
    if not strict_base_enforcement():
        print(f"PR #{number}: STRICT_BASE_PROTECTION_NOT_VERIFIED")
        return None
    if not latest_ci_green(number, sha, base_sha):
        print(f"PR #{number}: LATEST_EXACT_HEAD_CI_NOT_GREEN")
        return None
    if not review_gate_clean(number, sha, base_sha):
        print(f"PR #{number}: AUTHORIZED_EXACT_HEAD_REVIEW_NOT_CLEAN")
        return None
    if has_unresolved_threads(number):
        print(f"PR #{number}: UNRESOLVED_REVIEW_THREADS")
        return None
    return pr, sha, base_sha


for number in candidates():
    first = gates(number)
    if not first:
        continue
    _, sha, base_sha = first

    pr = gh(f"repos/{REPO}/pulls/{number}")
    if (
        pr.get("state") != "open"
        or pr.get("head", {}).get("sha") != sha
        or pr.get("base", {}).get("sha") != base_sha
        or pr.get("mergeable") is not True
    ):
        print(f"PR #{number}: HEAD_MOVED_OR_NOT_MERGEABLE")
        continue

    # Repeat every mutable review/CI predicate immediately before the
    # expected-head merge call.
    if not latest_ci_green(number, sha, base_sha):
        print(f"PR #{number}: FINAL_CI_RECHECK_BLOCKED")
        continue
    if not review_gate_clean(number, sha, base_sha) or has_unresolved_threads(number):
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
