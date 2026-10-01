#!/usr/bin/env python3
"""One-shot Veritas-specific final hardening patch for the L5 controller."""
from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"missing patch anchor in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# Kernel: explicit lease lifecycle and a verified-only MERGE_LOCKED exit.
replace_once("scripts/l5_kernel.py", '''    version: int
    intent: Intent | None = None
''', '''    version: int
    intent: Intent | None = None
    state: str = "ACTIVE"
''')
replace_once("scripts/l5_kernel.py", '''        human_clear: bool = False,
    ) -> bool: ...
''', '''        human_clear: bool = False,
        post_merge_verified: bool = False,
    ) -> bool: ...
''')
replace_once("scripts/l5_kernel.py", '''        human_clear: bool = False,
    ) -> bool:
        """CAS repo mode while enforcing human-only exits."""
''', '''        human_clear: bool = False,
        post_merge_verified: bool = False,
    ) -> bool:
        """CAS repo mode while enforcing protected exits."""
''')
replace_once("scripts/l5_kernel.py", '''        if old_mode in HUMAN_CLEAR_ONLY and old_mode != mode and not human_clear:
            return False
        self.modes[repo_id] = (mode, 1 if cur is None else cur[1] + 1)
''', '''        if old_mode in HUMAN_CLEAR_ONLY and old_mode != mode and not human_clear:
            return False
        if old_mode == RepoMode.MERGE_LOCKED and old_mode != mode and not post_merge_verified:
            return False
        self.modes[repo_id] = (mode, 1 if cur is None else cur[1] + 1)
''')
replace_once("scripts/l5_kernel.py", '''    if ttl <= 0 or now_srv >= lease.expires_at:
        return None
    cur = store.read(lease.key)
    if cur != lease or cur.holder != lease.holder or cur.epoch != lease.epoch:
        return None
    nxt = replace(lease, expires_at=now_srv + ttl, version=lease.version + 1)
''', '''    if ttl <= 0 or now_srv >= lease.expires_at or lease.state != "ACTIVE":
        return None
    new_expires_at = now_srv + ttl
    if new_expires_at <= lease.expires_at:
        return None
    cur = store.read(lease.key)
    if cur != lease or cur.holder != lease.holder or cur.epoch != lease.epoch or cur.state != "ACTIVE":
        return None
    nxt = replace(lease, expires_at=new_expires_at, version=lease.version + 1)
''')
replace_once("scripts/l5_kernel.py", '''    if operation not in DANGEROUS | HARMLESS or now_srv >= lease.expires_at:
        return None
''', '''    if operation not in DANGEROUS | HARMLESS or now_srv >= lease.expires_at or lease.state != "ACTIVE":
        return None
''')
replace_once("scripts/l5_kernel.py", '''def release(store: CASStore, lease: Lease, *, now_srv: float) -> Lease | None:
    """Expire only the exact current lease after any intent is terminal."""
    cur = store.read(lease.key)
    if cur != lease:
        return None
    if cur.intent is not None and cur.intent.state == "PENDING":
        return None
    nxt = replace(cur, expires_at=now_srv, version=cur.version + 1)
    return nxt if store.cas(cur.key, cur.version, nxt) else None
''', '''def verify_intent(store: CASStore, lease: Lease) -> Lease | None:
    """Durably mark a completed main-changing intent as post-merge verified."""
    cur = store.read(lease.key)
    if cur != lease or cur.intent is None or cur.intent.state != "DONE":
        return None
    nxt = replace(cur, intent=replace(cur.intent, state="VERIFIED"), version=cur.version + 1)
    return nxt if store.cas(cur.key, cur.version, nxt) else None


def release(store: CASStore, lease: Lease, *, now_srv: float) -> Lease | None:
    """Release only the exact current lease after any intent is terminal."""
    cur = store.read(lease.key)
    if cur != lease or cur.state != "ACTIVE":
        return None
    if cur.intent is not None and cur.intent.state == "PENDING":
        return None
    nxt = replace(cur, expires_at=now_srv, version=cur.version + 1, state="RELEASED")
    return nxt if store.cas(cur.key, cur.version, nxt) else None
''')
replace_once("scripts/l5_kernel.py", '''    if cur != lease:
        return False, "LEASE_LOST"
    if now_srv + max_write_latency + skew_margin >= cur.expires_at:
''', '''    if cur != lease:
        return False, "LEASE_LOST"
    if cur.state != "ACTIVE":
        return False, "LEASE_RELEASED"
    if now_srv + max_write_latency + skew_margin >= cur.expires_at:
''')

