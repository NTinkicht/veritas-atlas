#!/usr/bin/env python3
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from l5_controller import *
from l5_kernel import *
from test_l5_kernel import merge_snapshot

class Ports:
    def __init__(self, item=None):
        self.item = item or {"item_id": "1", "ci": "PENDING", "head_sha": "a" * 40, "base_sha": "b" * 40}
        self.items = [self.item]
        self.detect = "UNKNOWN"
        self.outcome = "APPLIED"
        self.health = "UNKNOWN"
        self.capacity = None
    def governance_snapshot(self):
        return {"ledger_reachable": True, "platform_enforcement_ok": True, "live_rules_at_least_pinned": True, "rulesets_or_protection_active": True, "required_check_sources_pinned": True, "controller_admin": False, "controller_bypass": False}
    def inventory(self): return list(self.items)
    def observe_item(self, item_id):
        for item in self.items:
            if item.get("item_id") == item_id: return dict(item)
        return dict(self.item)
    def budget_for(self, item_id): return Budget()
    def detect_intent(self, intent): return self.detect
    def perform(self, operation, item, **kwargs): return self.outcome
    def post_merge_health(self, item_id): return self.health
    def replenishment_candidate(self): return self.capacity

class ControllerTests(unittest.TestCase):
    def test_governance_drift_blocks_before_inventory(self):
        class Drift(Ports):
            def governance_snapshot(self):
                snap=super().governance_snapshot();snap["platform_enforcement_ok"]=False;return snap
        r=run_once("repo",MemoryCASStore(),Drift(),now_srv=1);self.assertEqual(r.stage,RunStage.REPOSITORY_MODE);self.assertEqual(r.status,"BLOCKED")
    def test_unknown_pending_intent_blocks_all_new_work(self):
        st=MemoryCASStore();o=Observation("a"*40,"b"*40);l=acquire(st,"k","old",o,now_srv=1);attach_intent(st,l,"repo","old","merge");r=run_once("repo",st,Ports(),now_srv=2);self.assertEqual(r.stage,RunStage.INTENT_RECOVERY);self.assertEqual(r.status,"BLOCKED")
    def test_nonmerge_recovery_releases(self):
        st=MemoryCASStore();o=Observation("a"*40,"b"*40);l=acquire(st,"k","old",o,now_srv=1);attach_intent(st,l,"repo","old","update_branch");p=Ports();p.detect="APPLIED";run_once("repo",st,p,now_srv=2);self.assertEqual(st.read("k").intent.state,"DONE");self.assertFalse(st.read("k").active)
    def test_ci_rerun_releases(self):
        item={"item_id":"1","ci":"INFRA_FAILED","head_sha":"a"*40,"base_sha":"b"*40};st=MemoryCASStore();r=run_once("repo",st,Ports(item),now_srv=1);self.assertEqual(r.status,"APPLIED");row=[x for x in st.list_leases() if x.intent][0];self.assertEqual(row.intent.state,"DONE");self.assertFalse(row.active)
    def test_merge_lock_until_health(self):
        item=merge_snapshot();item["item_id"]="1";st=MemoryCASStore();p=Ports(item);r=run_once("repo",st,p,now_srv=1);self.assertEqual((r.action,r.status),("merge","APPLIED"));lock=st.read(repo_merge_lock_key("repo"));self.assertTrue(lock.active);self.assertEqual(st.read_repo_mode("repo")[0],RepoMode.MERGE_LOCKED);merged=dict(item);merged["merged"]=True;merged["post_merge_verified"]=False;p.items=[merged];p.health="HEALTHY";r2=run_once("repo",st,p,now_srv=2);self.assertEqual(r2.status,"VERIFIED");self.assertFalse(st.read(repo_merge_lock_key("repo")).active);self.assertEqual(st.read_repo_mode("repo")[0],RepoMode.NORMAL)
    def test_unknown_merge_stays_pending(self):
        item=merge_snapshot();item["item_id"]="1";st=MemoryCASStore();p=Ports(item);p.outcome="UNKNOWN";r=run_once("repo",st,p,now_srv=1);self.assertEqual(r.status,"OUTCOME_UNKNOWN");self.assertEqual(len([x for x in st.list_leases() if x.intent and x.intent.state=="PENDING"]),1)
    def test_capacity_slot_serializes_replenishment(self):
        st=MemoryCASStore();p=Ports();p.items=[];p.capacity={"item_id":"wu-42","slot":1,"head_sha":"a"*40,"base_sha":"b"*40};a=run_once("repo",st,p,now_srv=1,run_id="a");b=run_once("repo",st,p,now_srv=2,run_id="b");self.assertEqual(a.status,"RESERVED");self.assertEqual(a.action,"capacity_slot:1");self.assertEqual(b.reason,"CAPACITY_SLOT_BUSY")

if __name__ == "__main__": unittest.main()
