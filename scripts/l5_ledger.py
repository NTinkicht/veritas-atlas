#!/usr/bin/env python3
"""Durable GitHub-ledger contract for Claude L5 controllers."""
from __future__ import annotations
import copy,json
from dataclasses import asdict
from typing import Any,Callable,Mapping,Protocol
from l5_kernel import HUMAN_CLEAR_ONLY,Intent,Lease,Observation,RepoMode
SCHEMA_VERSION=2;LEDGER_PATH=".l5/controller-ledger.json";LEDGER_BRANCH="l5/controller-ledger"
class LedgerConflict(RuntimeError):"""Stale compare-and-swap state."""
class LedgerInvalid(ValueError):"""Invalid durable ledger state."""
class LedgerBackend(Protocol):
 def read(self)->tuple[Mapping[str,Any],str]:...
 def write(self,document:Mapping[str,Any],expected_blob_sha:str)->str:...
def _validate_observation(v):
 if not isinstance(v,Mapping) or any(not isinstance(v.get(k),str) for k in ("head","base","wu_body_hash","pr_updated_at")):raise LedgerInvalid("LEASE_OBSERVATION")
def _validate_intent(v,epoch):
 if v is None:return
 if not isinstance(v,Mapping):raise LedgerInvalid("LEASE_INTENT")
 if any(not isinstance(v.get(k),str) or not v[k] for k in ("op_id","idem_key","operation","expected_head","expected_base","state")):raise LedgerInvalid("LEASE_INTENT")
 if v.get("state") not in {"PENDING","DONE","ABORTED"}:raise LedgerInvalid("LEASE_INTENT_STATE")
 if v.get("epoch")!=epoch:raise LedgerInvalid("LEASE_INTENT_EPOCH")
def _validate_lease_record(v):
 if not isinstance(v,Mapping):raise LedgerInvalid("LEASE_RECORD")
 if type(v.get("version")) is not int or v["version"]<1:raise LedgerInvalid("LEASE_RECORD_VERSION")
 if type(v.get("epoch")) is not int or v["epoch"]<1:raise LedgerInvalid("LEASE_RECORD_EPOCH")
 if not isinstance(v.get("holder"),str) or not v["holder"]:raise LedgerInvalid("LEASE_RECORD_HOLDER")
 if type(v.get("active")) is not bool:raise LedgerInvalid("LEASE_RECORD_ACTIVE")
 if not isinstance(v.get("acquired_at"),(int,float)) or not isinstance(v.get("expires_at"),(int,float)):raise LedgerInvalid("LEASE_TIME")
 _validate_observation(v.get("observed"));_validate_intent(v.get("intent"),v["epoch"])
 if not v["active"] and v.get("intent") is not None and v["intent"].get("state")=="PENDING":raise LedgerInvalid("INACTIVE_PENDING_INTENT")
def validate_ledger(doc:Mapping[str,Any],*,expected_repo:str|None=None)->None:
 if not isinstance(doc,Mapping) or doc.get("schema_version")!=SCHEMA_VERSION:raise LedgerInvalid("LEDGER_SCHEMA")
 repo=doc.get("repository")
 if not isinstance(repo,str) or not repo or (expected_repo is not None and repo!=expected_repo):raise LedgerInvalid("LEDGER_REPOSITORY")
 if type(doc.get("revision")) is not int or doc["revision"]<0:raise LedgerInvalid("LEDGER_REVISION")
 try:mode=RepoMode(doc.get("mode"))
 except Exception as exc:raise LedgerInvalid("LEDGER_MODE") from exc
 if type(doc.get("mode_version")) is not int or doc["mode_version"]<1:raise LedgerInvalid("LEDGER_MODE_VERSION")
 if type(doc.get("human_clear_required")) is not bool or (mode in HUMAN_CLEAR_ONLY and doc["human_clear_required"] is not True):raise LedgerInvalid("LEDGER_HUMAN_CLEAR")
 for key in ("leases","budgets","observations"):
  if not isinstance(doc.get(key),Mapping):raise LedgerInvalid(f"LEDGER_{key.upper()}")
 for row in doc["leases"].values():_validate_lease_record(row)
 pe=doc.get("platform_enforcement")
 if not isinstance(pe,Mapping) or type(pe.get("branch_protected")) is not bool or type(pe.get("active_rulesets")) is not int or pe["active_rulesets"]<0:raise LedgerInvalid("LEDGER_PLATFORM_ENFORCEMENT")
def canonical_json(doc):validate_ledger(doc);return json.dumps(doc,sort_keys=True,indent=2,separators=(",",": "))+"\n"
def blob_cas_ok(observed_blob_sha,expected_blob_sha):return isinstance(observed_blob_sha,str) and bool(observed_blob_sha) and observed_blob_sha==expected_blob_sha
def _begin(doc,expected_revision):
 validate_ledger(doc)
 if doc["revision"]!=expected_revision:raise LedgerConflict("LEDGER_REVISION_STALE")
 out=copy.deepcopy(dict(doc));out["revision"]+=1;return out
