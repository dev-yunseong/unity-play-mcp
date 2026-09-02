#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <unity-play-mcp-tarball>" >&2
  exit 64
fi

tarball_input="$1"
if [ ! -f "$tarball_input" ]; then
  echo "MCP package tarball does not exist: $tarball_input" >&2
  exit 1
fi
tarball="$(cd "$(dirname "$tarball_input")" && pwd)/$(basename "$tarball_input")"

entries="$(tar -tzf "$tarball")"
require_entry() {
  if ! grep -Fxq "$1" <<<"$entries"; then
    echo "Package is missing required entry: $1" >&2
    exit 1
  fi
}

require_entry "package/package.json"
require_entry "package/LICENSE"
require_entry "package/README.md"
require_entry "package/dist/index.js"

if grep -Eq '^package/dist/test/|\.d\.ts$|\.map$' <<<"$entries"; then
  echo "Package contains test output, declarations, or source maps." >&2
  exit 1
fi

if ! tar -xOf "$tarball" package/dist/index.js | head -n 1 | grep -Fxq '#!/usr/bin/env node'; then
  echo "Package binary does not start with the Node.js shebang." >&2
  exit 1
fi

binary_mode="$(tar -tvzf "$tarball" package/dist/index.js | awk '{print $1}')"
if [[ "$binary_mode" != *x* ]]; then
  echo "Package binary is not executable: $binary_mode" >&2
  exit 1
fi

package_bin="$(tar -xOf "$tarball" package/package.json | node --input-type=module -e '
  let input = "";
  process.stdin.on("data", (chunk) => { input += chunk; });
  process.stdin.on("end", () => {
    const packageJson = JSON.parse(input);
    process.stdout.write(packageJson.bin?.["unity-play-mcp"] ?? "");
  });
')"
if [ "$package_bin" != "dist/index.js" ]; then
  echo "Package bin.unity-play-mcp must be dist/index.js; got: $package_bin" >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
cleanup() {
  rm -rf "$temporary_directory"
}
trap cleanup EXIT

pushd "$temporary_directory" >/dev/null
npm install --ignore-scripts --no-package-lock "$tarball" >/dev/null
binary="${temporary_directory}/node_modules/.bin/unity-play-mcp"

set +e
tail -f /dev/null | timeout 2s "$binary" >server.stdout 2>server.stderr
server_exit_code=${PIPESTATUS[1]}
set -e

if [ "$server_exit_code" -ne 124 ]; then
  cat server.stderr >&2 || true
  echo "Installed MCP binary exited before it could wait for standard input (exit $server_exit_code)." >&2
  exit 1
fi
popd >/dev/null

echo "Verified MCP package: $tarball"
