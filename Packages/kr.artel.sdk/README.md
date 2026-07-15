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

`states` represents attributed member state. `actions` represents attributed
method invocation history; tracking is added in a later implementation stage.

Add `ArtelTestPageServer` to a scene when you want the browser test page:

- HTTP URL: `http://127.0.0.1:17310/`
- The test page connects to the `ArtelManager` WebSocket server.

## Included dependencies

The SDK vendors `websocket-sharp` under `Runtime/Plugins` and uses Unity's
`com.unity.nuget.newtonsoft-json` package for protocol serialization.
