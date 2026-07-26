# Artel SDK

## Runtime connection

Add `ArtelManager` and `ArtelOnboardingController` to a scene object. Configure
`secure`, `host`, and `port` on the manager's `Server` field. The server builds
the matching HTTP and WebSocket base URLs (`http`/`ws` or `https`/`wss`). API
clients own their endpoint paths.

Registration is authenticated with an **instance key** issued by the Artel
dashboard. Create a game instance there, copy its key, and paste it into the
onboarding panel's key field. The view-model-backed panel then calls:

```
POST /api/sdk/registrations
{ "instanceKey": "H4KQ2-8VTRM-9XZ0C-N5JWE", "sdkUuid": "<uuid>", "gameVersion": "1.2.3" }
```

`gameVersion` is `Application.version` from Player Settings, and `sdkUuid` is
the SDK's per-installation UUID — it identifies which runtime registered, and is
not a credential. On success the key is written to Unity `PlayerPrefs` under
`Artel.InstanceKey` and the SDK connects to `/ws/sdk?instanceKey={INSTANCE_KEY}`
automatically. Every later launch registers and connects with no interaction.

A key is stored only after the server accepts it. If the server answers `404`
the key is unknown or its instance was deleted, so the stored key is discarded
and the panel asks for a new one. Any other failure keeps the key so the `등록`
button can retry. The panel's `고급` section shows the SDK UUID and game
version, and offers a manual `연결` button plus `키 지우기` to forget the stored
key.

The per-installation UUID is generated once and stored in `PlayerPrefs` under
`Artel.SdkId`.

## Local PoC

Add both `ArtelManager` and `ArtelTestPageManager` to the same scene object.
The test page manager replaces the default client transport with its local
WebSocket server and manages both test servers:

- WebSocket URL: `ws://127.0.0.1:17311/ws`
- Scan request: `{ "jsonrpc": "2.0", "id": 1, "method": "scan_scene", "params": [] }`
- Action message: `ACTION` with `button_click`, `enter_text`, `key_click`, `scan_scene`, and `scan_all_scenes`

## Ordering a scan against actions

A top-level `scan_scene` answers as soon as the message arrives, so it can report
the scene while a preceding `button_click` is still moving the cursor. Put
`scan_scene` inside the `ACTION` batch instead and it runs on the same queue,
after every action ahead of it has finished:

```json
{
  "type": "ACTION",
  "id": 9,
  "actions": [
    { "id": 1, "method": "button_click", "params": [12345] },
    { "id": 2, "method": "scan_scene", "params": [] }
  ]
}
```

The batched scan sends its own `GAME_STATE` message — the same shape the poller
pushes — and leaves `{ "id": 2, "success": true }` in the `ACTION_RESULT` that
follows. The top-level request keeps working so existing clients can migrate at
their own pace.

## Scanning every scene in the build

`scan_all_scenes` walks Build Settings from index 0 upward, scanning each scene,
and answers with a single `ALL_SCENES` message. It is a batch method only —
the walk spans many frames, so there is no top-level form.

```json
{ "type": "ACTION", "id": 10, "actions": [{ "id": 1, "method": "scan_all_scenes", "params": [] }] }
```

```json
{
  "type": "ALL_SCENES",
  "id": 11,
  "scenes": [
    { "buildIndex": 0, "path": "Assets/Scenes/Lobby.unity", "scene": { "id": -1234, "type": "scene", "name": "Lobby", "children": [] } }
  ]
}
```

### Full mode

`params: ["full"]` widens the same walk. It reads every field Unity would
serialize on the MonoBehaviours the game itself wrote — public fields and
`[SerializeField]` private ones, whether or not they carry `[ArtelState]` — and
it walks into inactive objects, which come back as blocks with
`"active": false`. Everything else about the walk is unchanged.

```json
{ "type": "ACTION", "id": 10, "actions": [{ "id": 1, "method": "scan_all_scenes", "params": ["full"] }] }
```

Omitting `params` keeps the original behaviour: opted-in state only, active
objects only. `GAME_STATE` and the poller are never affected by this mode.

What full mode leaves out, on purpose:

- Components shipped by Unity or by this SDK. Reading every field of `Image` or
  `TMP_Text` buries the game's own data.
- Properties. Unity does not serialize them, and a getter can have side effects.
- References. A `UnityEngine.Object` field is reported as
  `{ "instanceId", "name", "type" }`, never followed — one `GameObject` field
  would otherwise drag the whole scene back into the payload.

Values are lowered to plain JSON before they are sent, and the lowering is
capped: 5 levels of nesting, 64 elements per array or list, 1024 characters per
string, and a reference cycle is cut where it closes. Fields whose values a game
stores in plain sight — tokens, keys — are sent as they are; nothing is masked.

Each `scene` is the same shape `GAME_STATE` sends. Scenes are loaded
`Additive`, scanned, then unloaded; a scene the game already has open is scanned
in place and left alone. The original active scene is restored and rescanned
afterwards, so `button_click` and `enter_text` target ids keep working.

The local test page drives this from its **Scan all scenes** and **Scan all
scenes (full)** buttons. It lists every returned scene under its build index and
path, drawn by the same renderer `GAME_STATE` uses. Controls belonging to a scene
the walk unloaded are disabled, since clicking them would address nothing; the
scene the game already had open stays clickable.

