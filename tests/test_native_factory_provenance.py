import unittest

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from native_factory_provenance import (
    attested_material_actors,
    complete_pr_commit_history,
    owner_attestation_allowed,
)


ALIASES = {
    "chatgpt": "chatgpt",
    "codex": "codex",
    "chatgpt-codex-connector": "codex",
}


def comment(body, *, login="NTinkicht", created="2026-09-27T12:00:00Z", updated=None):
    return {
        "body": body,
        "user": {"login": login},
        "created_at": created,
        "updated_at": created if updated is None else updated,
    }


class OwnerMaterialAttestationTests(unittest.TestCase):
    def setUp(self):
        self.sha = "a" * 40


    def test_commit_history_must_match_count_and_github_cap(self):
        commits = [{"sha": str(i)} for i in range(3)]
        self.assertTrue(complete_pr_commit_history(commits, 3))
        self.assertFalse(complete_pr_commit_history(commits, 4))
        self.assertFalse(complete_pr_commit_history(commits, True))
        self.assertFalse(complete_pr_commit_history([{}] * 250, 251))

    def test_exact_immutable_owner_attestation_is_accepted(self):
        actors = attested_material_actors(
            [comment(f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=chatgpt")],
            sha=self.sha,
            aliases=ALIASES,
        )
        self.assertEqual(actors, {"chatgpt"})

    def test_stale_or_edited_attestation_is_rejected(self):
        stale = comment(f"L4-MATERIAL-AUTHORS: sha={'b' * 40} actors=chatgpt")
        edited = comment(
            f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=chatgpt",
            updated="2026-09-27T12:01:00Z",
        )
        self.assertEqual(
            attested_material_actors([stale, edited], sha=self.sha, aliases=ALIASES),
            set(),
        )

    def test_non_owner_attestation_is_rejected(self):
        actors = attested_material_actors(
            [comment(
                f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=chatgpt",
                login="github-actions[bot]",
            )],
            sha=self.sha,
            aliases=ALIASES,
        )
        self.assertEqual(actors, set())

    def test_conflicting_exact_head_attestations_fail_closed(self):
        comments = [
            comment(f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=chatgpt"),
            comment(
                f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=codex",
                created="2026-09-27T12:02:00Z",
            ),
        ]
        self.assertEqual(
            attested_material_actors(comments, sha=self.sha, aliases=ALIASES),
            set(),
        )



    def test_owner_attestation_allowed_for_unsigned_owner_transport(self):
        commits = [{
            "commit": {"verification": {"verified": False}, "message": "change"},
            "author": {"login": "NTinkicht"},
            "committer": {"login": "NTinkicht"},
        }]
        self.assertTrue(owner_attestation_allowed(commits))

    def test_owner_attestation_allowed_for_ambiguous_verified_transport(self):
        commits = [{
            "commit": {"verification": {"verified": True}, "message": "change"},
            "author": {"login": "web-flow"},
            "committer": {"login": "web-flow"},
        }]
        self.assertTrue(owner_attestation_allowed(commits))

    def test_verified_unambiguous_transport_cannot_fallback(self):
        commits = [{
            "commit": {
                "verification": {"verified": True},
                "message": "change without material trailer",
            },
            "author": {"login": "trusted-worker"},
            "committer": {"login": "trusted-worker"},
        }]
        self.assertFalse(owner_attestation_allowed(commits))

    def test_mixed_ambiguous_and_verified_transport_fails_closed(self):
        commits = [
            {
                "commit": {"verification": {"verified": False}, "message": "change"},
                "author": {"login": "NTinkicht"},
                "committer": {"login": "NTinkicht"},
            },
            {
                "commit": {
                    "verification": {"verified": True},
                    "message": "change without material trailer",
                },
                "author": {"login": "trusted-worker"},
                "committer": {"login": "trusted-worker"},
            },
        ]
        self.assertFalse(owner_attestation_allowed(commits))

    def test_duplicate_equivalent_attestations_are_idempotent(self):
        comments = [
            comment(f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=chatgpt,codex"),
            comment(
                f"L4-MATERIAL-AUTHORS: sha={self.sha} actors=codex,chatgpt",
                created="2026-09-27T12:03:00Z",
            ),
        ]
        self.assertEqual(
            attested_material_actors(comments, sha=self.sha, aliases=ALIASES),
            {"chatgpt", "codex"},
        )


if __name__ == "__main__":
    unittest.main()
