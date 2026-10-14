import io
import json
import urllib.error
import unittest
import zipfile
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


    def _auto_bundle(self, *, mutate=None):
        dispatcher_run = 9001
        body = (
            "@mistral-vibe\nMISTRAL_EXTERNAL_REVIEW_V1\n"
            f"repo: {self.repo}\npr: {self.pr}\n"
            f"head_sha: {self.head}\nbase_sha: {self.base}\n"
            "material_authors: chatgpt\n"
            f"<!-- ONECOMPANY_L4_AUTO_DISPATCH_V1 run={dispatcher_run} -->\n"
        )
        source = {
            "id": self.dispatch_id,
            "user": {"login": "github-actions[bot]"},
            "created_at": "2026-09-27T12:00:00Z",
            "updated_at": "2026-09-27T12:00:00Z",
            "issue_url": f"https://api.github.com/repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}",
            "body": body,
        }
        dispatcher = {
            "id": dispatcher_run,
            "name": g.DISPATCH_WORKFLOW_NAME,
            "path": g.DISPATCH_WORKFLOW,
            "head_repository": {"full_name": g.HOST_REPO},
            "head_branch": "main",
            "event": "schedule",
            "status": "completed",
            "conclusion": "success",
            "created_at": "2026-09-27T11:59:00Z",
        }
        artifacts = {"artifacts": [
            {"id": 999, "name": g.PROOF_ARTIFACT_NAME, "expired": False},
        ]}
        proof = {"version": 1, "run_id": dispatcher_run, "sources": [
            {"comment_id": self.dispatch_id, "body": body, "repo": self.repo,
             "pr": self.pr, "head": self.head, "base": self.base},
        ]}
        event = "repository_dispatch"
        report = self.report
        if mutate is not None:
            source, dispatcher, artifacts, proof, event, report = mutate(
                source, dispatcher, artifacts, proof, event, report
            )
        pack = io.BytesIO()
        with zipfile.ZipFile(pack, "w") as archive:
            archive.writestr(g.PROOF_FILE, json.dumps(proof))
        def fake(route):
            if route.startswith(f"repos/{g.HOST_REPO}/issues/{g.HOST_ISSUE}/comments"):
                return [report]
            if route == f"repos/{g.HOST_REPO}/issues/comments/{self.dispatch_id}":
                return source
            if route == f"repos/{g.HOST_REPO}/actions/runs/{dispatcher_run}":
                return dispatcher
            if route == f"repos/{g.HOST_REPO}/actions/runs/{dispatcher_run}/artifacts?per_page=100":
                return artifacts
            if route == f"repos/{g.HOST_REPO}/actions/runs/{self.run_id}":
                return dict(self.fake_api(route), event=event)
            raise AssertionError(route)
        return fake, pack.getvalue()

    def test_automated_dispatch_requires_exact_signed_source_artifact(self):
        api, archive = self._auto_bundle()
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertTrue(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_fails_on_forged_artifact_source(self):
        def tamper(source, run, artifacts, proof, event, report):
            proof["sources"][0]["head"] = "d" * 40
            return source, run, artifacts, proof, event, report
        api, archive = self._auto_bundle(mutate=tamper)
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_fails_on_wrong_dispatcher_identity(self):
        def tamper(source, run, artifacts, proof, event, report):
            run["event"] = "pull_request"
            return source, run, artifacts, proof, event, report
        api, archive = self._auto_bundle(mutate=tamper)
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_fails_on_review_event_mismatch(self):
        def tamper(source, run, artifacts, proof, event, report):
            return source, run, artifacts, proof, "issue_comment", report
        api, archive = self._auto_bundle(mutate=tamper)
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_rejects_missing_proof_artifact(self):
        def tamper(source, run, artifacts, proof, event, report):
            artifacts["artifacts"] = []
            return source, run, artifacts, proof, event, report
        api, archive = self._auto_bundle(mutate=tamper)
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_rejects_modified_source_comment(self):
        def tamper(source, run, artifacts, proof, event, report):
            source["updated_at"] = "2026-09-27T12:01:00Z"
            return source, run, artifacts, proof, event, report
        api, archive = self._auto_bundle(mutate=tamper)
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_rejects_wrong_publisher(self):
        def tamper(source, run, artifacts, proof, event, report):
            source["user"] = {"login": "untrusted-agent"}
            return source, run, artifacts, proof, event, report
        api, archive = self._auto_bundle(mutate=tamper)
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=archive):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))

    def test_automated_dispatch_rejects_modified_proof_zip(self):
        api, archive = self._auto_bundle()
        with mock.patch.object(g, "public_json", side_effect=api), \
             mock.patch.object(g, "_download_host_proof", return_value=b"not a zip"):
            self.assertFalse(g.external_mistral_pass(
                repo=self.repo, pr=self.pr, head=self.head, base=self.base,
                authors={"chatgpt"},
            ))


    def test_host_reader_refuses_missing_scoped_credentials(self):
        for absent in (True, False):
            with self.subTest(absent=absent):
                values = {} if absent else dict.fromkeys(("ONECOMPANY_ACTIONS_READ_TOKEN",), "")
                with mock.patch.dict(g.os.environ, values, clear=True):
                    with mock.patch.object(g.urllib.request, "build_opener") as transport:
                        self.assertIsNone(g._download_host_proof(999))
                        transport.assert_not_called()

    def test_host_reader_fails_on_external_redirect_before_unsigned_get(self):
        from email.message import Message
        headers = Message()
        headers["Location"] = "https://evil.example.test/steal"
        redirect = urllib.error.HTTPError(
            "https://api.github.com/repos/NTinkicht/OneCompany/actions/artifacts/999/zip",
            302, "Found", headers, None,
        )
        opener = mock.Mock()
        opener.open.side_effect = redirect
        with mock.patch.dict(g.os.environ, dict.fromkeys(("ONECOMPANY_ACTIONS_READ_TOKEN",), "host-read-only")), \
             mock.patch.object(g.urllib.request, "build_opener", return_value=opener), \
             mock.patch.object(g.urllib.request, "urlopen") as unsigned_get:
            self.assertIsNone(g._download_host_proof(999))
            unsigned_get.assert_not_called()
        sent = opener.open.call_args.args[0]
        self.assertEqual(sent.get_header("Authorization"), "Bearer host-read-only")

    def test_host_token_is_removed_on_allowlisted_artifact_redirect(self):
        from email.message import Message
        headers = Message()
        headers["Location"] = "https://mybucket.blob.core.windows.net/signed-object?sig=test"
        redirect = urllib.error.HTTPError(
            "https://api.github.com/repos/NTinkicht/OneCompany/actions/artifacts/999/zip",
            302, "Found", headers, None,
        )
        opener = mock.Mock()
        opener.open.side_effect = redirect
        stream = mock.MagicMock()
        stream.__enter__.return_value.read.return_value = b"zip data"
        with mock.patch.dict(g.os.environ, dict.fromkeys(("ONECOMPANY_ACTIONS_READ_TOKEN",), "host-read-only")), \
             mock.patch.object(g.urllib.request, "build_opener", return_value=opener), \
             mock.patch.object(g.urllib.request, "urlopen", return_value=stream) as unsigned_get:
            self.assertEqual(g._download_host_proof(999), b"zip data")
            request = unsigned_get.call_args.args[0]
            self.assertIsNone(request.get_header("Authorization"))
            self.assertEqual(request.full_url, headers["Location"])


if __name__ == "__main__":
    unittest.main()
