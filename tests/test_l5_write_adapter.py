from __future__ import annotations
import os,sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"scripts"))
import l5_recovery as recovery
import l5_write_adapter as wa
H,B="a"*40,"b"*40
ACTIVE_CONTROL_PLANE=ROOT/"tests"/"fixtures"/"l5-control-plane-active.json"

def snap(**patch):
    row={"repository":"NTinkicht/veritas-atlas","issue":36,"canonical_pr":37,"active_prs":[37],"head_sha":H,"base_sha":B,"head_current":True,"base_current":True,"implementation_complete":True,"emergency_stop":False,"human_only":False,"release_go_no_go":False,"blocked":False,"destructive_production":False,"spend_required":False,"secret_scope_change":False,"security_control_weakening":False,"merged":False,"verified":False,"verified_head_sha":None,"verified_base_sha":None,"ci":"FAILURE","ci_head_sha":H,"ci_base_sha":B,"review":"UNKNOWN","review_head_sha":None,"review_base_sha":None,"reviewer_actor":None,"material_authors":["chatgpt"],"material_authors_head_sha":H,"review_eligible":False,"unresolved_threads":False,"mergeable":True,"retry_count":0,"retry_action":None,"event_id":"w1","ready_candidates":[],"prior_event_keys":[],"prior_mutation_tokens":[]};row.update(patch);return row

def merge_snap(**patch):
    row=snap(ci="SUCCESS",review="PASS",review_head_sha=H,review_base_sha=B,reviewer_actor="coderabbit",review_eligible=True);row.update(patch);return row

class Client:
    def __init__(self,*,effect=True):
        self.effect=effect;self.calls=0;self.last_params=None;self.reject=False;self.raise_unknown=False;self.boundaries={k:False for k in recovery.HARD_BOUNDARIES};self.live={"head_sha":H,"base_sha":B,"pr_state":"open","open_streams":{36:[37]},"review_eligible_nonauthor":True}
    def fetch_boundaries(self):return dict(self.boundaries)
    def fetch_live(self,_pr):return dict(self.live)
    def perform_cas(self,_mutation,params):
        self.calls+=1;self.last_params=dict(params)
        if self.reject:raise wa.WriteRejected("definitive no-write")
        if self.raise_unknown:raise RuntimeError("unknown after send")
        return True
    def verify_effect(self,_mutation,_params):return self.effect

