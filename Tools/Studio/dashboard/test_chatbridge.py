"""Chat launch contract tests without creating real user conversations."""

import io
import json
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

try:
    from . import chatbridge
except ImportError:
    import chatbridge


THREAD = "31bfa802-7d89-48c7-bca2-7885dc4bfc49"


class InputCapture(io.StringIO):
    def close(self):
        self.written = self.getvalue()
        super().close()


class ChatLaunchTests(unittest.TestCase):
    def test_new_triage_task_uses_stdin_and_emitted_thread_id(self):
        card = {"id": "BUG-C1", "title": 'Кавычки ", перенос\nи $(текст)', "kind": "note"}
        process = Mock(stdin=InputCapture(), stdout=io.StringIO(
            json.dumps({"type": "thread.started", "thread_id": THREAD}) + "\n"
        ))
        with patch.object(chatbridge, "_executable", return_value="codex.exe"), \
                patch.object(chatbridge.subprocess, "Popen", return_value=process) as popen:
            result = chatbridge.launch(Path.cwd(), card, True)
        self.assertEqual(result["url"], "codex://threads/" + THREAD)
        self.assertTrue(result["created"])
        self.assertNotIn(card["title"], popen.call_args.args[0])
        self.assertIn("bug_triage_analyst.toml", process.stdin.written)
        self.assertEqual(json.loads(process.stdin.written.split("\n\n", 1)[1]), card)

    def test_reopen_never_submits_another_turn(self):
        with patch.object(chatbridge.subprocess, "Popen") as popen:
            self.assertFalse(chatbridge.launch(Path.cwd(), {"chat": THREAD}, False)["created"])
            with self.assertRaises(ValueError):
                chatbridge.launch(Path.cwd(), {"chat": "https://example.org"}, False)
            popen.assert_not_called()

    def test_failed_cli_does_not_invent_a_thread(self):
        process = Mock(stdin=InputCapture(), stdout=io.StringIO('{"type":"error"}\n'))
        with patch.object(chatbridge, "_executable", return_value="codex.exe"), \
                patch.object(chatbridge.subprocess, "Popen", return_value=process):
            with self.assertRaisesRegex(RuntimeError, "отклонил"):
                chatbridge.launch(Path.cwd(), {"id": "BUG-C1"}, False)


if __name__ == "__main__":
    unittest.main()
