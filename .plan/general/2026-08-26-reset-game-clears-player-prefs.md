# 2026-08-26 — reset_game 이 PlayerPrefs 도 지운다

- Date: 2026-08-26
- Jira: ARTEL-499
- Status: Implemented

## Goal

`reset_game` 이 씬만 다시 여는 지금의 동작에 더해, 요청이 그렇게 말할 때 게임의
`PlayerPrefs` 까지 비운다. SDK 자신의 `Artel.*` 키는 살아남아야 한다 — 그것이 지워지면
리셋 한 번에 로그인·프로젝트 선택·인스턴스 등록이 함께 날아가고, 리셋을 시킨 세션이
자기 자신을 끊는다.

와이어 모양은 결정되어 있다: options 오브젝트 하나.

```json
{ "type": "ACTION", "id": 5, "actions": [{ "id": 1, "method": "reset_game", "params": [{ "clearPlayerPrefs": true }] }] }
```

`capture_screen` 의 `maxEdge`/`padding` 이 이미 이 모양이므로 camelCase 필드명을 따른다.

## Non-goals

- 정적 필드나 디스크에 쓴 세이브 파일을 지우는 것. 리로드도 이 flag 도 거기 닿지 못한다.
- 게임이 첫 실행 상태로 돌아간다는 보장. 리로드로 죽는 매니저가 `OnDestroy` 에서 키를
  다시 쓸 수 있고, 이 코루틴 안의 어떤 순서도 그것을 막지 못한다. 문서와 docstring 은
  "저장소를 비웠다"까지만 약속한다.
- 테스트 쪽 키 배열(`Tests/Runtime/SdkLoginTests.cs`, `Tests/Runtime/WebSocketTransportTests.cs`,
  `Tests/PlayMode/OverlayGuiBootstrapTests.cs`, `CursorControllerTests` / `KeyboardStatusControllerTests`
  의 인라인 리터럴)을 레지스트리로 돌리는 것. 그것들은 일부러 독립적으로 다시 적은 것이고,
  둘은 이미 production 과 어긋나 있다(refresh 토큰 키가 빠져 있다). 이 PR 안에서 돌리면
  테스트 다섯 개의 셋업 의미가 조용히 바뀐다.

## Context / Constraints

### 실제 wire 에서 options 캐스트가 항상 null 인 잠복 버그

`Runtime/Capture/CaptureRequest.cs:84` 이 `parameters[1] as IDictionary<string, object>` 로 읽는다.
- `Runtime/Serialization/NewtonsoftJsonCodec.cs:7-13` 은 기본 `JsonSerializerSettings` 를 쓴다.
- `Runtime/Protocol/Dto/ActionRequestDto.cs:15` 는 `List<object> Parameters` 로 선언되어 있다.
- Newtonsoft 는 `object` 자리에 `JObject` 를 넣고, `JObject` 는 `IDictionary<string, JToken>` 를
  구현하지 `IDictionary<string, object>` 를 구현하지 않는다.

그래서 저 캐스트는 실제 wire 에서 언제나 null 이다. 지금까지 터지지 않은 이유는 서버가
capture options 를 보낸 적이 없고, 기존 테스트가 `Dictionary<string, object>` 를 손으로
만들어 넘기기 때문이다. 이제 그 options 경로를 **파괴적인** 액션에 쓰므로 먼저 고친다.

### PlayerPrefs 의 성질

- 키를 열거할 수 없다. 그래서 "SDK 가 쓰는 모든 키를 레지스트리가 담고 있다"는 일반 테스트를
  쓸 수 없고, 대신 세션을 실제로 만들었다가 wipe 후 다시 읽는 테스트로 못을 박는다.
- int / float / string 이 하나의 이름 공간을 공유한다. `Artel.DarkTheme` 은 유일한 int 키이고,
  `GetString` 으로는 왕복하지 않는다.
- 기본값 있는 `GetInt` 로 읽고 되쓰면 없던 키가 생긴다. `Artel.DarkTheme` 에 그렇게 하면
  라이트 테마 사용자가 영영 다크에 고정된다. 그래서 읽기마다 `HasKey` 를 먼저 묻는다.

### 두 secret 키에 플랫폼 `#if` 를 두지 않는 이유

`ArtelSecretStore.CreatePlatformStore()`(`Runtime/Auth/ArtelSecretStore.cs:60-69`)가 macOS 는
Keychain, Windows 는 DPAPI, 나머지는 `PlayerPrefsSecretStore` 를 고른다. 두 키를 조건 없이
적어 두고 `HasKey` 가 판정하게 둔다 — macOS/Windows 에서는 그냥 없고, `DeleteAll()` 은
Keychain/DPAPI 에 닿지도 못한다.

