# Artel SDK

## Local PoC

Add `ArtelManager` to a scene to start the SDK WebSocket server:

- WebSocket URL: `ws://127.0.0.1:17311/ws`
- Scan request: `{ "jsonrpc": "2.0", "id": 1, "method": "scan_scene", "params": [] }`
- Action message: `ACTION` with `button_click` and `enter_text`

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

Add `ArtelTestPageServer` to a scene when you want the browser test page:

- HTTP URL: `http://127.0.0.1:17310/`
- The test page connects to the `ArtelManager` WebSocket server.

## Included dependencies

The SDK vendors `websocket-sharp` under `Runtime/Plugins`. It uses Unity's
`com.unity.nuget.newtonsoft-json` for protocol serialization and
`com.unity.nuget.mono-cecil` for Editor-only IL post-processing.
