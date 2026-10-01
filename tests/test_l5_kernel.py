#!/usr/bin/env python3
import sys,unittest
from dataclasses import replace
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"scripts"))
from l5_kernel import *
def merge_snapshot():
 h="a"*40;b="b"*40;src={"app_id":1,"workflow_path":"ci"};secsrc={"app_id":2,"workflow_path":"security"};check={**src,"head_sha":h,"tested_base_sha":b,"latest_attempt":True,"conclusion":"success","assertion_history_complete":True,"assertion_failure_any_attempt":False};seccheck={**secsrc,"head_sha":h,"tested_base_sha":b,"latest_attempt":True,"conclusion":"success","assertion_history_complete":True,"assertion_failure_any_attempt":False};review={"state":"APPROVED","commit_id":h,"base_sha":b,"complete":True,"skipped":False,"covers_full_diff":True,"author":"coderabbitai","material_authors":["chatgpt"],"controller_identities":["controller-1"],"designated_independent":True};s={"head_sha":h,"base_sha":b,"expected_head_sha":h,"expected_base_sha":b,"repo_mode":"NORMAL","required_checks":[check],"security_checks":[seccheck],"required_check_sources":[src],"security_check_sources":[secsrc],"review":review,"ci":"GREEN","independent_review_pass":True}
 for k in TRUE_FIELDS:s[k]=True
 for k in FALSE_FIELDS:s[k]=False
 return s
class T(unittest.TestCase):
 def test_epoch_and_pending(self):
  st=MemoryCASStore();o=Observation("a"*40,"b"*40);l=acquire(st,"k","a",o,now_srv=0,ttl=5);li=attach_intent(st,l,"r","i","merge");self.assertIsNone(acquire(st,"k","b",o,now_srv=10));self.assertEqual(intent_recovery(li,"UNKNOWN"),"READBACK_REQUIRED")
 def test_stale_release_and_replace_blocked(self):
  st=MemoryCASStore();o=Observation("a"*40,"b"*40);l=acquire(st,"k","a",o,now_srv=0);li=attach_intent(st,l,"r","i","merge");self.assertIsNone(release(st,l,now_srv=10));self.assertIsNone(release(st,li,now_srv=10));self.assertIsNone(attach_intent(st,li,"r","i","push"));self.assertEqual(st.read("k").intent.state,"PENDING")
 def test_fence(self):
  st=MemoryCASStore();o=Observation("a"*40,"b"*40);l=acquire(st,"k","a",o,now_srv=0);li=attach_intent(st,l,"r","i","merge");self.assertFalse(fence_ok(st,"r",li,replace(o,head="c"*40),now_srv=1)[0])
 def test_narrow_revert(self):
  st=MemoryCASStore();o=Observation("a"*40,"b"*40);st.cas_repo_mode("r",None,RepoMode.HALTED);l=acquire(st,"k","a",o,now_srv=0);li=attach_intent(st,l,"r","i","revert");self.assertFalse(fence_ok(st,"r",li,o,now_srv=1,emergency_revert_authorized=True)[0]);st.modes["r"]=(RepoMode.MAIN_BROKEN,2);self.assertTrue(fence_ok(st,"r",li,o,now_srv=1,emergency_revert_authorized=True)[0])
 def test_locks(self):
  for k in (repo_merge_lock_key("r"),capacity_slot_key("r",4)):
   st=MemoryCASStore();o=Observation("a"*40,"b"*40);self.assertEqual(sum(acquire(st,k,str(i),o,now_srv=0) is not None for i in range(4)),1)
 def test_governance(self):self.assertEqual(governance_mode({"ledger_reachable":True,"platform_enforcement_ok":False,"live_rules_at_least_pinned":True,"rulesets_or_protection_active":False,"required_check_sources_pinned":True}),RepoMode.GOVERNANCE_DRIFT);self.assertEqual(governance_mode({}),RepoMode.AUTOMATION_DEGRADED)
 def test_merge_ok(self):self.assertTrue(merge_ok(merge_snapshot())[0])
 def test_assertion_history_required(self):s=merge_snapshot();del s["required_checks"][0]["assertion_history_complete"];self.assertFalse(merge_ok(s)[0]);s=merge_snapshot();s["required_checks"][0]["assertion_failure_any_attempt"]=True;self.assertFalse(merge_ok(s)[0])
 def test_merge_queue_base_mismatch(self):s=merge_snapshot();s["required_checks"][0]["tested_base_sha"]="c"*40;s["required_checks"][0]["merge_queue"]=True;self.assertFalse(merge_ok(s)[0])
 def test_source_review_and_unknown(self):
  s=merge_snapshot();s["required_checks"][0]["app_id"]=999;self.assertFalse(merge_ok(s)[0]);s=merge_snapshot();s["review"]["author"]="chatgpt";self.assertFalse(merge_ok(s)[0]);s=merge_snapshot();del s["files_fully_enumerated"];self.assertFalse(merge_ok(s)[0])
 def test_budget_idle(self):self.assertEqual(classify_item({"ci":"GREEN"},Budget(fix_iterations=MAX_FIX_ITERATIONS)),ItemState.PARKED);self.assertEqual(classify_item({"no_actionable_work":True},Budget()),ItemState.IDLE)
if __name__=="__main__":unittest.main()