# Durable ledger/store invariants.
replace_once("scripts/l5_ledger.py", 'if row.get("state") not in {"PENDING", "DONE", "ABORTED"}:', 'if row.get("state") not in {"PENDING", "DONE", "ABORTED", "VERIFIED"}:')
replace_once("scripts/l5_ledger.py", '''    new_mode: RepoMode,
    human_clear: bool = False,
) -> dict[str, Any]:
    """CAS repository mode while enforcing human-only exits."""
''', '''    new_mode: RepoMode,
    human_clear: bool = False,
    post_merge_verified: bool = False,
) -> dict[str, Any]:
    """CAS repository mode while enforcing protected exits."""
''')
replace_once("scripts/l5_ledger.py", '''    if old in HUMAN_CLEAR_ONLY and old != new_mode and not human_clear:
        raise LedgerConflict("HUMAN_CLEAR_REQUIRED")
    out["mode"] = new_mode.value
''', '''    if old in HUMAN_CLEAR_ONLY and old != new_mode and not human_clear:
        raise LedgerConflict("HUMAN_CLEAR_REQUIRED")
    if old == RepoMode.MERGE_LOCKED and old != new_mode and not post_merge_verified:
        raise LedgerConflict("POST_MERGE_VERIFICATION_REQUIRED")
    out["mode"] = new_mode.value
''')
replace_once("scripts/l5_ledger.py", '''    if cur.get("state") == "ACTIVE" and row.get("state") == "RELEASED":
        intent = row.get("intent")
        if isinstance(intent, Mapping) and intent.get("state") == "PENDING":
            raise LedgerConflict("PENDING_INTENT_RELEASE")
''', '''    if isinstance(cur_intent, Mapping) and cur_intent.get("state") == "DONE" and same_owner_epoch:
        if not isinstance(next_intent, Mapping):
            raise LedgerConflict("DONE_INTENT_LOST")
        for field in ("op_id", "idem_key", "operation", "expected_head", "expected_base", "epoch"):
            if next_intent.get(field) != cur_intent.get(field):
                raise LedgerConflict("DONE_INTENT_MUTATED")
        if next_intent.get("state") not in {"DONE", "VERIFIED"}:
            raise LedgerInvalid("DONE_INTENT_TRANSITION")

    if cur.get("state") == "RELEASED" and same_owner_epoch and row.get("state") != "RELEASED":
        raise LedgerConflict("RELEASED_LEASE_REACTIVATION")

    if cur.get("state") == "ACTIVE" and row.get("state") == "RELEASED":
        intent = row.get("intent")
        if isinstance(intent, Mapping) and intent.get("state") == "PENDING":
            raise LedgerConflict("PENDING_INTENT_RELEASE")
''')
replace_once("scripts/l5_ledger_store.py", '''            version=row["version"],
            intent=cls._intent_from_record(row.get("intent")),
        )
''', '''            version=row["version"],
            intent=cls._intent_from_record(row.get("intent")),
            state=row["state"],
        )
''')
replace_once("scripts/l5_ledger_store.py", '''        """Serialize a lease, preserving released tombstones monotonically."""
        state = "ACTIVE"
        if current is not None:
            same_epoch = (
                lease.epoch == current.get("epoch")
                and lease.holder == current.get("holder")
            )
            terminal = lease.intent is None or lease.intent.state != "PENDING"
            if (
                same_epoch
                and current.get("state") == "ACTIVE"
                and terminal
                and lease.expires_at < float(current.get("expires_at", lease.expires_at))
            ):
                state = "RELEASED"
            elif current.get("state") == "RELEASED" and lease.epoch == current.get("epoch"):
                state = "RELEASED"

        return {
''', '''        """Serialize a lease with an explicit ACTIVE/RELEASED state."""
        if lease.state not in {"ACTIVE", "RELEASED"}:
            raise ValueError("L5_LEASE_STATE_INVALID")
        return {
''')
replace_once("scripts/l5_ledger_store.py", '            "state": state,', '            "state": lease.state,')
replace_once("scripts/l5_ledger_store.py", '''        human_clear: bool = False,
    ) -> bool:
        """CAS repository mode through mode version plus GitHub blob identity."""
''', '''        human_clear: bool = False,
        post_merge_verified: bool = False,
    ) -> bool:
        """CAS repository mode through mode version plus GitHub blob identity."""
''')
replace_once("scripts/l5_ledger_store.py", '''                new_mode=mode,
                human_clear=human_clear,
            )
''', '''                new_mode=mode,
                human_clear=human_clear,
                post_merge_verified=post_merge_verified,
            )
''')

