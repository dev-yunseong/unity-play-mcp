# 2026-09-08 — release version bump을 workflow_dispatch 입력으로 만든다

- Date: 2026-09-08
- GitHub Issue: #45
- Status: Implemented

## Goal

release마다 사람이 네 파일에 손으로 적던 version 값을 `workflow_dispatch` 입력으로 갱신하고,
그 결과를 draft pull request로 열어 사람이 review하고 merge하게 한다.

- `Packages/dev.yunseong.unityplaymcp/package.json`
- `Packages/dev.yunseong.unityplaymcp/Runtime/Affordance/Scan/PackageVersion.cs`
- `mcp/package.json`
- `Packages/dev.yunseong.unityplaymcp/Editor/McpConfig/mcp-server-version.txt`

새 workflow `.github/workflows/bump-version.yml`을 만든다. 이 workflow는:

1. `unity_version`과 `mcp_version` 두 입력을 받는다. 둘 다 optional이지만 최소 하나는 있어야 한다.
2. 이미 있는 `.github/scripts/set-package-version.sh`가 Unity 두 파일을 담당하므로 그대로 재사용한다.
3. MCP server 두 파일을 위한 새 sibling script `.github/scripts/set-mcp-server-version.sh`를
   만들어 같은 방식으로 재사용한다.
4. 실제로 바뀐 파일이 있을 때만 새 branch를 만들어 commit하고 draft pull request를 연다.
   `develop`에 직접 push하지 않는다.
5. 이미 release된 version으로 옮기려 하면 거절한다 — Unity 쪽은 git tag `v<unity_version>`, MCP
   쪽은 npm의 `unity-play-mcp@<mcp_version>`.
6. README.md에 release 절차를 적는다.

## Non-goals

- **version 번호를 자동으로 정하는 것.** 무엇이 breaking인지는 사람이 판단해 입력 값으로 적어
  넣는다.
- **GitHub Release를 자동으로 만드는 것.** 이 workflow는 PR을 여는 데서 끝난다. Release 생성과
  tag 발행은 여전히 사람이 한다.
- **CHANGELOG 도입.**
- **동시 dispatch 사이의 경쟁 상태를 완전히 없애는 것.** 같은 version 쌍을 겨눈 재시도는 같은
  branch 이름으로 수렴시켜 중복 PR을 줄이지만, 그 밖의 동시 dispatch까지 막지는 않는다. 사람이
  수동으로 트리거하는 저빈도 작업이라 그 정도 보장이면 충분하다.
- **`publish-mcp.yml`의 검증 규칙 변경.** `release_version = unity_version`,
  `mcp_version = server_version` 비교는 그대로 둔다.
- **이 workflow 자체의 dispatch 검증.** `workflow_dispatch`는 default branch에 있어야 실행할 수
  있어, 이 PR이 merge되기 전에는 dispatch할 수 없다. `## Validation`에 merge 후 사람이 돌릴 명령을
  적는다.
- **`README.ko.md` 갱신.** 이 작업의 쓰기 범위는 `README.md`로 한정됐다. `README.ko.md`도 release
  절차를 따로 다루는 절이 없어 같은 내용이 필요하겠지만, 별도 후속 작업으로 남긴다.

## Context / Constraints

- **Source of truth는 `package.json`이다.** tag는 그 값과 맞는지 검사받는 쪽이다 —
  `publish-mcp.yml`이 release 시점에 `release_version(태그) = unity_version(package.json)`,
  `mcp_version(mcp/package.json) = server_version(mcp-server-version.txt)`를 확인한다. 이
  workflow는 그 규칙을 재사용할 뿐 바꾸지 않는다.
- **tag를 옮기지 않는다.** `publish-mcp.yml`은 `release: published`에 걸려 있어 workflow가 돌 때
  Release는 이미 예전 commit을 가리키고, tag를 옮겨도 Release가 배포하는 tarball은 바뀌지 않는다.
  Unity Package Manager는 git URL 설치에서 tag를 fetch 시점에 해석하고 캐시하므로, tag가 움직이면
  같은 tag를 설치한 두 사람이 다른 commit을 받는다. 그래서 이 workflow는 tag를 만들거나 옮기는 어떤
  단계도 갖지 않는다 — tag는 여전히 사람이 GitHub Release를 만들 때 생긴다.
