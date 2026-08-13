# Artel SDK

## Runtime connection

Add `ArtelManager` and `ArtelOverlayController` to a scene object. Configure
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
- Action message: `ACTION` with `button_click`, `enter_text`, `key_click`, `key_down`,
  `key_up`, `move_mouse`, `mouse_down`, `mouse_up`, `scan_scene`, and `scan_all_scenes`

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
`[SerializeField]` private ones, whether or not they carry `[ArtelState]` — it
walks into inactive objects, which come back as blocks with `"active": false`,
and it reports what each button is wired to call. Everything else about the walk
is unchanged.

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

### What a button calls

A button in a full scan carries its `onClick` wiring, since the scene it belongs
to is unloaded before anyone can click it and see for themselves:

```json
{
  "type": "button",
  "name": "Start Button",
  "onClick": [
    { "target": "GameFlow", "targetType": "Game.Flow.GameFlowController", "method": "StartGame" }
  ]
}
```

`target` is the name of the object the call runs on, `targetType` its full type
name, and `method` the method invoked. Only calls wired in the inspector are
listed — Unity keeps `AddListener` registrations in a delegate it never exposes,
so a button wired entirely in code reports nothing. `onClick` is absent when
there is nothing to report, which is every button outside a full scan.

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
Each component lists its states and actions, open by default and foldable,
buttons list their `onClick` calls, and inactive blocks are labelled and dimmed.
The section also keeps the raw `ALL_SCENES` JSON behind a disclosure.

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

### Holding a key

`key_click` decides in advance how long the key stays down. When the agent
cannot know that yet — a charge that lasts until something on screen changes, a
modifier held across several other actions — `key_down` and `key_up` split the
press in two:

```json
{
  "type": "ACTION",
  "id": 3,
  "actions": [
    { "id": 1, "method": "key_down", "params": ["LeftShift"] },
    { "id": 2, "method": "button_click", "params": [12345] },
    { "id": 3, "method": "key_up", "params": ["LeftShift"] }
  ]
}
```

Both take a `KeyCode` enum name or numeric value. Nothing but `key_up` ends the
hold, so a client that forgets one leaves the key down for the rest of the run.
The SDK releases everything it is holding when the connection stops, which
bounds the damage to that connection.

## Agent pointer input

`move_mouse` puts the pointer at a screen position, and `mouse_down` /
`mouse_up` hold and release a button. The queue runs a batch in order, so drag
and drop needs no action of its own — it is those three in sequence:

```json
{
  "type": "ACTION",
  "id": 4,
  "actions": [
    { "id": 1, "method": "move_mouse", "params": [420, 300] },
    { "id": 2, "method": "mouse_down", "params": [] },
    { "id": 3, "method": "move_mouse", "params": [880, 300] },
    { "id": 4, "method": "mouse_up", "params": [] }
  ]
}
```

`move_mouse` takes the coordinates a scan already reported: pixels from the top
left of the screen, the same space as a block's `transform.rect`. Aiming at
something the agent just saw is therefore its `rect` numbers, unchanged — the
SDK flips them into Unity's bottom-left screen space itself, so no caller has to
know that space exists. `mouse_down` and `mouse_up` take `[]` for the left
button, or `[0]`, `[1]`, `[2]` for left, right, and middle.

These reach the game two ways at once, because games take pointer input two
ways:

- **Polling.** `Input.mousePosition`, `Input.GetMouseButton`,
  `GetMouseButtonDown`, and `GetMouseButtonUp` are rewritten to `ArtelInput` by
  the same IL post-processor that handles the key calls. Button state follows the
  frame rules the keys do. `mousePosition` reports the agent's pointer once
  `move_mouse` has been used, and the real one until then — a position is a
  single value, so it cannot combine the two the way a button can.

  That claim is given up the moment the real mouse moves, and again when the
  connection stops. Without both, a game goes on reading a pointer nobody is
  driving: every `Input.mousePosition` in the project answers with wherever the
  agent last left it, and the person at the machine cannot move anything until
  play mode is restarted.
- **uGUI.** The SDK dispatches `PointerEventData` through the scene's
  `EventSystem`, so `IPointerDownHandler`, `IBeginDragHandler`, `IDragHandler`,
  `IEndDragHandler`, `IDropHandler`, and `IPointerClickHandler` fire as they
  would for a person. A drag begins only once the pointer has travelled past
  `EventSystem.pixelDragThreshold`, and a press that never travels reports a
  click instead.

- **`OnMouse*` handlers.** `OnMouseEnter`, `OnMouseOver`, `OnMouseExit`,
  `OnMouseDown`, `OnMouseDrag`, `OnMouseUp`, and `OnMouseUpAsButton` are called
  on whatever collider the agent's pointer is over, for the left button, the way
  the engine calls them for the real one.

  These are not EventSystem events and no amount of input mocking reaches them:
  the engine picks a collider from the OS cursor itself, and the legacy input
  backend accepts no injected values. Most 2D Unity games are built on them, so
  without this the agent cannot touch such a game at all. Picking follows the
  engine's own rules: a ray from `Camera.main` filtered by `Camera.eventMask`,
  the nearest hit of 2D and 3D, and one object rather than everything under the
  pointer. `OnMouseDrag` keeps going to the object the press started on even
  after the pointer leaves it.

  Matching the engine is the point, including where it fails. A collider the
  engine cannot pick is one a person cannot click, and an agent that reaches it
  anyway would report a game working when it does not. Two gaps follow from
  this: a scene that renders interactive objects through a camera other than
  `Camera.main` is not covered, and overlapping sprites at the same depth are
  resolved by ray distance, which is not the order they are drawn in.

  They run only while the agent holds the pointer. The engine goes on sending its
  own from the real cursor, and both at once would deliver everything twice.

