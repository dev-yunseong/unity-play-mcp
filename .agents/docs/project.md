# Project Context

Fill this document during project initialization. Agents must verify commands against repository configuration before running them.

## Overview

- Product: Unity Play MCP
- Primary users: TODO
- Core domain: TODO
- Runtime environment: TODO

## Architecture

- Entry points: TODO
- Main modules: TODO
- Dependency direction: TODO
- External systems: GitHub repository `dev-yunseong/unity-play-mcp`
- Persistent data: TODO

## Commands

| Purpose | Command |
|---|---|
| Install dependencies | TODO |
| Run locally | TODO |
| Format | TODO |
| Lint | TODO |
| Type-check | TODO |
| Unit tests | See `## Running package tests` below |
| Integration tests | TODO |
| Build | TODO |

## Running package tests

The repository root is not a Unity project — the only one is the `samples/WordVenture`
submodule, and its `Packages/manifest.json` has no `testables` entry, so the Test Runner
does not discover `Packages/dev.yunseong.unityplaymcp/Tests` there. Tests run against a throwaway
project that declares the package as a testable.

`.github/scripts/setup-unity-test-project.sh <dest>` assembles that project, and CI runs
the same script, so a local run and a CI run test the same thing. It copies
`.github/unity-test-project/` (the pinned `ProjectSettings/ProjectVersion.txt` and the
`Packages/manifest.json` carrying the package's own dependencies, `com.unity.test-framework`,
every `com.unity.modules.*` the runtime touches — `physics` is required, because
`VirtualMouseMessenger` uses `RaycastHit` — and `"testables": ["dev.yunseong.unityplaymcp"]`), embeds
`Packages/dev.yunseong.unityplaymcp` under the project's `Packages/`, and creates an empty `Assets/`.
The package is embedded rather than referenced with `file:` so the manifest stays
location-independent. An existing `Library/` in the destination is left in place, so
re-running against the same directory keeps the import cache warm.

```bash
.github/scripts/setup-unity-test-project.sh /tmp/unity-play-mcp-test

/Applications/Unity/Hub/Editor/2022.3.34f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -runTests -testPlatform EditMode \
  -projectPath /tmp/unity-play-mcp-test \
  -testResults /tmp/unity-play-mcp-test/results.xml \
  -logFile /tmp/unity-play-mcp-test/unity.log

python3 .github/scripts/summarize-test-results.py /tmp/unity-play-mcp-test/results.xml EditMode
```

Swap `-testPlatform EditMode` for `PlayMode` to run the other suite. The play-mode assembly
holds what edit mode cannot drive: `Awake`, `OnEnable`, and `DontDestroyOnLoad` do not run
outside play mode.

Exit code 2 means tests ran and some failed; parse `results.xml` rather than reading the
exit code alone — that is what `summarize-test-results.py` is for, and CI runs the same
script to produce its annotations.

Both platforms are expected to be green in a bare throwaway project: no test needs the host
project to carry scenes in Build Settings, or any other configuration. Take a baseline on
the merge-base commit before attributing any failure to a change.

## Continuous integration

`.github/workflows/unity-tests.yml` runs EditMode and PlayMode on every pull request and on
every push to `develop`, using `game-ci/unity-test-runner@v4` against the throwaway project
described above. The editor version is pinned once, in
`.github/unity-test-project/ProjectSettings/ProjectVersion.txt`; the workflow passes
`unityVersion: auto` so it reads that file rather than repeating the version.

Failing test names and messages surface three ways: as check annotations and a job summary
table written by `.github/scripts/summarize-test-results.py`, as the `Test Results` check
run created by the action, and in the `unity-test-results-<mode>` artifact. The project's
`Library/` (~210 MB, mostly `PackageCache`) is cached per test mode, keyed on the manifest
and `package.json`, so a repeat run skips package resolution and full reimport.

### Required secrets

Unity refuses to start in batch mode without an activated licence, and the licence only
reaches the job through repository secrets. A `preflight` job checks they are present and
**fails the run with the name of each missing secret** rather than passing silently.

| Secret | Needed for | Where to get it |
| --- | --- | --- |
| `UNITY_LICENSE` | Personal (primary path) | Unity Hub → Preferences → Licenses → Add → free personal licence, then paste the full contents of `Unity_lic.ulf` (Windows `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`, Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`) |
| `UNITY_SERIAL` | Pro/Plus, instead of `UNITY_LICENSE` | Unity ID → Subscriptions page |
| `UNITY_EMAIL` | both | the Unity account's login email |
| `UNITY_PASSWORD` | both | the Unity account's password |

Register them under Settings → Secrets and variables → Actions. `GITHUB_TOKEN` is provided
by Actions and needs no setup. The workflow only ever tests whether a secret is non-empty;
it never echoes a value.

### Pull requests from forks

GitHub withholds repository secrets from fork pull requests, so Unity cannot be activated
there. The `preflight` job detects that case, skips the test jobs, and posts a notice
explaining that a maintainer must re-run the tests from a branch in this repository before
merging. Fork pull requests therefore never report a spurious pass — the test jobs show as
skipped, not green.

## Constraints

- Supported platforms:
- Compatibility requirements:
- Performance constraints:
- Security or privacy requirements:

## Ownership

- Maintainers:
- Sensitive modules:
- Changes requiring explicit review:
