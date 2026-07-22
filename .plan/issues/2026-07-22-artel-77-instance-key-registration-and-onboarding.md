# 2026-07-22 — 인스턴스 키 기반 등록 및 온보딩 창 재설계

- Date: 2026-07-22
- Jira Issue: ARTEL-77
- Status: Implemented
- Repository: sdk
- Work Type: feat
- Server counterpart: ARTEL-75 (orchestration-server)
- Dashboard counterpart: ARTEL-76 (home)

## Goal

The SDK currently invents its own identity: `ArtelSdkIdentity` mints a GUID, `POST /api/sdkId`
registers that GUID into a server-side in-memory set, and the socket connects with it. Nothing ties
the running game to a project, and any caller can register any string.

Replace that with a credential the dashboard issues:

1. The developer creates a game instance in the dashboard and receives an **instance key**.
2. The onboarding window — rebuilt around a text field — takes that key.
3. The SDK registers with `{ instanceKey, sdkUuid, gameVersion }`, where `gameVersion` is
   `Application.version` from Player Settings.
4. The key is stored in `PlayerPrefs`, so every later launch registers automatically and connects
   without the developer touching the window.
5. The WebSocket authenticates with the instance key instead of the SDK id.

## Non-goals

- **Removing `ArtelSdkIdentity`.** The per-runtime GUID survives as `sdkUuid` — the server records
  which runtime last registered. It is no longer a credential.
- **Backward compatibility with `POST /api/sdkId`.** The server deletes that endpoint in ARTEL-75;
  nothing is deployed, so there is no old-SDK population to support.
- **A settings/inspector UI for the key.** It is entered at runtime and persisted; it is not a
  `[SerializeField]`.
- **Key rotation or clearing from the dashboard.** A "forget this key" affordance in the window is
  in scope; server-side revocation is not (see ARTEL-75 Deferred Work).
- **TextMeshPro.** TMP is a package dependency and is used for *scanning* user scenes, but no Artel
  UI is built with it, and TMP needs a settings/font asset a bare package consumer may not have.
  The new input field is legacy `UnityEngine.UI.InputField`, like the rest of the window.

## Context

- Unity **2022.3** (`package.json`), C# 9 / .NET Standard 2.1.
- The window is built imperatively in `ArtelOnboardingController.CreateGui()` with legacy uGUI —
  no prefabs, no UXML. Helpers already exist: `CreateButton`, `CreateText`, `CreateToggle`,
  `SetRect`, `AnchorTopRight`, `EnsureEventSystem`.
- The view/logic split is deliberate and worth keeping: `ArtelOnboardingViewModel` is a plain
  testable class; the controller only builds GameObjects and binds `RefreshView`.
- `ArtelSdkRegistrationClient` returns an **unsent** `UnityWebRequest`; the view model sends and
  disposes it. That is what makes the URL and body assertable without a network.
- User-facing strings are Korean; exception messages and `Debug.Log` are English with an
  `[Artel] ` prefix.
- Style: no target-typed `new`, no expression-bodied members, Allman braces, all types `sealed`,
  `nameof` in every argument exception.

### Three hazards the recon surfaced

1. **`ArtelManager.Awake` adds `ArtelOnboardingController` before assigning `SdkId`**
   (`ArtelManager.cs:53-56` vs `:69`), and `AddComponent` runs the new component's `Awake`
   synchronously. Anything that reads identity must live in `Start()` or later — never `Awake`.
2. **`Server.HttpBaseUri` throws when `host` is empty**, and empty is the default. Auto-registration
   on `Start` must be inside try/catch or an unconfigured scene throws on every launch. The existing
   `"설정 오류: "` status path already models this.
3. **Transport ownership.** `SetWebSocketTransport` throws if called twice, `ArtelTestPageManager`
   injects a local transport with `takeOwnership: false`, and `StartTransport()` early-returns when
   `!ownsTransport`. Auto-connect must be a no-op in that configuration, and must not double-start
   alongside `connectOnEnable`.

## Architecture

### Storage

New `internal static class ArtelInstanceKey` beside `ArtelSdkIdentity`, PlayerPrefs key
`Artel.InstanceKey`, API `TryLoad(out string)` / `Save(string)` / `Clear()`.

It is a separate class rather than a method on `ArtelSdkIdentity` because that class's contract is
"always returns a valid GUID" — the instance key is legitimately absent on first run, and folding an
optional value into a never-fails accessor would blur both.

### State machine

`ArtelOnboardingViewModel` is rewritten rather than patched. The current model has `registered`
flipping back to `false` inside `Connect` so the connect button re-disables — incidental behaviour
that does not survive auto-connect.