- **기존 script를 재사용한다.** `.github/scripts/set-package-version.sh <version>`은 이미 Unity
  `package.json`과 `PackageVersion.cs`를 함께 옮긴다. workflow 안에 같은 일을 다시 적지 않고 이
  script를 그대로 호출한다.
- **MCP 두 파일은 같은 모양의 새 sibling script로 뺀다.** `set-package-version.sh`의 주석은
  "mcp/package.json과 mcp-server-version.txt는 건드리지 않는다"고 명시한다 — 그 경계를 지키면서
  같은 일을 하려면 별도 script가 필요하다. `set-mcp-server-version.sh`는 `set-package-version.sh`와
  같은 구조(인자 하나, semver 형식 검사, `node --input-type=module`로 JSON 재작성, 나머지 한 파일은
  같은 방식으로 재작성)를 따른다.
- **두 입력은 서로 기본값을 물려받지 않는다.** 처음 검토했던 대안은 `mcp_version`이 비어 있으면
  `unity_version` 값을 그대로 쓰는 것이었다. 이 저장소의 실제 history가 그 대안을 반박한다 —
  commit `b77f9f9`는 Unity package.json만 올렸고 `mcp/`는 건드리지 않았다. commit `185fe7e`
  (PR #46)는 반대로 `mcp/package.json`과 `mcp-server-version.txt`만 올렸고 Unity 쪽 두 파일은
  건드리지 않았다. 두 release 주기가 "같아야 할 이유가 없다"는 것은 이미 이 저장소에서 실제로
  일어난 일이다. `mcp_version`이 비었을 때 `unity_version`으로 채우면 Unity만 올리려는 dispatch가
  MCP server version까지 강제로 옮기게 되어 이 두 실제 사례 중 하나(PR #46 같은 MCP 전용 bump)를
  이 workflow로는 재현할 수 없게 막는다. 그래서 두 입력은 완전히 독립이다 — 비운 쪽은 그 쪽
  script를 아예 부르지 않는다. 최소 하나는 있어야 한다는 조건만 첫 단계에서 확인한다.
- **repository actions 권한이 기본적으로 pull request를 못 만든다.**
  `gh api repos/dev-yunseong/unity-play-mcp/actions/permissions/workflow`가
  `{"default_workflow_permissions":"read","can_approve_pull_request_reviews":false}`를 돌려줬다.
  `default_workflow_permissions: read`는 `GITHUB_TOKEN`이 기본으로 읽기 전용이라는 뜻이라
  workflow가 직접 `permissions:` block으로 `contents: write`, `pull-requests: write`를 선언해야
  한다. `can_approve_pull_request_reviews: false`는 저장소 설정
  **Settings → Actions → General → "Allow GitHub Actions to create and approve pull requests"**가
  꺼져 있다는 증거다 — 이 설정이 꺼진 채면 `permissions:` block을 선언해도 `gh pr create`가
  거절당한다. PR 본문의 `## Validation`에 이 사실과 확인해야 할 설정을 그대로 옮겨 적는다.
- **"이미 release된 version"의 판정 시점이 중요하다.** 무조건 두 입력 값에 대해 tag/npm 존재를
  검사하면 실제 문제가 생긴다: 사람이 Unity version만 올리고 MCP version은 지금 값 그대로
  유지하려고 `mcp_version`에 현재 값(이미 npm에 release된 값)을 그대로 다시 넣으면, 그 값은 npm에
  존재하므로 거절당한다 — 아무것도 바뀌지 않는데도 실패한다. 그래서 순서를 바꾼다: 입력이 있는
  쪽의 script를 먼저 실행해 작업 트리를 바꾸고, **그 그룹(Unity 두 파일 / MCP 두 파일)에 실제로
  diff가 생겼을 때만** 그 그룹의 "이미 release됨" 검사를 돌린다. 아무 diff도 없으면 검사 자체를
  건너뛰고 조용히 끝낸다.
- **`develop`이 default branch다.** `gh repo view --json defaultBranchRef`로 확인했다.
  `workflow_dispatch`는 default branch에 있어야 UI 목록에 나타나 실행할 수 있으므로, 이 workflow가
  실제로 도는 시점에는 사실상 항상 `develop`에서 시작한다. 그래도 안전을 위해 `github.ref`가
  `refs/heads/develop`이 아니면 즉시 실패하는 guard를 첫 단계에 둔다.
- **환경.** shell은 zsh, `node`/`npm`은 이 환경의 shell snapshot이 정의한 shell function이 nvm
  lazy-load 재귀를 일으켜 막힌다. `"$HOME/.nvm/versions/node/v24.18.0/bin/node"`처럼 전체 경로로
  직접 불러야 우회된다. `gh`는 `dev-yunseong`으로 인증되어 있고 `repo`, `workflow` scope를 가진다.
- **local 검증은 저장소 파일을 직접 건드리지 않는다.** 두 script를
  `/tmp/claude-1000/.../scratchpad/version-bump-test/`에 실제 저장소와 같은 상대 경로 구조로
  복사한 사본에 대고 돌렸다 — script의 `root=$(cd "$(dirname "$0")/../.." && pwd)` 계산이 스스로
  있는 위치를 기준으로 삼기 때문에, script까지 통째로 scratch에 복사하면 별도 수정 없이 그대로
  scratch를 가리킨다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `publish-mcp.yml`, `set-package-version.sh`, `verify-mcp-package.sh`,
  `unity-tests.yml` 읽기. 네 파일의 현재 값(`0.2.0`) 확인. actions 권한, default branch(`develop`),
  기존 label(`chore`, `infra`, `type:infra` 등) 확인. `git tag --list 'v*'`(`v0.1.0`만 존재)와
  `npm view unity-play-mcp versions`(`0.1.0`, `0.2.0`)로 tag와 npm이 서로 다른 시점에 있다는 것도
  확인 — 두 입력을 독립으로 둬야 하는 근거를 한 번 더 보여준다.
- [x] **Step 1: `.github/scripts/set-mcp-server-version.sh` 작성** — `set-package-version.sh`와
  같은 구조, `chmod +x`.
- [x] **Step 2: `.github/workflows/bump-version.yml` 작성.**
- [x] **Step 3: README.md에 `## Release procedure` 절 추가.**
- [x] **Step 4: 로컬 검증** (scratch 사본, `gh api`/`npm view` 실제 호출, YAML 파싱).
- [x] **Step 5: pair review, commit, PR.**

## Plan review (fast / medium / heavy — `.agents/skills/plan-review/SKILL.md`대로 subagent 세
개를 실제로 spawn)

Fast(haiku), medium(sonnet), heavy(opus) 세 reviewer를 병렬로 spawn해 첫 초안(당시 workflow 파일명
`bump-release-version.yml`, script 파일명 `set-mcp-version.sh`, tag 존재 확인은
`gh api .../git/refs/tags/<tag>`, branch 재시도는 "동명 branch를 지우고 새로 만든다")을 review받았다.
세 reviewer 모두 `NONPASS`.

### Fast — ambiguity, validation gap

- `gh pr create`에 `--title`/`--body`가 빠질 수 있다는 지적, tag 404 판별 mechanism을 shell
  수준으로 명시하라는 지적, branch 강제 갱신 mechanism을 명시하라는 지적. **채택** — 최종 구현은
  title/body를 전부 명시하고, tag 판별은 아래 heavy pass에서 나온 정확한 endpoint로 교체했다.

### Medium — overengineering, YAGNI, DRY

- "동명 branch를 지우고 새로 만든다" 대신 단순 force-push를 쓰라는 제안. **채택** — 최종 구현은
  삭제 없이 `git push --force-with-lease`만 쓴다(아래 heavy pass 근거 참고).
- `concurrency:` block이 Non-goals("동시 dispatch 경쟁 상태를 완전히 없애지 않는다")와 모순 아니냐는
  질문. **응답 — 유지, 근거를 plan에 추가.** 완전한 lock이 아니라 "같은 workflow가 겹쳐 돌 때 branch
  생성과 push가 서로 밟는 것"만 줄 세워 막는 한 줄짜리 장치라 Non-goals가 뺀 "완전한 경쟁 상태
  제거"보다 훨씬 좁은 범위다 — 방기하지 않는다.
- 두 script를 하나로 합치지 말라는 것, 두 입력을 독립으로 둔 것은 근거가 뚜렷해 그대로 유지.
  **채택(현행 유지).**

### Heavy — implementability, correctness

실제 API 호출로 다음을 확인한 뒤 지적했다: `mcp/package.json`과 Unity `package.json` 모두 JSON
round-trip이 byte 단위로 동일하다(포맷 흔들림 없음). `npm view unity-play-mcp@9.9.9 version`은
`E404`, `@0.2.0`은 성공. default branch는 ruleset `protect main`으로 보호되고
`require_extra_approval_for_unattributed_changes: true`.

1. **commit이 실행 자체를 못 한다.** `actions/checkout`은 git identity를 설정하지 않아 `git commit`이
   죽는다. **채택** — `Configure git identity` step 추가.
2. **tag 존재 검사가 prefix endpoint다.** `gh api repos/.../git/refs/tags/<ref>`(복수형)는 이름
   검색이다 — `git/refs/tags/v0.1`을 물으면 `v0.1.0`이 걸려 200을 돌려준다(직접 확인). prerelease
   tag(`v0.3.0-rc.1`)가 있으면 `0.3.0` bump가 거짓 거절당한다. **채택, 단 제안된 대안(`git
   rev-parse` + `fetch-depth: 0`)이 아니라 더 작은 수정을 썼다** — 단수형 endpoint
   `git/ref/tags/<ref>`는 정확히 그 이름의 tag만 매치하고 없으면 404다. `gh api
   repos/dev-yunseong/unity-play-mcp/git/ref/tags/v0.1.0`(성공)과 `.../v0.1`(404) 둘 다 직접
   호출해 확인했다(아래 Validation). shallow checkout을 그대로 유지할 수 있어 원래 구현의 나머지
   구조를 안 건드린다.
3. **"동명 branch를 지우고 새로 만든다"가 목표를 정반대로 깬다.** open PR의 head branch를 지우면
   그 PR이 닫힌다. **채택** — 삭제 단계 없이 `gh pr list --head "$branch" --state open`으로 먼저
   확인하고, 있으면 `git push --force-with-lease`만 하고 `gh pr create`는 건너뛴다.
4. **이 PR에는 CI가 돌지 않는다.** `GITHUB_TOKEN`이 만든 PR은 `pull_request` workflow(즉
   `unity-tests.yml`의 `PackageVersionTests`)를 발화시키지 않는다. **채택** — PR 본문에 굵은 글씨로
   명시하고, "사람이 할 일" 1번으로 CI를 직접 트리거하라는 항목을 추가했다.
5. **`--draft` 생략은 rationalization이다.** `pull-request.md`는 무조건형이고 bot 예외가 없다.
   **채택** — `--draft --label chore --assignee "$DISPATCH_ACTOR"` + Why/What
   Changed/Validation/Next steps 구조의 body.
- **Note(참고, 강제 아님) — 채택하지 않음:** diff gate를 `git diff` 대신 버전 값 비교로 바꾸면
  포맷 흔들림에 완전히 면역된다는 제안. 지금 round-trip이 byte 단위로 동일함을 직접 확인했고,
  `git diff --quiet`가 이미 그 사실을 정확히 반영한다 — 값 비교로 바꾸는 것은 지금 없는 문제를
  미리 막는 추가 코드라 **기각**(medium pass의 YAGNI 원칙과 일관).
- **Note — 채택하지 않음:** 이미 release된 tag보다 낮은 새 version(예: v0.2.0 존재 상태에서
  0.1.1)이 통과하는 것. 이 issue의 acceptance criteria를 어기지 않아 범위 밖으로 둔다.
- **Note — 기각:** `README.ko.md`도 같이 고치라는 제안. 이 작업의 쓰기 범위가 `README.md`로 명시적
  으로 한정돼 있어 범위 밖에 둔다(Non-goals에 기록).

### heavy review 이후, 구현 중 추가로 잡은 것 (reviewer가 지적하지 않았지만 직접 발견)

- **shell injection 표면.** `run:` block 안에서 `${{ inputs.unity_version }}` 등을 직접 전개하면
  version 문자열에 shell 특수문자가 섞였을 때 그대로 실행된다. `publish-mcp.yml`도 `TARGET_TAG`
  처럼 env var를 거쳐서만 `${{ }}` 값을 shell에 들여보낸다 — 같은 관용구로 `UNITY_VERSION`,
  `MCP_VERSION`, `DISPATCH_ACTOR` env var를 추가하고 모든 `run:` block이 그것만 읽게 했다.
- **자동 생성 commit/PR에 `Refs: #45`를 넣지 않는다.** 최초 구현은 모든 dispatch가 만드는 commit과
  PR 본문에 `Refs: #45`를 박아 놓았다. issue #45는 "이 자동화를 만드는 일"이고, 이 workflow가
  미래에 만들 개별 release PR은 그 issue와 무관하다 — 매번 #45를 참조하면 병합 후에도 계속
  잘못된 참조가 남는다. 제거했다.
- **commit/PR 제목 문구.** `chore: release version을 Unity 1.0.0로 올린다`처럼 숫자 뒤에 조사가
  어색하게 붙는 문제가 있어, 이 저장소의 실제 release commit 문구(`185fe7e`: "MCP server를
  0.2.0으로 올린다")를 따라 Unity 전용/MCP 전용/둘 다 세 가지 경우를 각각 자연스러운 문장으로
  분기했다.
- **branch 이름을 입력값이 아니라 diff 결과로 결정한다.** 최초 구현은 `unity_version`이 주어지기만
  하면 branch 이름에 그 값을 넣었다. 값이 이미 파일과 같아 diff가 없는 경우까지 branch 이름에
  끼면, "그 그룹은 건드리지 않았다"는 사실이 이름에서 사라진다. `unity_changed`/`mcp_changed`
  output을 기준으로 바꿨다.
- **scratch log 파일 삭제.** 검증 과정에서 생긴 `tag-exist.log`, `tag-missing.log`,
  `npm-exist.log`, `gh-error.log`, `npm-error.log`가 저장소 루트에 untracked로 남아 있었다 — commit
  전에 지웠다.

## Pair review (`.agents/skills/pair-review/SKILL.md`, `pair-review-critic` subagent)

첫 pass는 `NONPASS`.

1. **must-fix — `git push --force-with-lease origin "$branch"`가 재시도 경로에서 항상 실패한다.**
   `actions/checkout`은 `develop`만 얕게 가져오므로, 이 시점에는 `$branch`의 원격 상태를 담은
   remote-tracking ref가 로컬에 없다. `--force-with-lease`는 비교할 기준이 없으면 이전 실행이 만든
   branch를 향한 push를 "stale info"로 거절한다 — 이 branch가 존재하는 채로 재시도하는 경우(바로 이
   장치가 노리는 그 시나리오)마다 매번 실패한다는 뜻이다. reviewer가 직접 bare repository로
   재현했다. **채택** — push 직전에 `git fetch origin "$branch" 2>/dev/null || true`를 추가해
   remote-tracking ref를 만들어 둔다(branch가 아직 없으면 fetch가 실패하는데, 그 경우는 정상이라
   무시한다). 같은 방식(임시 bare repository + 얕은 fetch 재현)으로 **직접 다시 확인했다** —
   수정 전에는 재시도 push가 `! [rejected] ... (stale info)`로 실패, 수정 후에는
   `git fetch origin "$branch"`를 먼저 부르면 같은 push가 성공함을 확인했다(아래 Validation).
2. **should-fix — 이 재시도 경로가 로컬 검증에서 빠져 있었다는 지적.** `workflow_dispatch` 자체는
   검증할 수 없지만, git push semantics는 GitHub API 없이 bare repository만으로 검증 가능하다는
   지적. **채택** — 1번의 재현을 그대로 Validation 절에 기록했다.
3. **should-fix — `set-package-version.sh`와 `set-mcp-server-version.sh`의 semver 정규식 +
   JSON 재작성 block이 문자 그대로 중복된다는 지적.** 두 script를 합치지 않기로 한 결정 자체는
   맞다고 인정했다(issue의 constraint, 기존 script의 자기 선언 경계) — 다만 그 *검증/재작성* 부분만
   작은 공유 helper로 뺄 수 있다는 제안. **기각, 후속 과제로 남긴다.** reviewer 스스로 "blocking은
   아니다", "opportunistic하게 하면 된다"고 명시했다. 중복은 15줄 안팎이고 두 script 모두 거의
   바뀌지 않는 literal 값 처리다 — 지금 세 번째 공유 파일(또는 source 관용구)을 도입하는 비용이
   막는 실패보다 크다고 판단했다(medium pass에서 이미 채택한 YAGNI 원칙과 일관).

두 번째 pass: `VERDICT: PASS`. reviewer가 fix 위치(commit 이후, push 직전)를 직접 다시 읽어
확인했고, `actions/checkout`의 실제 동작(`git clone --branch`가 아니라 `git init` +
`git remote add`(wildcard `remote.origin.fetch` 설정) + 얕은 `git fetch`)을 기준으로 재현을 한 번
더 독립적으로 수행해 fix가 "branch가 아직 없는 최초 실행"과 "재시도" 두 경로 모두에서 올바르게
동작함을 확인했다. 나머지 파일 전체를 다시 읽어 회귀가 없음도 확인했다.

## Validation

- **Commands to run (로컬, 이 작업에서 실제로 실행):**
  - scratch 디렉터리(`/tmp/claude-1000/.../scratchpad/version-bump-test/`)에 저장소와 같은 상대
    경로 구조로 네 파일 + 두 script를 복사.
  - `set-package-version.sh 9.9.9` → `package.json`/`PackageVersion.cs` 값이 `9.9.9`로 바뀜을
    확인 → 같은 version으로 재실행 → 두 파일이 byte 단위로 동일함을 `diff -q`로 확인(idempotent).
  - `set-mcp-server-version.sh 8.8.8` → `mcp/package.json`/`mcp-server-version.txt` 값이 `8.8.8`로
    바뀜을 확인(trailing newline 보존) → 같은 version으로 재실행 → byte 단위 동일 확인.
  - 두 script 모두 `not-a-version` 인자로 호출 → `exit 2`, "not a semantic version" 확인.
  - `gh api repos/dev-yunseong/unity-play-mcp/git/ref/tags/v0.1.0` → 성공, `refs/tags/v0.1.0`.
  - `gh api repos/dev-yunseong/unity-play-mcp/git/ref/tags/v0.1` → 404 (prefix 오탐 없음 확인).
  - `gh api repos/dev-yunseong/unity-play-mcp/git/ref/tags/v9.9.9` → 404.
  - `npm view unity-play-mcp@0.2.0 version` → 성공, `0.2.0`.
  - `npm view unity-play-mcp@9.9.9 version` → `E404`.
  - `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/bump-version.yml'))"` →
    파싱 성공(`on:`이 PyYAML에서 boolean key로 뜨는 건 알려진 quirk이고 YAML 자체는 정상).
  - **재시도 push 경로**: `actions/checkout`의 얕은 fetch를 흉내 낸 임시 bare repository로
    재현했다. `develop`만 `--depth=1`로 fetch한 뒤 `bump-version/unity-1.0.0` branch를 만들어
    처음 push → 성공(branch가 원격에 없던 상태). 같은 방식으로 새 작업 디렉터리를 하나 더 만들어
    "재시도"를 흉내 냈더니, `git fetch origin "$branch"` 없이 바로
    `git push --force-with-lease`를 부르면 `! [rejected] ... (stale info)`로 실패했고, 그 앞에
    `git fetch origin "$branch" 2>/dev/null || true`를 넣은 뒤에는 같은 push가 성공했다 — pair
    review에서 지적받은 재시도 실패를 실제로 재현하고, 고친 코드가 그 실패를 없앤다는 것을
    확인했다.
  - scratch 디렉터리와 임시 진단 파일은 검증 후 삭제, 저장소에는 아무 흔적도 남기지 않음
    (`git status`로 확인).
- **이 작업에서 실행할 수 없는 것 (merge 후 사람이 할 일 — PR `## Validation`에 그대로 옮긴다):**
  - **workflow는 한 번도 dispatch되지 않았다.** `workflow_dispatch`는 default branch에 있어야
    Actions UI에 나타나 실행할 수 있어, 이 PR이 merge되기 전에는 실행할 방법이 없다.
  - Actions → `Bump release version` → `Run workflow`를 아직 release되지 않은 `unity_version`으로
    실제 dispatch해 draft pull request가 열리는지 확인.
  - 이미 release된 version(`v0.1.0`이나 `v0.2.0`에 대응하는 값)으로 다시 dispatch해 거절되는지
    확인.
  - 같은 version을 두 번 dispatch해 두 번째는 새 PR을 만들지 않고 같은 branch를 갱신만 하는지 확인.
  - PR을 열기 전에 **Settings → Actions → General → "Allow GitHub Actions to create and approve
    pull requests"**가 켜져 있는지 확인 — 지금은 꺼져 있다는 증거(`can_approve_pull_request_reviews:
    false`)가 있다.
  - merge 후 이 PR 자체에 CI가 안 돈다는 점 — 빈 commit이나 close/reopen으로 직접 트리거해야 함을
    확인.

## Risks & Rollback

- **dispatch를 이 PR 안에서 검증하지 못한다.** 위 Validation 절 참고.
- **저장소 설정이 꺼져 있으면 PR 생성이 막힌다.** 사람이 그 설정을 켜야 한다.
- **동시 dispatch 경쟁 상태.** `concurrency:` block으로 겹쳐 도는 실행을 줄 세우지만, 완전히 없애지
  않는다(Non-goals).
- **이 PR 자체에 CI가 안 돈다는 것과 같은 이유로, 이 workflow가 여는 모든 bump PR에도 CI가 안
  돈다.** PR 본문과 README에 명시했지만, review자가 놓치면 검증 안 된 version 파일이 merge될 수
  있다.
- **Rollback steps:** 이 PR을 되돌리면(`git revert`) workflow와 새 script가 사라지고, 사람이 다시
  손으로 네 파일을 고치는 기존 절차로 돌아간다. workflow가 이미 만든 draft PR이 있다면 그 PR을
  닫는다 — merge된 것이 없으므로 되돌릴 release 자체는 없다.

## Open Questions

- 없음.

## Rejected feedback

- `mcp_version`이 비었을 때 `unity_version`으로 채우자는 원래 검토안 — 이 저장소의 실제 history
  (`b77f9f9`, PR #46/`185fe7e`)가 두 release 주기가 독립적으로 움직인 사례를 보여줘 기각. 두 입력을
  완전히 독립으로 두고 최소 하나만 요구한다.
- 두 script를 하나로 합치자는 안 — `set-package-version.sh` 주석이 이미 "mcp 쪽은 건드리지
  않는다"는 경계를 선언했고, issue의 constraint("기존 script를 재사용하고 필요하면 sibling script를
  추가한다")도 분리를 요구한다. 기각.
- diff gate를 `git diff` 대신 버전 값 비교로 바꾸자는 heavy reviewer의 note — 지금 있는 문제가
  아니라 미래에 있을 수 있는 문제를 막는 추가 코드라 기각(YAGNI).
- `README.ko.md`도 같이 고치자는 제안 — 이 작업의 쓰기 범위가 `README.md`로 한정돼 기각, 후속
  작업으로 남긴다.
- 완전한 동시 dispatch 잠금(예: 별도 lock 파일이나 재시도 로직) — Non-goals에서 이미 범위 밖으로
  뒀고, `concurrency:` block 한 줄로 실질적 위험만 줄이는 선에서 그친다. 그 이상은 저빈도 수동
  작업에 과한 엔지니어링이라 기각.
- 이미 release된 tag보다 낮은 새 version이 통과하는 것을 막자는 note — acceptance criteria가
  요구하지 않아 범위 밖으로 두고 기각.
