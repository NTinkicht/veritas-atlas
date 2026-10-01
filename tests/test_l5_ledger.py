#!/usr/bin/env python3
import copy,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"scripts"))
from l5_kernel import Intent,Lease,Observation,RepoMode
from l5_ledger import *
def base_doc():return {"schema_version":2,"repository":"repo","revision":0,"mode":"GOVERNANCE_DRIFT","mode_version":1,"human_clear_required":True,"platform_enforcement":{"branch_protected":False,"active_rulesets":0},"leases":{},"budgets":{},"observations":{}}
def lease(version=1,epoch=1,active=True,intent=None,holder="run-a"):return Lease(key="k",holder=holder,epoch=epoch,observed=Observation("a"*40,"b"*40),acquired_at=1,expires_at=10,version=version,intent=intent,active=active)
class Backend:
 def __init__(self,doc):self.doc=copy.deepcopy(doc);self.sha="sha-1"
 def read(self):return copy.deepcopy(self.doc),self.sha
 def write(self,document,expected_blob_sha):
  if expected_blob_sha!=self.sha:raise LedgerConflict("BLOB_STALE")
  self.doc=copy.deepcopy(document);self.sha="sha-"+str(int(self.sha.split("-")[1])+1);return self.sha
class T(unittest.TestCase):
 def test_validate(self):validate_ledger(base_doc(),expected_repo="repo")
 def test_human_clear(self):
  with self.assertRaises(LedgerConflict):cas_mode(base_doc(),expected_revision=0,expected_mode_version=1,new_mode=RepoMode.NORMAL)
 def test_pending_intent_cannot_disappear(self):
  p=Intent("op","i","merge","a"*40,"b"*40,1);d=cas_lease(base_doc(),"k",expected_revision=0,expected_lease_version=None,new_record=lease_to_record(lease(intent=p)))
  with self.assertRaises(LedgerInvalid):cas_lease(d,"k",expected_revision=1,expected_lease_version=1,new_record=lease_to_record(lease(version=2,intent=None)))
 def test_epoch_floor(self):
  d=cas_lease(base_doc(),"k",expected_revision=0,expected_lease_version=None,new_record=lease_to_record(lease()));d=cas_lease(d,"k",expected_revision=1,expected_lease_version=1,new_record=lease_to_record(lease(version=2,active=False)))
  with self.assertRaises(LedgerInvalid):cas_lease(d,"k",expected_revision=2,expected_lease_version=2,new_record=lease_to_record(lease(version=3,epoch=1,holder="b")))
  d=cas_lease(d,"k",expected_revision=2,expected_lease_version=2,new_record=lease_to_record(lease(version=3,epoch=2,holder="b")));self.assertEqual(d["leases"]["k"]["epoch"],2)
 def test_store_cas(self):
  b=Backend(base_doc());s=LedgerCASStore(b,"repo");x=lease();self.assertTrue(s.cas("k",None,x));self.assertEqual(s.read("k"),x)
if __name__=="__main__":unittest.main()