```
NeedsKey    → (key entered, Register)    → Registering
Registering → success                    → Connecting → Connected
Registering → failure                    → NeedsKey (with error status, key kept in the field)
```

On `Start`: if a stored key exists, enter `Registering` immediately and keep the panel collapsed
unless it fails. If there is no stored key, show the panel expanded.

The key is only persisted **after** the server accepts it, so a bad paste is never remembered.

### Failure handling splits on whether the key can ever work again

- **404** — the key is unknown or its instance was deleted in the dashboard. No amount of retrying
  fixes it, so the stored key is **cleared** and the window returns to `NeedsKey` with an
  explanatory status. Leaving it would make every future launch fail silently in the same way.
- **Anything else** (network failure, 5xx, `Server` misconfiguration) — retrying is meaningful, so
  the key is **kept** and only the error is shown, with the `등록` button re-enabled to retry.

The developer pastes rather than types the key (the dashboard offers a copy button), so the field
is sized and validated as a paste target: no format masking, trimmed on submit.

### Version

`Application.version` is read in `ArtelManager` and passed into the view model and the registration
client as a plain `string`. It is deliberately not read inside the client or the view model: the
registration-client test asserts an exact JSON body, and a value that depends on the host project's
Player Settings cannot be pinned.

### Auto-connect

After a successful registration the view model calls the same `Action` the connect button used to
call (`ArtelManager.StartTransport`). Because `StartTransport` already guards on
`webSocketTransport == null` and `ownsTransport`, the injected-transport case degrades to a no-op
rather than an exception. A manual "연결" button stays available in the advanced section for the
case where auto-connect was skipped.

### Window layout

Collapsed state is unchanged: an `"Artel"` button top-right toggles the panel.

Expanded panel, top to bottom:

| Element | Notes |
|---|---|
| Title `Artel SDK` | |
| Instance key `InputField` | placeholder `대시보드에서 발급받은 키를 입력하세요`, character limit 24 |
| `등록` button | disabled while a request is in flight or the field is empty |
| Status text | Korean, multi-line, the single binding target |
| Advanced foldout (`고급`) | collapsed by default: SDK UUID, game version, 부드러운 커서 toggle, manual 연결 button, `키 지우기` |

`InputField` needs a `RectTransform + Image + InputField`, a child `Text` assigned to
`textComponent`, and a child placeholder `Text` — `textComponent` must be assigned or the field
silently does nothing. Both children come from the existing `CreateText` helper, wrapped in a new
`CreateInputField` static helper next to the others.

Panel height grows from 320 to roughly 380; exact `SetRect` offsets are settled during
implementation since the layout is absolute pixel math.

### Known leak, unchanged in kind

`SceneScanner.Scan()` walks every active root GameObject with no exclusion for Artel's own canvas,
so the onboarding window already appears in `GAME_STATE`. The new field will appear as an
`editText` component, which means an agent could read or overwrite the key field. This is not a
regression — the existing buttons and toggle leak the same way — but it is the first concrete case
where the leaked control is credential-shaped. Recorded in Risks; excluding Artel's canvas from the
scan is a separate change.

## Approach (Checklist)

- [x] **Step 0: Recon.** Re-read `ArtelManager` lifecycle, `WebSocketTransportTests` (it pins three
      exact strings and a button count that this change breaks).
- [x] **Step 1: Storage.** `Runtime/ArtelInstanceKey.cs`.
- [x] **Step 2: DTOs.** `Runtime/Protocol/Dto/SdkRegistrationRequestDto.cs` rewritten to
      `instanceKey` / `sdkUuid` / `gameVersion`; new `SdkRegistrationResponseDto.cs`
      (`instanceId`, `projectId`, `instanceName`, `gameBuildId`, `gameVersion`). `internal sealed`,
      one type per file, `[JsonProperty("camelCase")]` on every property — there is no global
      contract resolver.
- [x] **Step 3: Registration client.** `RegistrationPath` → `/api/sdk/registrations`;
      `CreateRequest(Server server, string instanceKey, string sdkUuid, string gameVersion)` with
      the existing argument validation shape.
- [x] **Step 4: WebSocket.** `ArtelWebSocketClient.BuildEndpoint(Server, string instanceKey)` emits
      `?instanceKey=…`; constructor parameter renamed. `ArtelManager.StartTransport` passes the
      stored key instead of `SdkId`, and refuses to start when no key is stored (English
      `Debug.LogWarning`).
