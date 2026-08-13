#!/usr/bin/env python3
"""Unity 로그에서 자격 증명을 지운 사본을 만든다.

업로드하는 이유는 라이선스 활성화 실패를 진단하려는 것인데, 하필 그 실패 경로에서
Unity가 계정 이메일과 시리얼을 로그에 남긴다. GitHub의 시크릿 마스킹은 콘솔 출력에만
걸리고 아티팩트 파일 내용은 훑지 않으므로, 원본을 그대로 올리면 저장소 읽기 권한이
있는 사람은 누구나 받아 볼 수 있다.

시크릿 값은 인자가 아니라 환경 변수로 받는다. 명령줄에 실으면 `ps`와 워크플로 로그
양쪽에 남는다.
"""

import os
import re
import shutil
import sys

REDACTED = "***REDACTED***"

# 시크릿이 등록되지 않은 경우까지 덮으려는 일반 패턴. 값 기반 치환이 1차이고 이건 그물이다.
PATTERNS = [
    # 이메일
    re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"),
    # Unity 시리얼 (예: SC-XXXX-XXXX-XXXX-XXXX-XXXX)
    re.compile(r"\b[A-Z]{2}-(?:[A-Z0-9]{4}-){4}[A-Z0-9]{4}\b"),
    # ulf/xml 라이선스 조각
    re.compile(r"<Signature[\s\S]*?</Signature>"),
]

SECRET_ENV_NAMES = ("UNITY_EMAIL", "UNITY_PASSWORD", "UNITY_SERIAL")


def secret_values():
    """길이 4 이하는 버린다. 짧은 값으로 치환하면 로그가 온통 마스크가 되어 못 읽는다."""
    values = []
    for name in SECRET_ENV_NAMES:
        value = os.environ.get(name, "").strip()
        if len(value) > 4:
            values.append(value)
    # 긴 것부터 지워야 짧은 값이 긴 값의 일부를 먼저 갉아먹지 않는다.
    return sorted(values, key=len, reverse=True)


def scrub(text, values):
    for value in values:
        text = text.replace(value, REDACTED)
    for pattern in PATTERNS:
        text = pattern.sub(REDACTED, text)
    return text


def main():
    if len(sys.argv) != 3:
        print("usage: scrub-unity-log.py <source-dir> <destination-dir>", file=sys.stderr)
        return 2

    source, destination = sys.argv[1], sys.argv[2]
    if not os.path.isdir(source):
        print(f"no Unity log directory at {source}; nothing to scrub")
        return 0

    values = secret_values()
    shutil.rmtree(destination, ignore_errors=True)
    os.makedirs(destination, exist_ok=True)

    scrubbed = 0
    for root, _, files in os.walk(source):
        for name in files:
            path = os.path.join(root, name)
            relative = os.path.relpath(path, source)
            target = os.path.join(destination, relative)
            os.makedirs(os.path.dirname(target), exist_ok=True)

            # 읽지 못한 파일을 원본째 넘기면 지우려던 것이 그대로 나간다. 건너뛰는 쪽이 안전하다.
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as handle:
                    text = handle.read()
            except OSError as error:
                print(f"skipping {relative}: {error}")
                continue

            with open(target, "w", encoding="utf-8") as handle:
                handle.write(scrub(text, values))
            scrubbed += 1

    print(f"scrubbed {scrubbed} log file(s) into {destination}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
