import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock


SCRIPT_PATH = Path(__file__).resolve().parent / "doc_agent.py"
SPEC = importlib.util.spec_from_file_location("doc_agent", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
doc_agent = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(doc_agent)


class ResolveSinceShaTests(unittest.TestCase):
    def test_explicit_since_sha_override_wins(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=True):
            since_sha, reason = doc_agent.resolve_since_sha("abc123")
        self.assertEqual(since_sha, "abc123")
        self.assertIn("explicit", reason)

    def test_before_sha_used_when_available(self) -> None:
        with mock.patch.dict(os.environ, {"BEFORE_SHA": "deadbeef"}, clear=True):
            since_sha, reason = doc_agent.resolve_since_sha("")
        self.assertEqual(since_sha, "deadbeef")
        self.assertIn("BEFORE_SHA", reason)

    def test_pull_request_base_sha_from_event_payload(self) -> None:
        payload = {"pull_request": {"base": {"sha": "feedface"}}}
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", delete=False) as fh:
            json.dump(payload, fh)
            event_path = fh.name
        try:
            with mock.patch.dict(
                os.environ,
                {"GITHUB_EVENT_NAME": "pull_request", "GITHUB_EVENT_PATH": event_path},
                clear=True,
            ):
                since_sha, reason = doc_agent.resolve_since_sha("")
        finally:
            Path(event_path).unlink(missing_ok=True)
        self.assertEqual(since_sha, "feedface")
        self.assertIn("event payload", reason)

    def test_fallback_is_head_parent(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=True):
            since_sha, reason = doc_agent.resolve_since_sha("")
        self.assertEqual(since_sha, doc_agent.DEFAULT_SINCE_SHA)
        self.assertIn("fallback", reason)


if __name__ == "__main__":
    unittest.main()
