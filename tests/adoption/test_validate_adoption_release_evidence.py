import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import validate_adoption_release_evidence as v


class AdoptionReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.h7 = v.load(v.H7)
        self.h8 = v.load(v.H8)

    def test_current_historical_evidence_validates_as_blocked_only(self):
        result = v.validate(self.h7, self.h8)
        self.assertEqual(result["status"], "VALIDATED_BLOCKED")
        self.assertFalse(result["production_action_authorized"])
        self.assertFalse(result["deployment_claimed"])

    def test_authority_drift_fails_closed(self):
        sample = copy.deepcopy(self.h8)
        sample["companyos_cutover_authorized"] = True
        with self.assertRaisesRegex(ValueError, "AUTHORITY_ESCALATION"):
            v.validate(self.h7, sample)

    def test_deployed_source_or_target_drift_fails(self):
        sample = copy.deepcopy(self.h7)
        sample["writer_inventory"]["render"]["workspace_id"] = "wrong"
        with self.assertRaisesRegex(ValueError, "RENDER_WORKSPACE_DRIFT"):
            v.validate(sample, self.h8)

    def test_secret_bearing_fields_are_rejected(self):
        sample = copy.deepcopy(self.h8)
        sample["neon"]["password"] = "must-never-appear"
        with self.assertRaisesRegex(ValueError, "SECRET_BEARING_FIELD_FORBIDDEN"):
            v.validate(self.h7, sample)

    def test_missing_real_world_blocker_cannot_be_declared_complete(self):
        sample = copy.deepcopy(self.h8)
        sample["open_blockers"] = ["Only some unrelated blocker"]
        with self.assertRaisesRegex(ValueError, "REQUIRED_H8_BLOCKER_MISSING"):
            v.validate(self.h7, sample)


if __name__ == "__main__":
    unittest.main()
