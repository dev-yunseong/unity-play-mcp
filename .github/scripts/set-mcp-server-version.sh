#!/usr/bin/env bash
# MCP server 의 version 을 한 번에 옮긴다.
#
# mcp/package.json 은 npm 에 올라가는 package 의 version 이고, mcp-server-version.txt 는
# Unity Editor 쪽(McpConfig)이 그 값을 읽어 설치된 MCP server 와 맞는지 확인하는 literal 사본이다.
# 두 파일이 어긋나면 publish-mcp.yml 의 "Validate release version contracts" 단계가 release 시점에
# 잡아내지만, 잡히기 전에 맞추는 편이 낫다.
#
# set-package-version.sh 는 Unity package.json 과 PackageVersion.cs 만 옮기고 이 두 파일은
# 건드리지 않는다고 스스로 선언한다 — 그 경계를 지키면서 MCP server 두 파일을 옮기려면 별도
# script 가 필요해서 이 파일을 만들었다. Unity 쪽 release 주기와 MCP server 쪽 release 주기는
# 서로 독립적으로 움직인다 (예: PR #46 은 Unity package.json 을 건드리지 않고 이 두 파일만 올렸다).
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

# mcp-server-version.txt 는 값 하나만 담는 literal 파일이라 통째로 덮어쓴다.
# 기존 파일도 trailing newline 을 하나 가지고 있어 그 모양을 그대로 맞춘다.
printf '%s\n' "$version" > "$server_version_file"

echo "MCP server version set to $version"
echo "  $manifest"
echo "  $server_version_file"
echo
echo "Release the same version as npm package unity-play-mcp@$version; publish-mcp.yml checks that mcp/package.json and mcp-server-version.txt match."