def cas_mode(doc,*,expected_revision,expected_mode_version,new_mode,human_clear=False):
 out=_begin(doc,expected_revision)
 if out["mode_version"]!=expected_mode_version:raise LedgerConflict("MODE_VERSION_STALE")
 old=RepoMode(out["mode"])
 if old in HUMAN_CLEAR_ONLY and old!=new_mode and not human_clear:raise LedgerConflict("HUMAN_CLEAR_REQUIRED")
 out["mode"]=new_mode.value;out["mode_version"]+=1;out["human_clear_required"]=new_mode in HUMAN_CLEAR_ONLY;validate_ledger(out);return out
def lease_to_record(lease):return asdict(lease)
def record_to_lease(key,record):
 _validate_lease_record(record);obs=Observation(**dict(record["observed"]));data=record.get("intent");intent=Intent(**dict(data)) if data is not None else None
 return Lease(key=key,holder=record["holder"],epoch=record["epoch"],observed=obs,acquired_at=record["acquired_at"],expires_at=record["expires_at"],version=record["version"],intent=intent,active=record["active"])
def cas_lease(doc,key,*,expected_revision,expected_lease_version,new_record):
 if not isinstance(key,str) or not key:raise LedgerInvalid("LEASE_KEY")
 _validate_lease_record(new_record);out=_begin(doc,expected_revision);cur=out["leases"].get(key);actual=None if cur is None else cur.get("version")
 if actual!=expected_lease_version:raise LedgerConflict("LEASE_VERSION_STALE")
 row=copy.deepcopy(dict(new_record))
 if cur is None:
  if row["version"]!=1 or row["epoch"]!=1:raise LedgerInvalid("LEASE_INITIAL_VERSION_EPOCH")
 else:
  _validate_lease_record(cur)
  if row["version"]<=cur["version"]:raise LedgerInvalid("LEASE_VERSION_NOT_MONOTONIC")
  if row["epoch"]<cur["epoch"]:raise LedgerInvalid("LEASE_EPOCH_REGRESSION")
  if row["epoch"]==cur["epoch"] and row["holder"]!=cur["holder"]:raise LedgerInvalid("LEASE_HOLDER_CHANGED_WITHOUT_NEW_EPOCH")
  old=cur.get("intent")
  if old is not None and old.get("state")=="PENDING":
   new=row.get("intent")
   if new is None or new.get("op_id")!=old.get("op_id"):raise LedgerInvalid("PENDING_INTENT_REPLACED")
  if not cur["active"] and row["active"] and row["epoch"]<=cur["epoch"]:raise LedgerInvalid("LEASE_REACTIVATION_EPOCH")
 out["leases"][key]=row;validate_ledger(out);return out
def cas_budget(doc,key,*,expected_revision,expected_budget_version,new_record):
 out=_begin(doc,expected_revision);cur=out["budgets"].get(key);actual=None if cur is None else cur.get("version")
 if actual!=expected_budget_version:raise LedgerConflict("BUDGET_VERSION_STALE")
 row=copy.deepcopy(dict(new_record))
 if type(row.get("version")) is not int or row["version"]<1 or (expected_budget_version is not None and row["version"]<=expected_budget_version):raise LedgerInvalid("BUDGET_VERSION_NOT_MONOTONIC")
 out["budgets"][key]=row;validate_ledger(out);return out
class LedgerCASStore:
 def __init__(self,backend,repository):self.backend=backend;self.repository=repository
 def _read_document(self):
  doc,sha=self.backend.read();validate_ledger(doc,expected_repo=self.repository)
  if not isinstance(sha,str) or not sha:raise LedgerInvalid("LEDGER_BLOB_SHA")
  return copy.deepcopy(dict(doc)),sha
 def read(self,key):
  doc,_=self._read_document();row=doc["leases"].get(key);return None if row is None else record_to_lease(key,row)
 def list_leases(self):
  doc,_=self._read_document();return [record_to_lease(k,v) for k,v in doc["leases"].items()]
 def cas(self,key,expected_version,value):
  doc,sha=self._read_document()
  try:self.backend.write(cas_lease(doc,key,expected_revision=doc["revision"],expected_lease_version=expected_version,new_record=lease_to_record(value)),sha)
  except LedgerConflict:return False
  return True
 def read_repo_mode(self,repo_id):
  if repo_id!=self.repository:raise LedgerInvalid("LEDGER_REPOSITORY_MISMATCH")
  doc,_=self._read_document();return RepoMode(doc["mode"]),doc["mode_version"]
 def cas_repo_mode(self,repo_id,expected_version,mode):
  if repo_id!=self.repository or expected_version is None:return False
  doc,sha=self._read_document()
  try:self.backend.write(cas_mode(doc,expected_revision=doc["revision"],expected_mode_version=expected_version,new_mode=mode,human_clear=False),sha)
  except LedgerConflict:return False
  return True
class CallbackGitHubLedgerBackend:
 def __init__(self,reader:Callable[[],tuple[Mapping[str,Any],str]],writer:Callable[[str,str],str]):self._reader=reader;self._writer=writer
 def read(self):return self._reader()
 def write(self,document,expected_blob_sha):validate_ledger(document);return self._writer(canonical_json(document),expected_blob_sha)
