#!/usr/bin/env python3
"""Executable Claude L5 BOOT-to-ACTION controller."""
from __future__ import annotations
from dataclasses import dataclass
from enum import Enum
from typing import Any,Mapping,Protocol
from l5_kernel import Budget,CASStore,HUMAN_CLEAR_ONLY,ItemState,Observation,RepoMode,acquire,attach_intent,classify_item,fence_ok,governance_mode,intent_recovery,lease_key,merge_ok,new_run_id,repo_merge_lock_key,resolve_intent
class RunStage(str,Enum):BOOT="BOOT";HALT_CHECK="HALT_CHECK";GOVERNANCE_AUDIT="GOVERNANCE_AUDIT";REPOSITORY_MODE="REPOSITORY_MODE";INTENT_RECOVERY="INTENT_RECOVERY";INVENTORY="INVENTORY";CLASSIFY="CLASSIFY";SELECT="SELECT";ACQUIRE_CAS_LEASE="ACQUIRE_CAS_LEASE";RECONCILE_ITEM="RECONCILE_ITEM";WRITE_INTENT="WRITE_INTENT";FENCE_CHECK="FENCE_CHECK";ACTION="ACTION"
@dataclass(frozen=True)
class RunResult:run_id:str;stage:RunStage;status:str;item_id:str|None=None;state:str|None=None;action:str|None=None;reason:str|None=None
class ControllerPorts(Protocol):
 def governance_snapshot(self)->Mapping[str,Any]:...
 def inventory(self)->list[Mapping[str,Any]]:...
 def observe_item(self,item_id:str)->Mapping[str,Any]:...
 def budget_for(self,item_id:str)->Budget:...
 def detect_intent(self,intent:Any)->str:...
 def perform(self,operation:str,item:Mapping[str,Any],*,expected_head:str,expected_base:str,idempotency_key:str)->str:...
 def post_merge_health(self,item_id:str)->str:...
_PRIORITY={ItemState.MERGED_UNVERIFIED:0,ItemState.MERGE_OUTCOME_UNKNOWN:1,ItemState.MAIN_BROKEN:2,ItemState.CI_RED_DETERMINISTIC:3,ItemState.FINDINGS_OPEN:4,ItemState.BEHIND_BASE:5,ItemState.CI_RED_INFRA:6,ItemState.CI_GREEN_UNREVIEWED:7,ItemState.MERGE_ELIGIBLE:8,ItemState.IMPLEMENT:9,ItemState.IDLE:99}
def _item_id(item):
 v=item.get("item_id")
 if not isinstance(v,str) or not v:raise ValueError("ITEM_ID_MISSING")
 return v
def _observation(item):
 h=item.get("head_sha");b=item.get("base_sha")
 if not isinstance(h,str) or not isinstance(b,str):raise ValueError("ITEM_REFS_UNKNOWN")
 return Observation(h,b,str(item.get("wu_body_hash","")),str(item.get("pr_updated_at","")))
def _select(rows):
 waiting={ItemState.WAIT_CI,ItemState.CI_MISSING,ItemState.WAIT_PROVIDER,ItemState.WAIT_DEPENDENCY,ItemState.BLOCK_HUMAN,ItemState.GOVERNANCE_CHANGE,ItemState.DISPUTED_FINDING,ItemState.PARKED,ItemState.MERGED_VERIFIED,ItemState.SUPERSEDED,ItemState.MERGE_QUEUED};a=[x for x in rows if x[1] not in waiting];return None if not a else min(a,key=lambda x:(_PRIORITY.get(x[1],50),_item_id(x[0])))
def _recover(store,ports):
 for lease in sorted(store.list_leases(),key=lambda x:x.key):
  if not lease.active or lease.intent is None or lease.intent.state!="PENDING":continue
  d=intent_recovery(lease,ports.detect_intent(lease.intent))
  if d=="READBACK_REQUIRED":return "BLOCKED",f"OUTCOME_UNKNOWN:{lease.key}"
  if resolve_intent(store,lease,"DONE" if d=="RESOLVE_DONE" else "ABORTED") is None:return "BLOCKED",f"INTENT_CAS_CONFLICT:{lease.key}"
 return None,None
