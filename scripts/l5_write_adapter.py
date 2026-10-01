#!/usr/bin/env python3
"""Guarded L5 write adapter for Veritas Atlas."""
from __future__ import annotations

import fcntl
import json
import os
from pathlib import Path
from typing import Any

from l5_recovery import HARD_BOUNDARIES, MAX_RETRIES, RETRY_SCOPES, SAFE_MUTATIONS, SHA40, TOKEN64, _bool, authorize_mutation

RETRYABLE = frozenset({"retry_ci", "dispatch_review", "remediate_review"})
MUTATIONS = frozenset(SAFE_MUTATIONS.values())
KNOWN_RETRY_SCOPES = frozenset(RETRY_SCOPES.values())
REVIEW_SENSITIVE = frozenset({"dispatch_review", "merge_expected_head"})

class LostResponse(Exception):
    """Remote outcome is unknown because the response was lost."""
class AlreadyExists(Exception):
    """A create-style mutation raced with an existing remote object."""
class WriteRejected(Exception):
    """Remote endpoint definitively rejected the write without applying it."""

def stream_key(auth: dict[str, Any], snapshot: dict[str, Any]) -> str:
    return json.dumps([snapshot.get("repository"), auth.get("issue"), auth.get("canonical_pr")], separators=(",", ":"))

def _result(status: str, reason: str, token: Any = None) -> dict[str, Any]:
    return {"status": status, "reason": reason, "mutation_token": token, "written": False}

def _validate_authorization(auth: Any, snapshot: Any) -> str | None:
    if not isinstance(auth, dict) or auth.get("authorized") is not True or auth.get("mutation_allowed") is not True:
        return "NOT_AUTHORIZED"
    if auth.get("mutation") not in MUTATIONS:
        return "MUTATION_NOT_WHITELISTED"
    token = auth.get("mutation_token")
    if not isinstance(token, str) or not TOKEN64.fullmatch(token):
        return "TOKEN_INVALID"
    for key in ("expected_head_sha", "expected_base_sha"):
        value = auth.get(key)
        if not isinstance(value, str) or not SHA40.fullmatch(value):
            return "EXPECTED_REFS_INVALID"
    try:
        fresh = authorize_mutation(snapshot)
    except ValueError:
        return "AUTHORIZATION_NOT_REPRODUCIBLE"
    if fresh != auth:
        return "AUTHORIZATION_MISMATCH"
    if auth["mutation"] in RETRYABLE:
        count, scope = auth.get("retry_count_after"), auth.get("retry_action_after")
        if type(count) is not int or not 1 <= count <= MAX_RETRIES or scope not in KNOWN_RETRY_SCOPES:
            return "RETRY_STATE_INVALID"
    return None

def _live_gate(auth: dict[str, Any], client: Any, *, retry: tuple[int, str | None] | None = None) -> str | None:
    boundaries = client.fetch_boundaries()
    if not isinstance(boundaries, dict):
        return "BOUNDARY_STATE_UNKNOWN"
    try:
        if any(_bool(boundaries, key) for key in HARD_BOUNDARIES):
            return "HARD_BOUNDARY"
    except ValueError:
        return "BOUNDARY_STATE_UNKNOWN"
    live = client.fetch_live(auth["canonical_pr"])
    if not isinstance(live, dict):
        return "LIVE_STATE_UNKNOWN"
    if live.get("head_sha") != auth["expected_head_sha"] or live.get("base_sha") != auth["expected_base_sha"]:
        return "STALE_HEAD_OR_BASE"
    mutation = auth["mutation"]
    expected_state = "merged" if mutation == "reserve_next_wu" else "open"
    if live.get("pr_state") != expected_state:
        return "PR_STATE_MISMATCH"
    streams = live.get("open_streams")
    if not isinstance(streams, dict):
        return "LIVE_STATE_UNKNOWN"
    if mutation == "reserve_next_wu":
        if any(bool(prs) for prs in streams.values()):
            return "DUPLICATE_STREAM"
    elif streams.get(auth["issue"]) != [auth["canonical_pr"]] or sum(len(v) for v in streams.values()) != 1:
        return "DUPLICATE_STREAM"
    if mutation in REVIEW_SENSITIVE and live.get("review_eligible_nonauthor") is not True:
        return "REVIEWER_NOT_ELIGIBLE"
    if retry is not None and mutation in RETRYABLE:
        count, scope = retry
        expected_scope = auth.get("retry_action_after")
        expected_count = auth.get("retry_count_after")
        if (count if scope == expected_scope else 0) + 1 != expected_count:
            return "RETRY_STATE_STALE"
    return None

