# 2026-07-28 — 등록 시 화면 깜박임 수정

- Date: 2026-07-28
- Jira: ARTEL-152
- Status: Implemented

## Goal

인스턴스 키 등록 중 빌드 씬을 순회 스캔하는 동안 게임 화면이 깜박이지 않는다. 대신
Artel 오버레이가 화면을 덮고 진행 상황을 보여 준다.

## Non-goals

- 런타임 `scan_all_scenes` 액션의 화면 깜박임. 에이전트가 의도해서 부르는 경로이고
  사람이 보고 있다는 보장이 없다.
- 씬 순회 자체를 없애거나 에디터 시점으로 옮기는 것. 등록 보고 내용이 바뀐다.
- 씬 로드 중 잠깐 재생되는 소리. 화면 문제와 원인은 같지만 별건이다.

## Context / Constraints

- 깜박임의 원인은 `AllSceneScanner.ScanAll`이다. 빌드 세팅의 모든 씬을 하나씩
  `LoadSceneMode.Additive`로 올리고 `SettleSeconds`(0.1초) 기다린 뒤 내린다. 그 사이
  올라온 씬의 카메라와 캔버스가 실제로 그려진다.
- `ArtelOnboardingController.Start`는 저장된 키가 있으면 자동으로 등록을 시작한다.
  즉 게임 부팅 직후에 이 깜박임이 그대로 보인다.
- 씬 수만큼 (로드 + 0.1초 + 언로드)가 쌓이므로 정지 화면만 덮어 두면 멈춘 것처럼
  보인다. 진행 표시가 필요하다.
- 온보딩 캔버스는 `sortingOrder = short.MaxValue - 1`이다. 덮개를 같은 캔버스의
  마지막 자식으로 만들면 정렬 순서 상수를 건드리지 않고도 패널과 게임 UI 위에 그려진다.
  가상 커서 캔버스(`short.MaxValue`)만 그 위에 남는데, 커서는 보이는 편이 맞다.
- `등록` 버튼은 스캔이 끝나고 `viewModel.Register`에 들어가야 `Registering` 상태가
  된다. 그전까지 `CanRegister`가 `true`라 연타하면 씬 순회가 겹쳐 돈다.

## Approach (Checklist)

- [x] `AllSceneScanner.ScanAll`에 선택적 진행 콜백 `Action<int, int>` 추가. 씬을 시작할
      때마다 (1-based 현재 씬, 전체 씬 수)를 알린다.
- [x] `SceneScanReporter.CreateReport`가 그 콜백을 그대로 통과시킨다.
- [x] `ArtelOnboardingController`가 온보딩 캔버스 마지막 자식으로 전체 화면 불투명
      덮개를 만든다. 제목, `viewModel.Status`에 묶인 상태 줄, 씬 진행 줄로 구성한다.
- [x] 스캔+등록 코루틴 전체 구간에서 덮개를 켜고, `finally`로 반드시 끈다.
- [x] `scanInProgress` 가드로 중복 등록 시작을 막는다. 덮개가 `raycastTarget`으로
      클릭을 막지만 키보드 조작까지 막지는 못한다.

## Validation

- `WebSocketTransportTests`의 온보딩 GUI 테스트로 캔버스 구성 회귀 확인.
- 새 테스트: 덮개가 기본으로 꺼져 있고 캔버스 마지막 자식이며 화면 전체를 덮는다.

## Risks / Rollback

- 덮개가 켜진 채로 남으면 게임 화면이 가려진다. `finally` 해제와 기본 비활성으로 막는다.
- 되돌리기는 커밋 하나 revert로 끝난다. 프로토콜과 저장 포맷 변경 없음.
