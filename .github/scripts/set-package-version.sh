#!/usr/bin/env bash
# Unity package 의 version 을 한 번에 바꾼다.
#
# UPM 은 git URL 설치 때 commit 된 package.json 을 그대로 읽으므로 version 은 literal 이어야 한다.
# player build 에는 package.json 이 들어가지 않아 C# 상수(PackageVersion.cs)에도 같은 값을 둔다.
# 어긋나면 EditMode 의 PackageVersionTests 가 잡는다.
#
# mcp/package.json 과 mcp-server-version.txt 는 release 주기가 따로이므로 건드리지 않는다
# (set-mcp-server-version.sh).
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 <version>   e.g. $0 0.3.0" >&2
  exit 2
fi

version="$1"

if ! printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$'; then
  echo "not a semantic version: $version" >&2
  exit 2
fi

root="$(cd "$(dirname "$0")/../.." && pwd)"
manifest="$root/Packages/dev.yunseong.unityplaymcp/package.json"
constant="$root/Packages/dev.yunseong.unityplaymcp/Runtime/Affordance/Scan/PackageVersion.cs"

for file in "$manifest" "$constant"; do
  test -f "$file" || { echo "missing: $file" >&2; exit 1; }
done

# package.json 은 JSON 으로 다시 쓴다. 정규식으로 고치면 같은 모양의 다른 key 까지 잡는다.
node --input-type=module -e '
  import { readFileSync, writeFileSync } from "node:fs";
  const [file, version] = process.argv.slice(1);
  const text = readFileSync(file, "utf8");
  const trailingNewline = text.endsWith("\n");
  const manifest = JSON.parse(text);
  manifest.version = version;
  writeFileSync(file, JSON.stringify(manifest, null, 2) + (trailingNewline ? "\n" : ""));
' "$manifest" "$version"

# 상수는 한 줄뿐이므로 그 줄만 바꾼다.
python3 - "$constant" "$version" <<'PY'
import io, re, sys

path, version = sys.argv[1], sys.argv[2]
text = io.open(path, encoding="utf-8").read()
updated, count = re.subn(
    r'(internal const string Value = ")[^"]*(";)',
    lambda m: m.group(1) + version + m.group(2),
    text,
)
if count != 1:
    raise SystemExit("expected exactly one Value constant, found %d in %s" % (count, path))
io.open(path, "w", encoding="utf-8").write(updated)
PY

echo "Unity package version set to $version"
echo "  $manifest"
echo "  $constant"
echo
echo "Release the same version as tag v$version; publish-mcp.yml checks that they match."