def _params(auth: dict[str, Any]) -> dict[str, Any]:
    return {"canonical_pr": auth["canonical_pr"], "issue": auth["issue"], "expected_head_sha": auth["expected_head_sha"], "expected_base_sha": auth["expected_base_sha"], "selected_issue": auth.get("selected_issue"), "idempotency_key": auth["mutation_token"]}

def _reconcile(auth: dict[str, Any], client: Any, store: Any, *, written: bool) -> dict[str, Any]:
    token = auth["mutation_token"]
    if client.verify_effect(auth["mutation"], _params(auth)) is True:
        store.set_status(token, "COMPLETE")
        return {**_result("COMPLETE", "EFFECT_VERIFIED", token), "written": written}
    return {**_result("IN_PROGRESS", "EFFECT_NOT_YET_VERIFIED", token), "written": written}

def execute_mutation(auth: dict[str, Any], snapshot: dict[str, Any], client: Any, store: Any) -> dict[str, Any]:
    reason = _validate_authorization(auth, snapshot)
    token = auth.get("mutation_token") if isinstance(auth, dict) else None
    if reason:
        return _result("BLOCKED", reason, token)
    stream = stream_key(auth, snapshot)
    prior = store.get(token)
    if prior is not None:
        status = prior.get("status")
        if status == "COMPLETE":
            return _result("REPLAY_NOOP", "ALREADY_COMPLETE", token)
        if status == "PENDING":
            return _reconcile(auth, client, store, written=False)
        if status != "RETRYABLE":
            return _result("BLOCKED", f"PRIOR_{status}", token)
    observed_retry = store.retry_state(stream) if auth["mutation"] in RETRYABLE else None
    block = _live_gate(auth, client, retry=observed_retry)
    if block:
        return _result("BLOCKED", block, token)
    perform_cas = getattr(client, "perform_cas", None)
    if not callable(perform_cas):
        return _result("BLOCKED", "ATOMIC_CAS_UNAVAILABLE", token)
    record = {"status": "PENDING", "mutation": auth["mutation"], "canonical_pr": auth["canonical_pr"], "expected_head_sha": auth["expected_head_sha"], "expected_base_sha": auth["expected_base_sha"]}
    if not store.begin(token, record, stream, auth.get("retry_count_after"), auth.get("retry_action_after"), expected_retry=observed_retry):
        existing = store.get(token)
        if existing is not None and existing.get("status") == "PENDING":
            return _result("REPLAY_NOOP", "TOKEN_ALREADY_PERSISTED", token)
        return _result("BLOCKED", "RETRY_STATE_STALE", token)
    try:
        block = _live_gate(auth, client)
    except Exception as exc:
        store.fail_and_restore(token, f"{type(exc).__name__}: {exc}", stream, observed_retry)
        return _result("FAILED", "UNEXPECTED_ERROR", token)
    if block:
        store.fail_and_restore(token, block, stream, observed_retry)
        return _result("BLOCKED", block, token)
    try:
        cas_result = perform_cas(auth["mutation"], _params(auth))
        if cas_result is False:
            raise WriteRejected("ATOMIC_CAS_REJECTED")
        if cas_result is not True:
            return _reconcile(auth, client, store, written=False)
    except WriteRejected as exc:
        store.fail_and_restore(token, str(exc), stream, observed_retry)
        return _result("FAILED", "WRITE_REJECTED", token)
    except Exception:
        return _reconcile(auth, client, store, written=False)
    return _reconcile(auth, client, store, written=True)

