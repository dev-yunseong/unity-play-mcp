"""실행 실패나 불완전한 artifact가 성공으로 바뀌지 않는지 확인한다."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("runner", Path(__file__).with_name("run-runtime-lifecycle.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class LifecycleRunnerTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.project = Path(self.directory.name)
        probe = self.project / "Assets/Editor/RuntimeLifecycleProbe.cs"
        probe.parent.mkdir(parents=True)
        probe.touch()
        self.path = self.project / "runtime-lifecycle.json"
        self.good = dict(passed=True, domainReloaded=True, statusOnlyWake=True, readingsWake=True, idleSeconds=15.1)

    def test_rejects_missing_or_partial_evidence(self):
        with self.assertRaises(FileNotFoundError):
            runner.validate_result(self.path)
        for change in ({"passed": False}, {"domainReloaded": False}, {"idleSeconds": 14.9},
                       {"statusOnlyWake": False}, {"readingsWake": False}, {"idleSeconds": 20},
                       {"idleSeconds": float("nan")}, {"idleSeconds": float("inf")}):
            self.path.write_text(json.dumps(self.good | change))
            with self.assertRaises(ValueError):
                runner.validate_result(self.path)

    @patch.object(runner.socket, "socket")
    @patch.object(runner.subprocess, "run")
    def test_success_requires_fresh_artifact_and_safe_arguments(self, execute, socket):
        self.path.write_text(json.dumps(self.good))
        def finish(command, **kwargs):
            self.assertFalse(self.path.exists())
            self.assertEqual(command[0], "Editor with spaces")
            self.assertNotIn("-quit", command)
            self.assertEqual(kwargs["timeout"], 240)
            self.path.write_text(json.dumps(self.good))
            return subprocess.CompletedProcess(command, 0)
        execute.side_effect = finish
        self.assertTrue(runner.run("Editor with spaces", self.project)["passed"])

    @patch.object(runner.socket, "socket")
    @patch.object(runner.subprocess, "run")
    def test_failed_exit_and_timeout_are_not_passes(self, execute, socket):
        execute.return_value = subprocess.CompletedProcess([], 1)
        with self.assertRaises(RuntimeError):
            runner.run("editor", self.project)
        execute.side_effect = subprocess.TimeoutExpired("editor", 1)
        with self.assertRaises(subprocess.TimeoutExpired):
            runner.run("editor", self.project, 1)

    @patch.object(runner.socket, "socket")
    @patch.object(runner.subprocess, "run")
    def test_occupied_port_does_not_launch_editor(self, execute, socket):
        socket.return_value.__enter__.return_value.bind.side_effect = OSError("occupied")
        with self.assertRaises(OSError):
            runner.run("editor", self.project)
        execute.assert_not_called()


if __name__ == "__main__":
    unittest.main()