- [x] **Step 5: View model.** Rewrite around the state machine above; expose `Status`, `CanRegister`,
      `KeyInput`, `ShowPanel`, and `Register(Server, key, sdkUuid, gameVersion, Action connect)`.
      Keep the "build the request inside try/catch, then `using` + `yield return SendWebRequest()`"
      split — `yield return` cannot sit inside `try/catch`, and the body must be read before the
      `using` disposes.
- [x] **Step 6: Controller and GUI.** New `CreateInputField` helper; rebuilt `CreateGui`; advanced
      foldout; auto-register kicked off from `Start` (never `Awake`).
- [x] **Step 7: Docs.** `Packages/kr.artel.sdk/README.md` documents `POST /api/sdkId`,
      `/ws/sdk?sdkId=…`, and the `Artel.SdkId` PlayerPrefs key — all three statements become wrong.
- [x] **Step 8: Tests.** See Validation.
- [ ] **Step 9: `.meta` files.** Every tracked file has a committed `.meta`. New files created
      outside the editor have no GUID; generate them by opening the package in Unity before
      committing, or the references break for everyone else. **Outstanding** — open the package in
      Unity to generate metas for `Runtime/ArtelInstanceKey.cs`, `Runtime/ArtelOnboardingState.cs`,
      and `Runtime/Protocol/Dto/SdkRegistrationResponseDto.cs`.

## Validation

**Commands**

```bash
dotnet build Packages/kr.artel.sdk/Artel.Runtime.csproj --no-restore
```

```bash
git diff --check
```

EditMode tests run in the Unity 2022.3 Test Runner against a scratch project with `kr.artel.sdk` in
`testables`. Batch mode (`-batchmode -runTests`) has previously failed in this environment with a
licensing-client IPC timeout (recorded in `.plan/general/2026-07-16-add-keyboard-status-visualization.md`).
If it fails again, the honest fallback is a compile check plus an in-editor run, and the skipped
command gets recorded in the PR — `.agents/docs/testing.md` requires stating what was not run and why.

**Tests to update** (`Tests/Runtime/WebSocketTransportTests.cs`)

- `RegistrationClient_OwnsSdkRegistrationPathAndBody` — URL becomes
  `http://127.0.0.1:8080/api/sdk/registrations`; body becomes the three-field object. Note
  `NullValueHandling.Include`, so an absent `gameVersion` serializes as `"gameVersion":null`.
- `WebSocketClient_OwnsSdkWebSocketPathAndQuery` — expected URI becomes `…/ws/sdk?instanceKey=…`.
- `ArtelManager_CreatesOnboardingGuiAutomatically` — the button count and the
  `"실시간 연결 Button"` lookup both change; tests locate buttons by GameObject name, so the new
  names must be chosen once and used in both places.
- `[SetUp]`/`[TearDown]` already snapshot and restore `Artel.SdkId`; extend the same treatment to
  `Artel.InstanceKey` or the suite leaks state into the developer's editor.

**Tests to add**

- `InstanceKey_RoundTripsThroughPlayerPrefs` and `InstanceKey_IsAbsentBeforeFirstSave`.
- `OnboardingViewModel_StartsInNeedsKeyWhenNoKeyStored` and
  `OnboardingViewModel_DoesNotPersistKeyWhenRegistrationFails` — the second is the regression test
  for the typo case.

**Manual check**

Open `samples/WordVenture` (a git submodule, currently not checked out — `git submodule update
--init` first), enter a key issued by a locally running orchestration server, confirm registration
succeeds, the build appears in the dashboard, the socket connects, and a second launch connects
with no interaction.

## Risks & Rollback

- **The key is scannable by the agent.** The onboarding `InputField` shows up in `GAME_STATE` as an
  `editText`. Pre-existing in kind, newly credential-shaped in consequence. Follow-up: exclude
  Artel's own canvas from `SceneScanner`.
- **The key sits in `PlayerPrefs` in plaintext**, which is a registry key or a plist on the
  developer's machine. Acceptable for a development-time credential; consistent with the product
  decision recorded in ARTEL-75.
- **Auto-connect on launch changes default behaviour** for anyone relying on `connectOnEnable` being
  the only connect path. Only the sample project is affected today.
- **The window rebuild breaks the two GUI tests by name.** Intended, not incidental — the plan lists
  them explicitly so a green run after the change is not mistaken for "nothing moved".
- **Rollback:** `git revert`. There is no persisted server state on the SDK side beyond the
  PlayerPrefs entry, which an old build simply ignores.

## Open Questions

- Character limit on the input field: 24 assumes the `XXXXX-XXXXX-XXXXX-XXXXX` format from ARTEL-75.
  If the server's format changes, this and the placeholder copy change with it.
