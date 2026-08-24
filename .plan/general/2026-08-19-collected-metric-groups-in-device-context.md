# 2026-08-19 — 수집 지표군 목록을 DEVICE_CONTEXT에 싣는다

- Date: 2026-08-19
- Jira: [ARTEL-486](https://artel-asm.atlassian.net/browse/ARTEL-486)
- Status: Implemented

## Goal

`DEVICE_CONTEXT`에 `collectedGroups`를 실어, 서버가 지표군 가용성을 3상태로 가를 수 있게 한다.
지금은 이 필드가 없어 `UNSUPPORTED`가 `NOT_REPORTED`로 뭉개진다.

## Non-goals

- 새 지표군 수집 (ARTEL-350/351/352)
- 서버·화면 변경. 양쪽 다 이미 이 필드를 받는다 (ARTEL-435/436, 머지 완료)
- SDK 버전 문자열 체계 변경

## Context / Constraints

- 서버 `SdkPerformanceMessage`는 `type`·`id`·`frameTimes`·`status`·`process`만 이름 붙이고,
  **나머지 객체 필드를 전부 지표군으로 받는다**(`@JsonAnySetter captureUnknown`).
  따라서 오늘 SDK가 보내는 군은 `frameTiming`과 `editorRender` 둘뿐이다.
- 서버 `KnownMetricGroups.names` = `editorRender`, `frameTiming`, `gc`, `renderCounters`,
  `sdkOverhead`. 이름은 서버가 정한 계약이고 SDK가 발명하지 않는다.
- 아직 수집하지 않는 군을 선언하면 서버가 `UNSUPPORTED`라고 답한다 — "재려 했으나 못 쟀다"는
  거짓말이 된다. 선언은 실제 시도와 일치해야 한다.
- `RuntimeEnvironment`는 읽기만 하고 계산하지 않는다는 기존 방침을 지킨다.

## 목록을 플랫폼 조건부로 두지 않는 이유

`editorRender`를 `#if UNITY_EDITOR`로 감싸면 Standalone 세션이 구버전 SDK와 똑같이
`NOT_REPORTED`로 보인다. 두 축을 나눈다:

| 축 | 무엇이 답하나 | 서버 판정 |
|---|---|---|
| 이 SDK 버전이 그 군을 아는가 | `collectedGroups` 포함 여부 | 없으면 `NOT_REPORTED` |
| 이 플랫폼에서 값이 나오는가 | 보고에 값이 실렸는가 | 있으면 `MEASURED`, 없으면 `UNSUPPORTED` |

## Approach (Checklist)

- [x] **Step 1: 이름을 한 곳에 모은다**
  - [x] `Runtime/Protocol/MetricGroupNames.cs` — 와이어 이름 `const`와 `Collected` 목록
  - [x] `PerformanceMessageDto`의 `[JsonProperty("frameTiming")]` / `("editorRender")`가
        같은 `const`를 쓰게 바꾼다. 이름이 두 곳에서 갈릴 수 없게 된다
- [x] **Step 2: 실어 보낸다**
  - [x] `DeviceContextDto.CollectedGroups`
  - [x] `RuntimeEnvironment.ReadDeviceContext()` — 목록을 복사해 담는다(공유 배열 노출 금지)
- [x] **Step 3: 잊을 수 없게 만든다**
  - [x] EditMode 테스트: `PerformanceMessageDto`에서 서버가 이름 붙이지 않은 객체 속성을
        리플렉션으로 모아 `Collected`와 집합이 같은지 단정. 군을 늘리고 목록을 안 고치면 깨진다
  - [x] EditMode 테스트: `ReadDeviceContext()`가 목록을 비우지 않고 싣는지

## Risks

- **테스트가 서버의 고정 필드 이름을 복제한다.** 서버가 `SdkPerformanceMessage`에 이름을
  하나 더 붙이면 SDK 테스트가 모르고 그 필드를 군으로 센다. 테스트 안에 출처를 주석으로
  박아 두고, 어긋나면 테스트가 깨지는 쪽(거짓 음성이 아니라 거짓 양성)으로 둔다.
- **Standalone 동작은 이 저장소에서 검증할 수 없다.** 테스트 어셈블리가
  `includePlatforms: [Editor]`라, 목록을 `#if UNITY_EDITOR`로 감싸도 에디터에서는 통과한다.
  그래서 "플랫폼에 따라 달라지지 않는다"를 단정하는 테스트는 **두지 않았다** — 통과할 근거가
  없는 단정은 없는 커버리지를 있다고 광고한다. 확인은 Standalone 빌드가 필요하고 PR에 미검증으로 남긴다.

## Validation

- `Artel.Runtime.Tests` EditMode 전체 — CI(`unity-tests.yml`)가 PR에서 돌린다
- 로컬 Unity 없음. 컴파일·테스트는 CI 결과로만 확인한다