# Controller: exact merge-intent binding, non-starving PENDING recovery, and fail-closed bridge errors.
replace_once("scripts/l5_controller.py", '''    governance_mode,
    intent_recovery,''', '''    governance_mode,
    idem_key,
    intent_recovery,''')
replace_once("scripts/l5_controller.py", '''    resolve_intent,
)''', '''    resolve_intent,
    verify_intent,
)''')
replace_once("scripts/l5_controller.py", '''        current = store.read(key)
        if current is not None and current.expires_at > now:
            continue
''', '''        current = store.read(key)
        if current is not None and (
            current.expires_at > now
            or (current.intent is not None and current.intent.state == "PENDING")
        ):
            continue
''')
replace_once("scripts/l5_controller.py", '''def _find_merged_unverified(
    io: ControllerIO,
    items: Sequence[Mapping[str, Any]],
) -> Mapping[str, Any] | None:
    """Return exactly one merged-unverified item while the repo is locked."""
    found = [
        item
        for item in items
        if classify_item(item, io.budget_for(item)) == ItemState.MERGED_UNVERIFIED
    ]
    if len(found) != 1:
        return None
    return found[0]
''', '''def _find_merged_unverified(
    repo_id: str,
    lock: Lease,
    io: ControllerIO,
    items: Sequence[Mapping[str, Any]],
) -> Mapping[str, Any] | None:
    """Return the merged-unverified item bound to the durable merge intent."""
    if lock.intent is None or lock.intent.operation not in {"merge", "revert"}:
        return None
    found = []
    for item in items:
        if classify_item(item, io.budget_for(item)) != ItemState.MERGED_UNVERIFIED:
            continue
        item_id = _item_id(item)
        bound = idem_key(repo_id, item_id, lock.intent.expected_head, lock.intent.expected_base, lock.intent.operation)
        if bound == lock.intent.idem_key:
            found.append(item)
    return found[0] if len(found) == 1 else None
''')
replace_once("scripts/l5_controller.py", '''    item = _find_merged_unverified(io, items)
    if item is None:
        return RunResult(
            rid,
            RunPhase.POST_MERGE_VERIFY,
            "WAIT",
            reason="MERGE_LOCK_RECONCILIATION_REQUIRED",
        )

    item_id = _item_id(item)
    lock_key = repo_merge_lock_key(repo_id)
    lock = store.read(lock_key)
    if (
        lock is None
        or lock.intent is None
        or lock.intent.state != "DONE"
        or lock.intent.operation not in {"merge", "revert"}
    ):
''', '''    lock_key = repo_merge_lock_key(repo_id)
    lock = store.read(lock_key)
    if (
        lock is None
        or lock.intent is None
        or lock.intent.state not in {"DONE", "VERIFIED"}
        or lock.intent.operation not in {"merge", "revert"}
    ):
        return RunResult(rid, RunPhase.POST_MERGE_VERIFY, "WAIT", reason="MERGE_LOCK_EVIDENCE_INVALID")

    item = _find_merged_unverified(repo_id, lock, io, items)
    if item is None:
        return RunResult(rid, RunPhase.POST_MERGE_VERIFY, "WAIT", reason="MERGE_LOCK_RECONCILIATION_REQUIRED")

    item_id = _item_id(item)
    if lock.intent.idem_key != idem_key(repo_id, item_id, lock.intent.expected_head, lock.intent.expected_base, lock.intent.operation):
''')
replace_once("scripts/l5_controller.py", '''    release_now = _trusted_now(io)
    released = release(store, lock, now_srv=release_now)
''', '''    if lock.intent.state == "DONE":
        verified = verify_intent(store, lock)
        if verified is None:
            return RunResult(rid, RunPhase.POST_MERGE_VERIFY, "WAIT", item_id=item_id, reason="POST_MERGE_VERIFY_CAS_FAILED")
        lock = verified

    release_now = _trusted_now(io)
    released = release(store, lock, now_srv=release_now)
''')
replace_once("scripts/l5_controller.py", '''    if not store.cas_repo_mode(repo_id, mode_version, target_mode):
''', '''    if not store.cas_repo_mode(repo_id, mode_version, target_mode, post_merge_verified=True):
''')
replace_once("scripts/l5_controller.py", '''            if current_mode not in HUMAN_CLEAR_ONLY:
                store.cas_repo_mode(repo_id, version, derived)
''', '''            if current_mode not in HUMAN_CLEAR_ONLY and current_mode != RepoMode.MERGE_LOCKED:
                store.cas_repo_mode(repo_id, version, derived)
''')
replace_once("scripts/l5_controller.py", '''        auth = authorize_mutation(snapshot)
        expected = {
''', '''        try:
            auth = authorize_mutation(snapshot)
        except ValueError as exc:
            return {"status": "BLOCKED", "reason": f"AUTHORIZATION_INVALID:{exc}"}
        expected = {
''')

