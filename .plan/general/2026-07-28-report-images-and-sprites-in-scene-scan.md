# 2026-07-28 — 씬 스캔에 이미지·스프라이트 추가

- Date: 2026-07-28
- Jira: ARTEL-173
- Status: Implemented, unverified

## Goal

`Image`와 `SpriteRenderer`를 씬 컴포넌트로 내보내, 읽을 수 있는 화면이 "마침
버튼인 것"에 한정되지 않게 한다. 스프라이트가 조준 가능한 면적을 갖도록
좌표 쪽도 함께 고친다.

## Non-goals

- 색상, 정렬 순서, 뒤집힘 같은 렌더링 세부. 지금 필요한 것은 "무엇이 어디에
  있는가"이고, 나머지는 실제 씬에서 필요해질 때 붙인다.
- 가려짐 판정. `onScreen`은 이미 "화면 안에 투영된다"까지만 뜻하며, 다른
  오브젝트에 덮였는지는 블록마다 레이캐스트를 요구해서 폴링 경로에 맞지 않는다.
- `RawImage`, `SpriteMask`, 파티클 등 나머지 렌더러.

## Context / Constraints

- `ScannedTarget`이 잡는 것은 Button / InputField / TMP_InputField / Text /
  TMP_Text뿐이다. 화면에 보이는 것 대부분이 스캔 결과에 아예 없다.
- `BlockTransformReader.Read`는 `RectTransform`이 아닌 블록을 `ReadPoint`로
  처리해 크기 0짜리 rect를 낸다. `SpriteRenderer`는 `RectTransform`이 아니므로
  스프라이트는 폭·높이가 0으로 나가고, 그러면 `move_mouse`로 조준할 영역이 없다.
- 오케스트레이션(ARTEL-174)과 Agent(ARTEL-175)가 이 계약을 이미 기대하고 있다.
  `type`은 `image` / `sprite`, `sprite`는 에셋 이름이며 없으면 생략한다.
- 스프라이트가 없는 단색 Image도 보낸다. 여전히 화면에 있고, 보이지 않는
  레이캐스트 캐처라면 포인터가 먼저 닿는 것이 바로 그것이다.

## Approach (Checklist)

- [x] **Step 0: Recon** `ScannedTarget` 수집 범위, 매퍼 분기, `BlockTransformReader`
      의 비-RectTransform 경로 확인
- [x] **Step 1: Domain** `VisualComponent`(`VisualKind.Image` / `Sprite`,
      `SpriteName`) 추가
- [x] **Step 2: DTO** `ImageComponentDto`, `SpriteComponentDto`. `sprite`는
      `NullValueHandling.Ignore`
- [x] **Step 3: Scanner** `Image`, `SpriteRenderer`를 잡아 컴포넌트로 낸다
- [x] **Step 4: 좌표** `Renderer`가 있는 비-RectTransform 블록은 bounds 8개 코너를
      투영해 실제 rect를 낸다. 8개인 이유는 회전·원근에서 앞면이 항상 바깥
      모서리가 아니기 때문이다
- [x] **Step 5: Tests** 이미지+스프라이트 이름, 스프라이트 없는 Image, 스프라이트
      종류, 그리고 스프라이트가 점이 아닌 면적을 받는지
- [x] **Step 6: 문서** README에 "What is on screen"

## Validation

- **Commands to run:** Unity 2022.3.34f1 batchmode `-runTests`
  (`EditMode`, `PlayMode`)
- **Expected output:** 신규 테스트 통과, 기존 실패 목록(EditMode 8건) 불변
- **Result:** **미실행.** Unity 에디터가 `samples/WordVenture`를 점유하고 있어
  batchmode가 `Multiple Unity instances cannot open the same project`로 거부됐다.
  컴파일도 확인되지 않았다. 실행은 담당자가 에디터에서 직접 한다.
- 테스트가 Test Runner 목록에 뜨려면 `samples/WordVenture/Packages/manifest.json`
  에 `"testables": ["kr.artel.sdk"]`가 필요하다. 그 파일은 별도 서브모듈이라 이
  레포 커밋으로는 넣을 수 없다.

## Risks & Rollback

- **Risks:**
  - 검증되지 않은 코드다. 특히 bounds 투영은 카메라 없는 씬, 회전된 스프라이트,
    원근 카메라에서 확인이 필요하다.
  - `SceneScannerTests`의 좌표 테스트는 EditMode에서 `Camera.main`이 잡히는지에
    기댄다. 잡히지 않으면 이 테스트만 실패한다.
  - 스프라이트가 많은 2D 씬은 `GAME_STATE` payload가 눈에 띄게 커진다. 상한은
    보내는 쪽이 아니라 줄이는 쪽(오케)에 두는 편이 맞고, 실제 프레임을 보기 전에
    숫자를 정하면 임의값이 된다.
  - `Renderer`를 가진 3D 오브젝트의 rect도 점에서 면적으로 바뀐다. 의도한
    개선이지만 기존 소비자에게는 값의 변화다.
- **Rollback steps:** 단일 커밋 revert.

## Open Questions

- 없음.
