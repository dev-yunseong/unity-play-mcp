# 2026-09-02 — Project Settings 에서 agent 네 곳의 MCP 설정을 쓴다

- Date: 2026-09-02
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/20
- Status: Implemented

## Goal

`Edit > Project Settings > Unity Play MCP` 를 열어, Claude Code · Cursor · VS Code · Codex 네 곳의 MCP 설정
파일에 `unity-play` server entry 를 버튼 하나로 넣고 뺀다. 써 넣는 command 는 이 기계에서 찾은
`mcp/dist/index.js` 의 절대경로다.

## Non-goals

- MCP server 를 npm 에 publish 하거나 `npx` 로 실행하도록 바꾸는 것.
- Unity 가 `npm install && npm run build` 를 대신 실행하는 것.
- 화면에서 command 나 args 를 직접 편집하는 것.
- 네 곳 밖의 agent 지원.

## Context / Constraints

- 패키지의 `Editor/` 에는 affordance code generation 과 reporting 만 있고, `SettingsProvider` 도 `EditorWindow`
  도 없다. editor assembly 를 새로 만든다.
- 패키지가 `com.unity.nuget.newtonsoft-json` 3.2.1 에 의존하므로 JSON 은 `JObject` 로 다룬다. 기존 assembly
  들처럼 `overrideReferences: true` + `precompiledReferences: ["Newtonsoft.Json.dll"]` 로 참조한다.
- 설정 파일이 세 형식으로 갈린다. 써 넣을 최종 모양은 이렇다.

  Claude Code — `<project>/.mcp.json`, Cursor — `<project>/.cursor/mcp.json`:

  ```json
  {
    "mcpServers": {
      "unity-play": {
        "command": "node",
        "args": ["/abs/path/mcp/dist/index.js"]
      }
    }
  }
  ```

  VS Code — `<project>/.vscode/mcp.json` (root key 가 `servers` 이고 `type` 은 `command`·`args` 와 나란한
  형제 field 다):

  ```json
  {
    "servers": {
      "unity-play": {
        "type": "stdio",
        "command": "node",
        "args": ["/abs/path/mcp/dist/index.js"]
      }
    }
  }
  ```

  Codex — `~/.codex/config.toml`:

  ```toml
  [mcp_servers.unity-play]
  command = "node"
  args = ["/abs/path/mcp/dist/index.js"]
  ```

- `<project>` 는 Unity project directory 다. 이 값은 `UnityPlayMcpSettingsProvider` 가
  `Directory.GetParent(Application.dataPath)` 로 **한 번만** 구해 `McpServerLocator` 와 `McpAgent.Catalog`
  양쪽에 넘긴다. 두 곳이 각자 계산하면 같은 개념이 두 가지 방식으로 갈라진다.
- Codex 만 홈 디렉터리 (`Environment.SpecialFolder.UserProfile`) 를 쓴다. 나머지 셋은 project 안이다.
  `.cursor/mcp.json` 은 Cursor 자신의 MCP 설정 파일이므로 다른 도구와 자리를 다투지 않는다.
- server 경로는 기계마다 다르므로 저장소에 커밋할 수 없다. 실행 시점에 package 위치에서 찾는다.
- 기존 파일을 절대 통째로 덮어쓰지 않는다. 다른 MCP server entry 와 (Codex 는) 다른 table 이 살아남아야 한다.
- **파싱에 실패하면 쓰지 않는다.** format 은 예외를 던지고, provider 가 잡아 화면에 error 를 띄운 뒤 파일을
  건드리지 않고 끝낸다. 사람이 손으로 쓴 설정을 깨뜨리는 것이 이 기능이 낼 수 있는 최악의 결과다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 완료. `Editor/Affordance/Reporting/Artel.Affordances.Editor.asmdef` 가 editor
      assembly 의 모양을, `Runtime/Artel.Runtime.asmdef` 가 Newtonsoft 참조 방식을, `Tests/Runtime/
      Artel.Runtime.Tests.asmdef` 가 EditMode test assembly 의 모양을 보여 준다.

