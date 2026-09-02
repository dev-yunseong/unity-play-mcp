# 2026-09-02 — agent MCP 설정 scope 선택 지원

- Date: 2026-09-02
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/27
- Status: Implemented

## Goal

Unity Project Settings에서 agent MCP 설정의 `Project`와 `User` scope를 선택하게 한다. 선택한 scope의 path에서만 Add, Remove, Configured 상태를 계산하고, 기존 설정은 각 JSON/TOML format 규칙으로 보존한다.

## Non-goals

- project와 user 설정을 서로 복사하거나 동기화하는 것.
- 기존 설정 파일을 자동 migration하거나 삭제하는 것.
- 새로운 agent를 추가하는 것.

## Context / Constraints

- 현재 Claude Code, Cursor, Visual Studio Code는 project path를 쓰고 Codex는 user home path를 쓴다.
- `McpAgent`가 display name, config path, format을 함께 소유하므로 scope path 계산도 이 경계 안에 둔다.
- Codex는 `$CODEX_HOME/config.toml`만 읽고 `CODEX_HOME`의 기본값은 `~/.codex`다. 그래서 Project scope의 `<Unity project>/.codex/config.toml`은 `CODEX_HOME`을 `<Unity project>/.codex`로 지정해 실행할 때만 적용된다. 이 제약을 설정 창과 README 두 곳에 적는다.
- User scope의 공식 path는 platform별로 계산한다: Claude Code `~/.claude.json`, Cursor `~/.cursor/mcp.json`, Visual Studio Code는 Windows `%APPDATA%/Code/User/mcp.json`, macOS `~/Library/Application Support/Code/User/mcp.json`, Linux `~/.config/Code/User/mcp.json`, Codex `~/.codex/config.toml`.
- Project scope의 현재 path는 유지한다: `.mcp.json`, `.cursor/mcp.json`, `.vscode/mcp.json`, `.codex/config.toml`.
- scope 전환은 파일에 side effect를 내지 않는다. 사용자가 선택한 scope에서 Add 또는 Remove를 눌렀을 때만 해당 path를 쓴다.
- 선택값은 versioned namespace와 canonical project root를 포함한 `EditorPrefs` key에 저장하고 기본값은 `Project`로 둔다. 기존 Codex user 설정은 자동 migration하지 않으므로 기본값 변경 시 기존 user entry가 새 project path로 이동하지 않는다는 안내를 UI와 문서에 표시한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `McpAgent`, SettingsProvider, config store와 기존 catalog tests를 확인하고 agent별 OS 공식 user path를 검증한다.
- [x] **Step 1: Scope model** — `McpConfigScope` enum과 scope·OS별 `McpAgent.Catalog` path 계산을 추가한다. platform provider와 home root를 injection할 수 있게 해 test가 host OS에 의존하지 않도록 한다. agent별 path와 format은 한 catalog에서 계속 관리한다.
- [x] **Step 2: Settings UI** — Project Settings에 `Project`/`User` popup을 추가하고 versioned namespace와 canonical project root 기반 `EditorPrefs` key를 만든다. 값이 없거나 알 수 없는 값이면 `Project`를 사용한다. scope 변경 시 catalog와 status를 즉시 reload하고, 기존 Codex user entry를 자동 migration하지 않는다는 안내를 표시한다. 선택 scope를 받아 catalog/read/mutate/write하는 작은 internal boundary를 두고 UI는 이를 호출한다.
- [x] **Step 3: Tests and docs** — 두 scope × 네 agent path, Windows/macOS/Linux user path, default와 저장값, scope 변경 후 Add/Remove가 선택 path만 바꾸고 반대 scope 파일과 malformed file을 건드리지 않는지 EditMode test로 검증한다. test는 temp root injection과 EditorPrefs cleanup을 사용한다. README.md와 README.ko.md에 두 scope의 의미와 기존 entry 자동 migration 없음, Project Settings가 UI 위치라는 점을 추가한다.
- [x] **Step 4: Review and handoff** — 전체 diff와 `git diff --check`를 검토하고 Unity EditMode test를 실행한 뒤 issue와 draft PR을 갱신한다.

## Validation

- **Commands to run:**
  - `.github/scripts/setup-unity-test-project.sh /mnt/c/Users/jys09/AppData/Local/Temp/unity-play-mcp-test`
  - Unity 2022.3.34f1 `-batchmode -nographics -runTests -testPlatform EditMode`
  - `python3 .github/scripts/summarize-test-results.py <results.xml> EditMode`
  - `git diff --check`
- **Expected output:** scope 관련 test를 포함해 Unity EditMode 전체 green, 0 failed.
- **Result (2026-09-02):** Unity 2022.3.34f1 EditMode 247 passed, 0 failed, 0 skipped. `git diff --check` clean.

## Risks & Rollback

- **Risks:** user-level path는 agent version과 platform에 따라 달라질 수 있다. README와 UI에 실제 path를 표시하고, path 변경은 catalog 한 곳에서만 한다. scope를 바꿔도 기존 파일은 이동하지 않으므로 사용자가 두 곳에 stale entry를 남길 수 있다.
- **Rollback steps:** `git revert`로 scope enum, UI, path catalog, 문서 변경을 되돌린다. 기존 config 파일은 자동으로 건드리지 않는다.

## Open Questions

- 없음. 기본값은 `Project`, scope 변경 시 자동 migration 없음으로 정했다.
