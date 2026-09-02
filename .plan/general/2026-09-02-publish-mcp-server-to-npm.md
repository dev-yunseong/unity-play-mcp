# 2026-09-02 — MCP server 를 npm 에 publish 하고 설정 창이 npx 를 쓴다

- Date: 2026-09-02
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/22
- Status: In Progress — implementation and local validation complete; first npm publish requires two-factor authentication

## Goal

`mcp/` 를 `unity-play-mcp` 로 npm 에 올리고, 설정 창이 로컬 빌드가 없을 때
`npx -y unity-play-mcp@<MCP server version>` 을 쓰게 한다. git URL 로 package 만 설치한 사용자도 설정 창으로
agent 를 붙일 수 있게 된다.

## Non-goals

- MCP server 를 별도 저장소로 분리하는 것.
- Unity package 를 저장소 루트로 옮겨 `?path=` 없이 설치되게 하는 것.
- release 와 무관한 push 에서 npm package 를 publish 하는 것.
- 기존 build script 구조 (`tsc` 로 `dist/src` 와 `dist/test` 를 만든 뒤 `dist/` 로 flatten) 를 갈아엎는 것.

## Context / Constraints

- `unity-play-mcp` 는 npm 에 비어 있다 (registry 404 확인). scope 없이 쓸 수 있다.
- 로그인은 되어 있다 (`npm whoami` → `dev-yunseong`). node v24.18.0, npm 11.16.0.
- **`mcp/.gitignore` 가 `dist/` 를 무시한다.** npm 은 `.npmignore` 가 없으면 `.gitignore` 를 쓰므로, 그대로
  publish 하면 실행 파일이 없는 package 가 올라간다. `files` 를 적으면 `files` 가 우선한다.
- 빌드 결과가 `dist/src/*.js`, `dist/test/*.js` 로 나온 뒤 `dist/src/*.js` 가 `dist/` 로 복사된다. `bin` 이
  가리키는 `dist/index.js` 와 그것이 import 하는 형제 파일들은 전부 `dist/` 바로 아래에 있다. 그래서
  `files: ["dist/*.js"]` 면 실행에 필요한 것만 담기고 test 빌드와 `.d.ts`, `.map` 은 빠진다.
- npm 은 `files` 와 무관하게 package 디렉터리의 `package.json`, `README`, `LICENSE` 를 항상 담는다. 저장소
  루트의 LICENSE 는 tarball 에 닿지 못하므로 `mcp/LICENSE` 에도 사본이 필요하다.
- npm version 은 Unity package release version 과 독립적이다. Unity package 에 함께 배포하는
  `Editor/McpConfig/mcp-server-version.txt` 가 compatible npm version 을 선언한다. Unity package 만 바뀐
  release 에서는 npm version 을 올리지 않는다.
- GitHub `release.published` event 는 `paths` filter 를 지원하지 않는다. workflow 안에서 직전 release tag 와
  현재 release tag 사이의 `mcp/**` 변경을 검사해야 한다.
- GitHub Actions 는 사용자의 local npm login session 을 공유하지 않는다. 첫 version 을 사람이 publish 한 뒤
  npm trusted publisher 를 `dev-yunseong/unity-play-mcp` 와 workflow filename 에 연결한다.
- publish 는 되돌릴 수 없다. 72시간 뒤 unpublish 가 막히고 이름은 영구히 점유된다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 완료. 위 Constraints 가 확인된 사실이다.

- [x] **Step 1: LICENSE** — 저장소 루트에 MIT `LICENSE` 를 만들고 `mcp/LICENSE` 에 사본을 둔다. 저작권자는
      `dev-yunseong`, 연도는 2026. Unity `Packages/dev.yunseong.unityplaymcp/package.json` 에
      `"license": "MIT"` 를 더한다.

- [x] **Step 2: `mcp/package.json` 을 publish 가능하게** — `"private": true` 제거. 더할 것:
      `description`, `license`, `author`, `repository` (`directory: "mcp"` 포함), `homepage`, `bugs`,
      `keywords`, `engines.node`, 그리고 `files: ["dist/*.js"]`.
      `prepack` 으로 `npm run build && npm test` 를 실행한다. publish 는 이 script 가 만든 `.tgz` 를
      `--ignore-scripts` 로 올려 검증과 publish 사이에 artifact 가 다시 만들어지지 않게 한다.

