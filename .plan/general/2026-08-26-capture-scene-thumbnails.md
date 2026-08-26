# 2026-08-26 — Capture scene thumbnails during the evidence walk

- Date: 2026-08-26
- Jira: ARTEL-503 (umbrella ARTEL-501; orchestration ARTEL-502, home ARTEL-504)
- Branch: `feat/sdk-씬마다-대표-이미지를-한-장-캡처해-올린다-ARTEL-503` (from `origin/develop`)
- Status: Implemented, EditMode·PlayMode green, PR not opened

## Goal

Capture one deterministic thumbnail for every scene the evidence walk visits, upload it through the
orchestration batch ticket, and register success/failure metadata with the evidence document.

## Non-goals

- Multiple UI-state captures per scene.
- Replacing the runtime `capture_screen` action.

## Context / Constraints

The SDK targets Unity 2022.3. Captures must never make evidence generation fail, and the evidence
JSON itself must not change, so the document digest stays stable.

## Approach (Checklist)

- [x] **Step 0: Recon** — inspect scene walking, DDOL cleanup, screen capture, and evidence upload.
- [x] **Step 1: Capture** — read the composited back buffer at the point the walk reads each scene.
- [x] **Step 2: Upload** — request batch tickets, PUT each image, register success/failure metadata.
- [x] **Step 3: Tests** — one scene per capture, duplicate scenes, nameless scenes, capture failure,
      an exception mid-capture, and hook attach/detach.
- [ ] **Step 4: Rollout / Rollback** — revert the SDK commits; the server accepts documents without captures.

## 계획에서 바뀐 것 — 카메라를 직접 렌더하지 않는다

원래 Step 1 은 "Built-in pipeline 의 카메라와 Overlay canvas 를 렌더한다" 였다. 그렇게 하지 않고
`ScreenCapturer` 가 이미 쓰는 back buffer 경로(`ScreenCapture.CaptureScreenshotIntoRenderTexture`)를
부른다. 이유가 둘이다.

1. **Overlay canvas 는 카메라 렌더에 안 담긴다.** 게임 화면에서 사람이 알아보는 것 대부분이 그 UI 라,
   카메라만 렌더하면 씬을 식별하라고 만든 이미지가 정작 식별에 못 쓰인다. 캔버스를
   `ScreenSpaceCamera` 로 잠시 바꿔 끼우는 우회가 있지만, 그 복원까지가 이 기능이 지는 위험이 된다.
2. **render pipeline 을 안 가린다.** back buffer 는 Built-in 이든 URP 든 HDRP 든 그려진 결과 하나다.
   그래서 원래 non-goal 이던 "URP·HDRP 는 v1 제외"가 저절로 없어졌다 — 빼는 게 아니라 그냥 된다.

결과적으로 계획보다 코드가 적고, 복원해야 할 임시 상태가 없다.

## What landed

- `Affordance/Scan/SceneWalkHooks.cs` — 순회가 씬 하나를 읽은 자리에서 바깥이 끼어들 수 있는 지점.
  `Artel.Affordances.Scan` 은 `Artel.Runtime` 을 참조하지 않으므로(참조는 한 방향뿐) 업로드 쪽이
  씬마다 무언가를 하려면 이 자리가 필요하다.
- `Evidence/SceneThumbnails.cs` — `SceneThumbnail` 과 `SceneThumbnailCollector`. 씬당 한 장,
  이름 없는 씬은 건너뛰고, 실패는 `failureCode` 로 적는다. 캡처 코루틴을 한 걸음씩 밀며 예외를 잡아
  순회가 멎지 않게 한다.
- `Evidence/EvidenceUploader.cs` — 배치 티켓 요청, 이미지별 PUT, 등록 body 의 `sceneCaptures`.
  이 경로의 실패는 전부 그 씬의 `failureCode` 가 될 뿐 업로드를 실패시키지 않는다. 서버가 티켓 경로를
  모르면(404) `server-has-no-capture-endpoint` 로 적고 캡처 없이 등록한다.
- `Evidence/EvidenceScan.cs` — 순회 전에 수집기를 붙이고 끝나면 뗀다(`finally`).
- `ArtelManager` — `WalkedEvidenceScan(new ScreenCapturer())`. `capture_screen` 과 같은 경로를 쓴다.

## Validation

- **Commands run:** `.github/scripts/setup-unity-test-project.sh`, Unity 2022.3.34f1 `-runTests -testPlatform EditMode`
- **Result:** 313 passed, 0 failed. 새 `SceneThumbnailCollectorTests` 10건 포함.
- **PlayMode:** 18 passed, 0 failed. 순회를 바꿨으므로 함께 돌렸다.
- **Not run:** 실제 게임에 붙여 화면을 뜬 확인. `samples/WordVenture` 로 돌려 봐야 한다.

## Risks & Rollback

- **Risks:** 씬 수백 개짜리 빌드에서 업로드가 그만큼 길어진다. 한 변 480px·씬당 한 장·최대 256장으로
  묶어 두었다.
- **Residual risk:** back buffer 는 그 프레임에 화면에 있던 것을 그대로 담는다. Artel 자체 오버레이(가상
  커서·키보드 표시)가 켜져 있으면 그것도 함께 찍힌다. `capture_screen` 은 이것을 의도한 동작으로 두고
  있는데, 씬 대표 이미지에도 같은 선택이 맞는지는 실제 화면을 보고 정해야 한다.
- **Rollback steps:** 이 branch 의 commit 을 되돌린다. 서버는 `sceneCaptures` 없는 등록을 그대로 받는다.

## Open Questions

- 씬 대표 이미지에 Artel 오버레이가 찍혀도 되는가. 위 Residual risk 참고.