class MemoryStore:
    def __init__(self) -> None:
        self.records: dict[str, dict[str, Any]] = {}
        self.retry: dict[str, tuple[int, str | None]] = {}
    def get(self, token: str) -> dict[str, Any] | None:
        return dict(self.records[token]) if token in self.records else None
    def begin(self, token: str, record: dict[str, Any], stream: str, count: int | None, action: str | None, *, expected_retry: tuple[int, str | None] | None = None) -> bool:
        existing = self.records.get(token)
        if existing is not None and existing.get("status") != "RETRYABLE":
            return False
        if expected_retry is not None and self.retry.get(stream, (0, None)) != expected_retry:
            return False
        row = dict(record)
        if expected_retry is not None:
            row["retry_before"] = list(expected_retry)
        if count is not None:
            row["retry_written"] = [count, action]
        self.records[token] = row
        if count is not None:
            self.retry[stream] = (count, action)
        return True
    def fail_and_restore(self, token: str, detail: Any, stream: str, prior_retry: tuple[int, str | None] | None) -> None:
        row = self.records[token]
        row["detail"] = detail
        row["status"] = "RETRYABLE"
        if prior_retry is None:
            return
        written = row.get("retry_written")
        if not isinstance(written, list) or len(written) != 2:
            return
        reserved = (written[0], written[1])
        if self.retry.get(stream, (0, None)) != reserved:
            return
        if prior_retry == (0, None):
            self.retry.pop(stream, None)
        else:
            self.retry[stream] = prior_retry
    def set_status(self, token: str, status: str, detail: Any = None) -> None:
        self.records[token]["status"] = status
        self.records[token]["detail"] = detail
    def retry_state(self, stream: str) -> tuple[int, str | None]:
        return self.retry.get(stream, (0, None))

class JsonFileStore(MemoryStore):
    """Crash-safe process-shared token/retry store guarded by flock."""
    def __init__(self, path: Path) -> None:
        super().__init__(); self.path = Path(path)
    def _locked(self, fn: Any) -> Any:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with open(str(self.path) + ".lock", "w", encoding="utf-8") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            if self.path.exists():
                data = json.loads(self.path.read_text(encoding="utf-8")); self.records = data["records"]; self.retry = {k:(v[0],v[1]) for k,v in data["retry"].items()}
            else:
                self.records, self.retry = {}, {}
            out = fn(); tmp = self.path.with_suffix(".tmp")
            payload = json.dumps({"records":self.records,"retry":{k:list(v) for k,v in self.retry.items()}}, sort_keys=True)
            with open(tmp,"w",encoding="utf-8") as fh:
                fh.write(payload); fh.flush(); os.fsync(fh.fileno())
            os.replace(tmp,self.path); dir_fd=os.open(self.path.parent,os.O_RDONLY)
            try: os.fsync(dir_fd)
            finally: os.close(dir_fd)
            return out
    def get(self, token: str) -> dict[str, Any] | None: return self._locked(lambda: MemoryStore.get(self, token))
    def begin(self, token: str, record: dict[str, Any], stream: str, count: int | None, action: str | None, *, expected_retry: tuple[int, str | None] | None = None) -> bool: return self._locked(lambda: MemoryStore.begin(self, token, record, stream, count, action, expected_retry=expected_retry))
    def fail_and_restore(self, token: str, detail: Any, stream: str, prior_retry: tuple[int, str | None] | None) -> None: self._locked(lambda: MemoryStore.fail_and_restore(self, token, detail, stream, prior_retry))
    def set_status(self, token: str, status: str, detail: Any = None) -> None: self._locked(lambda: MemoryStore.set_status(self, token, status, detail))
    def retry_state(self, stream: str) -> tuple[int, str | None]: return self._locked(lambda: MemoryStore.retry_state(self, stream))
