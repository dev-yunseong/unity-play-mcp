# Artel SDK

## Runtime connection

Add `ArtelManager` and `ArtelOnboardingController` to a scene object. Configure
`secure`, `host`, and `port` on the manager's `Server` field. The server builds
the matching HTTP and WebSocket base URLs (`http`/`ws` or `https`/`wss`). API
clients own their endpoint paths.

At runtime the view-model-backed onboarding panel registers the persistent SDK UUID with
`POST /api/sdkId`. After registration succeeds, the user can explicitly connect
to `/ws/sdk?sdkId={SDK_ID}`.

The UUID is generated once and stored in Unity `PlayerPrefs` under
`Artel.SdkId`.

## Local PoC

Add both `ArtelManager` and `ArtelTestPageManager` to the same scene object.
The test page manager replaces the default client transport with its local
WebSocket server and manages both test servers:

- WebSocket URL: `ws://127.0.0.1:17311/ws`
- Scan request: `{ "jsonrpc": "2.0", "id": 1, "method": "scan_scene", "params": [] }`
- Action message: `ACTION` with `button_click`, `enter_text`, `key_click`, and `scan_scene`

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
