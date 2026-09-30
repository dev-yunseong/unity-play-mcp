#!/usr/bin/env bash
# MCP server 의 version 을 한 번에 바꾼다.
#
# mcp/package.json 은 npm package 의 version 이고, mcp-server-version.txt 는 Unity Editor(McpConfig)가
# 설치된 MCP server 와 맞는지 확인하는 literal 사본이다. 어긋나면 publish-mcp.yml 의
# "Validate release version contracts" 단계가 잡는다.
#
# MCP server 와 Unity package 는 release 주기가 따로이므로 set-package-version.sh 와 분리한다.
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
manifest="$root/mcp/package.json"
server_version_file="$root/Packages/dev.yunseong.unityplaymcp/Editor/McpConfig/mcp-server-version.txt"

for file in "$manifest" "$server_version_file"; do
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

# mcp-server-version.txt 는 값 하나만 담으므로 trailing newline 하나와 함께 통째로 덮어쓴다.
printf '%s\n' "$version" > "$server_version_file"

echo "MCP server version set to $version"
echo "  $manifest"
echo "  $server_version_file"
echo
echo "Release the same version as npm package unity-play-mcp@$version; publish-mcp.yml checks that mcp/package.json and mcp-server-version.txt match."