- [ ] **Step 1: Implementation** — `Packages/dev.yunseong.unityplaymcp/Editor/McpConfig/` 아래에 새 assembly
      `Artel.McpConfig.Editor` (rootNamespace `Artel.McpConfig.Editor`, `includePlatforms: ["Editor"]`,
      `overrideReferences: true`, `precompiledReferences: ["Newtonsoft.Json.dll"]`,
      `autoReferenced: false`).

  - `McpServerEntry.cs` — `Command` 와 `Arguments` 를 담는 DTO. 세 형식이 모두 이 두 값만 쓴다.
    `type: stdio` 는 VS Code 의 형식 문제라 DTO 가 아니라 `JsonMcpConfigFormat` 이 붙인다.

  - `McpServerLocator.cs` — `mcp/dist/index.js` 를 찾는다. 순수 함수
    `FindEntryPoint(packageRoot, projectRoot, fileExists)` 의 순서는 이렇다:

    1. `packageRoot` 가 있으면 `<packageRoot>/../../mcp/dist/index.js` 를 본다. package 가 저장소의
       `<repo>/Packages/` 에 놓였을 때 (embedded 이든 sample 의 `file:` 참조이든) 여기서 맞는다.
    2. 아니면 `<projectRoot>/mcp/dist/index.js` 를 본다. Unity project 가 저장소 루트인 경우다.
    3. 둘 다 없으면 `null`.

    Unity API 를 읽는 wrapper `FindEntryPoint()` 는 `packageRoot` 를
    `PackageInfo.FindForAssembly(typeof(McpServerLocator).Assembly)?.resolvedPath` 로 구한다. 이 assembly 는
    패키지 안에 있으므로 자기 자신을 물어보면 된다. package 정보가 없으면 (`null`) 후보 1 을 건너뛴다.

  - `IMcpConfigFormat.cs` — `Contains(text, serverName)`, `Add(text, serverName, entry)`,
    `Remove(text, serverName)`. 셋 다 텍스트를 받아 텍스트를 돌려주는 순수 함수라 disk 없이 test 한다.
    읽을 수 없는 텍스트에는 예외를 던진다 (JSON 은 Newtonsoft 의 `JsonReaderException`, TOML 은 형식상
    실패할 자리가 없다). 호출자가 잡는다.

  - `JsonMcpConfigFormat.cs` — root key (`mcpServers` / `servers`) 와 `type: stdio` 를 쓸지 여부를 생성자로
    받는다. 빈 텍스트 (공백만 있는 것 포함) 는 새 `JObject` 로 시작한다.
    **주석이 있는 파일은 쓰지 않고 거절한다.** Newtonsoft 의 `JObject` 는 property 만 자식으로 받으므로
    `CommentHandling.Load` 로 읽어도 object 안의 주석은 담기지 못하고 사라진다 (구현 중에 test 로 확인했다).
    `.vscode/mcp.json` 은 주석을 허용하는 형식이라, 읽어서 다시 쓰면 사용자가 적어 둔 주석이 조용히
    지워진다. `JsonTextReader` 로 훑어 `JsonToken.Comment` 를 만나면 `InvalidOperationException` 을 던지고,
    화면이 손으로 고치라고 말한다. 상태를 읽는 `Contains` 는 파일을 바꾸지 않으므로 거절하지 않는다.

  - `TomlMcpConfigFormat.cs` — `[mcp_servers.<name>]` header 줄부터 **block 을 끝내는 줄** 직전까지를 한
    block 으로 보고 통째로 갈아 끼우거나 지운다. block 을 끝내는 줄은 trim 했을 때 `[` 로 시작하되
    `[mcp_servers.<name>.` 로는 시작하지 않는 줄이다. 자기 sub-table 을 block 에 포함시키는 이 예외가
    필요한 이유: Codex 문서가 env 를 `[mcp_servers.unity-play.env]` 라는 sub-table 로 적으라고 안내한다.
    이것을 다음 table 로 보고 block 을 끊으면, Remove 뒤에 `command` 없는 `[mcp_servers.unity-play.env]` 만
    남아 server 정의가 깨진 채 살아남는다. Add 의 갈아 끼우기도 같은 경계를 쓰므로 사용자가 붙여 둔 env
    sub-table 은 갈아 끼울 때 함께 사라진다 — 우리가 아는 것은 `command` 와 `args` 뿐이므로 block 전체를
    우리 것으로 다시 쓴다.
    header 인식은 `[` 로 여는 것만 본다: 실제 `~/.codex/config.toml` 은 `[projects."/home/..."]` 처럼
    따옴표 낀 header 를 쓴다. 그 밖의 줄은 손대지 않는다. 빈 텍스트에는 block 하나만 남는다. 값은 TOML
    basic string 으로 escape 한다 (backslash 와 큰따옴표).

  - `McpAgent.cs` — 표시 이름 · 설정 파일 절대경로 · format 을 묶은 값, 그리고 네 개를 만드는 static
    factory `Catalog(projectRoot, homeDirectory)` 를 한 파일에 둔다. 값과 그 네 instance 는 언제나 같이
    쓰이므로 파일을 가르지 않는다.

  - `McpConfigFileStore.cs` — 읽기 (없으면 빈 문자열) 와 쓰기 (상위 디렉터리 생성 후) **두 개만** 두는 얇은
    wrapper. 다른 파일 시스템 관심사를 여기에 더하지 않는다.

  - `UnityPlayMcpSettingsProvider.cs` — `[SettingsProvider]` 로 `Project/Unity Play MCP` 를 등록하는 IMGUI
    화면. 위에 entry point 경로, 못 찾았으면 "저장소를 clone 한 뒤 `mcp/` 에서
    `npm install && npm run build` 를 실행하세요" HelpBox 와 함께 Add 버튼 비활성화. package 만 git URL 로
    설치한 사용자에게는 `mcp/` 가 기계 어디에도 없으므로 문구가 clone 부터 말해야 한다. 아래에 agent 네 줄 (이름 · 경로 · 등록 여부 · Add/Remove). 등록 여부는
    화면을 열 때와 쓰기 직후에 다시 읽고, 읽다가 예외가 나면 그 줄에 error 를 띄우고 버튼을 막는다.

