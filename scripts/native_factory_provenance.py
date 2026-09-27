#!/usr/bin/env python3
"""Pure authenticated material-author provenance policy for native L4 merge."""
from __future__ import annotations

import re

MATERIAL_AUTHOR = re.compile(r"(?im)^Material-Author:\s*([^\n]+)$")
OWNER_ATTESTATION = re.compile(
    r"^L4-MATERIAL-AUTHORS: sha=([0-9a-f]{40}) actors=([a-z0-9_-]+(?:,[a-z0-9_-]+)*)$"
)
AMBIGUOUS_TRANSPORT_LOGINS = frozenset({"ntinkicht", "web-flow"})


def actor(login: str, aliases: dict[str, str]) -> str:
    value = login.lower()
    return aliases.get(value, value)



def complete_pr_commit_history(commits: list[dict], expected_count: object) -> bool:
    """Accept PR commit provenance only when the GitHub list is provably complete."""
    return (
        type(expected_count) is int
        and 1 <= expected_count <= 250
        and len(commits) == expected_count
    )


def authenticated_material_actors(
    commits: list[dict],
    *,
    aliases: dict[str, str],
    ambiguous_transport_logins: frozenset[str] = AMBIGUOUS_TRANSPORT_LOGINS,
) -> set[str]:
    """Return authenticated material actors, or empty when provenance is ambiguous."""
    if not commits:
        return set()

    authors: set[str] = set()
    for commit in commits:
        verification = ((commit.get("commit") or {}).get("verification") or {})
        if verification.get("verified") is not True:
            return set()

        raw_logins: set[str] = set()
        for field in ("author", "committer"):
            login = (commit.get(field) or {}).get("login")
            if isinstance(login, str) and login:
                raw_logins.add(login.lower())
        if not raw_logins or raw_logins & ambiguous_transport_logins:
            return set()

        platform_actors = {actor(login, aliases) for login in raw_logins}
        message = ((commit.get("commit") or {}).get("message") or "")
        trailers = MATERIAL_AUTHOR.findall(message)
        if len(trailers) != 1:
            return set()
        declared = {
            actor(item.strip(), aliases)
            for item in trailers[0].split(",")
            if item.strip()
        }
        if not declared or not declared.issubset(platform_actors):
            return set()

        authors.update(platform_actors)
        authors.update(declared)

    return authors



def owner_attestation_allowed(
    commits: list[dict],
    *,
    ambiguous_transport_logins: frozenset[str] = AMBIGUOUS_TRANSPORT_LOGINS,
) -> bool:
    """Allow fallback only when authenticated actor identity cannot be derived.

    An exact-head owner attestation may compensate for unsigned or transport-
    ambiguous commits. It must never override malformed provenance on a commit
    whose platform identity is already verified and unambiguous.
    """
    if not commits:
        return False
    for commit in commits:
        verification = ((commit.get("commit") or {}).get("verification") or {})
        raw_logins: set[str] = set()
        for field in ("author", "committer"):
            login = (commit.get(field) or {}).get("login")
            if isinstance(login, str) and login:
                raw_logins.add(login.lower())

        transport_ambiguous = (
            verification.get("verified") is not True
            or not raw_logins
            or bool(raw_logins & ambiguous_transport_logins)
        )
        if transport_ambiguous:
            continue

        # Verified + unambiguous transport means candidate provenance must stand
        # on its own. Missing/duplicated/mismatched Material-Author data is a
        # hard provenance failure and cannot be repaired by an owner comment.
        return False
    return True

def attested_material_actors(
    comments: list[dict],
    *,
    sha: str,
    aliases: dict[str, str],
    owner_login: str = "ntinkicht",
) -> set[str]:
    """Use immutable owner-authenticated exact-head authorship when Git commits are ambiguous.

    The attestation lives outside the candidate branch. It is valid only for one
    exact head SHA; edits invalidate it, and conflicting attestations fail closed.
    """
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        return set()
    candidates: set[tuple[str, ...]] = set()
    for comment in comments:
        login = ((comment.get("user") or {}).get("login") or "").lower()
        if login != owner_login.lower():
            continue
        created = comment.get("created_at")
        updated = comment.get("updated_at")
        if not created or created != updated:
            continue
        body = str(comment.get("body") or "").strip()
        match = OWNER_ATTESTATION.fullmatch(body)
        if not match or match.group(1) != sha:
            continue
        actors = tuple(sorted({
            actor(item, aliases)
            for item in match.group(2).split(",")
            if item
        }))
        if actors:
            candidates.add(actors)
    if len(candidates) != 1:
        return set()
    return set(next(iter(candidates)))