- [x] **Step 3: 설정 창이 두 갈래를 고른다** — `McpServerLocator` 가 경로 대신 `McpServerEntry` 를 돌려준다.
  - `Resolve(packageRoot, projectRoot, mcpServerVersion, fileExists)`: 로컬 빌드를 찾으면
    `node <절대경로>`, 못 찾으면 `npx -y unity-play-mcp@<mcpServerVersion>`.
  - version 파일이 없거나 비면 latest 로 진행하지 않고 error 를 보여 Add 를 막는다. compatible version 을
    모르는 상태에서 latest server 를 고르는 것은 wire protocol mismatch 를 숨긴다.
  - `UnityPlayMcpSettingsProvider` 는 `ServerCommand` 와 `_entryPoint` 대신 `_serverEntry` 하나만 갖고,
    고른 command 와 이유를 화면에 보여 준다.
  - `mcp/src/index.ts` 는 hard-coded version 대신 실행 중인 npm package 의 `package.json` version 을 읽어
    MCP server metadata 에 넣는다.

- [x] **Step 4: 사용자 문서** — root `README.md` 는 English, `README.ko.md` 는 Korean 으로 제공하고 서로
      language link 를 둔다. 두 문서는 architecture 설명보다 사용 순서를 앞세운다: Unity Package Manager git
      URL 설치 (`?path=Packages/dev.yunseong.unityplaymcp`), Unity Project Settings 에서 agent 설정 Add,
      play mode 실행, agent 에서 MCP 연결 확인, local development, troubleshooting 순서다. 설정 창이 local
      build 를 찾으면 그것을 쓰고 없으면 `npx` 를 쓰는 동작과 Node.js/network 전제를 명시한다.
      `mcp/README.md` 에 direct `npx -y unity-play-mcp` 사용법을 적고, MCP server 변경 release 에서 npm
      version 과 `mcp-server-version.txt` 를 같이 올려야 한다는 것을 적는다.

- [x] **Step 5: release publish workflow** — `.github/workflows/publish-mcp.yml` 을 만든다.
  - `release: types: [published]`, GitHub-hosted runner, `contents: read` 와 `id-token: write` 만 사용한다.
  - checkout 은 tag history 를 비교할 수 있도록 전체 history 를 받는다. GitHub API 가 돌려준 release 목록에서
    현재 release 를 제외한 직전 published release tag 를 기준으로 `git diff <previous>..<current> -- mcp/` 를
    계산하고, `mcp/**` 변경이 있을 때만 계속한다. 이전 published release 가 없으면 계속한다.
  - release event 에서는 `github.event.release.tag_name`, `workflow_dispatch` 에서는 필수 `release_tag` input 으로
    `TARGET_TAG` 를 만든다. tag 존재를 확인하고 checkout `ref` 를 정확히 `TARGET_TAG` 로 둔다. version 검사,
    previous release diff, pack 은 모두 그 checkout 을 기준으로 한다. dispatch 에서는 publish step 을 실행하지 않는다.
  - stable release tag 는 `^v[0-9]+\.[0-9]+\.[0-9]+$` 만 받고 prerelease tag 는 거부한다. `v` prefix 를 뺀
    값이 Unity package version 과 같은지 검사한다.
    `mcp/package.json` version 은 `mcp-server-version.txt` 와 같은지 별도로 검사한다.
  - Node 24, npm 11.5.1 이상, `actions/setup-node` 의 npm registry URL 을 사용한다. `mcp/` 에서 `npm ci` 후
    `npm pack --json` 으로 실제 `.tgz` 를 한 번 만든다. `prepack` 이 build 와 test 를 한 번 실행한다.
  - `.tgz` 를 검사해 `dist/index.js` 포함, `dist/test/`·`.d.ts`·source map 제외, `bin` target, shebang,
    executable mode 를 확인한다. 빈 temporary directory 에 local `.tgz` 를 설치하고 bounded process smoke test 로
    binary 가 즉시 crash하지 않고 stdio 를 기다리는지 확인한다.
  - registry 에 같은 version 이 있으면 local pack integrity 와 `dist.integrity` 가 같을 때 성공으로 끝내고,
    다르면 실패한다. version 이 없을 때만 `npm publish <검증된.tgz> --ignore-scripts` 를 실행한다. 이 idempotency가
    최초 manual publish 뒤 첫 release와 성공한 workflow 재실행을 안전하게 만든다.
    npm trusted publishing 이 OpenID Connect token 과 provenance 를 자동 생성하므로 `NPM_TOKEN` 은 저장하지 않는다.
  - `workflow_dispatch` 는 `release_tag` input 을 필수로 받아 publish 없이 변경 감지·version 검증·tarball 검증을
    실행한다.