- [ ] **Step 2: Tests** — `Packages/dev.yunseong.unityplaymcp/Tests/Editor/` 에 EditMode assembly
      `Artel.McpConfig.Editor.Tests` (`references: ["Artel.McpConfig.Editor"]`,
      `includePlatforms: ["Editor"]`, `optionalUnityReferences: ["TestAssemblies"]`,
      `overrideReferences: true`, `precompiledReferences: ["Newtonsoft.Json.dll"]`,
      `autoReferenced: false`). `overrideReferences` 를
      빠뜨리면 Unity 가 `precompiledReferences` 목록을 아예 쓰지 않고, `com.unity.nuget.newtonsoft-json` 의
      DLL 은 auto reference 가 꺼져 있어서 test 가 `JObject` 를 쓰는 순간 컴파일이 깨진다.
      `Tests/Runtime/Artel.Runtime.Tests.asmdef` 가 둘을 같이 적어 둔 것이 그 이유다.
      기존 `Tests/Runtime` 이 아니라 새 디렉터리인 이유는,
      그 assembly 가 `Artel.Runtime` 계열만 참조하는 runtime test 자리이고 editor assembly 를 참조하면
      성격이 섞이기 때문이다.

  - `JsonMcpConfigFormatTests` — 빈 텍스트에서 새로 만든다 / 다른 server entry 를 보존한다 / 이미 있는
    `unity-play` 를 덮어쓴다 / `unity-play` 만 지운다 / VS Code 모양은 `servers` 와 `type: stdio` 를 쓴다 /
    주석이 있는 파일에는 쓰기를 거절한다 / 그래도 상태는 읽어 준다 / 깨진 JSON 에는 예외를 던진다.
  - `TomlMcpConfigFormatTests` — 다른 table 이 있는 파일에 덧붙인다 / 이미 있는 block 을 갈아 끼운다 /
    block 만 지우고 이웃 table 을 남긴다 / `[mcp_servers.unity-play.env]` sub-table 이 block 과 함께 지워지고
    이웃 `[projects."..."]` 는 남는다 / `Contains` 가 header 를 알아본다 / 경로의 backslash 를 escape 한다 /
    빈 텍스트에서 새로 만든다.
  - `McpServerLocatorTests` — package 의 조부모에서 찾는다 / `packageRoot` 가 `null` 이면 project root 에서
    찾는다 / 둘 다 없으면 `null`.
  - `McpConfigFileStoreTests` — 없는 파일은 빈 문자열로 읽는다 / `.cursor/` 처럼 없는 상위 디렉터리를 만들고
    쓴다. 임시 디렉터리에서 돌리고 끝나면 지운다.
  - 화면은 자동화하지 않는다. 샘플 프로젝트에서 손으로 열어 screen capture 를 PR 에 남긴다.