# Intent/restraint hostile scenarios must fail for the intended reason, not merely any reason.
replace_once("scripts/l5_intent_hostile_sim.py", '''def scenario(sid: int, rng: random.Random):
    snap = valid(rng); ev = dict(snap["intent_restraint"]); snap["intent_restraint"] = ev
''', '''def scenario(sid: int, rng: random.Random):
    snap = valid(rng)
    assert intent_restraint_status(snap) == ("PASS", ()), sid
    ev = dict(snap["intent_restraint"]); snap["intent_restraint"] = ev
''')
replace_once("scripts/l5_intent_hostile_sim.py", '''    else: raise AssertionError(sid)
    state, reasons = intent_restraint_status(snap)
    assert state == "FAILED" and reasons, (sid, state, reasons)
''', '''    else: raise AssertionError(sid)
    expected = {31:"INTENT_DRIFT",32:"INTENT_DRIFT",33:"INTENT_DRIFT",34:"SELF_REVIEW",35:"OVERENGINEERED",36:"PERFORMANCE_REGRESSION",37:"SEMANTIC_CHANGE",38:"DIFF_DISPROPORTIONATE",39:"UNRESOLVED_DELETION_CANDIDATES",40:"ATTESTATION_INCOMPLETE"}[sid]
    state, reasons = intent_restraint_status(snap)
    assert state == "FAILED", (sid, state, reasons)
    assert expected in reasons, (sid, reasons)
''')

print("Veritas L5 final hardening patch applied")