The result is pinned in its own section, above the live scene, and stays there
until **Clear** — the poller pushes a `GAME_STATE` within a second of any change,
and a scan that took the whole walk to produce would otherwise vanish under it.
Each component lists its states and actions, open by default and foldable, and
inactive blocks are labelled and dimmed. The section also keeps the raw
`ALL_SCENES` JSON behind a disclosure.

This runs the game's other scenes, briefly. Their `Awake`, `OnEnable`, and
`Start` execute — anything they do on load (audio, network calls, writing to
`PlayerPrefs`) happens for real. Treat `scan_all_scenes` as a discovery step, not
something to call during a run you care about. Block ids collected this way
belong to objects that are destroyed when the scan finishes; only the returned
structure is durable.

A visited scene escapes its own unload two ways. `DontDestroyOnLoad` moves
objects to a scene of their own, and anything its `Awake` or `Start`
instantiates lands in whatever scene is active then — the game's, since a scene
cannot be made active until it has finished loading. Both surface as new root
objects.

So before each load the walk records every root alive, and after scanning it
hands the new ones to the scene it is about to unload with
`MoveGameObjectToScene`. They are destroyed by that unload, `OnDestroy` and all.
The comparison and the unload happen without yielding in between, so nothing
spawned after the comparison slips past it.

What that cannot undo:

- `static` fields and event subscriptions a destroyed manager left behind.
- Singletons that destroy the *duplicate* on `Awake` — the game's own instance
  may be the one that died.
- Side effects already committed: audio played, requests sent, prefs written.
- Objects a visited scene parents under something the game owns. Only roots can
  move between scenes, so those stay.
- Work a scene defers past the settle window — a coroutine, an `Invoke`, a web
  request callback. No fixed wait covers it.
- Anything the game itself creates during the walk, which is a new root like any
  other and goes with them.
- Mutated `ScriptableObject` and other asset state, which no scene owns.

## Agent keyboard input

Keep using `UnityEngine.Input` for keyboard polling. Artel's IL post-processor
rewrites supported calls to the `ArtelInput` proxy during compilation:

```csharp
using UnityEngine;

public sealed class PlayerInput : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }
    }

    private void Jump()
    {
    }
}
```

The proxy combines physical Unity input with virtual Agent input for
`GetKeyDown`, `GetKey`, `GetKeyUp`, `anyKey`, and `anyKeyDown`. A `key_click`
action accepts a `KeyCode` enum name or numeric value plus a positive duration
in seconds:

```json
{
  "type": "ACTION",
  "id": 2,
  "actions": [
    {
      "id": 2,
      "jsonrpc": "2.0",
      "method": "key_click",
      "params": ["Space", 0.5]
    }
  ]
}
```

Virtual input begins on the frame after the action is accepted. `GetKeyDown`
is true for that frame, `GetKey` remains true for the requested duration, and
`GetKeyUp` is true for one frame when the duration ends. These values are frame
snapshots and can be read by multiple callers without being consumed.

`GAME_STATE.scene` uses one block per active `GameObject`. Supported Unity UI
components are listed separately, so one block can expose multiple capabilities:

```json
{
  "id": 2,
  "type": "block",
  "name": "login panel",
  "components": [
    {
      "type": "editText",
      "name": "email edit text",
      "placeholder": "example@artel.kr",
      "states": [],
      "actions": []
    }
  ],
  "children": []
}
```

The scene `id` is its Unity scene handle, and each block `id` is the
`GameObject` instance ID. Treat both as opaque identifiers valid only while
their Unity objects remain alive in the current process. Do not persist them
across scene reloads, object recreation, or Unity restarts.

## State and action tracking

Add attributes to a `MonoBehaviour`. State is read at scan time. Action results
are captured by IL post-processing without changing the source class:

```csharp
using Artel.Tracking;
using UnityEngine;

public sealed class PlayerStatus : MonoBehaviour
{
    [ArtelState("hp")]
    public float Hp = 100f;

    [ArtelAction("attack")]
    public int Attack(int damage)
    {
        return damage * 2;
    }
}
```

`[ArtelAction]` records method tag/name, success, return value, timestamp, and
exception type/message on failure. The original exception is rethrown.

Scan only snapshots pending actions. It does not consume them. After the
WebSocket send succeeds, the SDK removes actions included in that snapshot.
Actions recorded between scan and send remain for the next message. Failed
sends leave the entire snapshot pending.

Current limits:

- `[ArtelAction]` supports synchronous instance methods on `Component` classes.
- Async methods, iterators, and coroutines are rejected during compilation.
- Method parameters are not captured.
- Return and state values must be serializable by Newtonsoft.Json.
- Each component keeps at most 256 pending actions; overflow drops the oldest.

- HTTP URL: `http://127.0.0.1:17310/`
- WebSocket URL: `ws://127.0.0.1:17311/ws`

## Included dependencies

The SDK vendors `websocket-sharp` under `Runtime/Plugins`. It uses Unity's
`com.unity.nuget.newtonsoft-json` for protocol serialization and
`com.unity.nuget.mono-cecil` for Editor-only IL post-processing.
