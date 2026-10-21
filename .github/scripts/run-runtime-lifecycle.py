#!/usr/bin/env python3
"""격리된 Unity project에서 실제 domain reload 검사를 실행한다."""
import argparse
import json
import math
from pathlib import Path
import socket
import subprocess
import sys


def validate_result(path):
    result = json.loads(Path(path).read_text(encoding="utf-8"))
    if (result.get("passed") is not True or result.get("domainReloaded") is not True
            or result.get("statusOnlyWake") is not True or result.get("readingsWake") is not True
            or not isinstance(result.get("idleSeconds"), (int, float))
            or not math.isfinite(result["idleSeconds"])
            or not 15 <= result["idleSeconds"] < 20):
        raise ValueError("Lifecycle probe did not establish all required evidence")
    return result


def run(editor, project, timeout=240):
    project = Path(project).resolve()
    if not (project / "Assets/Editor/RuntimeLifecycleProbe.cs").is_file():
        raise ValueError("Assemble the isolated test project before running the probe")
    with socket.socket() as listener:
        # 다른 Editor를 종료하거나 해당 transport에 접속하지 않는다.
        listener.bind(("127.0.0.1", 17311))
    result_path = project / "runtime-lifecycle.json"
    result_path.unlink(missing_ok=True)
    command = [str(editor), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "RuntimeLifecycleProbe.Begin", "-logFile",
               str(project / "runtime-lifecycle.log")]
    completed = subprocess.run(command, timeout=timeout, check=False)
    if completed.returncode:
        raise RuntimeError(f"Unity exited with {completed.returncode}; inspect runtime-lifecycle.log")
    return validate_result(result_path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", required=True)
    parser.add_argument("--project", required=True)
    parser.add_argument("--timeout", type=int, default=240)
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.editor, args.project, args.timeout), indent=2))
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