- [ ] **Step 6: first publish and trust setup** — workflow 와 같은 script 로 `.tgz` 를 만들고 모든 artifact
      검증을 통과한 뒤 현재 login session 으로 `npm publish <검증된.tgz> --ignore-scripts` 를 실행한다. publish
      뒤 registry version 의 integrity 가 local pack 과 같은지 확인한다. workflow 가 default branch 에 들어간
      뒤 npm trusted publisher 를 GitHub repository
      `dev-yunseong/unity-play-mcp`, workflow `publish-mcp.yml`, environment 없음, 허용 action `npm publish` 로
      연결한다. 빈 directory의 실행 확인은 bounded timeout 으로 process가 즉시 crash하지 않고 stdio를 기다리는지
      검사한다.

## Validation

- **Commands to run:**
  ```bash
  cd mcp && npm ci
  npm pack --json                        # prepack 이 build 와 test 실행, 실제 tarball 생성
  # 최초 version 만
  npm publish <검증된-tarball.tgz> --ignore-scripts

  project=/mnt/c/Users/jys09/AppData/Local/Temp/unity-play-mcp-test
  .github/scripts/setup-unity-test-project.sh "$project"
  "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe" \
    -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath 'C:\Users\jys09\AppData\Local\Temp\unity-play-mcp-test' \
    -testResults 'C:\Users\jys09\AppData\Local\Temp\unity-play-mcp-test\results.xml' \
    -logFile 'C:\Users\jys09\AppData\Local\Temp\unity-play-mcp-test\unity.log'
  python3 .github/scripts/summarize-test-results.py "$project/results.xml" EditMode
  ```
- **Expected output:** tarball 에 `dist/*.js` 와 `package.json`, `README.md`, `LICENSE` 만. EditMode 전부
  green. 직전 baseline 은 220 passed · 0 failed.
- **새 test:** `McpServerLocatorTests` 에 local build 우선순위, local `node` entry, local build 없는 `npx`
  entry, version file 누락/빈 값 error 를 검증한다. config format test 하나로 `npx`, `-y`, package specifier 가
  JSON과 TOML boundary 를 그대로 통과하는지 확인하고 agent 별 duplicate test 는 만들지 않는다.
- **Manual:** 화면은 여전히 자동화하지 않는다. WSL 에서 Unity editor GUI 를 띄울 수 없다는 한계가 그대로다.
  `workflow_dispatch` dry-run 으로 GitHub Actions 의 checkout, version gate, tarball 검증도 확인한다.

## Risks & Rollback

- **Risks:**
  - publish 를 되돌릴 수 없다. 잘못된 tarball 이 올라가면 version 을 올려 다시 올리는 것 말고 방법이 없다.
    `npm pack --dry-run` 확인이 그래서 필수다.
  - release tag 와 Unity package version 이 어긋나거나, npm version 과 `mcp-server-version.txt` 가 어긋나면
    workflow 가 publish 전에 실패한다.
  - npm trusted publisher 는 package 가 이미 존재하고 workflow 가 default branch 에 있어야 설정할 수 있다.
    첫 publish 와 PR merge 사이에는 자동 publish 가 아직 준비되지 않은 짧은 setup 구간이 있다.
  - `npx` 는 첫 실행에 네트워크가 필요하다. 오프라인 기계에서는 로컬 빌드 경로만 동작한다.
- **Rollback steps:** Unity 쪽 변경은 `git revert` 로 돌아간다. npm 쪽은 되돌릴 수 없으므로, 문제가 있으면
  `npm deprecate` 로 표시하고 다음 version 을 올린다.

## Open Questions

- 없음. license 는 MIT 로 정해졌고, 저장소 분리와 루트 이동은 non-goal 로 확정했다.