### develop 이 브리핑보다 앞서 있다

`ArtelSdkSession` 에 `Artel.GameBuildId`(`Runtime/Auth/ArtelSdkSession.cs:24`)가 이미 있다.
설계 브리핑은 세션 키 일곱 개를 말하지만 실제로는 여덟 개다. 레지스트리는 열 개를 담는다.
빠뜨리면 리셋 한 번에 근거 문서가 어느 빌드에 붙는지를 SDK 가 잃는다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `ActionExecutor.cs`, `Capture/CaptureRequest.cs`,
      `ArtelSdkIdentity.cs`, `Auth/ArtelSdkSession.cs`, `Auth/ArtelSecretStore.cs`,
      세 오버레이 클래스, 코덱과 DTO, 기존 테스트를 읽는다.

- [x] **Step 1: Implementation**
  1. `Runtime/Protocol/ActionParamsObject.cs` — `TryRead(object, out IReadOnlyDictionary<string, object>)`.
     `IReadOnlyDictionary<string, object>`(테스트가 만든 `Dictionary`) 와 `JObject`(실제 wire)
     둘 다 받는다.
  2. `Runtime/ArtelOwnedPlayerPrefs.cs` — SDK 가 소유한 열 개 키의 유일한 목록과
     `DeleteAllExceptOwn()`. 담아 두기 → `PlayerPrefs.DeleteAll()` → 되쓰기 → `Save()` 한 번.
     사이에 `yield` 를 두지 않는다.
  3. 런타임 선언 자리 다섯 곳을 레지스트리로 돌린다: `ArtelSdkIdentity.cs:8`,
     `Auth/ArtelSdkSession.cs:17-24`, `ArtelOverlayController.cs:15`, `CursorController.cs:13`,
     `KeyboardStatusController.cs:12`. 리터럴이 같으므로 동작 변화는 없다.
  4. `Runtime/ResetRequest.cs` — `ResetRequest` / `ResetRequestReader`.
     params 없음/빈 배열은 `ClearPlayerPrefs = false`(구 서버 호환). `bool` 만 받고
     `"true"` 와 `1` 은 거절한다 — 파괴적인 flag 를 truthy 에서 강제 변환하면 안 된다.
  5. `Runtime/ActionExecutor.cs` — `reset_game` 에 params 를 넘기고, 파싱 실패는
     Build Settings 가드보다 **먼저**, wipe 는 가드보다 **뒤**, `DoomPersistentObjects()` 와
     `LoadSceneAsync` 보다 **앞**에 둔다. XML doc 을 다시 쓴다.
  6. `Runtime/Capture/CaptureRequest.cs:84` 를 공용 리더로 바꾼다.
  7. `Packages/kr.artel.sdk/README.md` `## Resetting the game` 갱신.

- [x] **Step 2: Tests** (EditMode)
  - `Tests/Runtime/ResetGameTests.cs` — `Run` 헬퍼를 `params object[]` 로 넓히고,
    잘못된 params 거절 세 가지, 거절된 리셋이 `PlayerPrefs` 를 건드리지 않는다는 가드-우선
    증명, params 없는 호출이 그대로 동작한다는 구 서버 호환 케이스를 더한다.
  - `Tests/Runtime/ResetParamsWireTests.cs` (신규) — 실제 JSON 을 `NewtonsoftJsonCodec` 로
    통과시켜 `ResetRequestReader` 에 먹인다. 손으로 만든 `Dictionary` 테스트는 `JObject`
    버그가 있어도 통과하므로, 이 파일이 없으면 고른 wire 모양이 전혀 검증되지 않는다.
    같은 코덱을 지나는 `capture_screen` options 케이스 하나로 6번 수정도 못 박는다.
  - `Tests/Runtime/OwnedPlayerPrefsTests.cs` (신규) — SDK 키 생존, 없던 키를 만들지 않음,
    wipe 후에도 세션이 그대로 로드됨. `[SetUp]` 에서 `ArtelSecretStore.Current` 를
    `PlayerPrefsSecretStore` 로 갈아끼운다 — 안 하면 macOS/Windows 에서 무의미한 테스트가 된다.
  - PlayMode 테스트는 두지 않는다. wipe → reload → `Awake` 경로는 Build Settings 에 씬을
    요구하는데 `project.md` 가 그것을 금지한다. PR 에 그렇게 적는다.