- [ ] **Step 3: Rollout / Rollback** — editor 전용 신규 assembly 라 기존 동작에 닿지 않는다. flag 없음.
      README 에 설정 창 사용법 한 문단을 더한다. 새 `.cs` · 디렉터리 · asmdef 의 `.meta` 를 함께 commit 한다.
      batch mode test 는 `/tmp` 사본에서 도므로 저장소에 `.meta` 를 만들어 주지 않는다. 사본에서 Unity 가
      만든 `.meta` 를 이름으로 맞춰 저장소로 되가져온다.

## Validation

- **Commands to run:**
  ```bash
  project=/mnt/c/Users/jys09/AppData/Local/Temp/unity-play-mcp-test
  .github/scripts/setup-unity-test-project.sh "$project"
  "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe" \
    -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath 'C:\Users\jys09\AppData\Local\Temp\unity-play-mcp-test' \
    -testResults 'C:\Users\jys09\AppData\Local\Temp\unity-play-mcp-test\results.xml' \
    -logFile 'C:\Users\jys09\AppData\Local\Temp\unity-play-mcp-test\unity.log'
  python3 .github/scripts/summarize-test-results.py "$project/results.xml" EditMode
  ```
  이 기계에 2022.3.34f1 이 Windows Unity Hub 쪽에 설치돼 있다. WSL 경로를 Unity.exe 에 그대로 넘길 수 없으므로
  프로젝트를 `/mnt/c/Users/jys09/AppData/Local/Temp/unity-play-mcp-test` 에 만들고 Windows 경로로 넘긴다.
  이 경로로 실제 실행에 성공했고, 변경 전 baseline 은 **EditMode 181 passed · 0 failed** 다.

- **Expected output:** EditMode 전부 green. 새 test 네 묶음이 결과에 이름으로 보인다.

- **Manual (샘플 프로젝트에서):**
  - Project Settings 에 항목이 뜨고 agent 네 줄이 보인다.
  - entry point 를 못 찾을 때 HelpBox 가 뜨고 Add 가 눌리지 않는다.
  - Add 뒤 파일이 실제로 바뀌고, 줄의 등록 여부가 바로 바뀐다.
  - 이미 등록된 곳에 Add 를 다시 누르면 entry 하나가 덮어써진다 (중복이 생기지 않는다).
  - 등록되지 않은 곳의 Remove 는 파일을 바꾸지 않는다.
  - Remove 뒤 이웃 entry 와 이웃 table 이 남는다.
  - 일부러 깨뜨린 JSON 파일에서 error 가 뜨고 파일이 그대로다.

## Risks & Rollback

