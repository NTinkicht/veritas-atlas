from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import l5_recovery as recovery
import l5_write_adapter as wa


def snap(**patch):
    h, b = "a" * 40, "b" * 40
    row = {
        "repository":"NTinkicht/veritas-atlas","issue":36,"canonical_pr":37,"active_prs":[37],
        "head_sha":h,"base_sha":b,"head_current":True,"base_current":True,"implementation_complete":True,
        "emergency_stop":False,"human_only":False,"release_go_no_go":False,"blocked":False,
        "destructive_production":False,"spend_required":False,"secret_scope_change":False,"security_control_weakening":False,
        "merged":False,"verified":False,"verified_head_sha":None,"verified_base_sha":None,
        "ci":"FAILURE","ci_head_sha":h,"ci_base_sha":b,"review":"UNKNOWN","review_head_sha":None,"review_base_sha":None,
        "reviewer_actor":None,"material_authors":["chatgpt"],"material_authors_head_sha":h,"review_eligible":False,
        "unresolved_threads":False,"mergeable":True,"retry_count":0,"retry_action":None,"event_id":"w1",
        "ready_candidates":[],"prior_event_keys":[],"prior_mutation_tokens":[],
    }
    row.update(patch)
    return row


class Client:
    def __init__(self, *, effect=True):
        self.effect = effect
        self.calls = 0
        self.boundaries = {k:False for k in recovery.HARD_BOUNDARIES}
        self.live = {"head_sha":"a"*40,"base_sha":"b"*40,"pr_state":"open","open_streams":{36:[37]},"review_eligible_nonauthor":True}
    def fetch_boundaries(self): return dict(self.boundaries)
    def fetch_live(self, _pr): return dict(self.live)
    def perform(self, _mutation, _params): self.calls += 1
    def verify_effect(self, _mutation, _params): return self.effect


class AdapterTests(unittest.TestCase):
    def test_authorized_retry_executes_once_and_replay_noops(self):
        s=snap(); auth=recovery.authorize_mutation(s); store=wa.MemoryStore(); client=Client()
        first=wa.execute_mutation(auth,s,client,store); second=wa.execute_mutation(auth,s,client,store)
        self.assertEqual(first["status"],"COMPLETE")
        self.assertEqual(second["status"],"REPLAY_NOOP")
        self.assertEqual(client.calls,1)

    def test_release_go_no_go_rechecked_before_write(self):
        s=snap(); auth=recovery.authorize_mutation(s); store=wa.MemoryStore()
        class Flip(Client):
            def __init__(self): super().__init__(); self.n=0
            def fetch_boundaries(self):
                self.n += 1
                row={k:False for k in recovery.HARD_BOUNDARIES}
                if self.n > 1: row["release_go_no_go"] = True
                return row
        client=Flip(); out=wa.execute_mutation(auth,s,client,store)
        self.assertEqual(out["reason"],"HARD_BOUNDARY")
        self.assertEqual(client.calls,0)
        self.assertEqual(store.retry_state(wa.stream_key(auth,s)),(0,None))

    def test_unknown_perform_exception_reconciles_without_retry_refund(self):
        s=snap(); auth=recovery.authorize_mutation(s); store=wa.MemoryStore()
        class Boom(Client):
            def perform(self, _mutation, _params):
                self.calls += 1
                raise RuntimeError("unknown after send")
        client=Boom(effect=True); out=wa.execute_mutation(auth,s,client,store)
        self.assertEqual(out["status"],"COMPLETE")
        self.assertEqual(store.retry_state(wa.stream_key(auth,s)),(1,"CI"))
        self.assertEqual(client.calls,1)

    def test_atomic_retry_cas_rejects_stale_second_worker(self):
        s=snap(); auth=recovery.authorize_mutation(s); store=wa.MemoryStore(); stream=wa.stream_key(auth,s)
        observed=store.retry_state(stream)
        self.assertTrue(store.begin("1"*64,{"status":"PENDING"},stream,1,"CI",expected_retry=observed))
        self.assertFalse(store.begin("2"*64,{"status":"PENDING"},stream,1,"CI",expected_retry=observed))

    def test_stale_live_head_blocks_without_write(self):
        s=snap(); auth=recovery.authorize_mutation(s); client=Client(); client.live["head_sha"]="c"*40
        out=wa.execute_mutation(auth,s,client,wa.MemoryStore())
        self.assertEqual(out["reason"],"STALE_HEAD_OR_BASE")
        self.assertEqual(client.calls,0)

    def test_json_store_persists_pending_and_retry_state(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/"store.json"; store=wa.JsonFileStore(path)
            self.assertTrue(store.begin("3"*64,{"status":"PENDING"},"stream",1,"CI",expected_retry=(0,None)))
            reopened=wa.JsonFileStore(path)
            self.assertEqual(reopened.get("3"*64)["status"],"PENDING")
            self.assertEqual(reopened.retry_state("stream"),(1,"CI"))


if __name__ == "__main__":
    unittest.main()