class AdapterTests(unittest.TestCase):
    def setUp(self):
        self._old_control_plane=os.environ.get("L5_CONTROL_PLANE_MANIFEST")
        os.environ["L5_CONTROL_PLANE_MANIFEST"]=str(ACTIVE_CONTROL_PLANE)
    def tearDown(self):
        if self._old_control_plane is None:os.environ.pop("L5_CONTROL_PLANE_MANIFEST",None)
        else:os.environ["L5_CONTROL_PLANE_MANIFEST"]=self._old_control_plane
    def test_atomic_refs_and_replay(self):
        s=snap();a=recovery.authorize_mutation(s);store=wa.MemoryStore();c=Client();o=wa.execute_mutation(a,s,c,store);self.assertEqual(o["status"],"COMPLETE");self.assertEqual((c.last_params["expected_head_sha"],c.last_params["expected_base_sha"]),(H,B));self.assertEqual(wa.execute_mutation(a,s,c,store)["status"],"REPLAY_NOOP")
    def test_release_boundary_rechecked(self):
        s=snap();a=recovery.authorize_mutation(s);store=wa.MemoryStore()
        class Flip(Client):
            def __init__(self):super().__init__();self.n=0
            def fetch_boundaries(self):
                self.n+=1;row={k:False for k in recovery.HARD_BOUNDARIES}
                if self.n>1:row["release_go_no_go"]=True
                return row
        c=Flip();o=wa.execute_mutation(a,s,c,store);self.assertEqual(o["reason"],"HARD_BOUNDARY");self.assertEqual(store.get(a["mutation_token"])["status"],"RETRYABLE")
    def test_uncertain_write_pending_then_reconcile(self):
        s=snap();a=recovery.authorize_mutation(s);store=wa.MemoryStore();c=Client(effect=False);c.raise_unknown=True;o=wa.execute_mutation(a,s,c,store);self.assertEqual(o["status"],"IN_PROGRESS");self.assertEqual(store.get(a["mutation_token"])["status"],"PENDING");c.raise_unknown=False;c.effect=True;self.assertEqual(wa.execute_mutation(a,s,c,store)["status"],"COMPLETE");self.assertEqual(c.calls,1)
    def test_definitive_rejection_retryable_budgeted(self):
        s=snap();a=recovery.authorize_mutation(s);store=wa.MemoryStore();c=Client();c.reject=True;o=wa.execute_mutation(a,s,c,store);self.assertEqual(o["reason"],"WRITE_REJECTED");self.assertEqual(store.get(a["mutation_token"])["status"],"RETRYABLE");c.reject=False;self.assertEqual(wa.execute_mutation(a,s,c,store)["status"],"COMPLETE")
    def test_definitive_rejection_retryable_merge(self):
        s=merge_snap();a=recovery.authorize_mutation(s);store=wa.MemoryStore();c=Client();c.reject=True;o=wa.execute_mutation(a,s,c,store);self.assertEqual(o["reason"],"WRITE_REJECTED");self.assertEqual(store.get(a["mutation_token"])["status"],"RETRYABLE");c.reject=False;self.assertEqual(wa.execute_mutation(a,s,c,store)["status"],"COMPLETE")
    def test_token_owner_prevents_aba_restore(self):
        store=wa.MemoryStore();stream="stream";t1="1"*64;t2="2"*64
        self.assertTrue(store.begin(t1,{"status":"PENDING"},stream,1,"CI",expected_retry=(0,None),expected_owner=None))
        self.assertTrue(store.begin(t2,{"status":"PENDING"},stream,1,"REVIEW",expected_retry=(1,"CI"),expected_owner=t1))
        store.fail_and_restore(t2,"newer failed",stream,(1,"CI"));self.assertEqual(store.retry_state(stream),(1,"CI"));self.assertEqual(store.retry_owner(stream),t2)
        store.fail_and_restore(t1,"old late",stream,(0,None));self.assertEqual(store.retry_state(stream),(1,"CI"));self.assertEqual(store.retry_owner(stream),t2)
    def test_stale_second_worker_rejected_by_owner(self):
        store=wa.MemoryStore();self.assertTrue(store.begin("3"*64,{"status":"PENDING"},"s",1,"CI",expected_retry=(0,None),expected_owner=None));self.assertFalse(store.begin("4"*64,{"status":"PENDING"},"s",1,"CI",expected_retry=(0,None),expected_owner=None))
    def test_stale_head_blocks(self):
        s=snap();a=recovery.authorize_mutation(s);c=Client();c.live["head_sha"]="c"*40;self.assertEqual(wa.execute_mutation(a,s,c,wa.MemoryStore())["reason"],"STALE_HEAD_OR_BASE")
    def test_merge_rechecks_reviewer(self):
        s=merge_snap();a=recovery.authorize_mutation(s);c=Client();c.live["review_eligible_nonauthor"]=False;self.assertEqual(wa.execute_mutation(a,s,c,wa.MemoryStore())["reason"],"REVIEWER_NOT_ELIGIBLE")
    def test_atomic_cas_required(self):
        s=snap();a=recovery.authorize_mutation(s);c=Client();c.perform_cas=None;self.assertEqual(wa.execute_mutation(a,s,c,wa.MemoryStore())["reason"],"ATOMIC_CAS_UNAVAILABLE")
    def test_json_store_persists_owner(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/"store.json";store=wa.JsonFileStore(p);token="5"*64;self.assertTrue(store.begin(token,{"status":"PENDING"},"stream",1,"CI",expected_retry=(0,None),expected_owner=None));fresh=wa.JsonFileStore(p);self.assertEqual(fresh.retry_state("stream"),(1,"CI"));self.assertEqual(fresh.retry_owner("stream"),token)

if __name__=="__main__":unittest.main()
