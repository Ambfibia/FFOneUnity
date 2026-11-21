"""Isolated Studio regression checks; no real repositories, server or Codex process."""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock


def load_module(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


studio = load_module("studio")
probe_codex = load_module("probe_codex")


class StudioValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.data = self.root / "Docs/Studio"
        self.data.mkdir(parents=True)
        role_dir = self.root / ".codex/agents"
        role_dir.mkdir(parents=True)
        (role_dir / "owner.toml").write_text(
            'name = "owner"\ndescription = "Fixture owner"\ndeveloper_instructions = "Read only"\n',
            encoding="utf-8",
        )
        skill_dir = self.root / ".agents/skills/fixture"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text(
            "---\nname: fixture\ndescription: Isolated fixture\n---\nRead only.\n", encoding="utf-8"
        )
        (self.root / "evidence.txt").write_text("observed", encoding="utf-8")
        self.documents = {
            "orchestration.json": {
                "schema": "ffone.studio.orchestration.v1",
                "integration_owner": "owner",
                "modes": {"DEVELOP": {"roles": ["owner"], "skills": ["fixture"]}},
            },
            "sources.json": {"sources": [{"id": "source", "path": "evidence.txt"}]},
            "provenance.json": {"entries": [{"id": "p1", "sources": ["source"],
                "evidence": ["evidence.txt"], "status": "partial", "confidence": "observed"}]},
            "preservation.json": {"entries": [{"id": "f1", "sources": ["source"],
                "evidence": ["evidence.txt"], "category": "PRESERVE", "confidence": "observed"}]},
        }
        self.write_documents()
        root_patch = mock.patch.object(studio, "ROOT", self.root)
        data_patch = mock.patch.object(studio, "DATA", self.data)
        root_patch.start()
        data_patch.start()
        self.addCleanup(root_patch.stop)
        self.addCleanup(data_patch.stop)

    def write_documents(self):
        for name, value in self.documents.items():
            (self.data / name).write_text(json.dumps(value), encoding="utf-8")

    def test_valid_evidence_and_routing_fixture(self):
        result = studio.validate()
        self.assertTrue(result["passed"], result)

    def test_rejects_unknown_source_and_missing_evidence(self):
        row = self.documents["provenance.json"]["entries"][0]
        row["sources"] = ["absent"]
        row["evidence"] = ["missing.txt"]
        self.write_documents()
        result = studio.validate()
        self.assertFalse(result["passed"])
        self.assertGreaterEqual(len(result["errors"]), 2)

    def test_rejects_invalid_preservation_category(self):
        self.documents["preservation.json"]["entries"][0]["category"] = "DELETE_UNAPPROVED"
        self.write_documents()
        self.assertFalse(studio.validate()["passed"])

    def test_rejects_duplicate_source_identifiers(self):
        self.documents["sources.json"]["sources"] *= 2
        self.write_documents()
        self.assertFalse(studio.validate()["passed"])

    def test_rejects_missing_route_role_and_skill(self):
        route = self.documents["orchestration.json"]["modes"]["DEVELOP"]
        route["roles"] = ["nonexistent"]
        route["skills"] = ["nonexistent"]
        self.write_documents()
        self.assertFalse(studio.validate()["passed"])

    def test_rejects_unknown_integrator(self):
        self.documents["orchestration.json"]["integration_owner"] = "nonexistent"
        self.write_documents()
        self.assertFalse(studio.validate()["passed"], "An unresolvable integration owner must fail validation")

    def test_malformed_frontmatter_is_structured_failure(self):
        path = self.root / ".agents/skills/fixture/SKILL.md"
        path.write_text("---\nname: fixture\nnot-a-field\n---\nRead only.\n", encoding="utf-8")
        result = studio.validate()
        self.assertFalse(result["passed"])
        self.assertTrue(result["errors"])

    def test_source_content_change_detected_even_if_size_and_mtime_match(self):
        source = self.root / "Assets/Runtime/Example.cs"
        source.parent.mkdir(parents=True)
        source.write_bytes(b"before")
        stamp = source.stat()
        with mock.patch.object(studio, "REPOS", ()):
            before = studio.snapshot()
            source.write_bytes(b"after!")
            os.utime(source, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
            after = studio.snapshot()
        key = "Assets/Runtime/Example.cs"
        self.assertEqual(before["protected"][key]["bytes"], after["protected"][key]["bytes"])
        self.assertNotEqual(before["protected"][key]["sha256"], after["protected"][key]["sha256"])

    def test_existing_snapshot_output_rejected_before_expensive_scan(self):
        output = self.root / "baseline.json"
        output.write_text("original", encoding="utf-8")
        with mock.patch.object(studio, "snapshot") as snapshot, \
             mock.patch("sys.argv", ["studio.py", "snapshot", "--output", str(output)]), \
             contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                studio.main()
            snapshot.assert_not_called()
        self.assertEqual(output.read_text(encoding="utf-8"), "original")


class ProbeTests(unittest.TestCase):
    def fake_process(self, stdout):
        process = mock.Mock()
        process.stdout = io.StringIO(stdout)
        process.stdin = io.StringIO()
        process.poll.return_value = None
        process.wait.return_value = 0
        return process

    def test_failed_response_terminates_child(self):
        process = self.fake_process(json.dumps({"id": 1, "error": {"code": -1, "message": "fixture failure"}}) + "\n")
        with mock.patch.object(probe_codex.subprocess, "Popen", return_value=process):
            with self.assertRaises(RuntimeError):
                probe_codex.probe("fake-codex", timeout=0.2)
        process.terminate.assert_called_once()
        process.wait.assert_called_once_with(timeout=5)

    def test_timeout_escalates_to_kill_and_reaps_child(self):
        process = self.fake_process("")
        process.wait.side_effect = [subprocess.TimeoutExpired("fake-codex", 5), 0]
        with mock.patch.object(probe_codex.subprocess, "Popen", return_value=process):
            with self.assertRaises(TimeoutError):
                probe_codex.probe("fake-codex", timeout=0.001)
        process.terminate.assert_called_once()
        process.kill.assert_called_once()
        self.assertEqual(process.wait.call_count, 2)

    def test_project_skill_filter_excludes_similarly_named_sibling(self):
        root = probe_codex.ROOT
        responses = [
            {"id": 1, "result": {"userAgent": "fixture"}},
            {"id": 2, "result": {"data": [{"skills": [
                {"name": "inside", "path": str(root / ".agents/skills/inside/SKILL.md")},
                {"name": "sibling", "path": str(root.parent / (root.name + "-backup") / ".agents/skills/sibling/SKILL.md")},
            ]}]}},
            {"id": 3, "result": {"config": {"agents": {"enabled": True},
                "mcp_servers": {"sensitive": {"env": {"TOKEN": "do-not-export"}}}}, "layers": []}},
        ]
        process = self.fake_process("".join(json.dumps(row) + "\n" for row in responses))
        with mock.patch.object(probe_codex.subprocess, "Popen", return_value=process):
            result = probe_codex.probe("fake-codex", timeout=0.2)
        self.assertEqual([row["name"] for row in result["project_skills"]], ["inside"])
        self.assertNotIn("do-not-export", json.dumps(result))


if __name__ == "__main__":
    unittest.main()