- [x] **Step 3: Rollout / Rollback**
  - 이 PR 이 agent-server 의 ARTEL-500 보다 **먼저** 머지된다. 새 서버가 옛 SDK 를 만나면
    flag 가 조용히 사라지고, ACTION 프로토콜에는 그것을 감지할 버전 필드가 없다.
  - 롤백은 `git revert`. flag 를 보내지 않는 서버에게는 동작이 예전과 같다.

## Validation

- **Commands to run:**
  - `.github/scripts/setup-unity-test-project.sh <Windows 에서 보이는 경로>`
  - Windows `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode ...`
  - `python3 .github/scripts/summarize-test-results.py <results.xml> EditMode`
  - PlayMode 로 반복
- **Expected output:** 두 스위트 모두 green. exit code 가 아니라 `results.xml` 을 읽는다 —
  exit 2 는 테스트가 돌았고 일부가 실패했다는 뜻이다.
- **실제로 돈 결과** — 이 기계는 Linux/WSL2 이고 Linux Unity 가 없지만, Windows 쪽에
  고정 에디터 `2022.3.34f1` 이 있어 `C:\temp\artel-unity-test` 에서 그대로 돌렸다.
  EditMode 304 passed / 0 failed, PlayMode 18 passed / 0 failed.
- **음성 대조 두 번** — 검증이 실제로 무언가를 붙잡는지 확인했다. 둘 다 버리는 프로젝트
  안에서만 되돌리고 저장소는 건드리지 않았다.
  1. `CaptureRequest.cs` 의 수정을 예전 캐스트로 되돌리자
     `ResetParamsWireTests.CaptureReadsMaxEdgeFromTheWire` 가
     "capture_screen options must be an object." 로 실패했다 — `JObject` 버그는 실재했다.
  2. `ActionExecutor` 의 `if (request.ClearPlayerPrefs)` 블록을 지우자 아래의 수동
     end-to-end 검사가 "game.progress survived" 로 실패했다.
- **수동 end-to-end 검사** — 커밋된 테스트는 Build Settings 가드에서 되돌아오므로 flag 가
  실제 지우기까지 닿는지를 증명하지 못한다. 버리는 프로젝트에 빈 씬 하나를 Build Settings 에
  넣고 임시 PlayMode 테스트로 한 번 돌렸다: 실제 JSON 을 코덱으로 통과시켜 얻은 params 로
  `reset_game` 을 실행했고, `game.progress`/`game.coins` 는 사라졌으며 토큰·프로젝트·인스턴스·
  `Artel.SdkId` 는 값 그대로 남았다. flag 없는 호출에서는 `game.progress` 가 그대로 남았다.
  이 임시 파일들은 `project.md` 가 금지하는 Build Settings 의존이라 커밋하지 않는다.

## Risks & Rollback

- **Risks:**
  - 레지스트리가 어떤 키를 빠뜨리면 그 키가 리셋에 함께 지워진다. `Artel.GameBuildId` 가
    바로 그 사례가 될 뻔했다. `TheSessionIsStillLoadableAfterAWipe` 가 이 위험에 못을 박지만,
    새 키를 추가하면서 그 테스트도 함께 잊으면 여전히 뚫린다.
  - `OwnedPlayerPrefsTests` 는 진짜로 `PlayerPrefs.DeleteAll()` 을 부른다. `project.md` 가
    말하는 throwaway 프로젝트에서 돌리라는 규칙이 여기서 진짜 의미를 갖는다.
  - 게임이 리셋 뒤에 `OnDestroy` 에서 키를 다시 쓰면 "첫 실행" 처럼 보이지 않는다. 약속하지 않는다.
  - `DeleteAll()` 은 Unity 자신의 항목(`Screenmanager Resolution Width`/`Height`,
    `Screenmanager Fullscreen mode`, `unity.*`)도 가져간다. 그래서 이 flag 를 쓴 리셋은
    다음 실행의 창 크기와 전체화면 선택도 되돌린다. 이름이 Unity 버전에 묶여 있어 허용
    목록으로 지키지 않고, 코드 주석과 README 에 적어 두는 쪽을 골랐다 — 낡은 허용 목록은
    지키지 못하면서 지킨다고 주장한다.
- **Rollback steps:** `git revert`. 서버가 flag 를 보내지 않는 한 동작은 이전과 동일하다.

## Open Questions

- 없음. wire 모양은 결정되어 있고, `Artel.GameBuildId` 는 develop 에서 직접 확인했다.
