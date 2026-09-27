import unittest
from unittest import mock

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

import onecompany_external_review_gate as g


class ExternalReviewGateTests(unittest.TestCase):
    def setUp(self):
        self.repo = "NTinkicht/veritas-atlas"
        self.pr = 21
        self.head = "a" * 40
        self.base = "b" * 40
        self.dispatch_id = 111
        self.run_id = 222
        self.report = {
            "id": 333,
            "user": {"login": "github-actions[bot]"},
            "created_at": "2026-09-27T12:10:00Z",
            "updated_at": "2026-09-27T12:10:00Z",
            "body": (
                "**OneCompany external Mistral exact-head review evidence**\n"
                f"VERDICT: PASS\n"
                f"<!-- {g.EXTERNAL_MARKER if hasattr(g, 'EXTERNAL_MARKER') else 'ONECOMPANY_EXTERNAL_MISTRAL_REVIEW_V1'} "
                f"repo={self.repo} pr={self.pr} head={self.head} base={self.base} "
                f"run={self.run_id} dispatch={self.dispatch_id} verdict=PASS "
                f"result_sha256={'c' * 64} -->"
            ),
        }

    def fake_api(self, route):
        if route.startswith(f"repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}/comments"):
            return [self.report]
        if route == f"repos/{g.HOST_REPO}/issues/comments/{self.dispatch_id}":
            return {
                "user": {"login": "NTinkicht"},
                "created_at": "2026-09-27T12:00:00Z",
                "updated_at": "2026-09-27T12:00:00Z",
                "issue_url": f"https://api.github.com/repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}",
                "body": (
                    "@mistral-vibe\n"
                    "MISTRAL_EXTERNAL_REVIEW_V1\n"
                    f"repo: {self.repo}\n"
                    f"pr: {self.pr}\n"
                    f"head_sha: {self.head}\n"
                    f"base_sha: {self.base}\n"
                    "material_authors: chatgpt\n"
                ),
            }
        if route == f"repos/{g.HOST_REPO}/actions/runs/{self.run_id}":
            return {
                "name": g.WORKFLOW_NAME,
                "display_title": f"External Mistral review dispatch {self.dispatch_id}",
                "path": g.WORKFLOW_PATH + "@main",
                "head_branch": "main",
                "event": "issue_comment",
                "status": "completed",
                "conclusion": "success",
                "head_repository": {"full_name": g.HOST_REPO},
            }
        raise AssertionError(route)


    def test_public_api_requires_authenticated_token(self):
        with mock.patch.dict(g.os.environ, {}, clear=True):
            with self.assertRaisesRegex(RuntimeError, "GITHUB_TOKEN_UNAVAILABLE"):
                g.public_json("repos/NTinkicht/OneCompany")

    def test_valid_exact_chain_passes(self):
        with mock.patch.object(g, "public_json", side_effect=self.fake_api):
            self.assertTrue(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))


    def test_visible_verdict_must_match_hidden_marker(self):
        contradictory = dict(self.report)
        contradictory["body"] = self.report["body"].replace(
            "VERDICT: PASS", "VERDICT: CHANGES_REQUIRED"
        )
        def api(route):
            if route.startswith(
                f"repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}/comments"
            ):
                return [contradictory]
            return self.fake_api(route)
        with mock.patch.object(g, "public_json", side_effect=api):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))


    def test_failed_run_does_not_pass(self):
        def api(route):
            value = self.fake_api(route)
            if route == f"repos/{g.HOST_REPO}/actions/runs/{self.run_id}":
                value = dict(value, conclusion="failure")
            return value
        with mock.patch.object(g, "public_json", side_effect=api):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))


    def test_newer_adverse_verdict_supersedes_older_pass(self):
        adverse_dispatch = 444
        adverse_run = 555
        adverse = {
            "id": 666,
            "user": {"login": "github-actions[bot]"},
            "created_at": "2026-09-27T12:20:00Z",
            "updated_at": "2026-09-27T12:20:00Z",
            "body": (
                "**OneCompany external Mistral exact-head review evidence\n"
                "VERDICT: CHANGES_REQUIRED\n"
                f"<!-- ONECOMPANY_EXTERNAL_MISTRAL_REVIEW_V1 "
                f"repo={self.repo} pr={self.pr} head={self.head} base={self.base} "
                f"run={adverse_run} dispatch={adverse_dispatch} "
                f"verdict=CHANGES_REQUIRED result_sha256={'d' * 64} -->"
            ),
        }
        def api(route):
            if route.startswith(
                f"repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}/comments"
            ):
                return [self.report, adverse]
            if route == f"repos/{g.HOST_REPO}/issues/comments/{self.dispatch_id}":
                return self.fake_api(route)
            if route == f"repos/{g.HOST_REPO}/issues/comments/{adverse_dispatch}":
                value = self.fake_api(
                    f"repos/{g.HOST_REPO}/issues/comments/{self.dispatch_id}"
                )
                return dict(
                    value,
                    created_at="2026-09-27T12:15:00Z",
                    updated_at="2026-09-27T12:15:00Z",
                )
            if route == f"repos/{g.HOST_REPO}/actions/runs/{self.run_id}":
                value = self.fake_api(route)
                return dict(value, created_at="2026-09-27T12:05:00Z")
            if route == f"repos/{g.HOST_REPO}/actions/runs/{adverse_run}":
                value = self.fake_api(
                    f"repos/{g.HOST_REPO}/actions/runs/{self.run_id}"
                )
                return dict(
                    value,
                    display_title=f"External Mistral review dispatch {adverse_dispatch}",
                    conclusion="failure",
                    created_at="2026-09-27T12:16:00Z",
                )
            raise AssertionError(route)
        with mock.patch.object(g, "public_json", side_effect=api):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))



    def test_mistral_material_author_cannot_use_mistral_gate(self):
        with mock.patch.object(g, "public_json", side_effect=self.fake_api):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"mistral-vibe"},
            ))

    def test_pagination_bound_exhaustion_fails_closed(self):
        full_page = [dict(self.report, id=index + 1) for index in range(100)]
        calls = {"pages": 0}

        def api(route):
            if route.startswith(
                f"repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}/comments"
            ):
                calls["pages"] += 1
                return full_page
            return self.fake_api(route)

        with mock.patch.object(g, "MAX_PAGES", 2):
            with mock.patch.object(g, "public_json", side_effect=api):
                self.assertFalse(g.external_mistral_pass(
                    repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                    authors={"chatgpt"},
                ))
        self.assertEqual(calls["pages"], 2)


    def test_author_mismatch_does_not_pass(self):
        with mock.patch.object(g, "public_json", side_effect=self.fake_api):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"codex"},
            ))


if __name__ == "__main__":
    unittest.main()
