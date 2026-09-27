import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import native_factory_provenance as provenance
import native_factory_review_policy as p

AUTHORS = {"reviewer", "owner"}

def user(login):
    return {"login": login}


class NativeFactoryReviewPolicyTests(unittest.TestCase):
    def test_current_issue_fingerprint_can_be_reconciled(self):
        finding = {
            "id": 10, "user": user("reviewer"), "body": "P1: fix authorization",
            "created_at": "2026-09-27T08:00:00Z", "updated_at": "2026-09-27T08:05:00Z",
        }
        fp = p.fingerprint("issue", finding)
        marker = {
            "id": 20, "user": user("owner"),
            "body": f"L4-RESOLVED-FINDING: issue:10 sha={'a'*40} fingerprint={fp}",
            "created_at": "2026-09-27T08:06:00Z", "updated_at": "2026-09-27T08:06:00Z",
        }
        self.assertEqual(
            p.unresolved_substantive_findings(
                reviews=[], issue_comments=[finding, marker], inline_comments=[],
                sha="a"*40, finding_comment_authors=AUTHORS,
            ),
            [],
        )

    def test_edit_after_resolution_invalidates_old_fingerprint(self):
        original = {
            "id": 30, "user": user("reviewer"), "body": "P1: old issue",
            "created_at": "2026-09-27T08:00:00Z", "updated_at": "2026-09-27T08:00:00Z",
        }
        old_fp = p.fingerprint("inline", original)
        edited = dict(original)
        edited["body"] = "P1: new stronger issue"
        edited["updated_at"] = "2026-09-27T08:10:00Z"
        marker = {
            "id": 40, "user": user("owner"),
            "body": f"L4-RESOLVED-FINDING: inline:30 sha={'b'*40} fingerprint={old_fp}",
            "created_at": "2026-09-27T08:11:00Z", "updated_at": "2026-09-27T08:11:00Z",
        }
        self.assertEqual(
            p.unresolved_substantive_findings(
                reviews=[], issue_comments=[marker], inline_comments=[edited],
                sha="b"*40, finding_comment_authors=AUTHORS,
            ),
            ["inline:30"],
        )

    def test_marker_older_than_target_update_fails_closed(self):
        finding = {
            "id": 50, "user": user("reviewer"), "body": "P2: current issue",
            "created_at": "2026-09-27T08:00:00Z", "updated_at": "2026-09-27T08:10:00Z",
        }
        fp = p.fingerprint("review", finding)
        marker = {
            "id": 60, "user": user("owner"),
            "body": f"L4-RESOLVED-FINDING: review:50 sha={'c'*40} fingerprint={fp}",
            "created_at": "2026-09-27T08:09:00Z", "updated_at": "2026-09-27T08:09:00Z",
        }
        self.assertEqual(
            p.unresolved_substantive_findings(
                reviews=[finding], issue_comments=[marker], inline_comments=[],
                sha="c"*40, finding_comment_authors=AUTHORS,
            ),
            ["review:50"],
        )

    def test_severity_label_formats_are_blocking_findings(self):
        for body in (
            "Severity: P2 - unsafe path",
            "**Severity: Medium** - unsafe path",
            "### [P1] unsafe path",
            "### [P0] catastrophic path",
            "P0: catastrophic path",
        ):
            with self.subTest(body=body):
                finding = {
                    "id": 70,
                    "user": user("reviewer"),
                    "body": body,
                    "created_at": "2026-09-27T08:00:00Z",
                    "updated_at": "2026-09-27T08:00:00Z",
                }
                self.assertEqual(
                    p.unresolved_substantive_findings(
                        reviews=[],
                        issue_comments=[finding],
                        inline_comments=[],
                        sha="d" * 40,
                        finding_comment_authors=AUTHORS,
                    ),
                    ["issue:70"],
                )

    def test_human_or_webflow_transport_is_not_native_actor_proof(self):
        commit = {
            "commit": {
                "message": "fix: example\n\nMaterial-Author: ntinkicht",
                "verification": {"verified": True},
            },
            "author": {"login": "NTinkicht"},
            "committer": {"login": "web-flow"},
        }
        self.assertEqual(
            provenance.authenticated_material_actors([commit], aliases={}),
            set(),
        )

    def test_actor_specific_verified_transport_can_prove_material_actor(self):
        commit = {
            "commit": {
                "message": "fix: example\n\nMaterial-Author: codex",
                "verification": {"verified": True},
            },
            "author": {"login": "chatgpt-codex-connector[bot]"},
            "committer": {"login": "chatgpt-codex-connector[bot]"},
        }
        aliases = {"chatgpt-codex-connector[bot]": "codex", "codex": "codex"}
        self.assertEqual(
            provenance.authenticated_material_actors([commit], aliases=aliases),
            {"codex"},
        )

    def test_release_authority_paths_are_protected(self):
        text = (ROOT / "scripts/native_factory_merge.py").read_text(encoding="utf-8")
        self.assertIn('"docs/FIRST_RENDER_STAGING_DEPLOYMENT.md"', text)
        self.assertIn('"docs/operations/onecompany-adoption/docs/VERITAS-H"', text)


if __name__ == "__main__":
    unittest.main()
