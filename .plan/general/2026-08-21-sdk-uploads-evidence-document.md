# 2026-08-21 — SDK 가 근거 문서를 스스로 올린다

- Date: 2026-08-21
- Jira: ARTEL-490
- Status: Done

## Goal

등록 응답의 `gameBuildId` 를 세션이 붙들게 하고, `CaptureUploader` 와 같은 모양의
`EvidenceUploader` 로 근거 문서를 기존 세 걸음(`ticket` → 스토리지 `PUT` →
`content-map` 등록)에 태운다.

## Non-goals

- 원격 스캔 명령 (`scan_evidence`) — ARTEL-491.
- 서버 쪽 트리거 API 와 적재 — ARTEL-492.
- schema 7 (ARTEL-459) 과의 순서 결정 — 우산 이슈(ARTEL-487).
- 재시도. 캡처 업로드가 그렇듯, 다시 올릴지는 시나리오를 쥔 쪽의 판단이다.

## Context / Constraints

**지금 끊긴 자리**

- `AffordanceBootstrap.Save()` 가 `Application.persistentDataPath/artel-affordances.json`
  에 문서를 떨어뜨리고 끝난다. `Packages/` 안에 업로드 · 등록 호출이 0건이다.
- 등록 응답의 `gameBuildId` 는 `SdkRegistrationResponseDto` 에서 죽는다. Runtime 전체에서
  그 값을 읽는 곳은 `Tests/Runtime/WebSocketTransportTests.cs:319` 의 단언 하나뿐이다.

**서버가 실제로 받는 모양** (`artel-orchestration-server` 의
`contentmap/dto/EvidenceDocumentDtos.kt`, `controller/SdkContentMapController.kt` 에서 확인)

```
POST /api/sdk/game-builds/{gameBuildId}/content-map/ticket
     { "contentLength": <long> }
  -> { "objectKey", "uploadUrl", "requiredHeaders": {..}, "uploadExpiresAt" }

PUT  <uploadUrl>   (requiredHeaders 를 그대로 실어야 서명이 맞는다)

POST /api/sdk/game-builds/{gameBuildId}/content-map
     { "objectKey": "..." }
  -> { "contentMapId", "documentId", "capture", "schemaVersion",
       "evidenceDigest", "byteSize", "alreadyRegistered" }
```

티켓 요청 필드는 `byteSize` 가 아니라 **`contentLength`** 다. 등록 요청은 `objectKey`
하나만 받는다 — 서버가 문서 앞부분을 직접 읽으므로 SDK 가 schema · capture 를 신고하지
않는다.

**메모리**

문서는 실측 1,413 KB 다. 통째로 메모리에 올리는 것이 지금 규모에서 문제가 되지 않는다:
`AffordanceReport.Compose()` 가 이미 문서 전체를 한 `string` 으로 지어 `File.WriteAllText`
에 넘기므로 오늘도 이미 통째로 올라와 있고, `UploadHandlerRaw` 가 한 벌 더 복사해도 3 MB
남짓이다. 캡처 한 장의 원시 픽셀(1080p RGBA 는 8 MB)보다 작다. 스트리밍 업로드는 지금
사는 문제가 없으므로 하지 않는다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 끝. 위 Context 가 결과다.

- [x] **Step 1: `gameBuildId` 를 세션이 붙든다**
  - `Runtime/Auth/ArtelSdkSession.cs`: `Artel.GameBuildId` PlayerPrefs 키와
    `SaveGameBuildId` / `TryLoadGameBuildId` / `LoadGameBuildId`, 그리고 `Clear()` 가
    함께 지우는 것. `instanceId` 와 같은 취급 — 그 자체로 아무것도 열지 못하므로
    `ArtelSecretStore` 가 아니라 PlayerPrefs 다.
  - `Runtime/ArtelOverlayViewModel.cs`: 등록 응답을 읽는 자리
    (`ArtelSdkSession.SaveInstanceId(registration.InstanceId)` 바로 옆)에서
    `gameBuildId` 도 저장한다. **없다고 등록을 실패시키지는 않는다** — `instanceId`
    없이는 WebSocket 도 캡처도 붙을 곳을 모르지만, `gameBuildId` 가 없어 막히는 것은
    근거 업로드 하나뿐이고 그 사유는 업로드 결과에 실린다.

