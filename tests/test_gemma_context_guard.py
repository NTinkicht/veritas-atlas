import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import gemma_context_guard as guard


class GemmaContextGuardTests(unittest.TestCase):
    def test_sensitive_paths_fail_closed(self):
        for path in (".env", "ops/secrets/token.txt", "auth.json", "certs/client.pem"):
            with self.subTest(path=path):
                with self.assertRaisesRegex(SystemExit, "SENSITIVE_REPOSITORY_PATH_BLOCKED"):
                    guard.validate_changed_paths([path])

    def test_normal_paths_are_allowed(self):
        guard.validate_changed_paths(["src/service.py", ".github/workflows/gemma-l4-worker.yml"])

    def test_literal_secret_assignment_is_blocked(self):
        literal = "api_key" + "=" + ("A" * 24)
        with self.assertRaisesRegex(SystemExit, "SECRET_LIKE_DIFF_CONTENT_BLOCKED"):
            guard.validate_diff("+config " + literal)

    def test_literal_bearer_token_is_blocked(self):
        literal = "Bearer " + ("B" * 24)
        with self.assertRaisesRegex(SystemExit, "SECRET_LIKE_DIFF_CONTENT_BLOCKED"):
            guard.validate_diff("+Authorization: " + literal)

    def test_private_key_marker_is_blocked(self):
        marker = "-----BEGIN " + "PRIVATE KEY-----"
        with self.assertRaisesRegex(SystemExit, "SECRET_LIKE_DIFF_CONTENT_BLOCKED"):
            guard.validate_diff("+" + marker)

    def test_secret_references_are_not_treated_as_secret_values(self):
        github_ref = "+OPENROUTER_API_KEY: $" + "{{ secrets.OPENROUTER_API_KEY }}"
        env_ref = "+api_key=os.environ['OPENROUTER_API_KEY']"
        guard.validate_diff(github_ref + "\n" + env_ref)


if __name__ == "__main__":
    unittest.main()