A scene with no `EventSystem` gets the polling and `OnMouse*` halves and nothing
else, silently — a game that never used uGUI has nothing to miss.

What this does not change: `button_click` still invokes the button's `onClick`
directly rather than going through the EventSystem, and it moves the cursor
without firing hover events. The two paths are separate on purpose, so adding
pointer events does not alter what an existing `button_click` does to a game.

A held button has the same forgetting problem a held key does, with a worse
failure: the game stays mid-drag. Stopping the connection releases every button
and ends any drag in progress, so the game's `IEndDragHandler` still runs.

The on-screen status panel shows the agent's pointer position and any button it
is holding, beneath the pressed keys.

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
      "interactable": true,
      "states": [],
      "actions": []
    }
  ],
  "children": []
}
```

`button` and `editText` carry `interactable`: whether a person could press or type
into the target at scan time. It is false for a disabled component, a `Selectable`
with `interactable` off, and one blocked by a parent `CanvasGroup`. `button_click`
and `enter_text` on a target that is not interactable fail with
`Target is not interactable: <id>` instead of invoking the handler. Blocks a full
scan reveals with `"active": false` carry `"interactable": false` for the same
reason: their UI cannot receive input while the object is off.

### What is on screen

A scan also reports what the player can merely see, so that the readable scene is
not limited to whatever happens to be a button. `image` is a uGUI `Image`,
`sprite` is a `SpriteRenderer`:

```json
{
  "type": "sprite",
  "name": "enemy_goblin",
  "sprite": "goblin_idle",
  "states": [],
  "actions": []
}
```

`sprite` is the sprite asset's name and is absent when none is assigned — a
flat-colour panel or an invisible raycast catcher. Those are reported anyway:
they are still on screen, and an invisible one is still what the pointer lands on
first.

These carry no interaction of their own, but they are not inert. Every block
reports the area it covers, so the pointer actions can aim at one exactly as they
would at a button — which is the only way to drag something that was never built
as a control.

A `SpriteRenderer` is not a `RectTransform`, and a plain `Transform` is a point
with no extent. Their `transform.rect` therefore comes from the renderer's own
bounds rather than from the object's origin, or nothing could be aimed at them.

The scene `id` is its Unity scene handle, and each block `id` is the
`GameObject` instance ID. Treat both as opaque identifiers valid only while
their Unity objects remain alive in the current process. Do not persist them
across scene reloads, object recreation, or Unity restarts.

## Resetting the game

`reset_game` reloads the scene the run started in, which drops every scene the
game has opened since and rebuilds the first one from the data the launch used.

```json
{ "type": "ACTION", "id": 5, "actions": [{ "id": 1, "method": "reset_game", "params": [] }] }
```

It is a batch method only — the reload spans frames. The result arrives once the
new scene has loaded and settled, so a `scan_scene` after it in the same batch
reads the fresh scene. Every target id from before is dead by then; a
`button_click` queued behind a reset must come from a scan taken after it.

A pause left by `pause_time` is lifted and every held key and mouse button is
released before the load, so the new scene starts with the clock running and
nothing pressed.

The game's `DontDestroyOnLoad` objects go with it. A manager that survives scene
loads is usually where the run's progress is kept, so leaving it would defeat the
reset; the reloaded scene builds its own through the same singleton guard that
let the old one live. The SDK is the one persistent object kept, since it is
running the reset. A game whose managers are created by a bootstrap scene the run
did not start in loses them for good — the SDK logs every object it drops, by
name, for exactly that case.

What no reload can reach: static fields, `PlayerPrefs`, and save files. A game
that keeps its progress in one of those comes back holding it. The action fails,
changing nothing, when the scene the game started in is not in Build Settings —
there is no index to return to.

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

## Running while the window is not focused

**The SDK changes one of the host game's global settings.** From the moment it
opens its own connection to the orchestration server until that connection is
stopped, it sets `Application.runInBackground` to `true`, then puts the previous
value back. That covers the time the socket is down or retrying, not only the
time it is up. A build that never connects to Artel is left with whatever its
own Player Settings say.

It is needed because everything a run depends on lives in `Update()`: the WebRTC
encode pump, the screen capture loop, and the draining of the incoming message
queue. With the Player Settings default, losing window focus stops all three —
so switching from the game to a browser to watch the stream would be the very
thing that froze it, and no later message could restart it either.

Performance reports also continue while the window is not focused. Their frame
statistics include the background frames, which may reflect platform
throttling; `status.isFocused` remains available so consumers can identify
those reports.

The setting is a desktop one. On mobile the OS suspends the app regardless, so
the stream stops there while the game is in the background. What the SDK
guarantees on the way back is that the session survives the resume: the stream
lease is spent frame by frame and refuses to charge a single frame more than a
second, so a stretch in which the process was not running is not mistaken for a
viewer who went away. A viewer who really did go away still stops the stream —
once the app is running again, the lease runs out normally and the session is
torn down.

## Included dependencies

The SDK vendors `websocket-sharp` under `Runtime/Plugins`. It uses Unity's
`com.unity.nuget.newtonsoft-json` for protocol serialization and
`com.unity.nuget.mono-cecil` for Editor-only IL post-processing.