- **Risks:**
  - 사람이 손으로 쓴 설정 파일을 깨뜨리는 것. 파싱 실패 시 쓰지 않고 error 를 띄우는 것으로 막는다.
  - TOML 을 정식 parser 없이 다루므로, `mcp_servers` 가 table header 가 아니라 inline table
    (`mcp_servers = { ... }`) 이나 dotted key 로 적힌 파일은 알아보지 못하고 중복 정의를 만들 수 있다. 같은
    이유로, 우리 block 안을 사람이 여러 줄짜리 array 로 고쳐 놓고 그 줄이 `[` 로 시작하면 block 끝을 일찍
    잡는다. Codex 가 쓰는 형식은 table header 쪽이고 실제 `~/.codex/config.toml` 도 그렇다. 한계로 PR 에 적는다.
  - `.vscode/mcp.json` 에 주석을 적어 둔 사용자는 이 화면으로 그 파일을 고치지 못하고 손으로 고쳐야 한다.
    주석을 지우면서 쓰는 것보다 낫다고 보고 거절을 골랐다.
  - package 만 git URL 로 설치한 사용자에게는 `mcp/` 가 없어서 이 화면이 아무 agent 도 설정해 주지 못한다.
    HelpBox 가 clone 을 안내하는 것이 지금 할 수 있는 전부다. npm publish 와 `npx` 는 issue 의 non-goal 이다.
- **Rollback steps:** 새 파일만 더하는 변경이므로 `git revert` 한 번이면 원래대로 돌아간다. 이미 써 넣은
  설정 파일은 각 줄의 Remove 로, 또는 손으로 지운다.

## Pair review 에서 고친 것

구현을 마친 뒤 critic 이 여섯 건을 잡았고 전부 반영했다. 계획에 없던 것들이라 여기 남긴다.

1. **다음 table 을 소개하는 주석이 함께 지워졌다.** block 끝을 "다음 table header 줄" 로 잡으면 그 header 바로
   위의 주석과 빈 줄이 우리 block 에 들어간다. `TryFindBlock` 이 `end` 를 blank 와 `#` 줄 위로 되돌린다.
   우리 block 을 설명하던 주석이 고아로 남는 쪽이, 남의 주석을 지우는 것보다 낫다.
2. **따옴표 낀 header `[mcp_servers."unity-play"]` 를 못 알아봤다.** TOML 은 두 형태를 같은 이름으로 보므로,
   못 알아보면 table 을 하나 더 붙이게 되고 중복 정의는 parse error 라 Codex 가 설정을 통째로 읽지 못한다.
   header 와 sub-table 접두사가 두 형태를 다 받는다.
3. **Remove 경로에서도 `McpServerEntry` 를 만들었다.** `_entryPoint` 가 `null` 인 채 `args` 에 실릴 수 있었다.
   생성을 add 분기 안으로 옮겼다.
4. **project 루트와 홈 디렉터리를 검사 없이 넘겼다.** `null` 이면 `Path.Combine` 이 던지는 예외가 화면 밖으로
   나가 매 frame 깨지고, 홈이 비면 상대경로가 만들어져 엉뚱한 자리에 파일이 생긴다. `Reload` 가 막고 error 를
   띄운다.
5. **`McpAgent.Catalog` 에 test 가 없었다.** 네 경로와 형식이 이 기능의 계약인데 검증되지 않았다.
   `McpAgentCatalogTests` 를 더했다.
6. **JSON 개행이 섞였다.** `JObject.ToString` 은 `Environment.NewLine` 으로 줄을 바꾸므로 Windows 에서 본문만
   CRLF 이고 끝줄만 LF 인 파일이 나왔다. 파일이 쓰던 개행으로 통일한다.

비차단 지적 중 둘도 받았다: server 목록 자리에 object 가 아닌 값이 앉아 있으면 거절한다 (주석을 거절하는
기준과 같다), README 에 JSON 은 다시 쓰이며 서식이 펴진다고 적었다.

## Rejected feedback

- **TOML inline table 을 test 로 못 박자 (fast #11).** 지금 동작이 틀렸다고 아는 입력을 test 로 고정하면
  고칠 때 test 부터 지워야 한다. 한계는 Risks 와 PR 에 글로 남기는 것으로 충분하다.
- **test 를 `Tests/Runtime/McpConfig/` 로 옮기자 (fast #4).** 그 assembly 는 runtime assembly 만 참조한다.
  editor assembly 참조를 거기에 더하면 두 성격이 한 assembly 에 섞인다. 이유를 Step 2 에 적어 두었다.

## Open Questions

- 없음. UI 위치 (Project Settings), 대상 agent 네 곳, server 경로 방식 (빌드된 `dist` 절대경로) 은 사용자가
  정했다.
