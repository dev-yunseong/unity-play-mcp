#!/usr/bin/env bash
#
# Assemble the throwaway Unity project used to run the package's tests.
#
# The repository root is not a Unity project and the only Unity project in the
# tree (samples/WordVenture) does not declare dev.yunseong.unityplaymcp as a testable, so the
# Test Runner cannot discover Packages/dev.yunseong.unityplaymcp/Tests anywhere. This script
# materialises a minimal project that does:
#
#   <dest>/ProjectSettings/ProjectVersion.txt   pinned editor version
#   <dest>/Packages/manifest.json               deps + "testables": ["dev.yunseong.unityplaymcp"]
#   <dest>/Packages/dev.yunseong.unityplaymcp/  the package, embedded (not a file: ref)
#   <dest>/Assets/                              empty, required by Unity
#
# Everything except the package copy comes from .github/unity-test-project/, so
# CI and a local run use the exact same project definition.
#
# An existing <dest>/Library is left untouched so a restored CI cache survives.
#
# Usage: .github/scripts/setup-unity-test-project.sh <dest>

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
template="${repo_root}/.github/unity-test-project"
package="${repo_root}/Packages/dev.yunseong.unityplaymcp"

dest="${1:-}"
if [ -z "${dest}" ]; then
  echo "usage: $0 <dest>" >&2
  exit 64
fi

mkdir -p "${dest}"
dest="$(cd "${dest}" && pwd)"

cp -R "${template}/." "${dest}/"
mkdir -p "${dest}/Assets"

rm -rf "${dest}/Packages/dev.yunseong.unityplaymcp"
cp -R "${package}" "${dest}/Packages/dev.yunseong.unityplaymcp"

echo "Unity test project ready at ${dest}"
echo "  editor:  $(sed -n 's/^m_EditorVersion: //p' "${dest}/ProjectSettings/ProjectVersion.txt")"
echo "  library: $([ -d "${dest}/Library" ] && echo present || echo absent)"
