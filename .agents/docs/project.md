# Project Context

Fill this document during project initialization. Agents must verify commands against repository configuration before running them.

## Overview

- Product: artel-sdk
- Primary users: TODO
- Core domain: TODO
- Runtime environment: TODO

## Architecture

- Entry points: TODO
- Main modules: TODO
- Dependency direction: TODO
- External systems: GitHub repository `project-artel/artel-sdk`; Notion workspace via the `ntn` CLI; Jira project `ARTEL` via the `mcp-atlassian` MCP server
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
| Install Notion CLI | `curl -fsSL https://ntn.dev \| bash` |
| Verify Notion CLI auth | `ntn whoami` |
| Set up Jira credentials | `cp .jira.env.example .jira.env` |

## Running package tests

The repository root is not a Unity project — the only one is the `samples/WordVenture`
submodule, and its `Packages/manifest.json` has no `testables` entry, so the Test Runner
does not discover `Packages/kr.artel.sdk/Tests` there. Tests run against a throwaway
project that declares the package as a testable.

`.github/scripts/setup-unity-test-project.sh <dest>` assembles that project, and CI runs
the same script, so a local run and a CI run test the same thing. It copies
`.github/unity-test-project/` (the pinned `ProjectSettings/ProjectVersion.txt` and the
`Packages/manifest.json` carrying the package's own dependencies, `com.unity.test-framework`,
every `com.unity.modules.*` the runtime touches — `physics` is required, because
`VirtualMouseMessenger` uses `RaycastHit` — and `"testables": ["kr.artel.sdk"]`), embeds
`Packages/kr.artel.sdk` under the project's `Packages/`, and creates an empty `Assets/`.
The package is embedded rather than referenced with `file:` so the manifest stays
location-independent. An existing `Library/` in the destination is left in place, so
re-running against the same directory keeps the import cache warm.

```bash
.github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test

/Applications/Unity/Hub/Editor/2022.3.34f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -runTests -testPlatform EditMode \
  -projectPath /tmp/artel-unity-test \
  -testResults /tmp/artel-unity-test/results.xml \
  -logFile /tmp/artel-unity-test/unity.log

python3 .github/scripts/summarize-test-results.py /tmp/artel-unity-test/results.xml EditMode
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

Notion access goes through the `ntn` CLI. Agents follow
`.agents/skills/notion-cli/SKILL.md`, which Claude Code reaches through the
`.claude -> .agents` symlink as `.claude/skills/notion-cli`.

Authenticate with a token rather than `ntn login`: export `NOTION_API_TOKEN`
from your shell profile, using a token issued at
`https://www.notion.so/profile/integrations`. The integration must be connected
to each page and data source it needs, otherwise reads return 404. Never commit
the token.

Write operations (`ntn pages create`, `ntn files create`, `ntn workers deploy`)
are not pre-approved and require explicit confirmation.

Jira access goes through the `mcp-atlassian` MCP server, declared in `.mcp.json`
at the repository root. Claude Code starts it on demand and asks for approval
the first time it connects.

Credentials live in `.jira.env`, which the server reads through `--env-file`.
Copy `.jira.env.example` and fill in `JIRA_URL`, `JIRA_USERNAME`, and
`JIRA_API_TOKEN`, issuing the token at
`https://id.atlassian.com/manage-profile/security/api-tokens`. `.gitignore`
excludes `.jira.env`; never commit it.

Unlike `NOTION_API_TOKEN`, the Jira credentials do not come from your shell
profile. The server reads the env file itself, so the setup does not depend on
how Claude Code was launched or on which shell exports the variables. Do not
register a `jira` server in user scope as well, or two copies start.

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