def _op(state):return {ItemState.CI_RED_INFRA:"ci_rerun",ItemState.CI_GREEN_UNREVIEWED:"review_request",ItemState.BEHIND_BASE:"update_branch",ItemState.MERGE_ELIGIBLE:"merge"}.get(state)
def run_once(repo_id,store,ports,*,now_srv,run_id=None):
 rid=run_id or new_run_id();mode=governance_mode(ports.governance_snapshot());cur,ver=store.read_repo_mode(repo_id)
 if cur!=mode:
  if cur in HUMAN_CLEAR_ONLY:return RunResult(rid,RunStage.REPOSITORY_MODE,"BLOCKED",reason=f"HUMAN_CLEAR_REQUIRED:{cur.value}")
  if not store.cas_repo_mode(repo_id,ver,mode):return RunResult(rid,RunStage.REPOSITORY_MODE,"BLOCKED",reason="MODE_CAS_CONFLICT")
 if mode!=RepoMode.NORMAL:return RunResult(rid,RunStage.REPOSITORY_MODE,"BLOCKED",reason=f"REPO_MODE_{mode.value}")
 status,reason=_recover(store,ports)
 if status:return RunResult(rid,RunStage.INTENT_RECOVERY,status,reason=reason)
 rows=[]
 for item in ports.inventory():
  iid=_item_id(item);rows.append((item,classify_item(item,ports.budget_for(iid))))
 selected=_select(rows)
 if selected is None:return RunResult(rid,RunStage.SELECT,"QUIESCENT",state="IDLE")
 item,state=selected;iid=_item_id(item)
 if state in {ItemState.CI_RED_DETERMINISTIC,ItemState.FINDINGS_OPEN,ItemState.IMPLEMENT}:return RunResult(rid,RunStage.ACTION,"NEEDS_IMPLEMENTATION",iid,state.value)
 if state==ItemState.MAIN_BROKEN:return RunResult(rid,RunStage.ACTION,"BLOCKED",iid,state.value,reason="REVERT_REQUIRES_HUMAN_AUTHORITY")
 if state==ItemState.MERGED_UNVERIFIED:
  health=ports.post_merge_health(iid)
  if health=="HEALTHY":return RunResult(rid,RunStage.ACTION,"VERIFIED",iid,ItemState.MERGED_VERIFIED.value)
  if health=="BROKEN":
   _,v=store.read_repo_mode(repo_id);store.cas_repo_mode(repo_id,v,RepoMode.MAIN_BROKEN);return RunResult(rid,RunStage.ACTION,"BLOCKED",iid,ItemState.MAIN_BROKEN.value,reason="POST_MERGE_MAIN_BROKEN")
  return RunResult(rid,RunStage.ACTION,"WAIT",iid,state.value,reason="POST_MERGE_HEALTH_UNKNOWN")
 fresh=ports.observe_item(iid);fresh_state=classify_item(fresh,ports.budget_for(iid))
 if fresh_state!=state:return RunResult(rid,RunStage.RECONCILE_ITEM,"RECONCILE",iid,fresh_state.value,reason="STATE_CHANGED")
 op=_op(state)
 if op is None:return RunResult(rid,RunStage.ACTION,"WAIT",iid,state.value)
 obs=_observation(fresh);key=repo_merge_lock_key(repo_id) if op=="merge" else lease_key(repo_id,"item",iid,op.upper());lease=acquire(store,key,rid,obs,now_srv=now_srv)
 if lease is None:return RunResult(rid,RunStage.ACQUIRE_CAS_LEASE,"WAIT",iid,state.value,op,"LEASE_BUSY")
 intended=attach_intent(store,lease,repo_id,iid,op)
 if intended is None:return RunResult(rid,RunStage.WRITE_INTENT,"BLOCKED",iid,state.value,op,"INTENT_CAS_CONFLICT")
 if op=="merge":
  ok,fail=merge_ok(fresh)
  if not ok:resolve_intent(store,intended,"ABORTED");return RunResult(rid,RunStage.FENCE_CHECK,"BLOCKED",iid,state.value,op,"MERGE_OK:"+",".join(fail))
 now=_observation(ports.observe_item(iid));fenced,reason=fence_ok(store,repo_id,intended,now,now_srv=now_srv)
 if not fenced:resolve_intent(store,intended,"ABORTED");return RunResult(rid,RunStage.FENCE_CHECK,"BLOCKED",iid,state.value,op,reason)
 outcome=ports.perform(op,fresh,expected_head=obs.head,expected_base=obs.base,idempotency_key=intended.intent.idem_key)
 if outcome=="APPLIED":
  if resolve_intent(store,intended,"DONE") is None:return RunResult(rid,RunStage.ACTION,"BLOCKED",iid,state.value,op,"POST_WRITE_INTENT_CAS_CONFLICT")
  return RunResult(rid,RunStage.ACTION,"APPLIED",iid,state.value,op)
 if outcome=="NOT_APPLIED":resolve_intent(store,intended,"ABORTED");return RunResult(rid,RunStage.ACTION,"FAILED",iid,state.value,op,"WRITE_REJECTED")
 return RunResult(rid,RunStage.ACTION,"OUTCOME_UNKNOWN",iid,state.value,op,"READBACK_REQUIRED")
