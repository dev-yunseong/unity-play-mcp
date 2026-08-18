# 2026-08-13 — [SDK] affordance IL 분석기 활성화와 ILPP 순서 의존성 해결

- Date: 2026-08-13
- Jira: [ARTEL-393](https://artel-asm.atlassian.net/browse/ARTEL-393)
- Status: Draft

## Goal

ARTEL-392가 잠가 둔 분석기를 푼다. 그 순간 게임 어셈블리 하나를 ILPostProcessor 둘이
건드리게 되므로, 둘 사이의 순서 의존성을 **없앤 뒤에** 푼다.

세 가지를 한다.

1. `Unlocked` 스위치 삭제 (한 항 + 한 필드)
2. gesture 판독이 `UnityEngine.Input`과 `Artel.ArtelInput`을 **둘 다** 받아들이게 한다
3. 분석 실패 시 원본 어셈블리가 그대로 쓰이는지 **실제 실패를 주입해** 확인한다

## Non-goals

- 두 ILPostProcessor를 하나로 통합
- [ARTEL-383](https://artel-asm.atlassian.net/browse/ARTEL-383) 수정
- `ARTEL_AFFORDANCE` 게이트 제거 — 유지하기로 확정됐다
- gesture 수치 대조 — `Scan`이 없어 리포트가 안 나온다. ARTEL-394

## Context / Constraints

### 순서 문제의 실체

`VariantBuilder.cs:769`가 gesture를 읽는 조건은 한 줄이다.

```csharp
called.DeclaringType?.FullName != InputType   // InputType = "UnityEngine.Input"
```

SDK의 `InputMethodWeaver`는 바로 그 호출들을 `Artel.ArtelInput`으로 갈아치운다
(`SupportedMethodNames`에 `GetKeyDown`·`GetKey`·`GetKeyUp`·`get_anyKey`·
`get_anyKeyDown`·`GetMouseButton*`이 모두 들어 있다). SDK 위버가 먼저 돌면 affordance는
gesture를 하나도 못 읽는다.

**실패가 아니라 축소로 나타난다.** 컴파일도 테스트도 통과하는데 gesture만 0이 된다.

### 지금은 우연히 맞는 순서다

ARTEL-393 착수 전 A/B 실측([Jira 코멘트](https://artel-asm.atlassian.net/browse/ARTEL-393)):
SDK 패키지 유무와 무관하게 gesture 25로 동일했고, 빌드된 `Assembly-CSharp.dll`에 두
위버의 흔적이 모두 있었다. 즉 **지금은 affordance가 먼저 돈다.**

Unity는 ILPP 실행 순서를 보장하지 않고, 순서를 지정하는 지원되는 수단도 없다. 그래서
**순서를 고정하는 대신 순서에 무관하게 만든다.**

### 고치는 방법 — 선언 타입 둘을 받는다

프록시의 멤버 이름이 원본과 **정확히 같다**는 것을 확인했다
(`Packages/kr.artel.sdk/Runtime/UnityEngine/Input.cs`):

```
UnityEngine.Input           Artel.ArtelInput
  GetKeyDown/GetKey/GetKeyUp  동일
  anyKey / anyKeyDown         동일 (getter 이름 get_anyKey / get_anyKeyDown)
  GetMouseButton(Down|Up)     동일
```

따라서 `switch (called.Name)`은 **한 글자도 바꾸지 않는다.** 선언 타입 비교만
둘 중 하나를 받도록 넓힌다. 인자 모양도 그대로라(위버는 메서드 참조만 교체한다)
`Key()`·`Mouse()`의 피연산자 판독도 영향이 없다.

이 방식의 값: SDK 위버가 먼저 돌든 나중에 돌든 같은 답이 나온다. 순서에 기대는 코드가
사라지므로 Unity가 순서를 바꿔도 조용히 축소되지 않는다.

### 실패 경로가 이 이슈에서 가장 중요하다

`AffordanceILPostProcessor.cs:21`이 적어 둔 사고 기록이 이 자리의 것이다 — 이 분석이
세 차례 에디터를 먹통으로 만들었고 그 에디터는 열어서 원인을 볼 수조차 없었다.

게이트를 유지하기로 했으므로 안전망은 둘이다(심볼을 지우는 길, 그리고 실패 시 원본을
쓰는 길). 그래도 **심볼을 켠 프로젝트에서는 후자가 마지막 방어선**이다. 형식적으로
읽고 넘어가지 말고 실제로 실패를 주입해 확인한다.

### 측정 공백은 그대로다

`Scan`이 ARTEL-394에서 오므로 이 이슈에서는 리포트가 없다. gesture 수치를 셀 수 없다.

**그래서 이 이슈의 gesture 검증은 "구워졌는가"까지다.** 판독기 수정이 옳았는지는
ARTEL-394에서 `gesture ≥ 25`로 확인된다. 만약 거기서 0이 나오면 원인은 이 이슈의
수정이다 — 그것이 ARTEL-391에서 (B)를 고른 대가이며, 이미 기록했다.

## Approach (Checklist)

- [ ] **Step 0: Recon**
  - [ ] 부모 브랜치(ARTEL-392)에서 EditMode 기준선 확인 — 253/0 유지 중인지
  - [ ] 잠금 상태의 대조군 확보: 지금 `Library/ArtelScope`가 0개, `[Artel]` 진단 0건

- [ ] **Step 1: 순서 의존성 제거** — 잠금보다 **먼저** 한다

  `VariantBuilder.cs:21`의 상수 하나를 둘로 늘리고, `:769`의 비교를 판정 메서드로
  바꾼다. `switch (called.Name)`은 손대지 않는다.

  ```csharp
  // 21행 — 상수
  private const string InputType = "UnityEngine.Input";
  private const string ProxiedInputType = "Artel.ArtelInput";

  // 769행 — 비교
  !ReadsInput(called.DeclaringType?.FullName)

  // 새 판정 메서드
  private static bool ReadsInput(string declaringType) =>
      declaringType == InputType || declaringType == ProxiedInputType;
  ```

  - [ ] 상수 하나 추가, 비교를 `ReadsInput`으로 교체, 메서드 하나 추가
  - [ ] 왜 두 이름인지 주석으로 남긴다 — 같은 패키지의 `InputMethodWeaver`가 이 호출들을
        갈아치우고, Unity가 ILPP 순서를 보장하지 않으므로 어느 쪽을 만날지 알 수 없다

- [ ] **Step 2: 잠금 해제**
  - [ ] `WillProcess`에서 `Unlocked &&` 삭제
  - [ ] `Unlocked` 필드와 주석 삭제
  - [ ] `WillProcess`가 원래 형태로 돌아온다:
        `return IsEnabledFor(compiledAssembly) && !IsSkipped(compiledAssembly.Name);`

- [ ] **Step 3: 실패 주입 확인** — 임시 변경으로 확인하고 되돌린다. 커밋하지 않는다

  주입 지점은 `AffordanceILPostProcessor.cs:149`(`SimpleSetter.Forget();`) **직후**다.
  `Survey`의 첫 실행 문장 바로 뒤이고 `Process`의 `try` 안이므로, 분석이 시작하자마자
  터지는 가장 이른 실패를 흉내낸다.

  ```csharp
  SimpleSetter.Forget();
  throw new Exception("ARTEL-393 failure-path check");   // 확인 후 삭제
  ```

  - [ ] 위 한 줄을 넣고 WordVenture 컴파일 → **성공**해야 한다
  - [ ] `Library/ArtelScope`에 거절 사유가 남는지 (`skipped, ...`)
  - [ ] 예외를 심은 뒤에도 SDK 위빙이 살아남는지
  - [ ] 확인 후 원상복구하고 `git diff`로 잔여 변경 0 확인

- [ ] **Step 4: Rollout** — PR base는 ARTEL-392 브랜치

## Validation

```bash
WV=~/Desktop/soma/artel-sdk/samples/WordVenture
rm -rf "$WV/Library/ArtelScope"

/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -projectPath "$WV" -logFile /tmp/artel-393-compile.log
echo "exit=$?"

# 1) 컴파일 오류
grep -icE 'error CS|Compilation failed' /tmp/artel-393-compile.log || true

# 2) 분석기가 실제로 돌았나 — 392의 정반대를 기대한다
ls -A "$WV/Library/ArtelScope" | wc -l
grep -c '\[Artel\]' /tmp/artel-393-compile.log || true

# 3) 구워졌나 — attribute 와 두 resource 이름 (이름은 압축되지 않아 보인다)
strings "$WV/Library/ScriptAssemblies/Assembly-CSharp.dll" \
  | grep -E 'AffordanceAttribute|kr\.artel\.affordance\.(evidence|watch)' | sort -u

# 4) SDK 위빙이 살아남았나 — 두 위버 공존
strings "$WV/Library/ScriptAssemblies/Assembly-CSharp.dll" \
  | grep -E 'ArtelInput|artelActionBuffer' | sort -u

# 5) 원본 대비 diff — 이 이슈가 바꾼 파일만
cd ~/Desktop/soma/artel-sdk && git diff --stat HEAD~1 -- Packages/

# 6) EditMode
.github/scripts/setup-unity-test-project.sh /tmp/artel-393-tests
/Applications/Unity/Hub/Editor/2022.3.34f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -runTests -testPlatform EditMode \
  -projectPath /tmp/artel-393-tests \
  -testResults /tmp/artel-393-tests/results.xml -logFile /tmp/artel-393-tests/unity.log
python3 .github/scripts/summarize-test-results.py /tmp/artel-393-tests/results.xml EditMode
```

| # | 기대값 |
|---|---|
| 1 | `0` |
| 2 | **0보다 크다.** ARTEL-392에서는 둘 다 0이었다. 잠금이 풀렸다는 직접 증거 |
| 3 | `AffordanceAttribute`와 `kr.artel.affordance.evidence`·`.watch`가 나온다 |
| 4 | `ArtelInput`·`__artelActionBuffer` 여전히 있음 — 두 위버가 공존한다 |
| 5 | `VariantBuilder.cs`와 `AffordanceILPostProcessor.cs` 두 파일만 |
| 6 | `253 passed / 0 failed` |

**2번이 이 이슈의 핵심 지표다.** ARTEL-392와 정확히 반대 방향이라 잠금 해제가 실제로
효과를 냈는지가 한눈에 보인다.

## Risks & Rollback

- **gesture 판독 수정이 틀렸을 위험 (높음, 이 이슈에서 확인 불가).** 리포트가 없어
  수치를 셀 수 없다. ARTEL-394에서 `gesture ≥ 25`로 드러난다. 완화는 수정 범위를
  최소로 두는 것 — 선언 타입 비교 한 곳만 넓히고 `switch`는 건드리지 않는다
- **분석기가 실제로 도는 첫 이슈 (높음).** 먹통 사고 셋이 이 자리에서 났다. Step 3의
  실패 주입이 형식이 아니라 실제 확인이어야 하는 이유다
- **컴파일 시간 증가 (중간).** 어셈블리당 10,000ms 예산이 있지만 WordVenture 컴파일
  시간이 눈에 띄게 늘면 기록한다
- **Rollback:** `git revert` 한 번이면 다시 잠긴다. 되돌린 상태가 ARTEL-392와 같으므로
  안전한 지점이 바로 아래에 있다

## Open Questions

없음.
