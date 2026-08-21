# 2026-08-18 — 근거가 프리팹을 정체로 지목하고, 추적이 끊긴 자리를 적는다

- Date: 2026-08-18
- Jira: ARTEL-459
- Status: Draft

## Goal

`unplaced[type].createdBy` 를 문자열 배열에서 **객체 배열**로 바꿔 프리팹의 정체를 싣고,
걷기 한계에 걸려 버린 것을 `gaps` 와 `cut` 표시로 남긴다.

**빈 `createdBy` 가 "아무도 만들지 않는다"만 뜻하게 만드는 것**이 이 변경의 전부다. 지금은
"못 걸어갔다"도 같은 모양이라, 살아 있는 타입이 폐기로 적재된다.

## Non-goals

- content_map 적재 규칙(ARTEL-442). 이 문서가 `cut` 을 실은 뒤에 그쪽이 dead code 판정을 고친다
- 걷기 한계(2 · 8 · 16)를 올리는 것. **이 이슈는 버린 것을 적는 이슈다.** 한계를 바꾸는 것은
  비용 실측이 따로 필요하다
- GUID 를 싣는 것. 런타임 스캔은 `AssetDatabase` 를 못 쓰므로 editor 캡처에만 실리고, 그러면
  두 capture 가 서로 다른 정체를 말하게 된다
- 중첩 프리팹의 컴포넌트를 상위 `carries` 에 접는 것
- pulse 판독

## Context / Constraints

### 착수 전 실측으로 확인한 것

기준은 `origin/develop` = `f5ef566`. affordance 스택(#66~#79)은 이미 머지돼 있다.

두 문서(`wv-editor-latest.json` · `wv-devbuild-latest.json`, 둘 다 schema 6)로 대조했다.

```
                              editor      devbuild(capture=player)
unplaced                      14타입       7타입
objects                       27          43
SpellObj.createdBy            0건         8건
SpellObj 를 carries 로 쥔 ref  0건         15건      ← 7개가 표시 없이 사라진다
```

`MaxMakers = 8` 이 정확히 그 자리를 자른다. 8 이라는 숫자만 보고는 잘렸는지 알 수 없다.

`EnemyProjectile.createdBy` 는 이슈 본문이 2건이라 적었지만 devbuild 실측은 **3건**이다 —
`EnemyPoolController.enemyDataContainer` 가 하나 더 있다. 두 `fireShoot` 항목이 서로 다른
프리팹(`YellowProjectile` 6334 / `LightGreenProjectile` 6278)이라는 AC 의 요점은 그대로다.

### 정보를 잃는 자리는 한 줄이다

`SerializedReferences.Follow` 가 프리팹을 손에 쥔 채로 `AffordanceReport.Creates(carried,
ownerType, field)` 를 부른다. 넘기는 것은 타입 이름과 필드 이름 둘뿐이고, **`subject` 는 거기서
놓인다.** `CarriedByPrefab` 캐시가 `subject.GetInstanceID()` 로 도는 것 자체가 정체를 이미
안다는 증거다.

깊이 컷도 같다. `depth > MaxTraceDepth` 로 반환하는 그 순간에도 `value` 는 손에 있다.
**읽을 수 없어서 없는 것이 아니라, 이미 읽은 것을 안 적는 것이다.**

### prefabId 의 뜻

한 리포트 안에서만 유효하다. `Reference.Id` 가 그렇게 설계돼 있고 이 이슈는 그것을 바꾸지
않는다 — 실행을 넘는 지문은 `prefab` 이름과 `carries` 이고, 그 조인은 소비자 몫이다.

```
Card 프리팹    editor 27582    devbuild 6356    같은 프리팹, 다른 id
```

### schema 를 7 로 올린다

`createdBy` 항목의 **타입이 바뀐다**(문자열 → 객체). 늘어나기만 하는 변화가 아니므로 번호를
올린다. 문서 자신의 규율이 그렇고, 소비자(ARTEL-441 수용기)는 아는 번호만 받는다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `SerializedReferences.Follow`/`Add`/`CarriedBy`, `AffordanceReport.Creates`,
      `createdBy` 방출부, 두 실측 문서 대조
- [ ] **Step 1: `Creates` 가 프리팹을 받는다** — `Creates(carriedType, ownerType, field, prefabName,
      prefabId)`. `Makers` 값 타입을 구조체 목록으로
- [ ] **Step 2: 깊이 컷을 기록한다** — `Follow` 가 `depth > MaxTraceDepth` 에서 반환하기 전에
      손에 쥔 프리팹을 `cut: "depth"` 로 남기고 `trace-depth-exceeded:<type>` gap
- [ ] **Step 3: 폭 컷을 기록한다** — `MaxMakers` 초과 시 `makers-truncated:<type>`,
      `MaxCarriedTypes` 초과 시 `carried-truncated:<prefab>`
- [ ] **Step 4: 방출** — `createdBy` 를 객체 배열로, `schema` 7
- [ ] **Step 5: 테스트** — EditMode. `Creates` 가 같은 타입의 서로 다른 프리팹을 가르는지,
      한계 초과가 gap 을 남기는지
- [ ] **Step 6: 문서** — schema 6 → 7 의 변화를 `SchemaVersion` 주석에 적는다(그 주석이 세대별
      이력을 들고 있다)

## Validation

- **Commands to run:**
  - `.github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test` + Unity `-runTests -testPlatform EditMode`
- **Expected output:** 기존 EditMode 통과 유지 + 신규 테스트 통과
- ⚠️ **이 환경에 Unity 가 없다.** 내가 돌릴 수 없으므로 CI 또는 로컬 Unity 에서 확인해야 한다.
  PR 에 그대로 적는다 — 돌리지 않은 검증을 돌렸다고 적지 않는다.
- 실측 문서 재생성도 Unity 가 있어야 한다. "devbuild 에서 `SpellObj` 가 `makers-truncated` 를
  달고 나온다"는 AC 는 그 실행으로만 확인된다.

## Risks & Rollback

- **Risks:**
  - **소비자가 깨진다.** `createdBy` 를 문자열로 읽는 쪽이 있으면 schema 7 거절로 막히지만,
    막힌다는 것 자체가 파이프라인 정지다. ARTEL-441 이 아는 세대에 7 을 더해야 한다
  - **cut 항목이 `field` 를 갖지 못한다.** 깊이 컷 시점의 `field` 는 출발 필드라 의미가 있지만,
    `carries` 는 읽지 않았으므로 없다. 소비자가 `carries` 없음을 "컴포넌트 없음"으로 읽으면 안 된다
  - **Unity 없이 구현한다.** 컴파일 오류를 CI 에서 처음 본다
- **Rollback steps:** `git revert`. 문서 형식 변경이라 되돌리면 schema 6 으로 돌아간다.

## Open Questions

- `cut` 항목을 `createdBy` 에 섞을지 따로 둘지. AC 가 섞으라고 정했고 그편이 "빈 배열 = 죽은
  코드"를 지킨다 — 따로 두면 소비자가 두 곳을 봐야 하고, 한 곳만 보는 쪽이 다시 오판한다
