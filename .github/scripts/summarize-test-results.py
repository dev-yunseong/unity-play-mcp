#!/usr/bin/env python3
"""Turn Unity's NUnit3 results XML into GitHub annotations and a job summary.

Unity's batch-mode exit code only says "something failed"; the test names and
assertion messages live in the results XML. This renders them where a reviewer
actually looks: `::error::` workflow commands (which surface as annotations on
the pull request) and a Markdown table appended to $GITHUB_STEP_SUMMARY.

Usage: summarize-test-results.py <results.xml> [label]

Exits 0 when every test passed, 1 when at least one failed, and 2 when the
results file is missing or unparseable (a run that produced no results is a
failure to report, not a pass).
"""

from __future__ import annotations

import os
import sys
import xml.etree.ElementTree as ET

MAX_MESSAGE_CHARS = 600


def collapse(text: str | None, limit: int = MAX_MESSAGE_CHARS) -> str:
    if not text:
        return ""
    flat = " ".join(text.split())
    return flat if len(flat) <= limit else flat[: limit - 1] + "…"


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: summarize-test-results.py <results.xml> [label]", file=sys.stderr)
        return 2

    path = sys.argv[1]
    label = sys.argv[2] if len(sys.argv) > 2 else os.path.basename(path)

    if not os.path.isfile(path):
        print(f"::error::{label}: no test results at {path} — Unity produced no results file")
        return 2

    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as exc:
        print(f"::error::{label}: could not parse {path}: {exc}")
        return 2

    total = int(root.get("total") or 0)
    passed = int(root.get("passed") or 0)
    failed = int(root.get("failed") or 0)
    skipped = int(root.get("skipped") or 0)
    inconclusive = int(root.get("inconclusive") or 0)

    failures = []
    for case in root.iter("test-case"):
        if case.get("result") != "Failed":
            continue
        node = case.find("failure")
        message = collapse(node.findtext("message") if node is not None else None)
        stack = collapse(node.findtext("stack-trace") if node is not None else None, 300)
        failures.append((case.get("fullname") or case.get("name") or "<unnamed>", message, stack))

    lines = [
        f"### {label}",
        "",
        f"{passed} passed · **{failed} failed** · {skipped} skipped · {inconclusive} inconclusive · {total} total",
        "",
    ]

    if failures:
        lines += ["| Test | Message |", "| --- | --- |"]
        for name, message, _ in failures:
            cell = message.replace("|", "\\|") or "(no message)"
            lines.append(f"| `{name}` | {cell} |")
        lines.append("")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    text = "\n".join(lines) + "\n"
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text)
    print(text)

    for name, message, stack in failures:
        detail = message or "(no message)"
        if stack:
            detail = f"{detail} — {stack}"
        print(f"::error title={label}: {name}::{detail}")

    return 1 if failed or failures else 0


if __name__ == "__main__":
    sys.exit(main())
