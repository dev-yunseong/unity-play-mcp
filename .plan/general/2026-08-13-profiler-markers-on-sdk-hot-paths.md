# 2026-08-13 — SDK 핫 패스 ProfilerMarker 계측

- Date: 2026-08-13
- Jira: [ARTEL-413](https://artel-asm.atlassian.net/browse/ARTEL-413)
- Status: Draft

## Goal

SDK를 붙인 게임의 렉이 SDK의 어느 서브시스템에서 오는지 Unity Profiler에서 바로 읽히게 한다.
`ProfilerMarker`를 핫 패스에 심어, Deep Profile 없이 실빌드에서 구간별 ms를 잰다.

## Non-goals

- 실제 최적화(`ReadPixels` → `AsyncGPUReadback`, 스캔 주기 조정, `GetComponents` 할당 제거).
  측정 결과가 나온 뒤 별도 이슈로 뗀다.
- Profiler 데이터를 수집해 서버로 보내는 것.
- 게임 코드 쪽 계측.

## Context / Constraints

- 기존 계측은 `Artel.Diagnostics`의 `FrameTimeRecorder`/`FrameTimingSampler`뿐이다. 프레임 총량만
  나오고 원인 구간을 가리키지 못한다.
- 의심 경로: 1초 주기 `SceneScanner.Scan`(씬 전수 순회 + `GetComponents<Component>()`),
  `StateReader`/`SerializedFieldReader`의 값 읽기, `ScreenCapturer`의 동기 `ReadPixels`.
- `SceneScanner`와 폴링 경로는 ARTEL-398 · ARTEL-400에서 폐기 예정. 마커 이름은 서브시스템 기준으로
  지어 후속 생산자(evidence scan)가 그대로 물려받게 한다.
- 계측만 한다. 동작 변경 금지.

## Approach (Checklist)

- [x] **Step 0: Recon** — `ArtelManager.Update`(216-236), `SceneScanner.Scan`,
      `SceneStatePoller.TryPoll`, `StateReader.Read`, `ScreenCapturer.Capture`,
      `CaptureUploader.Upload`, `ScreenVideoSource.CaptureFrame` 확인
- [ ] **Step 1: Implementation**
  - [ ] `Runtime/Diagnostics/ArtelProfilerMarkers.cs` — `static readonly ProfilerMarker` 한 곳에 모음.
        이름 규칙 `Artel.<Subsystem>.<Operation>`
  - [ ] `ArtelManager` — `Update` 및 `PumpStreaming` · `HandleMessage` · `PollSceneState` ·
        `SendPerformanceReport`
  - [ ] `SceneScanner.Scan(options)` — 씬 순회 전체
  - [ ] `SceneStatePoller` — 스캔 / DTO 매핑 / 해시 관찰 분리
  - [ ] `StateReader.Read` — 태그 멤버 루프와 직렬화 필드 루프를 각각.
        필드 단위가 아니라 컴포넌트 단위로 잰다 (아래 Risks 참고)
  - [ ] `ScreenCapturer` — `ReadPixels`+`Apply` 구간과 인코딩 구간을 분리.
        `yield return endOfFrame`는 감싸지 않는다 — 대기 시간이 비용으로 잡힌다
  - [ ] `CaptureUploader` — 동기 구간(티켓 JSON 직렬화, PUT 요청 구성)만.
        `SendWebRequest()` 대기는 감싸지 않는다
  - [ ] `ScreenVideoSource.CaptureFrame`
- [ ] **Step 2: Tests** — 기존 EditMode 테스트 그대로 통과. 마커는 순수 계측이라 신규 테스트 없음
- [ ] **Step 3: Rollout** — 플래그 없음. `ProfilerMarker`는 non-development 빌드에서 no-op

## Validation

- **Commands to run:**
  - EditMode 테스트: `.agents/docs/project.md`의 throwaway 프로젝트 절차. merge-base 기준 baseline 대조
  - `samples/WordVenture` Development Build + Autoconnect Profiler
- **Expected output:**
  - Profiler Hierarchy에 `Artel.*` 마커가 뜬다
  - 스파이크 주기가 1초면 `Artel.SceneScan.Scan`, 매 프레임이면 `Artel.Capture.*` / `Artel.Stream.*`
  - EditMode 실패 목록이 baseline과 동일 (환경 사유 8건)

## Risks & Rollback

- **Risks:**
  - 마커를 너무 잘게 심으면 Profiler 자체 오버헤드와 샘플 노이즈가 측정을 망친다.
    `StateReader`는 필드 단위가 아니라 컴포넌트 단위로 잰다
  - 코루틴의 `yield` 구간을 감싸면 대기 시간이 CPU 비용으로 보고된다. 동기 구간만 감싼다
- **Rollback steps:** `git revert`. 계측만이라 런타임 동작 의존성이 없다

## Open Questions

- 없음