- [x] **Step 2: 업로더** — `Runtime/Evidence/EvidenceUploader.cs`
  - `CaptureUploader` 를 본으로 삼는다: `IEvidenceUploader` 인터페이스, 결과 구조체
    `EvidenceUpload`, `Func<Server>` / `Func<string>` 로 자격 증명을 업로드 시점에 읽기.
  - 세 걸음을 순서대로 돌고, 어느 걸음에서 멎었든 그 사유를 결과에 싣는다
    (`HTTP <code>: <body>` — `CaptureUploader.Describe` 와 같은 모양).
  - 토큰이나 `gameBuildId` 가 없으면 첫 걸음을 떼기 전에 그렇다고 답한다.
  - `Runtime/Evidence/` 에 두는 이유: `Runtime/Affordance/` 아래는 별도 어셈블리
    (`Artel.Affordances.Runtime`, `noEngineReferences: true`) 라 `UnityWebRequest` 도
    Newtonsoft 도 닿지 않는다. `Artel.Runtime` 안이어야 한다.
  - DTO 는 `Runtime/Protocol/Dto/EvidenceDocumentDto.cs`.

- [x] **Step 3: Tests** — `Tests/Runtime/SdkLoginTests.cs` 의 세션 왕복 테스트를 넓혀
  `gameBuildId` 가 저장 · 트림 · 조회되고 `Clear()` 로 사라지는 것을 덮는다.

- [x] **Step 4: Rollout / Rollback** — 플래그 없음. 아무도 `Upload` 를 부르지 않으면
  이 커밋은 죽은 코드고, 되돌리기는 `git revert` 한 번이다.

## Validation

- **Commands to run:**

  ```bash
  .github/scripts/setup-unity-test-project.sh <dest>
  Unity -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath <dest> -testResults <dest>/results.xml -logFile <dest>/unity.log
  python3 .github/scripts/summarize-test-results.py <dest>/results.xml EditMode
  # PlayMode 도 같은 방식으로
  ```

- **실측 (Unity 2022.3.34f1):** 두 커밋을 합친 상태로 쟀다.
  - 기준선 — EditMode 274 passed / 0 failed, PlayMode 14 passed / 0 failed
  - 변경 후 — EditMode 280 passed / 0 failed, PlayMode 14 passed / 0 failed
- **자동 테스트로 덮지 못한 것:**
  - 실제 HTTP 세 걸음. 서명한 스토리지 URL 을 내주는 서버가 있어야 한다.
  - 등록 응답에서 `gameBuildId` 를 꺼내는 자리 (`ArtelOverlayViewModel`). 그 코루틴은
    실제 등록 요청을 보낸다.

## Risks & Rollback

- **Risks:**
  - 실제 서버에 대고 세 걸음을 돌려 본 확인이 없다. 필드 이름은 서버 코드에서 읽어 맞췄지만
    (`contentLength`, `objectKey`) 실행으로 확인한 것은 아니다.
  - 등록 응답이 `gameBuildId` 를 주지 않는 서버 버전에서는 업로드가 "붙을 빌드를 모른다"로
    거절된다. 조용히 실패하지는 않는다.
- **Rollback steps:** `git revert`.

## Open Questions

- 이슈 본문은 업로드 시점을 `AffordanceBootstrap.Save()` 자리(씬 로드마다)로 잡고
  "내용이 달라졌을 때만 올린다"는 인수 조건을 단다. 이번 작업의 지시는 자동 업로드를
  범위 밖으로 두고 트리거를 서버가 보내는 액션 하나로 못 박았으므로, 그 두 조건은
  구현하지 않았다. 중복은 서버가 `content_hash` 로 접고 `alreadyRegistered` 로 답한다.
